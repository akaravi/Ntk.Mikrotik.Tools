using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ntk.Mikrotik.Tools.Models;

namespace Ntk.Mikrotik.Tools.Services
{
    public class FrequencyScanner
    {
        private readonly MikroTikSshClient _sshClient;
        private readonly ScanSettings _settings;
        private CancellationTokenSource? _cancellationTokenSource;
        private JsonDataService? _jsonDataService;

        public event EventHandler<FrequencyScanResult>? ScanProgress;
        public event EventHandler<string>? StatusUpdate;
        public event EventHandler<string>? TerminalData;

        public FrequencyScanner(ScanSettings settings, MikroTikSshClient? existingSshClient = null, JsonDataService? jsonDataService = null)
        {
            _settings = settings;
            _jsonDataService = jsonDataService;
            
            if (existingSshClient != null)
            {
                _sshClient = existingSshClient;
            }
            else
            {
                _sshClient = new MikroTikSshClient();
                // Forward terminal data events only if we created the client
                _sshClient.DataSent += (s, data) => OnTerminalData(data);
                _sshClient.DataReceived += (s, data) => OnTerminalData(data);
            }
        }

        public event EventHandler<int>? ProgressChanged;

        public async Task<List<FrequencyScanResult>> StartScanAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<FrequencyScanResult>();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // Check if already connected
                if (!_sshClient.IsConnected)
                {
                    OnStatusUpdate("خطا: اتصال به روتر برقرار نیست. لطفاً ابتدا به روتر متصل شوید.");
                    return results;
                }

                OnStatusUpdate("اتصال برقرار است. در حال دریافت وضعیت فعلی روتر...");
                
                // Get current status as base
                var baseResult = await GetCurrentStatusAsync();
                if (baseResult != null)
                {
                    baseResult.Status = "base";
                    results.Add(baseResult);
                    OnScanProgress(baseResult);
                }

                OnStatusUpdate("وضعیت فعلی دریافت شد. شروع اسکن...");

                // Parse wireless protocols and channel widths
                var wirelessProtocols = ParseList(_settings.WirelessProtocols);
                var channelWidths = ParseList(_settings.ChannelWidths);
                var frequencies = GenerateFrequencyList(_settings.StartFrequency, _settings.EndFrequency, _settings.FrequencyStep);

                // If no protocols or channel widths specified, use empty lists (will test with current settings)
                if (wirelessProtocols.Count == 0) wirelessProtocols.Add(null);
                if (channelWidths.Count == 0) channelWidths.Add(null);

                // Calculate total combinations
                var totalCombinations = frequencies.Count * wirelessProtocols.Count * channelWidths.Count;
                var currentIndex = 0;

                // Start new scan file
                _jsonDataService?.StartNewScan();

                foreach (var frequency in frequencies)
                {
                    foreach (var wirelessProtocol in wirelessProtocols)
                    {
                        foreach (var channelWidth in channelWidths)
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                            {
                                OnStatusUpdate("اسکن متوقف شد.");
                                break;
                            }

                            currentIndex++;
                            var progress = (int)((currentIndex * 100.0) / totalCombinations);
                            OnProgressChanged(progress);

                            var result = await ScanFrequencyAsync(frequency, wirelessProtocol, channelWidth);
                            results.Add(result);
                            
                            // Save immediately and update UI
                            _jsonDataService?.SaveSingleResult(result, _settings);
                            OnScanProgress(result);
                        }
                        
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            break;
                    }
                    
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;
                }

