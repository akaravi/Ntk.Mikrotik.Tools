using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace Ntk.Mikrotik.Tools.Services
{
    public class MikroTikSshClient : IDisposable
    {
        private SshClient? _sshClient;
        private bool _disposed = false;
        
        // Store connection info for auto-reconnect
        private string? _lastHost;
        private int _lastPort;
        private string? _lastUsername;
        private string? _lastPassword;

        public event EventHandler<string>? DataSent;
        public event EventHandler<string>? DataReceived;

        public bool IsConnected => _sshClient?.IsConnected ?? false;

        public async Task<bool> ConnectAsync(string host, int port, string username, string password, int timeoutSeconds = 10)
        {
            try
            {
                // Store connection info for auto-reconnect
                _lastHost = host;
                _lastPort = port;
                _lastUsername = username;
                _lastPassword = password;
                
                OnDataSent($"Connecting to {host}:{port}...");
                
                var connectionInfo = new ConnectionInfo(host, port, username,
                    new PasswordAuthenticationMethod(username, password))
                {
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };

                _sshClient = new SshClient(connectionInfo);
                await Task.Run(() => _sshClient.Connect());

                if (!_sshClient.IsConnected)
                {
                    OnDataReceived("Connection failed!");
                    return false;
                }

                OnDataReceived("Connected successfully!");
                
                // Test connection with a simple command
                await Task.Delay(500);
                var testCommand = _sshClient.CreateCommand(":put \"test\"");
                testCommand.CommandTimeout = TimeSpan.FromSeconds(5);
                
                var testResult = await Task.Run(() => testCommand.Execute());
                var cleanedResult = RemoveAnsiEscapeSequences(testResult);
                
                OnDataReceived($"Test command result: {cleanedResult}");
                
                // If we got here, connection is working
                // Note: RouterOS commands via SSH may return empty output for some commands
                // This is normal behavior, not necessarily an error
                return true;
            }
            catch (Exception ex)
            {
                OnDataReceived($"Connection error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    OnDataReceived($"Inner exception: {ex.InnerException.Message}");
                }
                Disconnect();
                return false;
            }
        }
        
        private async Task<bool> EnsureConnectedAsync()
        {
            // If already connected, return true
            if (IsConnected)
                return true;
            
            // If we have connection info, try to reconnect
            if (!string.IsNullOrEmpty(_lastHost) && !string.IsNullOrEmpty(_lastUsername))
            {
                OnDataReceived("اتصال قطع شده است. در حال اتصال مجدد...");
                return await ConnectAsync(_lastHost, _lastPort, _lastUsername, _lastPassword);
            }
            
            return false;
        }

        public async Task<string> SendCommandAsync(string command, int timeoutMs = 5000)
        {
            // Ensure connection before sending command
            if (!IsConnected || _sshClient == null)
            {
                var connected = await EnsureConnectedAsync();
                if (!connected)
                {
                    throw new InvalidOperationException("Not connected to router and auto-reconnect failed");
                }
            }

            try
            {
                OnDataSent($"> {command}");
                
                // Use SshCommand for RouterOS
                // Note: RouterOS commands should be sent as-is, no need for special escaping
                var sshCommand = _sshClient.CreateCommand(command);
                sshCommand.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs);
                
                var result = await Task.Run(() => sshCommand.Execute());
                var exitStatus = sshCommand.ExitStatus;
                var error = sshCommand.Error;
                
                var cleanedResult = RemoveAnsiEscapeSequences(result);
                var cleanedError = RemoveAnsiEscapeSequences(error);
                
                // Log exit status for debugging
                if (exitStatus != 0)
                {
                    OnDataReceived($"[Exit Status: {exitStatus}]");
                }
                
                // Combine result and error if error exists
                var fullResponse = cleanedResult;
                if (!string.IsNullOrWhiteSpace(cleanedError))
                {
                    fullResponse = string.IsNullOrWhiteSpace(cleanedResult) 
                        ? cleanedError 
                        : $"{cleanedResult}\n{cleanedError}";
                }
                
                // If both result and error are empty, log info
                // Note: Some RouterOS commands return empty output on success (like "set" commands)
                if (string.IsNullOrWhiteSpace(fullResponse))
                {
                    OnDataReceived("[Info: Command executed, but no output returned. This may be normal for 'set' commands.]");
                }
                else
                {
                    OnDataReceived(fullResponse);
                }
                
                // Always return the response, even if empty
                // Empty response doesn't necessarily mean error in RouterOS
                return fullResponse;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error sending command: {ex.Message}";
                OnDataReceived(errorMsg);
                if (ex.InnerException != null)
                {
                    OnDataReceived($"Inner exception: {ex.InnerException.Message}");
                }
                throw new Exception(errorMsg, ex);
            }
        }


        private string RemoveAnsiEscapeSequences(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Remove ANSI escape sequences
            // Pattern matches: ESC[ followed by numbers, semicolons, and ending with a letter
            var ansiEscapeRegex = new Regex(@"\x1B\[[0-9;]*[A-Za-z]");
            var cleaned = ansiEscapeRegex.Replace(text, string.Empty);

            // Remove other control characters except newline, carriage return, and tab
            var controlCharRegex = new Regex(@"[\x00-\x08\x0B-\x0C\x0E-\x1F\x7F]");
            cleaned = controlCharRegex.Replace(cleaned, string.Empty);

            // Remove bell character (0x07)
            cleaned = cleaned.Replace("\x07", string.Empty);

            // Clean up extra whitespace but preserve line breaks
            cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
            
            // Remove lines that are only whitespace or control characters
            var lines = cleaned.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanLines = new List<string>();
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine) && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    cleanLines.Add(trimmedLine);
                }
            }

            return string.Join("\r\n", cleanLines);
        }

        public void Disconnect()
        {
            try
            {
                _sshClient?.Disconnect();
            }
            catch { }
            finally
            {
                _sshClient?.Dispose();
                _sshClient = null;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }

        protected virtual void OnDataSent(string data)
        {
            DataSent?.Invoke(this, $"[SENT] {data}");
        }

        protected virtual void OnDataReceived(string data)
        {
            DataReceived?.Invoke(this, $"[RECEIVED] {data}");
        }
    }
}

