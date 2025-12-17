# تاریخچه تغییرات پروژه

## 2025-12-17 - اضافه کردن سیستم چند زبانه

### تغییرات انجام شده:

1. **ایجاد سرویس LocalizationService** (`Services/LocalizationService.cs`)
   - مدیریت زبان‌های مختلف برنامه
   - بارگذاری و ذخیره فایل‌های JSON زبان‌ها
   - رویداد تغییر زبان برای به‌روزرسانی خودکار رابط کاربری

2. **ایجاد فایل‌های زبان** (`Resources/Languages/`)
   - `fa.json` - فارسی (زبان پایه)
   - `ar.json` - عربی
   - `en.json` - انگلیسی
   - `de.json` - آلمانی

3. **به‌روزرسانی مدل ScanSettings** (`Models/ScanSettings.cs`)
   - اضافه شدن فیلد `Language` برای ذخیره زبان انتخابی کاربر

4. **به‌روزرسانی SettingsService** (`Services/SettingsService.cs`)
   - اضافه شدن مقدار پیش‌فرض برای فیلد `Language`

5. **به‌روزرسانی MainForm** (`MainForm.cs`)
   - اضافه شدن کنترل انتخاب زبان در بالای فرم
   - جایگزینی تمام متون با فراخوانی‌های LocalizationService
   - اضافه شدن متد `UpdateAllTexts()` برای به‌روزرسانی خودکار متون
   - به‌روزرسانی تمام MessageBox.Show ها با متون چند زبانه

6. **به‌روزرسانی ErrorHandler** (`Services/ErrorHandler.cs`)
   - جایگزینی تمام متون خطا با فراخوانی‌های LocalizationService

7. **به‌روزرسانی Program** (`Program.cs`)
   - جایگزینی تمام متون خطا با فراخوانی‌های LocalizationService

### ویژگی‌های پیاده‌سازی شده:

- ✅ پشتیبانی از 4 زبان: فارسی، عربی، انگلیسی، آلمانی
- ✅ تغییر زبان به صورت زنده بدون نیاز به راه‌اندازی مجدد
- ✅ ذخیره زبان انتخابی در تنظیمات
- ✅ بارگذاری خودکار زبان از تنظیمات در زمان راه‌اندازی
- ✅ به‌روزرسانی خودکار تمام متون رابط کاربری با تغییر زبان

### فایل‌های ایجاد شده:

- `Services/LocalizationService.cs`
- `Resources/Languages/fa.json`
- `Resources/Languages/ar.json`
- `Resources/Languages/en.json`
- `Resources/Languages/de.json`

### فایل‌های تغییر یافته:

- `MainForm.cs`
- `Models/ScanSettings.cs`
- `Services/SettingsService.cs`
- `Services/ErrorHandler.cs`
- `Program.cs`

### تاریخ و ساعت: 2025-12-17 - 09:55

