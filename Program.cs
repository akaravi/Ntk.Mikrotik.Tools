using System;
using System.Windows.Forms;
using System.Drawing;

namespace Ntk.Mikrotik.Tools
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Try to set application icon before creating main form
            SetApplicationIcon();
            
            Application.Run(new MainForm());
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

