using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ntk.Mikrotik.Tools.Models;

namespace Ntk.Mikrotik.Tools.Services
{
    /// <summary>
    /// سرویس مدیریت اتصال و اعتبارسنجی اینترفیس‌های wireless
    /// این کلاس مسئول بررسی معتبر بودن نام اینترفیس‌ها در روتر MikroTik است
    /// </summary>
    public class ConnectionService
    {
        /// <summary>
        /// اعتبارسنجی نام اینترفیس wireless
        /// این متد لیست اینترفیس‌های wireless موجود در روتر را دریافت می‌کند و چک می‌کند که آیا اینترفیس مورد نظر وجود دارد
        /// </summary>
        /// <param name="sshClient">کلاینت SSH برای ارتباط با روتر</param>
        /// <param name="settings">تنظیمات برنامه که شامل کامند اعتبارسنجی است</param>
        /// <param name="interfaceName">نام اینترفیس مورد نظر برای بررسی</param>
        /// <returns>یک شیء ValidationResult که شامل نتیجه اعتبارسنجی و لیست اینترفیس‌های موجود است</returns>
        public async Task<InterfaceValidationResult> ValidateInterfaceNameAsync(
            MikroTikSshClient sshClient, 
            ScanSettings settings, 
            string interfaceName)
        {
            var result = new InterfaceValidationResult
            {
                IsValid = false,
                AvailableInterfaces = new List<string>()
            };

            try
            {
                if (sshClient == null || !sshClient.IsConnected)
                {
                    result.ErrorMessage = "اتصال به روتر برقرار نیست.";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(interfaceName))
                {
                    result.ErrorMessage = "نام اینترفیس خالی است.";
                    return result;
                }

                // Get command from settings
                var command = settings.CommandValidateInterface;
                
                var response = await sshClient.SendCommandAsync(command, 5000);
                
                if (string.IsNullOrWhiteSpace(response))
                {
                    result.ErrorMessage = "خطا در دریافت لیست اینترفیس‌های wireless از روتر.";
                    return result;
                }

                // Parse interface names from response
                var interfaceNames = ParseInterfaceNames(response);

                result.AvailableInterfaces = interfaceNames;

                // Check if the requested interface exists
                var interfaceExists = interfaceNames.Any(name => 
                    string.Equals(name, interfaceName, StringComparison.OrdinalIgnoreCase));

                result.IsValid = interfaceExists;

                if (!interfaceExists)
                {
                    var availableInterfaces = interfaceNames.Count > 0 
                        ? string.Join(", ", interfaceNames) 
                        : "(هیچ اینترفیس wireless یافت نشد)";

                    result.ErrorMessage = $"اینترفیس wireless با نام '{interfaceName}' در روتر یافت نشد.\n\n" +
                                         $"اینترفیس‌های wireless موجود در روتر:\n{availableInterfaces}\n\n" +
                                         $"لطفاً نام اینترفیس را در تنظیمات اصلاح کنید.\n\n" +
                                         $"توجه: اگر نام اینترفیس اشتباه باشد، اطلاعات به درستی جمع‌آوری نمی‌شود و در مراحل بعدی مشکل ایجاد می‌کند.";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"خطا در بررسی نام اینترفیس: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// تجزیه نام اینترفیس‌ها از خروجی RouterOS
        /// این متد خروجی کامند /interface wireless print را تجزیه می‌کند و لیست نام اینترفیس‌ها را استخراج می‌کند
        /// </summary>
        /// <param name="response">خروجی کامند RouterOS</param>
        /// <returns>لیست نام اینترفیس‌های wireless موجود در روتر</returns>
        private List<string> ParseInterfaceNames(string response)
        {
            var interfaceNames = new List<string>();
            var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                // Skip empty lines, header lines, and comment-only lines
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || 
                    trimmedLine.StartsWith("Flags:") || 
                    (trimmedLine.StartsWith(";;;") && !trimmedLine.Contains("name=")))
                    continue;

                // Look for "name=" pattern
                var nameIndex = trimmedLine.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
                if (nameIndex >= 0)
                {
                    var nameStart = nameIndex + 5; // "name=".Length
                    string name = "";
                    
                    // Check if name is quoted (e.g., name="wlan1 H20 AP")
                    if (nameStart < trimmedLine.Length && trimmedLine[nameStart] == '"')
                    {
                        // Name is quoted - extract everything between quotes
                        nameStart++; // Skip opening quote
                        var quoteEnd = trimmedLine.IndexOf('"', nameStart);
                        if (quoteEnd >= nameStart)
                        {
                            name = trimmedLine.Substring(nameStart, quoteEnd - nameStart);
                        }
                    }
                    else
                    {
                        // Name is not quoted - extract until next space or end of line
                        var nameEnd = trimmedLine.IndexOf(' ', nameStart);
                        if (nameEnd < 0) nameEnd = trimmedLine.Length;
                        name = trimmedLine.Substring(nameStart, nameEnd - nameStart).Trim();
                    }
                    
                    if (!string.IsNullOrWhiteSpace(name) && !interfaceNames.Contains(name))
                    {
                        interfaceNames.Add(name);
                    }
                }
            }

            return interfaceNames;
        }
    }

    /// <summary>
    /// نتیجه اعتبارسنجی اینترفیس
    /// </summary>
    public class InterfaceValidationResult
    {
        /// <summary>
        /// آیا اینترفیس معتبر است یا نه
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// پیام خطا در صورت نامعتبر بودن اینترفیس
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// لیست اینترفیس‌های wireless موجود در روتر
        /// </summary>
        public List<string> AvailableInterfaces { get; set; } = new List<string>();
    }
}