                OnStatusUpdate($"اسکن کامل شد. {results.Count} ترکیب تست شد.");
                OnProgressChanged(100);
            }
            catch (Exception ex)
            {
                OnStatusUpdate($"خطا: {ex.Message}");
            }
            finally
            {
                _sshClient.Disconnect();
            }

            return results;
        }

        public void StopScan()
        {
            _cancellationTokenSource?.Cancel();
        }

        private List<double> GenerateFrequencyList(double start, double end, double step)
        {
            var frequencies = new List<double>();
            for (double freq = start; freq <= end; freq += step)
            {
                frequencies.Add(Math.Round(freq, 2));
            }
            return frequencies;
        }

        private List<string?> ParseList(string input)
        {
            var list = new List<string?>();
            if (string.IsNullOrWhiteSpace(input))
                return list;

            // Split by comma or newline
            var items = input.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var trimmed = item.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    list.Add(trimmed);
                }
            }
            return list;
        }

        public async Task<FrequencyScanResult> GetCurrentStatusAsync()
        {
            var result = new FrequencyScanResult
            {
                Frequency = 0, // Will be set from current interface
                ScanTime = DateTime.Now,
                Status = "base"
            };

            try
            {
                OnStatusUpdate("در حال دریافت اطلاعات اینترفیس فعلی...");
                
                // Get current frequency - use print command to get interface info
                var getInfoCommand = _settings.CommandGetInterfaceInfo
                    .Replace("{interface}", _settings.InterfaceName);
                
                OnStatusUpdate($"اجرای کامند: {getInfoCommand}");
                var interfaceInfo = await _sshClient.SendCommandAsync(getInfoCommand, 8000);
                
                if (string.IsNullOrWhiteSpace(interfaceInfo))
                {
                    OnStatusUpdate("هشدار: هیچ پاسخی از کامند دریافت نشد. تلاش با فرمت جایگزین...");
                    // Try alternative command format with quotes
                    var altCommand = $"/interface wireless print detail where name=\"{_settings.InterfaceName}\"";
                    OnStatusUpdate($"تلاش با فرمت جایگزین: {altCommand}");
                    interfaceInfo = await _sshClient.SendCommandAsync(altCommand, 8000);
                }
                
                // Parse frequency from the full interface info
                var currentFreq = ParseValue(interfaceInfo, "frequency");
                if (currentFreq.HasValue)
                {
                    result.Frequency = currentFreq.Value;
                }

                // Collect all statistics
                result = await CollectStatisticsAsync(result);
                result.Status = "base";

                OnStatusUpdate($"وضعیت فعلی دریافت شد - فرکانس: {result.Frequency} MHz");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"خطا در دریافت وضعیت فعلی: {ex.Message}";
                result.Status = "خطا";
            }

            return result;
        }

        private async Task<FrequencyScanResult> ScanFrequencyAsync(double frequency, string? wirelessProtocol = null, string? channelWidth = null)
        {
            var result = new FrequencyScanResult
            {
                Frequency = frequency,
                ScanTime = DateTime.Now,
                WirelessProtocol = wirelessProtocol,
                ChannelWidth = channelWidth
            };

            try
            {
                var statusMsg = $"تنظیم فرکانس {frequency} MHz";
                if (!string.IsNullOrEmpty(wirelessProtocol))
                    statusMsg += $", Protocol: {wirelessProtocol}";
                if (!string.IsNullOrEmpty(channelWidth))
                    statusMsg += $", Channel Width: {channelWidth}";
                OnStatusUpdate($"{statusMsg}...");

                // Set frequency
                var setFreqCommand = _settings.CommandSetFrequency
                    .Replace("{interface}", _settings.InterfaceName)
                    .Replace("{frequency}", frequency.ToString(System.Globalization.CultureInfo.InvariantCulture));
                
                OnStatusUpdate($"اجرای کامند: {setFreqCommand}");
                var response = await _sshClient.SendCommandAsync(setFreqCommand);
                
                // Check for errors in response
                if (!string.IsNullOrWhiteSpace(response) && 
                    (response.Contains("invalid") || response.Contains("error") || 
                     response.Contains("failure") || response.Contains("failed") ||
                     response.Contains("not found") || response.Contains("no such")))
                {
                    result.ErrorMessage = $"خطا در تنظیم فرکانس: {response}";
                    result.Status = "خطا";
                    return result;
                }

                // Set wireless-protocol if specified
                if (!string.IsNullOrEmpty(wirelessProtocol))
                {
                    var setProtocolCommand = $"/interface wireless set \"{_settings.InterfaceName}\" wireless-protocol={wirelessProtocol}";
                    OnStatusUpdate($"اجرای کامند: {setProtocolCommand}");
                    await _sshClient.SendCommandAsync(setProtocolCommand);
                }

                // Set channel-width if specified
                if (!string.IsNullOrEmpty(channelWidth))
                {
                    var setChannelWidthCommand = $"/interface wireless set \"{_settings.InterfaceName}\" channel-width={channelWidth}";
                    OnStatusUpdate($"اجرای کامند: {setChannelWidthCommand}");
                    await _sshClient.SendCommandAsync(setChannelWidthCommand);
                }

                OnStatusUpdate($"انتظار برای استیبل شدن ({_settings.StabilizationTimeMinutes} دقیقه)...");
                
                // Wait for stabilization
                await Task.Delay(TimeSpan.FromMinutes(_settings.StabilizationTimeMinutes), _cancellationTokenSource?.Token ?? CancellationToken.None);

                OnStatusUpdate($"در حال جمع‌آوری اطلاعات...");

                // Collect statistics
                result = await CollectStatisticsAsync(result);
                result.Status = "موفق";

                OnStatusUpdate($"تکمیل - فرکانس: {frequency} MHz, SNR: {result.SignalToNoiseRatio} dB");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.Status = "خطا";
            }

            return result;
        }

        private async Task<FrequencyScanResult> CollectStatisticsAsync(FrequencyScanResult result)
        {
            try
            {
                // Get wireless interface info (main source of data)
                var infoCommand = _settings.CommandGetInterfaceInfo
                    .Replace("{interface}", _settings.InterfaceName);
                
                OnStatusUpdate($"اجرای کامند: {infoCommand}");
                var info = await _sshClient.SendCommandAsync(infoCommand, 8000);
                
                if (string.IsNullOrWhiteSpace(info))
                {
                    OnStatusUpdate("هشدار: هیچ پاسخی از کامند دریافت اطلاعات دریافت نشد. تلاش با فرمت جایگزین...");
                    // Try alternative command format with quotes
                    var altCommand = $"/interface wireless print detail where name=\"{_settings.InterfaceName}\"";
                    OnStatusUpdate($"تلاش با فرمت جایگزین: {altCommand}");
                    info = await _sshClient.SendCommandAsync(altCommand, 8000);
                }
                
                // If still empty, try with simpler print command
                if (string.IsNullOrWhiteSpace(info))
                {
                    OnStatusUpdate("تلاش با کامند print ساده‌تر...");
                    var printCommand = $"/interface wireless print where name=\"{_settings.InterfaceName}\"";
                    OnStatusUpdate($"اجرای کامند: {printCommand}");
                    info = await _sshClient.SendCommandAsync(printCommand, 8000);
                }

                // Parse signal strength and noise floor
                result.SignalStrength = ParseValue(info, "signal-strength");
                result.NoiseFloor = ParseValue(info, "noise-floor");
                
                if (result.SignalStrength.HasValue && result.NoiseFloor.HasValue)
                {
                    result.SignalToNoiseRatio = result.SignalStrength.Value - result.NoiseFloor.Value;
                }

                // Parse other parameters from interface info
                result.AntennaPower = ParseValue(info, "tx-power");
                result.TxRate = ParseValue(info, "tx-rate");
                result.RxRate = ParseValue(info, "rx-rate");
                
                // Parse band, channel-width, and wireless-protocol
                result.Band = ParseStringValue(info, "band");
                result.ChannelWidth = ParseStringValue(info, "channel-width");
                result.WirelessProtocol = ParseStringValue(info, "wireless-protocol");

                // Get wireless registration table for remote antenna info
                try
                {
                    var regTableCommand = _settings.CommandGetRegistrationTable
                        .Replace("{interface}", _settings.InterfaceName);
                    var regTable = await _sshClient.SendCommandAsync(regTableCommand, 8000);
                    
                    // Parse local CCQ from registration table (if not found in monitor)
                    if (!result.CCQ.HasValue)
                    {
                        result.CCQ = ParseValue(regTable, "ccq");
                    }
                    
                    // Parse remote antenna information from registration table
                    // Registration table shows connected remote antennas
                    if (!string.IsNullOrWhiteSpace(regTable))
                    {
                        // Get first connected remote antenna info
                        result.RemoteMacAddress = ParseStringValue(regTable, "mac-address");
                        result.RemoteIdentity = ParseStringValue(regTable, "identity");
                        result.RemoteSignalStrength = ParseValue(regTable, "signal-strength");
                        result.RemoteSignalToNoiseRatio = ParseValue(regTable, "signal-to-noise");
                        result.RemoteTxRate = ParseValue(regTable, "tx-rate");
                        result.RemoteRxRate = ParseValue(regTable, "rx-rate");
                        result.RemoteCCQ = ParseValue(regTable, "ccq");
                        result.RemoteTxSignalStrength = ParseValue(regTable, "tx-signal-strength");
                        result.RemoteRxSignalStrength = ParseValue(regTable, "rx-signal-strength");
                    }
                }
                catch
                {
                    // Registration table might not be available, continue
                }

                // Get monitoring data for real-time rates and better statistics
                try
                {
                    var monitorCommand = _settings.CommandMonitorInterface
                        .Replace("{interface}", _settings.InterfaceName);
                    var monitor = await _sshClient.SendCommandAsync(monitorCommand, 8000);
                    
                    // Parse noise-floor from monitor (more accurate)
                    var monitorNoiseFloor = ParseValue(monitor, "noise-floor");
                    if (monitorNoiseFloor.HasValue)
                    {
                        result.NoiseFloor = monitorNoiseFloor;
                        // Recalculate SNR if we have signal strength
                        if (result.SignalStrength.HasValue)
                        {
                            result.SignalToNoiseRatio = result.SignalStrength.Value - result.NoiseFloor.Value;
                        }
                    }
                    
                    // Parse overall-tx-ccq from monitor (more accurate than registration table)
                    var overallCcq = ParseValue(monitor, "overall-tx-ccq");
                    if (overallCcq.HasValue)
                    {
                        result.CCQ = overallCcq;
                    }
                    
                    // Parse wireless-protocol from monitor if not found in info
                    if (string.IsNullOrEmpty(result.WirelessProtocol))
                    {
                        result.WirelessProtocol = ParseStringValue(monitor, "wireless-protocol");
                    }
                    
                    // Parse channel to extract frequency if needed
                    var channel = ParseStringValue(monitor, "channel");
                    if (!string.IsNullOrEmpty(channel) && result.Frequency == 0)
                    {
                        // Channel format: "5225/5/an" - extract frequency (first number)
                        var parts = channel.Split('/');
                        if (parts.Length > 0 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var freq))
                        {
                            result.Frequency = freq;
                        }
                    }
                    
                    // Try to get actual rates from monitor
                    var txRate = ParseValue(monitor, "tx-rate");
                    var rxRate = ParseValue(monitor, "rx-rate");
                    
                    if (txRate.HasValue) result.TxRate = txRate;
                    if (rxRate.HasValue) result.RxRate = rxRate;
                    
                    // For download/upload, we use rx/tx rates as approximation
                    // Actual throughput would need interface statistics
                    result.DownloadSpeed = rxRate; // RX is download
                    result.UploadSpeed = txRate;   // TX is upload
                }
                catch
                {
                    // Monitor might fail, use values from interface info
                    result.DownloadSpeed = result.RxRate;
                    result.UploadSpeed = result.TxRate;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"خطا در جمع‌آوری اطلاعات: {ex.Message}";
            }

            return result;
        }

        private double? ParseValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            // RouterOS output format: "key: value" or "key=value"
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Skip command prompts and comment lines (;;;)
                if (trimmedLine.StartsWith(">") || trimmedLine.StartsWith("#") || trimmedLine.StartsWith("@") || trimmedLine.StartsWith(";;;"))
                    continue;

                // Try different separators
                string[] separators = { ":", "=" };
                foreach (var separator in separators)
                {
                    // Check if line contains the key with separator (case-insensitive)
                    var keyWithSeparator = key + separator;
                    var keyIndex = trimmedLine.IndexOf(keyWithSeparator, StringComparison.OrdinalIgnoreCase);
                    if (keyIndex >= 0)
                    {
                        var index = trimmedLine.IndexOf(separator, keyIndex);
                        if (index >= 0 && index < trimmedLine.Length - 1)
                        {
                            var valueStr = trimmedLine.Substring(index + 1).Trim();
                            
                            // Remove units: dBm, dB, %, MHz, GHz, Mbps, etc. (case-insensitive)
                            // Handle formats like "-116dBm", "85%", "20 dBm", etc.
                            var cleanValue = System.Text.RegularExpressions.Regex.Replace(
                                valueStr, 
                                @"\s*(dBm|dB|%|MHz|GHz|Mbps|Kbps|bps)\s*", 
                                "", 
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            // Extract the first number (may be negative)
                            var match = System.Text.RegularExpressions.Regex.Match(cleanValue, @"(-?\d+\.?\d*)");
                            if (match.Success)
                            {
                                if (double.TryParse(match.Value, System.Globalization.NumberStyles.Any, 
                                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                                {
                                    return value;
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        private string? ParseStringValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            // RouterOS output format: "key: value" or "key=value"
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Skip command prompts
                if (trimmedLine.Contains(">") || trimmedLine.Contains("#") || trimmedLine.Contains("@"))
                    continue;

                // Try different separators
                string[] separators = { ":", "=" };
                foreach (var separator in separators)
                {
                    if (trimmedLine.Contains(key + separator) || trimmedLine.StartsWith(key + separator))
                    {
                        var index = trimmedLine.IndexOf(separator);
                        if (index >= 0 && index < trimmedLine.Length - 1)
                        {
                            var valueStr = trimmedLine.Substring(index + 1).Trim();
                            
                            // Return the value as string (may contain spaces, e.g., "2ghz-b/g/n")
                            return valueStr;
                        }
                    }
                }
            }
            return null;
        }

        protected virtual void OnScanProgress(FrequencyScanResult result)
        {
            ScanProgress?.Invoke(this, result);
        }

        protected virtual void OnStatusUpdate(string message)
        {
            StatusUpdate?.Invoke(this, message);
        }

        protected virtual void OnProgressChanged(int progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }

        protected virtual void OnTerminalData(string data)
        {
            TerminalData?.Invoke(this, data);
        }
    }
}

