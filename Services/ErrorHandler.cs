using System;
using System.Windows.Forms;
using MethodInvoker = System.Windows.Forms.MethodInvoker;

namespace Ntk.Mikrotik.Tools.Services
{
    /// <summary>
    /// کلاس مدیریت خطاها و نمایش پیام‌های خطا به کاربر
    /// این کلاس مسئول نمایش خطاها به صورت کاربرپسند و جلوگیری از crash برنامه است
    /// </summary>
    public class ErrorHandler
    {
        /// <summary>
        /// نمایش خطا با پیام پشتیبانی
        /// این متد خطا را به صورت کاربرپسند نمایش می‌دهد و از crash برنامه جلوگیری می‌کند
        /// </summary>
        /// <param name="ex">خطای رخ داده</param>
        /// <param name="context">متن توضیحی درباره محل وقوع خطا</param>
        /// <param name="terminalLog">اختیاری: کنترل RichTextBox برای لاگ ترمینال (در صورت وجود)</param>
        public static void ShowErrorWithSupport(Exception ex, string context = "", System.Windows.Forms.RichTextBox? terminalLog = null)
        {
            try
            {
                var loc = LocalizationService.Instance;
                var errorMessage = string.Format(loc.GetString("ErrorInContext", "خطا در {0}"), context) + $":\n\n{ex.Message}";
                
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
                    loc.GetString("MsgError", "خطا"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                
                // لاگ خطا در ترمینال (اگر ارائه شده باشد)
                if (terminalLog != null && terminalLog.IsHandleCreated)
                {
                    try
                    {
                        if (terminalLog.InvokeRequired)
                        {
                            terminalLog.Invoke((MethodInvoker)delegate
                            {
                                try
                                {
                                    // اضافه کردن خطا با رنگ قرمز
                                    var errorText = $"[{DateTime.Now:HH:mm:ss}] [ERROR] {context}: {ex.Message}\r\n";
                                    terminalLog.SelectionStart = terminalLog.Text.Length;
                                    terminalLog.SelectionLength = 0;
                                    terminalLog.SelectionColor = System.Drawing.Color.Red;
                                    terminalLog.AppendText(errorText);
                                    
                                    if (ex.InnerException != null)
                                    {
                                        var innerErrorText = $"[{DateTime.Now:HH:mm:ss}] [ERROR] Inner: {ex.InnerException.Message}\r\n";
                                        terminalLog.SelectionColor = System.Drawing.Color.Red;
                                        terminalLog.AppendText(innerErrorText);
                                    }
                                    
                                    terminalLog.SelectionColor = terminalLog.ForeColor; // بازگشت به رنگ پیش‌فرض
                                    terminalLog.SelectionStart = terminalLog.Text.Length;
                                    terminalLog.ScrollToCaret();
                                }
                                catch
                                {
                                    // Ignore errors in terminal log update
                                }
                            });
                        }
                        else
                        {
                            try
                            {
                                // اضافه کردن خطا با رنگ قرمز
                                var errorText = $"[{DateTime.Now:HH:mm:ss}] [ERROR] {context}: {ex.Message}\r\n";
                                terminalLog.SelectionStart = terminalLog.Text.Length;
                                terminalLog.SelectionLength = 0;
                                terminalLog.SelectionColor = System.Drawing.Color.Red;
                                terminalLog.AppendText(errorText);
                                
                                if (ex.InnerException != null)
                                {
                                    var innerErrorText = $"[{DateTime.Now:HH:mm:ss}] [ERROR] Inner: {ex.InnerException.Message}\r\n";
                                    terminalLog.SelectionColor = System.Drawing.Color.Red;
                                    terminalLog.AppendText(innerErrorText);
                                }
                                
                                terminalLog.SelectionColor = terminalLog.ForeColor; // بازگشت به رنگ پیش‌فرض
                                terminalLog.SelectionStart = terminalLog.Text.Length;
                                terminalLog.ScrollToCaret();
                            }
                            catch
                            {
                                // Ignore errors in terminal log update
                            }
                        }
                    }
                    catch
                    {
                        // اگر لاگ خطا داد، نادیده بگیر
                    }
                }
            }
            catch
            {
                // اگر نمایش خطا هم خطا داد، حداقل یک پیام ساده نمایش بده
                try
                {
                    var loc = LocalizationService.Instance;
                    MessageBox.Show(
                        $"{loc.GetString("ErrorUnexpected", "خطای غیرمنتظره رخ داد. لطفاً به پشتیبانی اطلاع دهید.")}\n\n{ex.Message}",
                        loc.GetString("MsgError", "خطا"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch
                {
                    // اگر حتی این هم خطا داد، هیچ کاری نکن - برنامه crash نمی‌کند
                }
            }
        }
    }
}

