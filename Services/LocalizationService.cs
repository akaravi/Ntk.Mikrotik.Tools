using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Ntk.Mikrotik.Tools.Services
{
    /// <summary>
    /// سرویس مدیریت چند زبانه بودن برنامه
    /// این کلاس مسئول بارگذاری و مدیریت ترجمه‌های مختلف زبان‌ها است
    /// </summary>
    public class LocalizationService
    {
        private static LocalizationService? _instance;
        private Dictionary<string, string> _translations;
        private string _currentLanguage;
        private readonly string _languagesDirectory;

        /// <summary>
        /// رویداد تغییر زبان
        /// </summary>
        public event EventHandler? LanguageChanged;

        /// <summary>
        /// نمونه یکتای سرویس
        /// </summary>
        public static LocalizationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LocalizationService();
                }
                return _instance;
            }
        }

        /// <summary>
        /// زبان فعلی
        /// </summary>
        public string CurrentLanguage
        {
            get => _currentLanguage;
            private set => _currentLanguage = value;
        }

        /// <summary>
        /// لیست زبان‌های موجود
        /// </summary>
        public List<string> AvailableLanguages => new List<string> { "fa", "ar", "en", "de" };

        /// <summary>
        /// نام‌های نمایشی زبان‌ها
        /// </summary>
        public Dictionary<string, string> LanguageNames => new Dictionary<string, string>
        {
            { "fa", "فارسی" },
            { "ar", "العربية" },
            { "en", "English" },
            { "de", "Deutsch" }
        };

        private LocalizationService()
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
                _languagesDirectory = Path.Combine(startupPath, "Resources", "Languages");
                
                // اطمینان از وجود پوشه
                if (!Directory.Exists(_languagesDirectory))
                {
                    Directory.CreateDirectory(_languagesDirectory);
                }
            }
            catch
            {
                _languagesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Languages");
                if (!Directory.Exists(_languagesDirectory))
                {
                    Directory.CreateDirectory(_languagesDirectory);
                }
            }

            _translations = new Dictionary<string, string>();
            _currentLanguage = "fa"; // زبان پیش‌فرض فارسی است
            
            // بارگذاری زبان پیش‌فرض
            LoadLanguage(_currentLanguage);
        }

        /// <summary>
        /// بارگذاری زبان مشخص شده
        /// </summary>
        /// <param name="languageCode">کد زبان (fa, ar, en, de)</param>
        /// <returns>true اگر بارگذاری موفقیت‌آمیز باشد</returns>
        public bool LoadLanguage(string languageCode)
        {
            try
            {
                if (!AvailableLanguages.Contains(languageCode))
                {
                    languageCode = "fa"; // Fallback to Persian
                }

                var languageFile = Path.Combine(_languagesDirectory, $"{languageCode}.json");
                
                if (File.Exists(languageFile))
                {
                    var json = File.ReadAllText(languageFile);
                    _translations = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) 
                        ?? new Dictionary<string, string>();
                }
                else
                {
                    // اگر فایل وجود نداشت، از فارسی استفاده کن
                    if (languageCode != "fa")
                    {
                        return LoadLanguage("fa");
                    }
                    _translations = new Dictionary<string, string>();
                }

                _currentLanguage = languageCode;
                LanguageChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading language {languageCode}: {ex.Message}");
                // Fallback to Persian
                if (languageCode != "fa")
                {
                    return LoadLanguage("fa");
                }
                return false;
            }
        }

        /// <summary>
        /// دریافت ترجمه برای کلید مشخص شده
        /// </summary>
        /// <param name="key">کلید ترجمه</param>
        /// <param name="defaultValue">مقدار پیش‌فرض در صورت عدم وجود ترجمه</param>
        /// <returns>متن ترجمه شده</returns>
        public string GetString(string key, string? defaultValue = null)
        {
            if (_translations.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            // اگر ترجمه پیدا نشد، مقدار پیش‌فرض یا خود کلید را برگردان
            return defaultValue ?? key;
        }

        /// <summary>
        /// ذخیره ترجمه‌ها در فایل JSON
        /// </summary>
        /// <param name="languageCode">کد زبان</param>
        /// <param name="translations">دیکشنری ترجمه‌ها</param>
        /// <returns>true اگر ذخیره موفقیت‌آمیز باشد</returns>
        public bool SaveLanguageFile(string languageCode, Dictionary<string, string> translations)
        {
            try
            {
                var languageFile = Path.Combine(_languagesDirectory, $"{languageCode}.json");
                var json = JsonConvert.SerializeObject(translations, Formatting.Indented);
                File.WriteAllText(languageFile, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving language file {languageCode}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// دریافت تمام ترجمه‌های زبان فعلی
        /// </summary>
        /// <returns>دیکشنری ترجمه‌ها</returns>
        public Dictionary<string, string> GetAllTranslations()
        {
            return new Dictionary<string, string>(_translations);
        }
    }
}

