using System;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using Ntk.Mikrotik.Tools.Models;

namespace Ntk.Mikrotik.Tools.Services
{
    /// <summary>
    /// سرویس مدیریت تنظیمات برنامه
    /// این کلاس مسئول بارگذاری، ذخیره و مدیریت تنظیمات از/به فایل JSON است
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsFilePath;

        /// <summary>
        /// سازنده کلاس SettingsService
        /// مسیر فایل تنظیمات را در مسیر اجرای برنامه تنظیم می‌کند
        /// </summary>
        public SettingsService()
        {
            try
            {
                var startupPath = Application.StartupPath;
                if (string.IsNullOrEmpty(startupPath))
                {
                    startupPath = AppDomain.CurrentDomain.BaseDirectory;
                }
                if (string.IsNullOrEmpty(startupPath))
                {
                    startupPath = Directory.GetCurrentDirectory();
                }
                _settingsFilePath = Path.Combine(startupPath, "settings.json");
            }
            catch
            {
                // Fallback to current directory if all else fails
                _settingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
            }
        }

        /// <summary>
        /// بارگذاری تنظیمات از فایل JSON
        /// اگر فایل وجود نداشته باشد، تنظیمات پیش‌فرض را برمی‌گرداند
        /// </summary>
        /// <returns>شیء ScanSettings بارگذاری شده از فایل یا تنظیمات پیش‌فرض</returns>
        public ScanSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonConvert.DeserializeObject<ScanSettings>(json);
                    
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return defaults instead
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }

            // Return default settings if file doesn't exist or deserialization failed
            return GetDefaultSettings();
        }

        /// <summary>
        /// ذخیره تنظیمات در فایل JSON
        /// </summary>
        /// <param name="settings">شیء ScanSettings که باید ذخیره شود</param>
        /// <returns>true اگر ذخیره موفقیت‌آمیز باشد، false در غیر این صورت</returns>
        public bool SaveSettings(ScanSettings settings)
        {
            try
            {
                if (settings == null)
                {
                    return false;
                }

                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// دریافت تنظیمات پیش‌فرض برنامه
        /// این متد یک نمونه جدید از ScanSettings با مقادیر پیش‌فرض برمی‌گرداند
        /// </summary>
        /// <returns>شیء ScanSettings با مقادیر پیش‌فرض</returns>
        public ScanSettings GetDefaultSettings()
        {
            return new ScanSettings
            {
                RouterIpAddress = "192.168.88.1",
                SshPort = 22,
                Username = "admin",
                Password = "",
                StartFrequency = 2400,
                EndFrequency = 2500,
                FrequencyStep = 5,
                StabilizationTimeMinutes = 2,
                InterfaceName = "wlan1",
                PingTestIpAddress = "8.8.8.8",
                WirelessProtocols = "nstreme\r\nnv2\r\n802.11",
                ChannelWidths = "20/40mhz-eC\r\n20/40mhz-Ce\r\n20mhz",
                CommandGetFrequency = "/interface wireless print where name=\"{interface}\" value-name=frequency",
                CommandSetFrequency = "/interface wireless set \"{interface}\" frequency={frequency}",
                CommandSetWirelessProtocol = "/interface wireless set \"{interface}\" wireless-protocol={protocol}",
                CommandSetChannelWidth = "/interface wireless set \"{interface}\" channel-width={channelWidth}",
                CommandGetInterfaceInfo = "/interface wireless print detail where name=\"{interface}\"",
                CommandGetRegistrationTable = "/interface wireless registration-table print stat where interface=\"{interface}\"",
                CommandMonitorInterface = "/interface wireless monitor \"{interface}\" once",
                CommandValidateInterface = "/interface wireless print",
                Language = "fa" // Default: Persian
            };
        }
    }
}

