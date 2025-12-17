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

        public async Task<bool> ConnectAsync(string host, int port, string username, string password, int timeoutSeconds = 30)
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
                
                // Connect synchronously within Task.Run to avoid blocking UI thread
                // Use GetAwaiter().GetResult() pattern to properly unwrap exceptions
                try
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            _sshClient.Connect();
                        }
                        catch
                        {
                            // Preserve all exceptions as-is through Task.Run
                            // This ensures SocketException and other exceptions are properly propagated
                            throw;
                        }
                    });
                }
                catch (AggregateException aggEx)
                {
                    // Unwrap AggregateException from Task.Run
                    // Get the first inner exception (usually the real exception)
                    var innerEx = aggEx.Flatten().InnerExceptions.Count > 0 
                        ? aggEx.Flatten().InnerExceptions[0] 
                        : aggEx.InnerException ?? aggEx;
                    
                    // Re-throw the inner exception to be caught by outer catch blocks
                    throw innerEx;
                }

                if (!_sshClient.IsConnected)
                {
                    OnDataReceived("Connection failed!");
                    Disconnect();
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
            catch (Renci.SshNet.Common.SshOperationTimeoutException ex)
            {
                var errorMsg = $"⏱️ خطا: اتصال به روتر در زمان تعیین شده ({timeoutSeconds} ثانیه) برقرار نشد.\n\n" +
                              $"🔍 لطفاً موارد زیر را بررسی کنید:\n" +
                              $"1. آدرس IP روتر صحیح است\n" +
                              $"2. پورت SSH ({port}) صحیح است\n" +
                              $"3. روتر روشن است و به شبکه متصل است\n" +
                              $"4. فایروال یا آنتی‌ویروس مانع اتصال نمی‌شود\n" +
                              $"5. شبکه شما به درستی کار می‌کند\n\n" +
                              $"📋 جزئیات فنی: {ex.Message}\n\n" +
                              $"💡 پیشنهاد: اگر شبکه شما کند است، می‌توانید timeout را در تنظیمات افزایش دهید.\n\n" +
                              $"⚠️ اگر مشکل ادامه داشت، لطفاً این پیام را به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                Disconnect();
                return false;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                var errorMsg = $"🔌 خطا در اتصال شبکه:\n\n" +
                              $"📋 جزئیات: {ex.Message}\n" +
                              $"🔢 کد خطا: {ex.ErrorCode}\n\n" +
                              $"🔍 لطفاً موارد زیر را بررسی کنید:\n" +
                              $"1. آدرس IP روتر ({host}:{port}) صحیح است\n" +
                              $"2. روتر روشن است و به شبکه متصل است\n" +
                              $"3. پورت SSH ({port}) باز است و فایروال مانع نمی‌شود\n" +
                              $"4. اتصال شبکه شما فعال است\n" +
                              $"5. روتر در همان شبکه یا قابل دسترسی است\n\n" +
                              $"💡 اگر با برنامه‌های دیگر اتصال برقرار می‌شود، ممکن است مشکل از تنظیمات timeout یا نحوه اتصال باشد.\n\n" +
                              $"⚠️ اگر مشکل ادامه داشت، لطفاً این پیام را به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                Disconnect();
                return false;
            }
            catch (InvalidOperationException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                // Handle wrapped SocketException
                var socketEx = ex.InnerException as System.Net.Sockets.SocketException;
                var errorMsg = $"🔌 خطا در اتصال شبکه:\n\n" +
                              $"📋 جزئیات: {ex.Message}\n" +
                              $"🔢 کد خطا: {socketEx?.ErrorCode ?? 0}\n\n" +
                              $"🔍 لطفاً موارد زیر را بررسی کنید:\n" +
                              $"1. آدرس IP روتر ({host}:{port}) صحیح است\n" +
                              $"2. روتر روشن است و به شبکه متصل است\n" +
                              $"3. پورت SSH ({port}) باز است و فایروال مانع نمی‌شود\n" +
                              $"4. اتصال شبکه شما فعال است\n" +
                              $"5. روتر در همان شبکه یا قابل دسترسی است\n\n" +
                              $"💡 اگر با برنامه‌های دیگر اتصال برقرار می‌شود، ممکن است مشکل از تنظیمات timeout یا نحوه اتصال باشد.\n\n" +
                              $"⚠️ اگر مشکل ادامه داشت، لطفاً این پیام را به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                Disconnect();
                return false;
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                var errorMsg = $"خطا: مشکل در برقراری اتصال SSH.\n" +
                              $"لطفاً مطمئن شوید که:\n" +
                              $"1. روتر روشن است\n" +
                              $"2. SSH فعال است\n" +
                              $"3. IP و پورت صحیح است\n\n" +
                              $"جزئیات: {ex.Message}\n\n" +
                              $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                Disconnect();
                return false;
            }
            catch (Renci.SshNet.Common.SshAuthenticationException ex)
            {
                var errorMsg = $"خطا: نام کاربری یا رمز عبور اشتباه است.\n\n" +
                              $"جزئیات: {ex.Message}\n\n" +
                              $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                Disconnect();
                return false;
            }
            catch (Exception ex)
            {
                var errorMsg = $"خطا در اتصال: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $"\nخطای داخلی: {ex.InnerException.Message}";
                }
                errorMsg += $"\n\nنوع خطا: {ex.GetType().Name}\n\n" +
                           $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
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
            catch (Renci.SshNet.Common.SshOperationTimeoutException ex)
            {
                var errorMsg = $"خطا: کامند در زمان تعیین شده اجرا نشد.\n" +
                              $"کامند: {command}\n" +
                              $"جزئیات: {ex.Message}\n\n" +
                              $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                return string.Empty; // Return empty instead of throwing
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                var errorMsg = $"خطا: اتصال SSH قطع شده است.\n" +
                              $"کامند: {command}\n" +
                              $"جزئیات: {ex.Message}\n\n" +
                              $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                return string.Empty; // Return empty instead of throwing
            }
            catch (Exception ex)
            {
                var errorMsg = $"خطا در اجرای کامند: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $"\nخطای داخلی: {ex.InnerException.Message}";
                }
                errorMsg += $"\nکامند: {command}\n" +
                           $"نوع خطا: {ex.GetType().Name}\n\n" +
                           $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                OnDataReceived($"[ERROR] {errorMsg}");
                return string.Empty; // Return empty instead of throwing
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

