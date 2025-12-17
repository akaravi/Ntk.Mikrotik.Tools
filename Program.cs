using System;
using System.Windows.Forms;
using System.Drawing;
using Ntk.Mikrotik.Tools.Services;

namespace Ntk.Mikrotik.Tools
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                // Set up global exception handler BEFORE creating any forms or controls
                // This must be done before any Controls are created
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, e) =>
                {
                    try
                    {
                        var loc = LocalizationService.Instance;
                        var errorMessage = $"{loc.GetString("ErrorUnexpectedInApp", "خطای غیرمنتظره در برنامه")}:\n\n{e.Exception.Message}";
                        
                        if (e.Exception.InnerException != null)
                        {
                            errorMessage += $"\n\n{loc.GetString("ErrorInner", "خطای داخلی")}: {e.Exception.InnerException.Message}";
                        }
                        
                        errorMessage += $"\n\n{loc.GetString("ErrorType", "نوع خطا")}: {e.Exception.GetType().Name}";
                        
                        if (!string.IsNullOrEmpty(e.Exception.StackTrace))
                        {
                            errorMessage += $"\n\n{loc.GetString("ErrorTechnicalDetails", "جزئیات فنی")}:\n{e.Exception.StackTrace.Substring(0, Math.Min(500, e.Exception.StackTrace.Length))}...";
                        }
                        
                        errorMessage += $"\n\n{loc.GetString("ErrorContactSupport", "⚠️ اگر مشکل ادامه داشت، لطفاً این پیام را به پشتیبانی اطلاع دهید.")}";
                        
                        MessageBox.Show(
                            errorMessage,
                            loc.GetString("ErrorUnexpectedInApp", "خطای غیرمنتظره در برنامه"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    catch
                    {
                        // If even showing error fails, try simple message
                        try
                        {
                            var loc = LocalizationService.Instance;
                            MessageBox.Show(
                                $"{loc.GetString("ErrorUnexpected", "خطای غیرمنتظره رخ داد. لطفاً به پشتیبانی اطلاع دهید.")}\n\n{e.Exception.Message}",
                                loc.GetString("MsgError", "خطا"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                        catch
                        {
                            // Ignore - prevent crash
                        }
                    }
                };
                
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    try
                    {
                        if (e.ExceptionObject is Exception ex)
                        {
                            var loc = LocalizationService.Instance;
                            var errorMessage = $"{loc.GetString("ErrorUnexpectedInDomain", "خطای غیرمنتظره در دامنه برنامه")}:\n\n{ex.Message}";
                            
                            if (ex.InnerException != null)
                            {
                                errorMessage += $"\n\n{loc.GetString("ErrorInner", "خطای داخلی")}: {ex.InnerException.Message}";
                            }
                            
                            errorMessage += $"\n\n{loc.GetString("ErrorType", "نوع خطا")}: {ex.GetType().Name}";
                            
                            if (!string.IsNullOrEmpty(ex.StackTrace))
                            {
                                errorMessage += $"\n\n{loc.GetString("ErrorTechnicalDetails", "جزئیات فنی")}:\n{ex.StackTrace.Substring(0, Math.Min(500, ex.StackTrace.Length))}...";
                            }
                            
                            errorMessage += $"\n\n{loc.GetString("ErrorContactSupport", "⚠️ اگر مشکل ادامه داشت، لطفاً این پیام را به پشتیبانی اطلاع دهید.")}";
                            
                            MessageBox.Show(
                                errorMessage,
                                loc.GetString("ErrorUnexpectedInDomain", "خطای غیرمنتظره در دامنه برنامه"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                    catch
                    {
                        // Ignore errors in exception handler
                    }
                };
                
                // Try to set application icon before creating main form
                SetApplicationIcon();
                
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                // Global exception handler - prevent application crash
                var loc = LocalizationService.Instance;
                var errorMessage = $"{loc.GetString("ErrorUnexpectedInApp", "خطای غیرمنتظره در برنامه")}:\n\n{ex.Message}\n\n" +
                                  $"{loc.GetString("ErrorType", "نوع خطا")}: {ex.GetType().Name}\n\n" +
                                  $"{loc.GetString("ErrorContactSupport", "لطفاً این پیام را به پشتیبانی اطلاع دهید.")}";
                
                MessageBox.Show(
                    errorMessage,
                    loc.GetString("ErrorApp", "خطای برنامه"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                
                // Log to file if possible
                try
                {
                    var logPath = System.IO.Path.Combine(
                        Application.StartupPath,
                        $"error_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    System.IO.File.WriteAllText(logPath, 
                        $"Error: {ex}\n\nStack Trace:\n{ex.StackTrace}");
                }
                catch
                {
                    // Ignore log file errors
                }
            }
        }
        
        private static void SetApplicationIcon()
        {
            try
            {
                // Try multiple possible locations for the icon
                var possiblePaths = new[]
                {
                    System.IO.Path.Combine(Application.StartupPath, "icon.ico"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "icon.ico"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "icon.ico"), // Project root in debug
                    "icon.ico" // Relative path
                };
                
                Icon? loadedIcon = null;
                
                foreach (var iconPath in possiblePaths)
                {
                    if (System.IO.File.Exists(iconPath))
                    {
                        try
                        {
                            using (var iconStream = new System.IO.FileStream(iconPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                            {
                                loadedIcon = new Icon(iconStream);
                                System.Diagnostics.Debug.WriteLine($"Icon loaded from: {iconPath}");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to load icon from {iconPath}: {ex.Message}");
                            continue;
                        }
                    }
                }
                
                // If icon file not found or invalid, create a simple icon programmatically
                if (loadedIcon == null)
                {
                    System.Diagnostics.Debug.WriteLine("Creating programmatic icon...");
                    loadedIcon = CreateProgrammaticIcon();
                }
                
                if (loadedIcon != null)
                {
                    // Store icon for use by forms
                    ApplicationIcon = loadedIcon;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting icon: {ex.Message}");
                try
                {
                    ApplicationIcon = CreateProgrammaticIcon();
                }
                catch
                {
                    // If all fails, continue without icon
                }
            }
        }
        
        private static Icon CreateProgrammaticIcon()
        {
            // Create a simple 32x32 icon programmatically
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                // Draw a simple network/router icon
                graphics.Clear(Color.White);
                
                // Draw router/antenna symbol
                using (var pen = new Pen(Color.FromArgb(25, 118, 210), 3))
                using (var brush = new SolidBrush(Color.FromArgb(25, 118, 210)))
                {
                    // Draw a simple router box
                    graphics.FillRectangle(brush, 8, 10, 16, 12);
                    graphics.DrawRectangle(pen, 8, 10, 16, 12);
                    
                    // Draw antenna lines
                    graphics.DrawLine(pen, 12, 6, 12, 10);
                    graphics.DrawLine(pen, 20, 6, 20, 10);
                    
                    // Draw signal waves
                    graphics.DrawArc(pen, 4, 14, 8, 8, 0, 180);
                    graphics.DrawArc(pen, 20, 14, 8, 8, 0, 180);
                }
                
                // Convert bitmap to icon
                // Note: GetHicon() returns a handle that must be kept alive
                // We need to clone the icon to avoid handle issues
                var hIcon = bitmap.GetHicon();
                var icon = Icon.FromHandle(hIcon);
                // Clone to create a standalone icon that doesn't depend on the handle
                return (Icon)icon.Clone();
            }
        }
        
        public static Icon? ApplicationIcon { get; private set; }
    }
}

