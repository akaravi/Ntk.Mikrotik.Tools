using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using Ntk.Mikrotik.Tools.Models;

namespace Ntk.Mikrotik.Tools.Services
{
    /// <summary>
    /// کلاس اصلی برای اسکن فرکانس‌های مختلف در روترهای میکروتیک
    /// این کلاس با استفاده از SSH به روتر متصل می‌شود و فرکانس‌های مختلف را تست می‌کند
    /// </summary>
    public class FrequencyScanner
    {
        private readonly MikroTikSshClient _sshClient;
        private readonly ScanSettings _settings;
        private CancellationTokenSource? _cancellationTokenSource;
        private JsonDataService? _jsonDataService;

        /// <summary>
        /// رویداد برای اطلاع از پیشرفت اسکن - هر بار که یک فرکانس اسکن می‌شود این رویداد فراخوانی می‌شود
        /// </summary>
        public event EventHandler<FrequencyScanResult>? ScanProgress;
        
        /// <summary>
        /// رویداد برای به‌روزرسانی وضعیت - برای نمایش پیام‌های وضعیت در رابط کاربری
        /// </summary>
        public event EventHandler<string>? StatusUpdate;
        
        /// <summary>
        /// رویداد برای نمایش داده‌های ترمینال - برای نمایش خروجی کامندهای RouterOS
        /// </summary>
        public event EventHandler<string>? TerminalData;

        /// <summary>
        /// سازنده کلاس FrequencyScanner
        /// </summary>
        /// <param name="settings">تنظیمات اسکن شامل فرکانس‌ها، پروتکل‌ها و کامندهای RouterOS</param>
        /// <param name="existingSshClient">کلاینت SSH موجود (اختیاری) - اگر null باشد یک کلاینت جدید ایجاد می‌شود</param>
        /// <param name="jsonDataService">سرویس ذخیره‌سازی JSON (اختیاری) - برای ذخیره نتایج اسکن</param>
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

        /// <summary>
        /// رویداد برای به‌روزرسانی پیشرفت اسکن - درصد پیشرفت (0-100)
        /// </summary>
        public event EventHandler<int>? ProgressChanged;

        /// <summary>
        /// شروع اسکن فرکانس‌های مختلف
        /// این متد تمام ترکیبات فرکانس، پروتکل و channel width را تست می‌کند
        /// </summary>
        /// <param name="cancellationToken">توکن لغو برای امکان توقف اسکن</param>
        /// <returns>لیست نتایج اسکن برای هر فرکانس</returns>
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
                try
                {
                    var errorMsg = $"خطا در اسکن: {ex.Message}";
                    if (ex.InnerException != null)
                    {
                        errorMsg += $"\nخطای داخلی: {ex.InnerException.Message}";
                    }
                    errorMsg += $"\n\nنوع خطا: {ex.GetType().Name}\n\n" +
                               $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                    OnStatusUpdate($"خطا: {ex.Message}");
                    OnTerminalData($"[ERROR] {errorMsg}");
                }
                catch
                {
                    // اگر حتی لاگ کردن هم خطا داد، حداقل یک پیام ساده نمایش بده
                    OnStatusUpdate("خطا در اسکن");
                }
            }
            finally
            {
                try
                {
                    _sshClient?.Disconnect();
                }
                catch
                {
                    // Ignore disconnect errors
                }
            }

