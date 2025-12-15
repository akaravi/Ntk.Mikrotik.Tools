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
                    // Try alternative command format - use CommandGetInterfaceInfo from settings
                    var altCommand = _settings.CommandGetInterfaceInfo
                        .Replace("{interface}", _settings.InterfaceName);
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
                    var setProtocolCommand = _settings.CommandSetWirelessProtocol
                        .Replace("{interface}", _settings.InterfaceName)
                        .Replace("{protocol}", wirelessProtocol);
                    OnStatusUpdate($"اجرای کامند: {setProtocolCommand}");
                    await _sshClient.SendCommandAsync(setProtocolCommand);
                }

                // Set channel-width if specified
                if (!string.IsNullOrEmpty(channelWidth))
                {
                    var setChannelWidthCommand = _settings.CommandSetChannelWidth
                        .Replace("{interface}", _settings.InterfaceName)
                        .Replace("{channelWidth}", channelWidth);
                    OnStatusUpdate($"اجرای کامند: {setChannelWidthCommand}");
                    await _sshClient.SendCommandAsync(setChannelWidthCommand);
                }

                OnStatusUpdate($"انتظار برای استیبل شدن ({_settings.StabilizationTimeMinutes} دقیقه)...");
                
                // Wait for stabilization with countdown timer
                var totalSeconds = (int)(_settings.StabilizationTimeMinutes * 60);
                var remainingSeconds = totalSeconds;
                
                while (remainingSeconds > 0)
                {
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                    {
                        OnStatusUpdate("⏹ تایمر متوقف شد.");
                        break;
                    }
                    
                    // Update status with countdown
                    var minutes = remainingSeconds / 60;
                    var seconds = remainingSeconds % 60;
                    if (minutes > 0)
                    {
                        OnStatusUpdate($"⏳ انتظار برای استیبل شدن: {minutes} دقیقه و {seconds} ثانیه باقی مانده...");
                    }
                    else
                    {
                        OnStatusUpdate($"⏳ انتظار برای استیبل شدن: {seconds} ثانیه باقی مانده...");
                    }
                    
                    // Wait 1 second
                    await Task.Delay(TimeSpan.FromSeconds(1), _cancellationTokenSource?.Token ?? CancellationToken.None);
                    
                    remainingSeconds--;
                }
                
                if (remainingSeconds == 0)
                {
                    OnStatusUpdate("✓ زمان انتظار به پایان رسید.");
                }

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
                    // Try alternative command format - use CommandGetInterfaceInfo from settings
                    var altCommand = _settings.CommandGetInterfaceInfo
                        .Replace("{interface}", _settings.InterfaceName);
                    OnStatusUpdate($"تلاش با فرمت جایگزین: {altCommand}");
                    info = await _sshClient.SendCommandAsync(altCommand, 8000);
                }
                
                // If still empty, try with simpler print command - use CommandGetFrequency format
                if (string.IsNullOrWhiteSpace(info))
                {
                    OnStatusUpdate("تلاش با کامند print ساده‌تر...");
                    var printCommand = _settings.CommandGetFrequency
                        .Replace("{interface}", _settings.InterfaceName)
                        .Replace(" value-name=frequency", ""); // Remove value-name restriction for full output
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
                    OnStatusUpdate($"در حال دریافت اطلاعات registration table...");
                    OnTerminalData($"Command: {regTableCommand}");
                    var regTable = await _sshClient.SendCommandAsync(regTableCommand, 8000);
                    
                    if (string.IsNullOrWhiteSpace(regTable))
                    {
                        OnStatusUpdate("⚠️ هشدار: registration table خالی است. ممکن است آنتن remote متصل نباشد.");
                        OnTerminalData("Response: (empty)");
                    }
                    else
                    {
                        OnStatusUpdate($"✓ Registration table دریافت شد ({regTable.Length} کاراکتر)");
                        OnTerminalData($"Registration Table Response:\n{regTable}");
                        
                        // Parse remote antenna information from registration table
                        // Registration table may contain multiple entries, we need to parse each entry separately
                        var remoteAntennaInfo = ParseRegistrationTableEntry(regTable);
                        
                        if (remoteAntennaInfo != null)
                        {
                            result.RemoteMacAddress = remoteAntennaInfo.MacAddress;
                            result.RemoteIdentity = remoteAntennaInfo.Identity;
                            result.RemoteSignalStrength = remoteAntennaInfo.SignalStrength;
                            result.RemoteSignalToNoiseRatio = remoteAntennaInfo.SignalToNoiseRatio;
                            result.RemoteTxRate = remoteAntennaInfo.TxRate;
                            result.RemoteRxRate = remoteAntennaInfo.RxRate;
                            result.RemoteCCQ = remoteAntennaInfo.CCQ;
                            result.RemoteTxSignalStrength = remoteAntennaInfo.TxSignalStrength;
                            result.RemoteRxSignalStrength = remoteAntennaInfo.RxSignalStrength;
                            
                            var infoSummary = $"MAC={remoteAntennaInfo.MacAddress ?? "N/A"}, " +
                                            $"Identity={remoteAntennaInfo.Identity ?? "N/A"}, " +
                                            $"Signal={remoteAntennaInfo.SignalStrength?.ToString("F1") ?? "N/A"}dBm, " +
                                            $"SNR={remoteAntennaInfo.SignalToNoiseRatio?.ToString("F1") ?? "N/A"}dB, " +
                                            $"TxRate={remoteAntennaInfo.TxRate?.ToString("F1") ?? "N/A"}Mbps, " +
                                            $"RxRate={remoteAntennaInfo.RxRate?.ToString("F1") ?? "N/A"}Mbps, " +
                                            $"CCQ={remoteAntennaInfo.CCQ?.ToString("F1") ?? "N/A"}%";
                            
                            OnStatusUpdate($"✓ اطلاعات remote antenna دریافت شد: {infoSummary}");
                            OnTerminalData($"Parsed Remote Antenna Info: {infoSummary}");
                        }
                        else
                        {
                            OnStatusUpdate("⚠️ هشدار: نتوانستیم اطلاعات remote antenna را از registration table استخراج کنیم.");
                            OnTerminalData("⚠️ Warning: Could not parse remote antenna info from registration table.");
                        }
                        
                        // Parse local CCQ from registration table (if not found in monitor)
                        if (!result.CCQ.HasValue)
                        {
                            result.CCQ = ParseValue(regTable, "ccq");
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnStatusUpdate($"⚠️ خطا در دریافت registration table: {ex.Message}");
                    OnTerminalData($"Error getting registration table: {ex.Message}");
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
                    
                    // Parse wireless-protocol from monitor (more accurate than info)
                    var monitorProtocol = ParseStringValue(monitor, "wireless-protocol");
                    if (!string.IsNullOrEmpty(monitorProtocol))
                    {
                        result.WirelessProtocol = monitorProtocol;
                    }
                    
                    // Parse channel-width from monitor (more accurate than info)
                    var monitorChannelWidth = ParseStringValue(monitor, "channel-width");
                    if (!string.IsNullOrEmpty(monitorChannelWidth))
                    {
                        result.ChannelWidth = monitorChannelWidth;
                    }
                    
                    // Parse band from monitor if available
                    var monitorBand = ParseStringValue(monitor, "band");
                    if (!string.IsNullOrEmpty(monitorBand))
                    {
                        result.Band = monitorBand;
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

            // RouterOS output format can be:
            // 1. "key: value" (single line)
            // 2. "key=value" (single line) - MOST COMMON FORMAT
            // 3. "key=value1 key2=value2 key3=value3" (multiple key-value pairs in one line, space-separated)
            // 4. "some-text key=value more-text" (key-value embedded in text)
            // Example: "band=5ghz-onlyn channel-width=20/40mhz-eC wireless-protocol=nv2"
            
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Skip comment lines and command prompts
                if (trimmedLine.StartsWith(";;;") || trimmedLine.StartsWith(">") || 
                    trimmedLine.StartsWith("#") || trimmedLine.StartsWith("@") ||
                    trimmedLine.StartsWith("Flags:") || trimmedLine.StartsWith(" 0") || trimmedLine.StartsWith(" 1"))
                    continue;

                // RouterOS typically uses "key=value" format, space-separated
                // Try to find "key=value" pattern (most common)
                var keyEqualPattern = key + "=";
                var keyEqualIndex = trimmedLine.IndexOf(keyEqualPattern, StringComparison.OrdinalIgnoreCase);
                
                if (keyEqualIndex >= 0)
                {
                    // Check if this is really our key (not part of another word)
                    // Key should be at start of line, after space, or after another key-value separator
                    var beforeKey = keyEqualIndex > 0 ? trimmedLine[keyEqualIndex - 1] : ' ';
                    if (char.IsLetterOrDigit(beforeKey) && beforeKey != ' ' && beforeKey != '\t')
                    {
                        continue; // This is part of another word, skip
                    }
                    
                    // Extract value after "key="
                    var valueStart = keyEqualIndex + keyEqualPattern.Length;
                    if (valueStart < trimmedLine.Length)
                    {
                        var remaining = trimmedLine.Substring(valueStart);
                        
                        // Value ends at:
                        // 1. Next space followed by a key= pattern
                        // 2. End of line
                        // 3. Quote (if value is quoted)
                        
                        var valueEnd = remaining.Length;
                        var inQuotes = false;
                        var quoteChar = '\0';
                        
                        for (int i = 0; i < remaining.Length; i++)
                        {
                            var ch = remaining[i];
                            
                            // Handle quoted values
                            if ((ch == '"' || ch == '\'') && !inQuotes)
                            {
                                inQuotes = true;
                                quoteChar = ch;
                                continue;
                            }
                            
                            if (inQuotes && ch == quoteChar)
                            {
                                inQuotes = false;
                                valueEnd = i + 1;
                                break;
                            }
                            
                            if (!inQuotes)
                            {
                                // If we hit a space, check if next token is a key= pattern
                                if (char.IsWhiteSpace(ch))
                                {
                                    var afterSpace = remaining.Substring(i + 1).TrimStart();
                                    // Check if next token looks like "key="
                                    if (afterSpace.Length > 0)
                                    {
                                        var nextSpaceIndex = afterSpace.IndexOfAny(new[] { ' ', '=', ':' });
                                        if (nextSpaceIndex > 0 && afterSpace[nextSpaceIndex] == '=')
                                        {
                                            // Next token is a key=value pair, so our value ends here
                                            valueEnd = i;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        
                        var valueStr = remaining.Substring(0, valueEnd).Trim();
                        
                        // Remove quotes if present
                        if (valueStr.Length >= 2 && 
                            ((valueStr[0] == '"' && valueStr[valueStr.Length - 1] == '"') ||
                             (valueStr[0] == '\'' && valueStr[valueStr.Length - 1] == '\'')))
                        {
                            valueStr = valueStr.Substring(1, valueStr.Length - 2);
                        }
                        
                        // Return the clean value
                        if (!string.IsNullOrEmpty(valueStr))
                        {
                            return valueStr;
                        }
                    }
                }
                
                // Fallback: Try "key: value" format (less common in RouterOS)
                var keyColonPattern = key + ":";
                var keyColonIndex = trimmedLine.IndexOf(keyColonPattern, StringComparison.OrdinalIgnoreCase);
                
                if (keyColonIndex >= 0)
                {
                    var beforeKey = keyColonIndex > 0 ? trimmedLine[keyColonIndex - 1] : ' ';
                    if (char.IsLetterOrDigit(beforeKey) && beforeKey != ' ' && beforeKey != '\t')
                    {
                        continue;
                    }
                    
                    var valueStart = keyColonIndex + keyColonPattern.Length;
                    if (valueStart < trimmedLine.Length)
                    {
                        var remaining = trimmedLine.Substring(valueStart).Trim();
                        
                        // For colon format, value typically ends at next space or end of line
                        var spaceIndex = remaining.IndexOf(' ');
                        var valueStr = spaceIndex > 0 ? remaining.Substring(0, spaceIndex) : remaining;
                        
                        // Remove quotes if present
                        if (valueStr.Length >= 2 && 
                            ((valueStr[0] == '"' && valueStr[valueStr.Length - 1] == '"') ||
                             (valueStr[0] == '\'' && valueStr[valueStr.Length - 1] == '\'')))
                        {
                            valueStr = valueStr.Substring(1, valueStr.Length - 2);
                        }
                        
                        if (!string.IsNullOrEmpty(valueStr))
                        {
                            return valueStr;
                        }
                    }
                }
            }
            return null;
        }

        // Helper class for remote antenna information
        private class RemoteAntennaInfo
        {
            public string? MacAddress { get; set; }
            public string? Identity { get; set; }
            public double? SignalStrength { get; set; }
            public double? SignalToNoiseRatio { get; set; }
            public double? TxRate { get; set; }
            public double? RxRate { get; set; }
            public double? CCQ { get; set; }
            public double? TxSignalStrength { get; set; }
            public double? RxSignalStrength { get; set; }
        }

        /// <summary>
        /// Parses registration table output to extract remote antenna information.
        /// Registration table may contain multiple entries, we parse the first valid entry.
        /// RouterOS registration table can be in different formats:
        /// 1. Multi-line format: "key: value" pairs (one per line)
        /// 2. Single-line format: "key=value key2=value2" (all in one line)
        /// 3. Tabular format: space-separated columns
        /// </summary>
        private RemoteAntennaInfo? ParseRegistrationTableEntry(string regTable)
        {
            if (string.IsNullOrWhiteSpace(regTable))
                return null;

            var info = new RemoteAntennaInfo();
            var lines = regTable.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            bool foundAnyData = false;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Skip comment lines and command prompts
                if (trimmedLine.StartsWith(";;;") || trimmedLine.StartsWith(">") || 
                    trimmedLine.StartsWith("#") || trimmedLine.StartsWith("@"))
                    continue;

                // Skip header lines (Flags, Interface, etc.)
                if (trimmedLine.StartsWith("Flags") || trimmedLine.StartsWith("Interface") ||
                    trimmedLine.StartsWith("MAC") || trimmedLine.StartsWith("Identity"))
                {
                    continue;
                }

                // If line starts with a number (like "0" or "1"), it might be entry number, skip it
                if (trimmedLine.Length > 0 && char.IsDigit(trimmedLine[0]) && trimmedLine.Length < 5)
                {
                    continue;
                }

                // Try to parse key-value pairs with different separators
                // First, try to split by spaces if it looks like tabular format
                var parts = trimmedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                
                // If we have many parts, it might be tabular format - try to match by position or key-value
                if (parts.Length > 3)
                {
                    // Try to find key-value pairs within the line
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        var key = parts[i].ToLower();
                        var value = parts[i + 1];
                        
                        ParseKeyValuePair(key, value, info, ref foundAnyData);
                    }
                }
                else
                {
                    // Standard key-value format with ":" or "="
                    string[] separators = { ":", "=" };
                    foreach (var separator in separators)
                    {
                        var sepIndex = trimmedLine.IndexOf(separator);
                        if (sepIndex > 0)
                        {
                            var key = trimmedLine.Substring(0, sepIndex).Trim().ToLower();
                            var value = trimmedLine.Substring(sepIndex + separator.Length).Trim();
                            ParseKeyValuePair(key, value, info, ref foundAnyData);
                        }
                    }
                }
            }

            // Return info only if we found at least some data
            return foundAnyData ? info : null;
        }

        /// <summary>
        /// Helper method to parse a key-value pair and populate RemoteAntennaInfo
        /// </summary>
        private void ParseKeyValuePair(string key, string value, RemoteAntennaInfo info, ref bool foundAnyData)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return;

            key = key.Trim().ToLower();
            value = value.Trim();

            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value.Substring(1, value.Length - 2);

            // Parse mac-address
            if (key.Contains("mac-address") || key == "mac")
            {
                info.MacAddress = value;
                foundAnyData = true;
            }
            // Parse identity
            else if (key.Contains("identity"))
            {
                info.Identity = value;
                foundAnyData = true;
            }
            // Parse signal-strength
            else if (key.Contains("signal-strength") || (key.Contains("signal") && !key.Contains("noise") && !key.Contains("tx") && !key.Contains("rx")))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.SignalStrength = numValue;
                    foundAnyData = true;
                }
            }
            // Parse signal-to-noise
            else if (key.Contains("signal-to-noise") || key.Contains("snr"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.SignalToNoiseRatio = numValue;
                    foundAnyData = true;
                }
            }
            // Parse tx-rate
            else if (key.Contains("tx-rate"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.TxRate = numValue;
                    foundAnyData = true;
                }
            }
            // Parse rx-rate
            else if (key.Contains("rx-rate"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.RxRate = numValue;
                    foundAnyData = true;
                }
            }
            // Parse ccq
            else if (key.Contains("ccq"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.CCQ = numValue;
                    foundAnyData = true;
                }
            }
            // Parse tx-signal-strength
            else if (key.Contains("tx-signal-strength") || (key.Contains("tx-signal") && !key.Contains("rx")))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.TxSignalStrength = numValue;
                    foundAnyData = true;
                }
            }
            // Parse rx-signal-strength
            else if (key.Contains("rx-signal-strength") || (key.Contains("rx-signal") && !key.Contains("tx")))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.RxSignalStrength = numValue;
                    foundAnyData = true;
                }
            }
        }

        /// <summary>
        /// Parses a numeric value from a string, handling units like "dBm", "Mbps", "%"
        /// </summary>
        private double? ParseNumericValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            // Remove common units
            value = value.Replace("dBm", "").Replace("dB", "").Replace("Mbps", "").Replace("Mbps", "").Replace("%", "").Trim();

            // Try to parse as double
            if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            return null;
        }

        /// <summary>
        /// Extracts value from a line after a separator (for string values)
        /// </summary>
        private string? ExtractValue(string line, string separator)
        {
            var index = line.IndexOf(separator);
            if (index >= 0 && index < line.Length - 1)
            {
                var valueStr = line.Substring(index + separator.Length).Trim();
                // Remove quotes if present
                if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                    valueStr = valueStr.Substring(1, valueStr.Length - 2);
                return valueStr;
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