            return results;
        }

        /// <summary>
        /// توقف اسکن در حال انجام
        /// این متد با لغو کردن CancellationToken اسکن را متوقف می‌کند
        /// </summary>
        public void StopScan()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// تولید لیست فرکانس‌ها بر اساس محدوده و گام
        /// </summary>
        /// <param name="start">فرکانس شروع (MHz)</param>
        /// <param name="end">فرکانس پایان (MHz)</param>
        /// <param name="step">گام فرکانس (MHz)</param>
        /// <returns>لیست فرکانس‌ها با دقت 2 رقم اعشار</returns>
        private List<double> GenerateFrequencyList(double start, double end, double step)
        {
            var frequencies = new List<double>();
            for (double freq = start; freq <= end; freq += step)
            {
                frequencies.Add(Math.Round(freq, 0));
            }
            return frequencies;
        }

        /// <summary>
        /// پارس کردن لیست مقادیر از یک رشته
        /// مقادیر می‌توانند با کاما یا خط جدید جدا شده باشند
        /// </summary>
        /// <param name="input">رشته ورودی شامل مقادیر جدا شده با کاما یا خط جدید</param>
        /// <returns>لیست مقادیر پارس شده</returns>
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

        /// <summary>
        /// دریافت وضعیت فعلی اینترفیس و آنتن‌های متصل
        /// این متد اطلاعات کامل از اینترفیس فعلی و آنتن‌های رجیستر شده را دریافت می‌کند
        /// </summary>
        /// <param name="frequency">فرکانس مورد نظر (اختیاری) - اگر null باشد، فرکانس فعلی از اینترفیس خوانده می‌شود</param>
        /// <param name="status">وضعیت نتیجه (پیش‌فرض: "base")</param>
        /// <returns>نتیجه اسکن شامل تمام اطلاعات اینترفیس و آنتن‌های remote</returns>
        public async Task<FrequencyScanResult> GetCurrentStatusAsync(double? frequency = null, string status = "base")
        {
            var result = new FrequencyScanResult
            {
                Frequency = frequency.HasValue ? Math.Round(frequency.Value, 0) : 0, // Will be set from current interface if not provided
                ScanTime = DateTime.Now,
                Status = status
            };

            try
            {
                OnStatusUpdate("در حال دریافت اطلاعات اینترفیس فعلی...");
                OnTerminalData("[GetCurrentStatus] شروع دریافت اطلاعات پایه...");
                
                // If frequency is not provided, read it from interface
                if (!frequency.HasValue)
                {
                    // Get current frequency - use print command to get interface info
                    var getInfoCommand = ReplaceCommandPlaceholders(_settings.CommandGetInterfaceInfo);
                    
                    // Try alternative command if primary fails
                    var fallbackCommand = ReplaceCommandPlaceholders(_settings.CommandGetFrequency)
                        .Replace(" value-name=frequency", "");
                    
                    var interfaceInfo = await SendCommandWithRetryAsync(
                        getInfoCommand, 
                        new List<string> { fallbackCommand }, 
                        8000, 
                        "GetCurrentStatus");
                    
                    // Parse frequency from the full interface info
                    var currentFreq = ParseValue(interfaceInfo, "frequency");
                    if (currentFreq.HasValue)
                    {
                        // Round frequency to integer (no decimal places)
                        result.Frequency = Math.Round(currentFreq.Value, 0);
                        OnTerminalData($"[GetCurrentStatus] فرکانس فعلی: {result.Frequency} MHz");
                    }
                }
                else
                {
                    OnTerminalData($"[GetCurrentStatus] استفاده از فرکانس ارائه شده: {result.Frequency} MHz");
                }

                // Collect all statistics (this will also call CommandGetRegistrationTable)
                OnTerminalData("[GetCurrentStatus] شروع جمع‌آوری آمار کامل (شامل Registration Table)...");
                result = await CollectStatisticsAsync(result);
                result.Status = status;
                OnTerminalData("[GetCurrentStatus] جمع‌آوری آمار کامل شد.");

                // Perform ping test
                OnTerminalData("[GetCurrentStatus] شروع تست پینگ...");
                result = await PerformPingTestAsync(result);
                OnTerminalData("[GetCurrentStatus] تست پینگ انجام شد.");

                OnStatusUpdate($"وضعیت فعلی دریافت شد - فرکانس: {result.Frequency} MHz");
            }
            catch (Exception ex)
            {
                try
                {
                    var errorMsg = $"خطا در دریافت وضعیت فعلی: {ex.Message}";
                    if (ex.InnerException != null)
                    {
                        errorMsg += $"\nخطای داخلی: {ex.InnerException.Message}";
                    }
                    errorMsg += $"\n\nنوع خطا: {ex.GetType().Name}\n\n" +
                               $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                    OnTerminalData($"[ERROR] {errorMsg}");
                    result.ErrorMessage = $"خطا در دریافت وضعیت فعلی: {ex.Message}";
                    result.Status = "خطا";
                }
                catch
                {
                    // اگر حتی لاگ کردن هم خطا داد، حداقل یک پیام ساده نمایش بده
                    result.ErrorMessage = "خطا در دریافت وضعیت فعلی";
                    result.Status = "خطا";
                }
            }

            return result;
        }

        /// <summary>
        /// اسکن یک فرکانس خاص
        /// این متد فرکانس را تنظیم می‌کند، منتظر استیبل شدن می‌ماند و سپس آمار را جمع‌آوری می‌کند
        /// </summary>
        /// <param name="frequency">فرکانس مورد نظر برای تست (MHz)</param>
        /// <param name="wirelessProtocol">پروتکل wireless (اختیاری) - اگر null باشد از تنظیمات فعلی استفاده می‌شود</param>
        /// <param name="channelWidth">عرض کانال (اختیاری) - اگر null باشد از تنظیمات فعلی استفاده می‌شود</param>
        /// <returns>نتیجه اسکن برای این فرکانس</returns>
        private async Task<FrequencyScanResult> ScanFrequencyAsync(double frequency, string? wirelessProtocol = null, string? channelWidth = null)
        {
            // Round frequency to integer (no decimal places)
            var frequencyInt = Math.Round(frequency, 0);
            var result = new FrequencyScanResult
            {
                Frequency = frequencyInt,
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

                // Set frequency (round to integer, no decimal places)
                var setFreqCommand = ReplaceCommandPlaceholders(_settings.CommandSetFrequency, frequency);
                
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
                    var setProtocolCommand = ReplaceCommandPlaceholders(_settings.CommandSetWirelessProtocol, protocol: wirelessProtocol);
                    OnStatusUpdate($"اجرای کامند: {setProtocolCommand}");
                    await _sshClient.SendCommandAsync(setProtocolCommand);
                }

                // Set channel-width if specified
                if (!string.IsNullOrEmpty(channelWidth))
                {
                    var setChannelWidthCommand = ReplaceCommandPlaceholders(_settings.CommandSetChannelWidth, channelWidth: channelWidth);
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
                OnTerminalData($"[ScanFrequency] شروع جمع‌آوری آمار برای فرکانس {frequency} MHz...");

                // Use GetCurrentStatusAsync to collect statistics and perform ping test (reuse code)
                result = await GetCurrentStatusAsync(frequency, "موفق");
                
                OnTerminalData($"[ScanFrequency] جمع‌آوری آمار و تست پینگ برای فرکانس {frequency} MHz تکمیل شد.");
                OnStatusUpdate($"تکمیل - فرکانس: {frequency} MHz, SNR: {result.SignalToNoiseRatio} dB");
            }
            catch (Exception ex)
            {
                try
                {
                    var errorMsg = $"خطا در اسکن فرکانس: {ex.Message}";
                    if (ex.InnerException != null)
                    {
                        errorMsg += $"\nخطای داخلی: {ex.InnerException.Message}";
                    }
                    errorMsg += $"\n\nنوع خطا: {ex.GetType().Name}\n\n" +
                               $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                    OnTerminalData($"[ERROR] {errorMsg}");
                    result.ErrorMessage = ex.Message;
                    result.Status = "خطا";
                }
                catch
                {
                    // اگر حتی لاگ کردن هم خطا داد، حداقل یک پیام ساده نمایش بده
                    result.ErrorMessage = "خطا در اسکن فرکانس";
                    result.Status = "خطا";
                }
            }

            return result;
        }

        /// <summary>
        /// جمع‌آوری آمار کامل از اینترفیس و آنتن‌های remote
        /// این متد اطلاعات را از سه منبع دریافت می‌کند:
        /// 1. Interface Info - اطلاعات پایه اینترفیس
        /// 2. Registration Table - اطلاعات آنتن‌های remote رجیستر شده
        /// 3. Monitor - اطلاعات real-time و دقیق‌تر
        /// </summary>
        /// <param name="result">نتیجه اسکن که باید با اطلاعات جمع‌آوری شده پر شود</param>
        /// <returns>نتیجه اسکن با اطلاعات کامل</returns>
        private async Task<FrequencyScanResult> CollectStatisticsAsync(FrequencyScanResult result)
        {
            try
            {
                // Get wireless interface info (main source of data) with retry
                var infoCommand = ReplaceCommandPlaceholders(_settings.CommandGetInterfaceInfo);
                
                // Try alternative commands if primary fails
                var fallbackCommands = new List<string>
                {
                    ReplaceCommandPlaceholders(_settings.CommandGetFrequency)
                        .Replace(" value-name=frequency", "") // Remove value-name restriction for full output
                };
                
                var info = await SendCommandWithRetryAsync(infoCommand, fallbackCommands, 8000, "CollectStatistics");

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
                OnTerminalData($"[CollectStatistics] ========================================");
                OnTerminalData($"[CollectStatistics] شروع دریافت Registration Table");
                OnTerminalData($"[CollectStatistics] ========================================");
                try
                {
                    OnStatusUpdate($"در حال دریافت اطلاعات registration table...");
                    OnTerminalData($"[Registration Table] ========================================");
                    OnTerminalData($"[Registration Table] شروع دریافت Registration Table");
                    OnTerminalData($"[Registration Table] Command Template: {_settings.CommandGetRegistrationTable}");
                    OnTerminalData($"[Registration Table] Interface Name: {_settings.InterfaceName}");
                    
                    var regTableCommand = ReplaceCommandPlaceholders(_settings.CommandGetRegistrationTable);
                    
                    OnTerminalData($"[Registration Table] کامند نهایی: {regTableCommand}");
                    OnTerminalData($"[Registration Table] ========================================");
                    
                    var regTable = await _sshClient.SendCommandAsync(regTableCommand, 8000);
                    
                    OnTerminalData($"[Registration Table] کامند اجرا شد.");
                    OnTerminalData($"[Registration Table] طول پاسخ: {regTable?.Length ?? 0} کاراکتر");
                    OnTerminalData($"[Registration Table] پاسخ خالی است: {string.IsNullOrWhiteSpace(regTable)}");
                    
                    if (string.IsNullOrWhiteSpace(regTable))
                    {
                        OnStatusUpdate("⚠️ هشدار: registration table خالی است. ممکن است آنتن remote متصل نباشد.");
                        OnTerminalData($"[Registration Table] ⚠️ هشدار: پاسخ خالی است!");
                        OnTerminalData($"[Registration Table] Response: (empty or null)");
                    }
                    else
                    {
                        OnStatusUpdate($"✓ Registration table دریافت شد ({regTable.Length} کاراکتر)");
                        OnTerminalData($"[Registration Table] ========================================");
                        OnTerminalData($"[Registration Table] پاسخ کامل Registration Table:");
                        OnTerminalData($"[Registration Table] ========================================");
                        OnTerminalData(regTable);
                        OnTerminalData($"[Registration Table] ========================================");
                        OnTerminalData($"[Registration Table] پایان پاسخ Registration Table");
                        OnTerminalData($"[Registration Table] ========================================");
                        
                        // Parse remote antenna information from registration table
                        // Registration table may contain multiple entries, we need to parse each entry separately
                        OnTerminalData($"[Registration Table] شروع پارس کردن اطلاعات...");
                        var remoteAntennaInfo = ParseRegistrationTableEntry(regTable);
                        OnTerminalData($"[Registration Table] پارس کردن انجام شد. foundAnyData: {remoteAntennaInfo != null}");
                        
                        if (remoteAntennaInfo != null)
                        {
                            result.RemoteMacAddress = remoteAntennaInfo.MacAddress;
                            result.RemoteIdentity = remoteAntennaInfo.Identity;
                            result.RemoteRadioName = remoteAntennaInfo.RadioName;
                            result.RemoteSignalStrength = remoteAntennaInfo.SignalStrength;
                            result.RemoteSignalToNoiseRatio = remoteAntennaInfo.SignalToNoiseRatio;
                            result.RemoteTxRate = remoteAntennaInfo.TxRate;
                            result.RemoteRxRate = remoteAntennaInfo.RxRate;
                            result.RemoteCCQ = remoteAntennaInfo.CCQ;
                            result.RemoteTxCCQ = remoteAntennaInfo.TxCCQ;
                            result.RemoteRxCCQ = remoteAntennaInfo.RxCCQ;
                            result.RemoteTxSignalStrength = remoteAntennaInfo.TxSignalStrength;
                            result.RemoteRxSignalStrength = remoteAntennaInfo.RxSignalStrength;
                            result.RemoteSignalStrengthCh0 = remoteAntennaInfo.SignalStrengthCh0;
                            result.RemoteSignalStrengthCh1 = remoteAntennaInfo.SignalStrengthCh1;
                            result.RemoteTxSignalStrengthCh0 = remoteAntennaInfo.TxSignalStrengthCh0;
                            result.RemoteTxSignalStrengthCh1 = remoteAntennaInfo.TxSignalStrengthCh1;
                            result.RemotePThroughput = remoteAntennaInfo.PThroughput;
                            result.RemotePacketsRx = remoteAntennaInfo.PacketsRx;
                            result.RemotePacketsTx = remoteAntennaInfo.PacketsTx;
                            result.RemoteBytesRx = remoteAntennaInfo.BytesRx;
                            result.RemoteBytesTx = remoteAntennaInfo.BytesTx;
                            result.RemoteFramesRx = remoteAntennaInfo.FramesRx;
                            result.RemoteFramesTx = remoteAntennaInfo.FramesTx;
                            result.RemoteFrameBytesRx = remoteAntennaInfo.FrameBytesRx;
                            result.RemoteFrameBytesTx = remoteAntennaInfo.FrameBytesTx;
                            result.RemoteHwFramesRx = remoteAntennaInfo.HwFramesRx;
                            result.RemoteHwFramesTx = remoteAntennaInfo.HwFramesTx;
                            result.RemoteHwFrameBytesRx = remoteAntennaInfo.HwFrameBytesRx;
                            result.RemoteHwFrameBytesTx = remoteAntennaInfo.HwFrameBytesTx;
                            result.RemoteTxFramesTimedOut = remoteAntennaInfo.TxFramesTimedOut;
                            result.RemoteUptime = remoteAntennaInfo.Uptime;
                            result.RemoteLastActivity = remoteAntennaInfo.LastActivity;
                            result.RemoteNstreme = remoteAntennaInfo.Nstreme;
                            result.RemoteNstremePlus = remoteAntennaInfo.NstremePlus;
                            result.RemoteFramingMode = remoteAntennaInfo.FramingMode;
                            result.RemoteRouterOsVersion = remoteAntennaInfo.RouterOsVersion;
                            result.RemoteLastIp = remoteAntennaInfo.LastIp;
                            result.Remote8021xPortEnabled = remoteAntennaInfo.Port8021xEnabled;
                            result.RemoteAuthenticationType = remoteAntennaInfo.AuthenticationType;
                            result.RemoteEncryption = remoteAntennaInfo.Encryption;
                            result.RemoteGroupEncryption = remoteAntennaInfo.GroupEncryption;
                            result.RemoteManagementProtection = remoteAntennaInfo.ManagementProtection;
                            result.RemoteCompression = remoteAntennaInfo.Compression;
                            result.RemoteWmmEnabled = remoteAntennaInfo.WmmEnabled;
                            result.RemoteTxRateSet = remoteAntennaInfo.TxRateSet;
                            
                            var infoSummary = $"MAC={remoteAntennaInfo.MacAddress ?? "N/A"}, " +
                                            $"Identity={remoteAntennaInfo.Identity ?? "N/A"}, " +
                                            $"Signal={remoteAntennaInfo.SignalStrength?.ToString("F1") ?? "N/A"}dBm, " +
                                            $"SNR={remoteAntennaInfo.SignalToNoiseRatio?.ToString("F1") ?? "N/A"}dB, " +
                                            $"TxRate={remoteAntennaInfo.TxRate?.ToString("F1") ?? "N/A"}Mbps, " +
                                            $"RxRate={remoteAntennaInfo.RxRate?.ToString("F1") ?? "N/A"}Mbps, " +
                                            $"CCQ={remoteAntennaInfo.CCQ?.ToString("F1") ?? "N/A"}%";
                            
                            if (remoteAntennaInfo.TxCCQ.HasValue || remoteAntennaInfo.RxCCQ.HasValue)
                            {
                                infoSummary += $", TxCCQ={remoteAntennaInfo.TxCCQ?.ToString("F1") ?? "N/A"}%, RxCCQ={remoteAntennaInfo.RxCCQ?.ToString("F1") ?? "N/A"}%";
                            }
                            
                            if (remoteAntennaInfo.PThroughput.HasValue)
                            {
                                infoSummary += $", P-Throughput={remoteAntennaInfo.PThroughput?.ToString("F1") ?? "N/A"}";
                            }
                            
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
                    OnTerminalData($"[Registration Table] ========================================");
                    OnTerminalData($"[Registration Table] ⚠️ خطا در دریافت Registration Table!");
                    OnTerminalData($"[Registration Table] Exception Type: {ex.GetType().Name}");
                    OnTerminalData($"[Registration Table] Exception Message: {ex.Message}");
                    OnTerminalData($"[Registration Table] Stack Trace: {ex.StackTrace}");
                    OnTerminalData($"[Registration Table] ========================================");
                    // Registration table might not be available, continue
                }
                finally
                {
                    OnTerminalData($"[CollectStatistics] ========================================");
                    OnTerminalData($"[CollectStatistics] پایان دریافت Registration Table");
                    OnTerminalData($"[CollectStatistics] ========================================");
                }

                // Get monitoring data for real-time rates and better statistics
                try
                {
                    var monitorCommand = ReplaceCommandPlaceholders(_settings.CommandMonitorInterface);
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

        /// <summary>
        /// جایگزینی placeholderها در کامند RouterOS
        /// این متد placeholderهای رایج را با مقادیر واقعی جایگزین می‌کند
        /// </summary>
        /// <param name="commandTemplate">قالب کامند با placeholderها</param>
        /// <param name="frequency">فرکانس (اختیاری)</param>
        /// <param name="protocol">پروتکل wireless (اختیاری)</param>
        /// <param name="channelWidth">عرض کانال (اختیاری)</param>
        /// <returns>کامند با placeholderهای جایگزین شده</returns>
        private string ReplaceCommandPlaceholders(string commandTemplate, double? frequency = null, string? protocol = null, string? channelWidth = null)
        {
            var command = commandTemplate
                .Replace("{interface}", _settings.InterfaceName);
            
            if (frequency.HasValue)
            {
                var roundedFrequency = (int)Math.Round(frequency.Value, 0);
                command = command.Replace("{frequency}", roundedFrequency.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            if (!string.IsNullOrEmpty(protocol))
            {
                command = command.Replace("{protocol}", protocol);
            }
            
            if (!string.IsNullOrEmpty(channelWidth))
            {
                command = command.Replace("{channelWidth}", channelWidth);
            }
            
            return command;
        }

        /// <summary>
        /// اجرای یک کامند RouterOS با retry در صورت خالی بودن پاسخ
        /// این متد کامند اصلی را اجرا می‌کند و در صورت خالی بودن، کامندهای جایگزین را امتحان می‌کند
        /// </summary>
        /// <param name="primaryCommand">کامند اصلی</param>
        /// <param name="fallbackCommands">لیست کامندهای جایگزین (اختیاری)</param>
        /// <param name="timeout">تایم‌اوت برای هر کامند (میلی‌ثانیه)</param>
        /// <param name="logPrefix">پیشوند برای لاگ (اختیاری)</param>
        /// <returns>پاسخ کامند یا string.Empty اگر همه کامندها ناموفق بودند</returns>
        private async Task<string> SendCommandWithRetryAsync(string primaryCommand, List<string>? fallbackCommands = null, int timeout = 8000, string logPrefix = "")
        {
            var commands = new List<string> { primaryCommand };
            if (fallbackCommands != null && fallbackCommands.Count > 0)
            {
                commands.AddRange(fallbackCommands);
            }

            foreach (var command in commands)
            {
                OnStatusUpdate($"اجرای کامند: {command}");
                if (!string.IsNullOrEmpty(logPrefix))
                {
                    OnTerminalData($"[{logPrefix}] اجرای کامند: {command}");
                }

                var response = await _sshClient.SendCommandAsync(command, timeout);
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    return response;
                }

                if (commands.IndexOf(command) < commands.Count - 1)
                {
                    OnStatusUpdate("هشدار: هیچ پاسخی از کامند دریافت نشد. تلاش با کامند جایگزین...");
                    if (!string.IsNullOrEmpty(logPrefix))
                    {
                        OnTerminalData($"[{logPrefix}] هشدار: هیچ پاسخی از کامند دریافت نشد. تلاش با کامند جایگزین...");
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// پارس کردن یک مقدار عددی از خروجی RouterOS
        /// این متد مقادیر عددی را از فرمت‌های مختلف RouterOS استخراج می‌کند
        /// </summary>
        /// <param name="text">متن خروجی RouterOS</param>
        /// <param name="key">نام کلید مورد نظر (مثلاً "signal-strength", "noise-floor")</param>
        /// <returns>مقدار عددی استخراج شده یا null اگر پیدا نشد</returns>
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

        /// <summary>
        /// پارس کردن یک مقدار رشته‌ای از خروجی RouterOS
        /// این متد مقادیر رشته‌ای را از فرمت key=value یا key:value استخراج می‌کند
        /// </summary>
        /// <param name="text">متن خروجی RouterOS</param>
        /// <param name="key">نام کلید مورد نظر (مثلاً "band", "wireless-protocol")</param>
        /// <returns>مقدار رشته‌ای استخراج شده یا null اگر پیدا نشد</returns>
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

        /// <summary>
        /// کلاس کمکی برای ذخیره اطلاعات آنتن remote
        /// این کلاس اطلاعات آنتن‌های متصل به روتر را نگه می‌دارد
        /// </summary>
        private class RemoteAntennaInfo
        {
            public string? MacAddress { get; set; }
            public string? Identity { get; set; }
            public string? RadioName { get; set; }
            public double? SignalStrength { get; set; }
            public double? SignalToNoiseRatio { get; set; }
            public double? TxRate { get; set; }
            public double? RxRate { get; set; }
            public double? CCQ { get; set; }
            public double? TxCCQ { get; set; }
            public double? RxCCQ { get; set; }
            public double? TxSignalStrength { get; set; }
            public double? RxSignalStrength { get; set; }
            public double? SignalStrengthCh0 { get; set; }
            public double? SignalStrengthCh1 { get; set; }
            public double? TxSignalStrengthCh0 { get; set; }
            public double? TxSignalStrengthCh1 { get; set; }
            public double? PThroughput { get; set; }
            public long? PacketsRx { get; set; }
            public long? PacketsTx { get; set; }
            public long? BytesRx { get; set; }
            public long? BytesTx { get; set; }
            public long? FramesRx { get; set; }
            public long? FramesTx { get; set; }
            public long? FrameBytesRx { get; set; }
            public long? FrameBytesTx { get; set; }
            public long? HwFramesRx { get; set; }
            public long? HwFramesTx { get; set; }
            public long? HwFrameBytesRx { get; set; }
            public long? HwFrameBytesTx { get; set; }
            public long? TxFramesTimedOut { get; set; }
            public string? Uptime { get; set; }
            public string? LastActivity { get; set; }
            public bool? Nstreme { get; set; }
            public bool? NstremePlus { get; set; }
            public string? FramingMode { get; set; }
            public string? RouterOsVersion { get; set; }
            public string? LastIp { get; set; }
            public bool? Port8021xEnabled { get; set; }
            public string? AuthenticationType { get; set; }
            public string? Encryption { get; set; }
            public string? GroupEncryption { get; set; }
            public bool? ManagementProtection { get; set; }
            public bool? Compression { get; set; }
            public bool? WmmEnabled { get; set; }
            public string? TxRateSet { get; set; }
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
            
            OnTerminalData($"[ParseRegistrationTable] Total lines to parse: {lines.Length}");
            
            bool foundAnyData = false;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;
                
                OnTerminalData($"[ParseRegistrationTable] Processing line: {trimmedLine.Substring(0, Math.Min(150, trimmedLine.Length))}...");

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

                // If line starts with a number followed by space (like " 1 "), it's entry number
                // Extract the part after the number which contains key=value pairs
                if (trimmedLine.Length > 0 && char.IsDigit(trimmedLine[0]))
                {
                    var firstSpaceIndex = trimmedLine.IndexOf(' ');
                    if (firstSpaceIndex > 0 && firstSpaceIndex < 10) // Allow up to 9 digits for entry number
                    {
                        // Check if the part before space is just a number
                        var numberPart = trimmedLine.Substring(0, firstSpaceIndex);
                        if (numberPart.All(char.IsDigit))
                        {
                            // Extract the part after the number
                            trimmedLine = trimmedLine.Substring(firstSpaceIndex).Trim();
                            if (string.IsNullOrEmpty(trimmedLine))
                                continue;
                        }
                    }
                    else if (trimmedLine.Length < 10 && trimmedLine.All(char.IsDigit))
                    {
                        // It's just a number, skip it
                        continue;
                    }
                }

                // Try to parse key-value pairs with different separators
                // First, try to parse key=value format (most common in RouterOS stat output)
                // This format can have multiple key=value pairs in one line separated by spaces
                if (trimmedLine.Contains("="))
                {
                    // Use improved regex to find all key=value pairs
                    // Pattern must handle:
                    // - Quoted values: key="value" or key='value'
                    // - Unquoted values: key=value
                    // - Values with commas: key=value1,value2
                    // - Values with special chars: key=-75dBm@6Mbps
                    // Strategy: Match key=, then match until next key= or end of line
                    var keyValuePairs = new List<(string key, string value)>();
                    var currentPos = 0;
                    
                    while (currentPos < trimmedLine.Length)
                    {
                        // Find next key= pattern
                        var keyPattern = @"(\w+(?:-\w+)*)=";
                        var keyMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine.Substring(currentPos), keyPattern);
                        if (!keyMatch.Success)
                            break;
                        
                        var keyStart = currentPos + keyMatch.Index;
                        var key = keyMatch.Groups[1].Value.ToLower();
                        var valueStart = keyStart + keyMatch.Length;
                        
                        // Find where value ends (next key= or end of line)
                        var nextKeyMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine.Substring(valueStart), keyPattern);
                        var valueEnd = nextKeyMatch.Success ? valueStart + nextKeyMatch.Index : trimmedLine.Length;
                        
                        var value = trimmedLine.Substring(valueStart, valueEnd - valueStart).Trim();
                        
                        // Remove quotes if present
                        if (value.StartsWith("\"") && value.EndsWith("\""))
                            value = value.Substring(1, value.Length - 2);
                        else if (value.StartsWith("'") && value.EndsWith("'"))
                            value = value.Substring(1, value.Length - 2);
                        
                        keyValuePairs.Add((key, value));
                        currentPos = valueEnd;
                    }
                    
                    // Parse each key-value pair
                    foreach (var (key, value) in keyValuePairs)
                    {
                        ParseKeyValuePair(key, value, info, ref foundAnyData);
                    }
                    
                    // Log parsing result
                    if (keyValuePairs.Count > 0)
                    {
                        OnTerminalData($"[ParseRegistrationTable] Parsed {keyValuePairs.Count} key-value pairs from line");
                    }
                    else
                    {
                        // Fallback: Try a simpler approach - split by spaces and find key=value patterns
                        // This handles cases where values contain spaces or special characters
                        var parts = trimmedLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            if (part.Contains("="))
                            {
                                var equalIndex = part.IndexOf('=');
                                if (equalIndex > 0 && equalIndex < part.Length - 1)
                                {
                                    var key = part.Substring(0, equalIndex).Trim().ToLower();
                                    var value = part.Substring(equalIndex + 1).Trim();
                                    // Remove quotes if present
                                    if (value.StartsWith("\"") && value.EndsWith("\""))
                                        value = value.Substring(1, value.Length - 2);
                                    ParseKeyValuePair(key, value, info, ref foundAnyData);
                                }
                            }
                        }
                    }
                    
                    // Additional fallback: if both methods didn't work, try manual parsing
                    if (keyValuePairs.Count == 0 && !foundAnyData)
                    {
                        // Split by spaces, but preserve key=value pairs
                        // Example: "interface=wlan1 radio-name=\"NTK_O\" mac-address=4C:5E:0C:7F:D4:B1"
                        var manualKeyValuePairs = new List<string>();
                        var currentPair = "";
                        var inQuotes = false;
                        var quoteChar = '\0';
                        
                        for (int i = 0; i < trimmedLine.Length; i++)
                        {
                            var ch = trimmedLine[i];
                            
                            // Handle quoted values
                            if ((ch == '"' || ch == '\'') && !inQuotes)
                            {
                                inQuotes = true;
                                quoteChar = ch;
                                currentPair += ch;
                            }
                            else if (inQuotes && ch == quoteChar)
                            {
                                inQuotes = false;
                                currentPair += ch;
                            }
                            else if (!inQuotes && char.IsWhiteSpace(ch) && currentPair.Contains("="))
                            {
                                // End of current key=value pair
                                if (!string.IsNullOrEmpty(currentPair))
                                {
                                    manualKeyValuePairs.Add(currentPair);
                                    currentPair = "";
                                }
                            }
                            else
                            {
                                currentPair += ch;
                            }
                        }
                        
                        // Add last pair if exists
                        if (!string.IsNullOrEmpty(currentPair) && currentPair.Contains("="))
                        {
                            manualKeyValuePairs.Add(currentPair);
                        }
                        
                        // Parse each key=value pair
                        foreach (var pair in manualKeyValuePairs)
                        {
                            var equalIndex = pair.IndexOf('=');
                            if (equalIndex > 0 && equalIndex < pair.Length - 1)
                            {
                                var key = pair.Substring(0, equalIndex).Trim().ToLower();
                                var value = pair.Substring(equalIndex + 1).Trim();
                                ParseKeyValuePair(key, value, info, ref foundAnyData);
                            }
                        }
                    }
                }
                else
                {
                    // Fallback: Try to split by spaces if it looks like tabular format
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
                        // Standard key-value format with ":"
                        var sepIndex = trimmedLine.IndexOf(':');
                        if (sepIndex > 0)
                        {
                            var key = trimmedLine.Substring(0, sepIndex).Trim().ToLower();
                            var value = trimmedLine.Substring(sepIndex + 1).Trim();
                            ParseKeyValuePair(key, value, info, ref foundAnyData);
                        }
                    }
                }
            }

            // Log final parsing result
            if (foundAnyData)
            {
                OnTerminalData($"[ParseRegistrationTable] Successfully parsed data. MAC: {info.MacAddress ?? "N/A"}, Signal: {info.SignalStrength?.ToString("F1") ?? "N/A"}, TxCCQ: {info.TxCCQ?.ToString("F1") ?? "N/A"}, RxCCQ: {info.RxCCQ?.ToString("F1") ?? "N/A"}");
            }
            else
            {
                OnTerminalData("[ParseRegistrationTable] No data found in registration table");
            }
            
            // Return info only if we found at least some data
            return foundAnyData ? info : null;
        }

        /// <summary>
        /// متد کمکی برای پارس کردن یک جفت key-value و پر کردن RemoteAntennaInfo
        /// این متد کلیدهای مختلف را شناسایی می‌کند و مقادیر را در کلاس RemoteAntennaInfo ذخیره می‌کند
        /// </summary>
        /// <param name="key">نام کلید (مثلاً "signal-strength", "tx-rate")</param>
        /// <param name="value">مقدار کلید</param>
        /// <param name="info">شیء RemoteAntennaInfo که باید پر شود</param>
        /// <param name="foundAnyData">ارجاع به متغیر boolean که نشان می‌دهد آیا داده‌ای پیدا شده است</param>
        private void ParseKeyValuePair(string key, string value, RemoteAntennaInfo info, ref bool foundAnyData)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return;

            key = key.Trim().ToLower();
            value = value.Trim();

            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value.Substring(1, value.Length - 2);
            
            // Debug: Log important keys being parsed (using System.Diagnostics for debugging)
            var importantKeys = new[] { "signal-strength", "signal-to-noise", "tx-rate", "rx-rate", "tx-ccq", "rx-ccq", "ccq", "mac-address", "identity", "radio-name", "packets", "bytes", "nstreme" };
            if (importantKeys.Any(k => key.Contains(k)))
            {
                System.Diagnostics.Debug.WriteLine($"[ParseKeyValuePair] Key: {key}, Value: {value}");
            }

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
            // Parse radio-name
            else if (key == "radio-name")
            {
                info.RadioName = value;
                foundAnyData = true;
            }
            // Parse signal-strength (handle formats like "-75dBm@6Mbps" or "-75dBm")
            else if (key == "signal-strength" || (key.Contains("signal-strength") && !key.Contains("ch0") && !key.Contains("ch1") && !key.Contains("tx") && !key.Contains("rx")))
            {
                // Extract numeric value from formats like "-75dBm@6Mbps" or "-75dBm"
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.SignalStrength = numValue;
                    foundAnyData = true;
                }
            }
            // Parse signal-to-noise
            else if (key == "signal-to-noise" || key == "signal-to-noise-ratio" || key.Contains("signal-to-noise") || key == "snr")
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.SignalToNoiseRatio = numValue;
                    foundAnyData = true;
                }
            }
            // Parse tx-rate (handle formats like "36Mbps" or "\"36Mbps\"")
            else if (key == "tx-rate")
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.TxRate = numValue;
                    foundAnyData = true;
                }
            }
            // Parse rx-rate (handle formats like "6Mbps" or "\"6Mbps\"")
            else if (key == "rx-rate")
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.RxRate = numValue;
                    foundAnyData = true;
                }
            }
            // Parse ccq (general)
            else if (key == "ccq" || key.Contains("ccq") && !key.Contains("tx-ccq") && !key.Contains("rx-ccq"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.CCQ = numValue;
                    foundAnyData = true;
                }
            }
            // Parse tx-ccq
            else if (key == "tx-ccq" || key.Contains("tx-ccq"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.TxCCQ = numValue;
                    foundAnyData = true;
                }
            }
            // Parse rx-ccq
            else if (key == "rx-ccq" || key.Contains("rx-ccq"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.RxCCQ = numValue;
                    foundAnyData = true;
                }
            }
            // Parse p-throughput
            else if (key == "p-throughput" || key.Contains("p-throughput"))
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.PThroughput = numValue;
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
            // Parse signal-strength-ch0
            else if (key == "signal-strength-ch0")
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.SignalStrengthCh0 = numValue;
                    foundAnyData = true;
                }
            }
            // Parse signal-strength-ch1
            else if (key == "signal-strength-ch1")
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.SignalStrengthCh1 = numValue;
                    foundAnyData = true;
                }
            }
            // Parse tx-signal-strength-ch0
            else if (key == "tx-signal-strength-ch0")
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.TxSignalStrengthCh0 = numValue;
                    foundAnyData = true;
                }
            }
            // Parse tx-signal-strength-ch1
            else if (key == "tx-signal-strength-ch1")
            {
                var numValue = ParseNumericValue(value);
                if (numValue.HasValue)
                {
                    info.TxSignalStrengthCh1 = numValue;
                    foundAnyData = true;
                }
            }
            // Parse packets (format: "23334,25484" - rx,tx)
            else if (key == "packets")
            {
                var values = ParseCommaSeparatedValues(value);
                if (values.Count >= 2)
                {
                    if (long.TryParse(values[0], out long rx))
                        info.PacketsRx = rx;
                    if (long.TryParse(values[1], out long tx))
                        info.PacketsTx = tx;
                    foundAnyData = true;
                }
            }
            // Parse bytes (format: "6664416,9273327" - rx,tx)
            else if (key == "bytes")
            {
                var values = ParseCommaSeparatedValues(value);
                if (values.Count >= 2)
                {
                    if (long.TryParse(values[0], out long rx))
                        info.BytesRx = rx;
                    if (long.TryParse(values[1], out long tx))
                        info.BytesTx = tx;
                    foundAnyData = true;
                }
            }
            // Parse frames (format: "23334,25487" - rx,tx)
            else if (key == "frames")
            {
                var values = ParseCommaSeparatedValues(value);
                if (values.Count >= 2)
                {
                    if (long.TryParse(values[0], out long rx))
                        info.FramesRx = rx;
                    if (long.TryParse(values[1], out long tx))
                        info.FramesTx = tx;
                    foundAnyData = true;
                }
            }
            // Parse frame-bytes (format: "6527112,9120766" - rx,tx)
            else if (key == "frame-bytes")
            {
                var values = ParseCommaSeparatedValues(value);
                if (values.Count >= 2)
                {
                    if (long.TryParse(values[0], out long rx))
                        info.FrameBytesRx = rx;
                    if (long.TryParse(values[1], out long tx))
                        info.FrameBytesTx = tx;
                    foundAnyData = true;
                }
            }
            // Parse hw-frames (format: "64798,390855" - rx,tx)
            else if (key == "hw-frames")
            {
                var values = ParseCommaSeparatedValues(value);
                if (values.Count >= 2)
                {
                    if (long.TryParse(values[0], out long rx))
                        info.HwFramesRx = rx;
                    if (long.TryParse(values[1], out long tx))
                        info.HwFramesTx = tx;
                    foundAnyData = true;
                }
            }
            // Parse hw-frame-bytes (format: "22447466,27595917" - rx,tx)
            else if (key == "hw-frame-bytes")
            {
                var values = ParseCommaSeparatedValues(value);
                if (values.Count >= 2)
                {
                    if (long.TryParse(values[0], out long rx))
                        info.HwFrameBytesRx = rx;
                    if (long.TryParse(values[1], out long tx))
                        info.HwFrameBytesTx = tx;
                    foundAnyData = true;
                }
            }
            // Parse tx-frames-timed-out
            else if (key == "tx-frames-timed-out")
            {
                if (long.TryParse(value, out long timeout))
                {
                    info.TxFramesTimedOut = timeout;
                    foundAnyData = true;
                }
            }
            // Parse uptime
            else if (key == "uptime")
            {
                info.Uptime = value;
                foundAnyData = true;
            }
            // Parse last-activity
            else if (key == "last-activity")
            {
                info.LastActivity = value;
                foundAnyData = true;
            }
            // Parse nstreme (yes/no)
            else if (key == "nstreme")
            {
                info.Nstreme = value.ToLower() == "yes";
                foundAnyData = true;
            }
            // Parse nstreme-plus (yes/no)
            else if (key == "nstreme-plus")
            {
                info.NstremePlus = value.ToLower() == "yes";
                foundAnyData = true;
            }
            // Parse framing-mode
            else if (key == "framing-mode")
            {
                info.FramingMode = value;
                foundAnyData = true;
            }
            // Parse routeros-version
            else if (key == "routeros-version")
            {
                info.RouterOsVersion = value;
                foundAnyData = true;
            }
            // Parse last-ip
            else if (key == "last-ip")
            {
                info.LastIp = value;
                foundAnyData = true;
            }
            // Parse 802.1x-port-enabled (yes/no)
            else if (key == "802.1x-port-enabled")
            {
                info.Port8021xEnabled = value.ToLower() == "yes";
                foundAnyData = true;
            }
            // Parse authentication-type
            else if (key == "authentication-type")
            {
                info.AuthenticationType = value;
                foundAnyData = true;
            }
            // Parse encryption
            else if (key == "encryption")
            {
                info.Encryption = value;
                foundAnyData = true;
            }
            // Parse group-encryption
            else if (key == "group-encryption")
            {
                info.GroupEncryption = value;
                foundAnyData = true;
            }
            // Parse management-protection (yes/no)
            else if (key == "management-protection")
            {
                info.ManagementProtection = value.ToLower() == "yes";
                foundAnyData = true;
            }
            // Parse compression (yes/no)
            else if (key == "compression")
            {
                info.Compression = value.ToLower() == "yes";
                foundAnyData = true;
            }
            // Parse wmm-enabled (yes/no)
            else if (key == "wmm-enabled")
            {
                info.WmmEnabled = value.ToLower() == "yes";
                foundAnyData = true;
            }
            // Parse tx-rate-set
            else if (key == "tx-rate-set")
            {
                info.TxRateSet = value;
                foundAnyData = true;
            }
        }

        /// <summary>
        /// پارس کردن مقادیر جدا شده با کاما (مثلاً "23334,25484")
        /// </summary>
        /// <param name="value">رشته شامل مقادیر جدا شده با کاما</param>
        /// <returns>لیست مقادیر پارس شده</returns>
        private List<string> ParseCommaSeparatedValues(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return result;

            var parts = value.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    result.Add(trimmed);
                }
            }
            return result;
        }

        /// <summary>
        /// Parses a numeric value from a string, handling units like "dBm", "Mbps", "%"
        /// Also handles formats like "-75dBm@6Mbps" by extracting the first number
        /// </summary>
        private double? ParseNumericValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            // Remove quotes if present
            value = value.Trim();
            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value.Substring(1, value.Length - 2).Trim();

            // Handle formats like "-75dBm@6Mbps" - extract the first number before @
            if (value.Contains("@"))
            {
                var parts = value.Split('@');
                if (parts.Length > 0)
                {
                    value = parts[0].Trim();
                }
            }

            // Remove common units (case-insensitive)
            value = System.Text.RegularExpressions.Regex.Replace(
                value, 
                @"\s*(dBm|dB|%|Mbps|Kbps|bps|MHz|GHz)\s*", 
                "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Extract the first number (may be negative)
            var match = System.Text.RegularExpressions.Regex.Match(value, @"(-?\d+\.?\d*)");
            if (match.Success)
            {
                if (double.TryParse(match.Value, System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out double result))
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// فراخوانی رویداد ScanProgress
        /// این متد برای اطلاع از پیشرفت اسکن استفاده می‌شود
        /// </summary>
        /// <param name="result">نتیجه اسکن که باید به listeners ارسال شود</param>
        protected virtual void OnScanProgress(FrequencyScanResult result)
        {
            ScanProgress?.Invoke(this, result);
        }

        /// <summary>
        /// فراخوانی رویداد StatusUpdate
        /// این متد برای به‌روزرسانی پیام‌های وضعیت استفاده می‌شود
        /// </summary>
        /// <param name="message">پیام وضعیت</param>
        protected virtual void OnStatusUpdate(string message)
        {
            StatusUpdate?.Invoke(this, message);
        }

        /// <summary>
        /// انجام تست پینگ به آدرس IP مشخص شده در تنظیمات
        /// این متد چندین پینگ ارسال می‌کند و آمار کامل (میانگین، حداقل، حداکثر، درصد از دست رفتن) را محاسبه می‌کند
        /// </summary>
        /// <param name="result">نتیجه اسکن که باید اطلاعات پینگ به آن اضافه شود</param>
        /// <returns>نتیجه اسکن با اطلاعات پینگ اضافه شده</returns>
        private async Task<FrequencyScanResult> PerformPingTestAsync(FrequencyScanResult result)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_settings.PingTestIpAddress))
                {
                    OnTerminalData("[PingTest] آدرس IP برای تست پینگ تنظیم نشده است.");
                    return result;
                }

                result.PingTestIpAddress = _settings.PingTestIpAddress;
                OnStatusUpdate($"در حال تست پینگ به {_settings.PingTestIpAddress}...");
                OnTerminalData($"[PingTest] شروع تست پینگ به {_settings.PingTestIpAddress}");

                using var ping = new Ping();
                const int pingCount = 4; // تعداد پینگ‌های ارسالی
                const int timeout = 5000; // تایم‌اوت هر پینگ (میلی‌ثانیه)
                
                var pingTimes = new List<long>();
                int successCount = 0;
                int failedCount = 0;

                for (int i = 0; i < pingCount; i++)
                {
                    try
                    {
                        OnTerminalData($"[PingTest] ارسال پینگ {i + 1}/{pingCount}...");
                        var reply = await ping.SendPingAsync(_settings.PingTestIpAddress, timeout);
                        
                        if (reply.Status == IPStatus.Success)
                        {
                            pingTimes.Add(reply.RoundtripTime);
                            successCount++;
                            OnTerminalData($"[PingTest] پینگ {i + 1} موفق: {reply.RoundtripTime}ms");
                        }
                        else
                        {
                            failedCount++;
                            OnTerminalData($"[PingTest] پینگ {i + 1} ناموفق: {reply.Status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        OnTerminalData($"[PingTest] خطا در پینگ {i + 1}: {ex.Message}");
                    }

                    // فاصله کوتاه بین پینگ‌ها
                    if (i < pingCount - 1)
                    {
                        await Task.Delay(500);
                    }
                }

                result.PingPacketsSent = pingCount;
                result.PingPacketsReceived = successCount;
                result.PingPacketsLost = failedCount;
                result.PingLossPercentage = pingCount > 0 ? (double)failedCount / pingCount * 100 : 0;
                result.PingSuccess = successCount > 0;

                if (pingTimes.Count > 0)
                {
                    result.PingMinTime = pingTimes.Min();
                    result.PingMaxTime = pingTimes.Max();
                    result.PingAverageTime = (long)pingTimes.Average();
                    result.PingTime = result.PingAverageTime; // زمان اصلی را میانگین قرار می‌دهیم
                    
                    OnTerminalData($"[PingTest] نتایج: موفق={successCount}, ناموفق={failedCount}, میانگین={result.PingAverageTime}ms, حداقل={result.PingMinTime}ms, حداکثر={result.PingMaxTime}ms");
                    OnStatusUpdate($"پینگ: {result.PingAverageTime}ms (از {pingCount} پینگ: {successCount} موفق، {failedCount} ناموفق)");
                }
                else
                {
                    result.PingTime = null;
                    OnTerminalData($"[PingTest] هیچ پینگ موفقی دریافت نشد. همه {pingCount} پینگ ناموفق بودند.");
                    OnStatusUpdate($"پینگ ناموفق: همه {pingCount} پینگ ناموفق بودند.");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var errorMsg = $"خطا در انجام تست پینگ: {ex.Message}";
                    if (ex.InnerException != null)
                    {
                        errorMsg += $"\nخطای داخلی: {ex.InnerException.Message}";
                    }
                    errorMsg += $"\n\nنوع خطا: {ex.GetType().Name}\n\n" +
                               $"اگر مشکل ادامه داشت، لطفاً به پشتیبانی اطلاع دهید.";
                    OnTerminalData($"[PingTest] [ERROR] {errorMsg}");
                    OnStatusUpdate($"خطا در تست پینگ: {ex.Message}");
                    result.PingSuccess = false;
                }
                catch
                {
                    // اگر حتی لاگ کردن هم خطا داد، حداقل یک پیام ساده نمایش بده
                    result.PingSuccess = false;
                }
            }

            return result;
        }

        /// <summary>
        /// فراخوانی رویداد ProgressChanged
        /// این متد برای به‌روزرسانی درصد پیشرفت اسکن استفاده می‌شود
        /// </summary>
        /// <param name="progress">درصد پیشرفت (0-100)</param>
        protected virtual void OnProgressChanged(int progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }

        /// <summary>
        /// فراخوانی رویداد TerminalData
        /// این متد برای نمایش داده‌های ترمینال استفاده می‌شود
        /// </summary>
        /// <param name="data">داده ترمینال که باید نمایش داده شود</param>
        protected virtual void OnTerminalData(string data)
        {
            TerminalData?.Invoke(this, data);
        }
    }
}

