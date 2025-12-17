using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ntk.Mikrotik.Tools.Models;
using Ntk.Mikrotik.Tools.Services;
using MethodInvoker = System.Windows.Forms.MethodInvoker;
using System.Drawing;
using SettingsValidationResult = Ntk.Mikrotik.Tools.Services.ValidationResult;
using ScottPlotWinForms = ScottPlot.WinForms;

namespace Ntk.Mikrotik.Tools
{
    // Custom header cell with sort icons
    public class SortableHeaderCell : DataGridViewColumnHeaderCell
    {
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        private bool _isSorted = false;

        public ListSortDirection SortDirection
        {
            get => _sortDirection;
            set
            {
                _sortDirection = value;
                _isSorted = true;
            }
        }

        public bool IsSorted
        {
            get => _isSorted;
            set => _isSorted = value;
        }

        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates cellState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
        {
            // Paint the base header first
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);

            // Draw sort arrow if sorted
            if (_isSorted && (paintParts & DataGridViewPaintParts.ContentForeground) != 0)
            {
                // Draw sort arrow - make it more visible
                var arrowSize = 10;
                var arrowX = cellBounds.Right - arrowSize - 8;
                var arrowY = cellBounds.Top + (cellBounds.Height - arrowSize) / 2;
                
                // Ensure arrow is within bounds
                if (arrowX >= cellBounds.Left && arrowX + arrowSize <= cellBounds.Right &&
                    arrowY >= cellBounds.Top && arrowY + arrowSize <= cellBounds.Bottom)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(64, 64, 64))) // Dark gray for better visibility
                    {
                        if (_sortDirection == ListSortDirection.Ascending)
                        {
                            // Up arrow (▲) - pointing up
                            Point[] points = new Point[]
                            {
                                new Point(arrowX, arrowY + arrowSize),
                                new Point(arrowX + arrowSize / 2, arrowY),
                                new Point(arrowX + arrowSize, arrowY + arrowSize)
                            };
                            graphics.FillPolygon(brush, points);
                        }
                        else
                        {
                            // Down arrow (▼) - pointing down
                            Point[] points = new Point[]
                            {
                                new Point(arrowX, arrowY),
                                new Point(arrowX + arrowSize / 2, arrowY + arrowSize),
                                new Point(arrowX + arrowSize, arrowY)
                            };
                            graphics.FillPolygon(brush, points);
                        }
                    }
                }
            }
        }
    }

    public partial class MainForm : Form
    {
        private FrequencyScanner? _scanner;
        private CancellationTokenSource? _cancellationTokenSource;
        private JsonDataService _jsonService;
        private SettingsService _settingsService;
        private ConnectionService _connectionService;
        private DataFilterService _dataFilterService;
        private LocalizationService _localizationService;
        private BindingList<FrequencyScanResult> _currentResults;
        private BindingSource? _bindingSource;
        private List<FrequencyScanResult> _allResults; // Store all results for filtering
        private MikroTikSshClient? _sshClient;
        private bool _isConnected = false;
        
        // Base settings to restore after scan
        private FrequencyScanResult? _baseSettings;
        
        // Language selector
        private ComboBox? _cmbLanguage;
        
        // Control references
        private TextBox? _txtRouterIp;
        private NumericUpDown? _txtSshPort;
        private TextBox? _txtUsername;
        private TextBox? _txtPassword;
        private NumericUpDown? _txtStartFreq;
        private NumericUpDown? _txtEndFreq;
        private NumericUpDown? _txtFreqStep;
        private NumericUpDown? _txtStabilizationTime;
        private TextBox? _txtInterface;
        private TextBox? _txtPingIp;
        private Label? _lblStatus;
        private ProgressBar? _progressBar;
        private Button? _btnStart;
        private Button? _btnStop;
        private DataGridView? _dgvResults;
        private RichTextBox? _txtTerminalLog;
        private Dictionary<string, TextBox>? _filterTextBoxes;
        private Dictionary<string, string>? _columnNameToPropertyMap; // Map column name to property name

        public MainForm()
        {
            // Initialize fields before InitializeComponent (which calls CreateResultsAndTerminalTab)
            _jsonService = new JsonDataService();
            _settingsService = new SettingsService();
            _connectionService = new ConnectionService();
            _dataFilterService = new DataFilterService();
            _localizationService = LocalizationService.Instance;
            _currentResults = new BindingList<FrequencyScanResult>();
            _allResults = new List<FrequencyScanResult>();
            _bindingSource = new BindingSource();
            _columnNameToPropertyMap = new Dictionary<string, string>();
            
            // Subscribe to language change event
            _localizationService.LanguageChanged += (s, e) => UpdateAllTexts();
            
            try
            {
                // Load settings first to get language preference
                var settings = _settingsService.LoadSettings();
                _localizationService.LoadLanguage(settings.Language ?? "fa");
                
                InitializeComponent();
                LoadSettings();
                UpdateAllTexts();
            }
            catch (Exception ex)
            {
                // اگر حتی ساخت فرم هم خطا داد، خطا را نمایش بده
                try
                {
                    var loc = LocalizationService.Instance;
                    var errorDetails = string.Format(loc.GetString("ErrorInContext", "خطا در {0}"), loc.GetString("ErrorStartup", "راه‌اندازی برنامه")) + $":\n\n{ex.Message}";
                    
                    if (ex.InnerException != null)
                    {
                        errorDetails += $"\n\n{loc.GetString("ErrorInner", "خطای داخلی")}: {ex.InnerException.Message}";
                    }
                    
                    errorDetails += $"\n\n{loc.GetString("ErrorType", "نوع خطا")}: {ex.GetType().Name}";
                    
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        errorDetails += $"\n\n{loc.GetString("ErrorTechnicalDetails", "جزئیات فنی")}:\n{ex.StackTrace.Substring(0, Math.Min(500, ex.StackTrace.Length))}...";
                    }
                    
                    errorDetails += $"\n\n{loc.GetString("ErrorContactSupport", "⚠️ اگر مشکل ادامه داشت، لطفاً این پیام را به پشتیبانی اطلاع دهید.")}";
                    
                    MessageBox.Show(
                        errorDetails,
                        loc.GetString("ErrorStartup", "خطای راه‌اندازی"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    
                    // Log to debug output
                    System.Diagnostics.Debug.WriteLine($"Startup Error: {ex}");
                }
                catch
                {
                    // اگر حتی نمایش خطا هم خطا داد، برنامه را ببند
                    try
                    {
                        Application.Exit();
                    }
                    catch
                    {
                        // Ignore - prevent crash
                    }
                }
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form
            var loc = _localizationService;
            this.Text = loc.GetString("FormTitle", "اسکنر فرکانس میکروتیک");
            this.Size = new System.Drawing.Size(1400, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.Font = new System.Drawing.Font("Tahoma", 9F);
            
            // Set application icon
            SetApplicationIcon();

            // Top Panel for buttons and status
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110, // Fixed height to accommodate all controls
                Padding = new Padding(10),
                BackColor = System.Drawing.Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Status label (at top, fixed height)
            _lblStatus = new Label
            {
                Text = loc.GetString("StatusReady", "آماده"),
                Dock = DockStyle.Top,
                Height = 25,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 5)
            };

            // Progress bar (below status label, fixed height)
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 25,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 0, 0, 5)
            };

            // Buttons panel (at bottom, fixed height)
            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 5, 8, 5)
            };

            // Helper method to create styled button with icon (با سایه و حالت سه‌بعدی ظریف)
            Button CreateStyledButton(string text, string icon, Color backColor, int width = 110, int height = 38)
            {
                var btn = new Button
                {
                    Text = $"{icon} {text}",
                    Width = width,
                    Height = height,
                    BackColor = backColor,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold),
                    Cursor = Cursors.Hand
                };

                // حاشیه و سایه ظریف
                btn.FlatAppearance.BorderSize = 0;
                btn.Padding = new Padding(0, 0, 0, 2); // کمی فضای پایین برای حس سایه

                // رنگ‌های هاور و کلیک
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                    Math.Min(255, backColor.R + 20),
                    Math.Min(255, backColor.G + 20),
                    Math.Min(255, backColor.B + 20));
                btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(
                    Math.Max(0, backColor.R - 20),
                    Math.Max(0, backColor.G - 20),
                    Math.Max(0, backColor.B - 20));

                // سایه ساده با رویداد Paint
                btn.Paint += (s, e) =>
                {
                    var shadowColor = Color.FromArgb(60, 0, 0, 0);
                    var shadowRect = new Rectangle(2, 2, btn.Width - 4, btn.Height - 4);
                    using (var shadowBrush = new SolidBrush(shadowColor))
                    {
                        e.Graphics.FillRectangle(shadowBrush, shadowRect);
                    }
                    // نوار بالایی روشن برای حس عمق
                    using (var topHighlight = new Pen(Color.FromArgb(50, Color.White), 1))
                    {
                        e.Graphics.DrawLine(topHighlight, 2, 2, btn.Width - 4, 2);
                    }
                };

                return btn;
            }

            _btnStart = CreateStyledButton(loc.GetString("BtnStartScan", "شروع اسکن"), "▶", Color.FromArgb(46, 125, 50), 110, 38);
            _btnStop = CreateStyledButton(loc.GetString("BtnStop", "توقف"), "⏹", Color.FromArgb(198, 40, 40), 110, 38);
            _btnStop.Enabled = false;
            _btnStop.BackColor = Color.FromArgb(150, 150, 150);
            
            var btnConnect = CreateStyledButton(loc.GetString("BtnConnect", "اتصال"), "🔌", Color.FromArgb(25, 118, 210), 110, 38);
            btnConnect.Name = "btnConnect";
            
            var btnDisconnect = CreateStyledButton(loc.GetString("BtnDisconnect", "قطع اتصال"), "🔌❌", Color.FromArgb(198, 40, 40), 120, 38);
            btnDisconnect.Enabled = false;
            btnDisconnect.BackColor = Color.FromArgb(150, 150, 150);
            btnDisconnect.Name = "btnDisconnect";
            
            var btnTestReconnect = CreateStyledButton(loc.GetString("BtnTestReconnect", "تست اتصال مجدد"), "🔄", Color.FromArgb(123, 31, 162), 140, 38);
            btnTestReconnect.Name = "btnTestReconnect";
            
            var btnStatus = CreateStyledButton(loc.GetString("BtnStatus", "وضعیت"), "📊", Color.FromArgb(0, 150, 136), 110, 38);
            btnStatus.Name = "btnStatus";
            btnStatus.Enabled = false;
            btnStatus.BackColor = Color.FromArgb(150, 150, 150);
            
            // Add hover effects
            void AddHoverEffect(Button btn, Color originalColor)
            {
                btn.MouseEnter += (s, e) => { if (btn.Enabled) btn.BackColor = Color.FromArgb(Math.Min(255, originalColor.R + 20), Math.Min(255, originalColor.G + 20), Math.Min(255, originalColor.B + 20)); };
                btn.MouseLeave += (s, e) => { if (btn.Enabled) btn.BackColor = originalColor; };
            }
            
            AddHoverEffect(_btnStart, Color.FromArgb(46, 125, 50));
            AddHoverEffect(_btnStop, Color.FromArgb(198, 40, 40));
            AddHoverEffect(btnConnect, Color.FromArgb(25, 118, 210));
            AddHoverEffect(btnDisconnect, Color.FromArgb(198, 40, 40));
            AddHoverEffect(btnTestReconnect, Color.FromArgb(123, 31, 162));
            AddHoverEffect(btnStatus, Color.FromArgb(0, 150, 136));

            // Add event handlers
            btnConnect.Click += async (s, e) => await ConnectToRouterAsync();
            btnDisconnect.Click += (s, e) => DisconnectFromRouter();
            _btnStart.Click += async (s, e) => await StartScanAsync();
            _btnStop.Click += (s, e) => StopScan();
            btnTestReconnect.Click += async (s, e) => await TestReconnectionAsync();
            btnStatus.Click += async (s, e) => await GetCurrentStatusAsync();

            buttonsPanel.Controls.Add(_btnStop);
            buttonsPanel.Controls.Add(_btnStart);
            buttonsPanel.Controls.Add(btnStatus);
            buttonsPanel.Controls.Add(btnTestReconnect);
            buttonsPanel.Controls.Add(btnDisconnect);
            buttonsPanel.Controls.Add(btnConnect);

            // Add controls to topPanel in correct order (top to bottom)
            topPanel.Controls.Add(_lblStatus);
            topPanel.Controls.Add(_progressBar);
            topPanel.Controls.Add(buttonsPanel);

            // Tab Control with custom drawing
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold),
                Appearance = TabAppearance.Normal,
                Padding = new Point(0, 0),
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new System.Drawing.Size(220, 35), // Increased width for better text visibility
                SizeMode = TabSizeMode.Fixed
            };

            // Settings Tab
            // Language selector (add to top panel)
            var languagePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5),
                AutoSize = true
            };
            
            var lblLanguage = new Label
            {
                Text = loc.GetString("Language", "زبان") + ":",
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                AutoSize = true,
                Padding = new Padding(0, 8, 5, 0)
            };
            
            _cmbLanguage = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120,
                Height = 25
            };
            
            foreach (var langCode in _localizationService.AvailableLanguages)
            {
                var langName = _localizationService.LanguageNames[langCode];
                _cmbLanguage.Items.Add(new { Code = langCode, Name = langName });
            }
            
            _cmbLanguage.DisplayMember = "Name";
            _cmbLanguage.ValueMember = "Code";
            _cmbLanguage.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbLanguage.SelectedItem != null)
                {
                    var selectedLang = ((dynamic)_cmbLanguage.SelectedItem).Code;
                    _localizationService.LoadLanguage(selectedLang);
                    
                    // Save language preference
                    var settings = GetSettingsFromForm();
                    settings.Language = selectedLang;
                    _settingsService.SaveSettings(settings);
                    
                    // Update all texts immediately
                    UpdateAllTexts();
                }
            };
            
            // Set current language
            var currentLangIndex = _localizationService.AvailableLanguages.IndexOf(_localizationService.CurrentLanguage);
            if (currentLangIndex >= 0)
            {
                _cmbLanguage.SelectedIndex = currentLangIndex;
            }
            
            languagePanel.Controls.Add(lblLanguage);
            languagePanel.Controls.Add(_cmbLanguage);
            topPanel.Controls.Add(languagePanel);
            topPanel.Controls.SetChildIndex(languagePanel, 0);

            var settingsTab = new TabPage(loc.GetString("TabSettings", "⚙️ تنظیمات"));
            settingsTab.Tag = (Color.FromArgb(25, 118, 210), Color.White); // (BackColor, ForeColor)
            CreateSettingsTab(settingsTab);
            tabControl.TabPages.Add(settingsTab);

            // Results and Terminal Tab (combined)
            var resultsTab = new TabPage(loc.GetString("TabResults", "📊 نتایج و لاگ"));
            resultsTab.Tag = (Color.FromArgb(46, 125, 50), Color.White); // (BackColor, ForeColor)
            CreateResultsAndTerminalTab(resultsTab);
            tabControl.TabPages.Add(resultsTab);

            // Charts Tab
            var chartsTab = new TabPage(loc.GetString("TabCharts", "📈 نمودارها"));
            chartsTab.Tag = (Color.FromArgb(255, 152, 0), Color.White); // (BackColor, ForeColor) - Orange
            CreateChartsTab(chartsTab);
            tabControl.TabPages.Add(chartsTab);

            // About Tab
            var aboutTab = new TabPage(loc.GetString("TabAbout", "ℹ️ درباره ما"));
            aboutTab.Tag = (Color.FromArgb(123, 31, 162), Color.White); // (BackColor, ForeColor)
            CreateAboutTab(aboutTab);
            tabControl.TabPages.Add(aboutTab);

            // Apply custom drawing to tabs after they are added
            tabControl.DrawItem += (sender, e) =>
            {
                try
                {
                    if (e.Index < 0 || e.Index >= tabControl.TabPages.Count)
                        return;

                    var tab = tabControl.TabPages[e.Index];
                    if (tab == null) return;

                    var rect = e.Bounds;
                    var isSelected = tabControl.SelectedIndex == e.Index;

                    // Get colors from Tag
                    Color backColor = Color.FromArgb(25, 118, 210); // Default blue
                    Color foreColor = Color.White;
                    if (tab.Tag is ValueTuple<Color, Color> colors)
                    {
                        backColor = colors.Item1;
                        foreColor = colors.Item2;
                    }

                    // Shadow rect (زیر تب برای حس عمق)
                    var shadowRect = new Rectangle(rect.X + 2, rect.Bottom - 3, rect.Width - 4, 3);
                    using (var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                    {
                        e.Graphics.FillRectangle(shadowBrush, shadowRect);
                    }

                    // Draw background با گوشه‌های نرم‌تر
                    var bgColor = isSelected ? backColor : Color.FromArgb(245, 245, 245);
                    using (var bgBrush = new SolidBrush(bgColor))
                    {
                        var innerRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 4);
                        e.Graphics.FillRectangle(bgBrush, innerRect);
                    }

                    // Draw border for selected tab با خط بالایی روشن
                    if (isSelected)
                    {
                        using (var borderPen = new Pen(backColor, 2))
                        {
                            var borderRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 4);
                            e.Graphics.DrawRectangle(borderPen, borderRect);
                        }
                        using (var highlightPen = new Pen(Color.FromArgb(80, Color.White), 1))
                        {
                            e.Graphics.DrawLine(highlightPen, rect.X + 2, rect.Y + 2, rect.Right - 2, rect.Y + 2);
                        }
                    }

                    // Draw text with icon - ensure proper spacing and visibility
                    var text = tab.Text ?? "";
                    var textColor = isSelected ? foreColor : Color.Black;
                    
                    // Calculate text rectangle with padding for better visibility
                    var textRect = new RectangleF(
                        rect.X + 10, 
                        rect.Y + 5, 
                        rect.Width - 20, 
                        rect.Height - 10
                    );
                    
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap
                    };
                    
                    // Use TextRenderer for better text rendering with emoji/icon support
                    TextRenderer.DrawText(
                        e.Graphics,
                        text,
                        tabControl.Font,
                        Rectangle.Round(textRect),
                        textColor,
                        TextFormatFlags.HorizontalCenter | 
                        TextFormatFlags.VerticalCenter | 
                        TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis
                    );
                }
                catch (Exception ex)
                {
                    // Fallback: use default drawing
                    e.DrawBackground();
                    e.DrawFocusRectangle();
                    System.Diagnostics.Debug.WriteLine($"Error drawing tab: {ex.Message}");
                }
            };

            // Add controls to form - order matters for Dock layout
            // tabControl with Dock=Fill should be added first, then topPanel with Dock=Top
            // This ensures topPanel appears on top and tabControl fills remaining space
            this.Controls.Add(tabControl);
            this.Controls.Add(topPanel); // Add topPanel last so it appears on top
            
            this.ResumeLayout(true);
            this.PerformLayout();
        }

        private void CreateSettingsTab(TabPage tab)
        {
            // Outer panel with scrolling to ensure buttons remain fully visible on small screens
            var outerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(0)
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 17,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

            // Top action buttons (moved to top for visibility)
            var buttonPanelTop = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
            
            Button CreateStyledButton2(string text, string icon, Color backColor, int width = 200, int height = 35)
            {
                var btn = new Button
                {
                    Text = $"{icon} {text}",
                    Size = new System.Drawing.Size(width, height),
                    BackColor = backColor,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold),
                    Cursor = Cursors.Hand
                };

                // حاشیه و سایه ظریف
                btn.FlatAppearance.BorderSize = 0;
                btn.Padding = new Padding(0, 0, 0, 2);
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                    Math.Min(255, backColor.R + 20),
                    Math.Min(255, backColor.G + 20),
                    Math.Min(255, backColor.B + 20));
                btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(
                    Math.Max(0, backColor.R - 20),
                    Math.Max(0, backColor.G - 20),
                    Math.Max(0, backColor.B - 20));

                btn.Paint += (s, e) =>
                {
                    var shadowColor = Color.FromArgb(60, 0, 0, 0);
                    var shadowRect = new Rectangle(2, 2, btn.Width - 4, btn.Height - 4);
                    using (var shadowBrush = new SolidBrush(shadowColor))
                    {
                        e.Graphics.FillRectangle(shadowBrush, shadowRect);
                    }
                    using (var topHighlight = new Pen(Color.FromArgb(50, Color.White), 1))
                    {
                        e.Graphics.DrawLine(topHighlight, 2, 2, btn.Width - 4, 2);
                    }
                };

                return btn;
            }
            
            var btnSave = CreateStyledButton2("ذخیره تنظیمات", "💾", Color.FromArgb(46, 125, 50));
            btnSave.Name = "btnSave";
            
            var btnLoadResults = CreateStyledButton2("بارگذاری نتایج قبلی", "📂", Color.FromArgb(25, 118, 210));
            btnLoadResults.Name = "btnLoadResults";
            
            var btnResetDefaults = CreateStyledButton2("بازگشت به پیش‌فرض", "🔄", Color.FromArgb(255, 152, 0));
            btnResetDefaults.Name = "btnResetDefaults";
            
            // ترتیب منطقی: بازگشت به پیش‌فرض، بارگذاری نتایج، ذخیره تنظیمات
            buttonPanelTop.Controls.Add(btnResetDefaults);
            buttonPanelTop.Controls.Add(btnLoadResults);
            buttonPanelTop.Controls.Add(btnSave);
            
            btnLoadResults.Click += (s, e) => LoadPreviousResults();
            btnSave.Click += (s, e) => SaveSettings();
            btnResetDefaults.Click += (s, e) => ResetToDefaults();
            
            panel.SetColumnSpan(buttonPanelTop, 2);
            panel.Controls.Add(buttonPanelTop, 0, row++);
            
            // Language selector in settings tab
            var loc = _localizationService;
            var languageSettingsPanel = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Fill, 
                FlowDirection = FlowDirection.RightToLeft, 
                Padding = new Padding(5),
                AutoSize = true
            };
            
            var lblLanguageSettings = new Label
            {
                Text = loc.GetString("Language", "زبان") + ":",
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                AutoSize = true,
                Padding = new Padding(0, 8, 5, 0),
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
            };
            
            var cmbLanguageSettings = new ComboBox
            {
                Name = "cmbLanguageSettings",
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
                Height = 25
            };
            
            foreach (var langCode in _localizationService.AvailableLanguages)
            {
                var langName = _localizationService.LanguageNames[langCode];
                cmbLanguageSettings.Items.Add(new { Code = langCode, Name = langName });
            }
            
            cmbLanguageSettings.DisplayMember = "Name";
            cmbLanguageSettings.ValueMember = "Code";
            cmbLanguageSettings.SelectedIndexChanged += (s, e) =>
            {
                if (cmbLanguageSettings.SelectedItem != null)
                {
                    var selectedLang = ((dynamic)cmbLanguageSettings.SelectedItem).Code;
                    _localizationService.LoadLanguage(selectedLang);
                    
                    // Update main language selector too
                    if (_cmbLanguage != null)
                    {
                        var mainLangIndex = _localizationService.AvailableLanguages.IndexOf(selectedLang);
                        if (mainLangIndex >= 0)
                        {
                            _cmbLanguage.SelectedIndex = mainLangIndex;
                        }
                    }
                    
                    // Save language preference
                    var settings = GetSettingsFromForm();
                    settings.Language = selectedLang;
                    _settingsService.SaveSettings(settings);
                    
                    // Update all texts immediately
                    UpdateAllTexts();
                }
            };
            
            // Set current language
            var currentLangIndexSettings = _localizationService.AvailableLanguages.IndexOf(_localizationService.CurrentLanguage);
            if (currentLangIndexSettings >= 0)
            {
                cmbLanguageSettings.SelectedIndex = currentLangIndexSettings;
            }
            
            languageSettingsPanel.Controls.Add(cmbLanguageSettings);
            languageSettingsPanel.Controls.Add(lblLanguageSettings);
            
            panel.SetColumnSpan(languageSettingsPanel, 2);
            panel.Controls.Add(languageSettingsPanel, 0, row++);

            // Router IP
            var lblRouterIp = new Label { Name = "lblRouterIp", Text = loc.GetString("LabelRouterIp", "آدرس IP روتر:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblRouterIp, 0, row);
            _txtRouterIp = new TextBox { Name = "txtRouterIp", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtRouterIp, 1, row++);

            // SSH Port
            var lblSshPort = new Label { Name = "lblSshPort", Text = loc.GetString("LabelSshPort", "پورت SSH:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblSshPort, 0, row);
            _txtSshPort = new NumericUpDown { Name = "txtSshPort", Minimum = 1, Maximum = 65535, Value = 22, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtSshPort, 1, row++);

            // Username
            var lblUsername = new Label { Name = "lblUsername", Text = loc.GetString("LabelUsername", "نام کاربری:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblUsername, 0, row);
            _txtUsername = new TextBox { Name = "txtUsername", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtUsername, 1, row++);

            // Password
            var lblPassword = new Label { Name = "lblPassword", Text = loc.GetString("LabelPassword", "رمز عبور:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblPassword, 0, row);
            _txtPassword = new TextBox { Name = "txtPassword", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtPassword, 1, row++);

            // Start Frequency
            var lblStartFreq = new Label { Name = "lblStartFreq", Text = loc.GetString("LabelStartFrequency", "فرکانس شروع (MHz):"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblStartFreq, 0, row);
            _txtStartFreq = new NumericUpDown { Name = "txtStartFreq", Minimum = 1000, Maximum = 6000, Value = 2400, DecimalPlaces = 0, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtStartFreq, 1, row++);

            // End Frequency
            var lblEndFreq = new Label { Name = "lblEndFreq", Text = loc.GetString("LabelEndFrequency", "فرکانس پایان (MHz):"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblEndFreq, 0, row);
            _txtEndFreq = new NumericUpDown { Name = "txtEndFreq", Minimum = 1000, Maximum = 6000, Value = 2500, DecimalPlaces = 0, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtEndFreq, 1, row++);

            // Frequency Step
            var lblFreqStep = new Label { Name = "lblFreqStep", Text = loc.GetString("LabelFrequencyStep", "پرش فرکانس (MHz):"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblFreqStep, 0, row);
            _txtFreqStep = new NumericUpDown { Name = "txtFreqStep", Minimum = 1, Maximum = 100, Value = 5, DecimalPlaces = 0, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtFreqStep, 1, row++);

            // Stabilization Time
            var lblStabilizationTime = new Label { Name = "lblStabilizationTime", Text = loc.GetString("LabelStabilizationTime", "زمان استیبل شدن (دقیقه):"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblStabilizationTime, 0, row);
            _txtStabilizationTime = new NumericUpDown { Name = "txtStabilizationTime", Minimum = 1, Maximum = 60, Value = 2, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtStabilizationTime, 1, row++);

            // Interface Name
            var lblInterface = new Label { Name = "lblInterface", Text = loc.GetString("LabelInterfaceName", "نام اینترفیس:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblInterface, 0, row);
            _txtInterface = new TextBox { Name = "txtInterface", Text = "wlan1", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtInterface, 1, row++);

            // Ping Test IP Address
            var lblPingIp = new Label { Name = "lblPingIp", Text = loc.GetString("LabelPingTestIp", "آدرس IP تست پینگ:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblPingIp, 0, row);
            _txtPingIp = new TextBox { Name = "txtPingIp", Text = "8.8.8.8", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtPingIp, 1, row++);

            // Wireless Protocols (multiple, comma or newline separated)
            var lblWirelessProtocols = new Label { Name = "lblWirelessProtocols", Text = loc.GetString("LabelWirelessProtocols", "Wireless Protocols\n(جدا شده با کاما یا خط جدید):"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblWirelessProtocols, 0, row);
            var txtWirelessProtocols = new TextBox { Name = "txtWirelessProtocols", Multiline = true, Height = 60, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            panel.Controls.Add(txtWirelessProtocols, 1, row++);

            // Channel Widths (multiple, comma or newline separated)
            var lblChannelWidths = new Label { Name = "lblChannelWidths", Text = loc.GetString("LabelChannelWidths", "Channel Widths\n(جدا شده با کاما یا خط جدید):"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblChannelWidths, 0, row);
            var txtChannelWidths = new TextBox { Name = "txtChannelWidths", Multiline = true, Height = 60, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            panel.Controls.Add(txtChannelWidths, 1, row++);

            // Commands Section
            var lblCommands = new Label { Name = "lblCommands", Text = loc.GetString("LabelRouterOSCommands", "کامندهای RouterOS (پیشرفته):"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold) };
            panel.SetColumnSpan(lblCommands, 2);
            panel.Controls.Add(lblCommands, 0, row++);

            // Command Validate Interface (اول باید چک شود)
            var lblCmdValidateInterface = new Label { Name = "lblCmdValidateInterface", Text = loc.GetString("LabelCmdValidateInterface", "کامند اعتبارسنجی اینترفیس:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdValidateInterface, 0, row);
            var txtCmdValidateInterface = new TextBox { Name = "txtCmdValidateInterface", Text = "/interface wireless print", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdValidateInterface, 1, row++);

            // Command Get Frequency
            var lblCmdGetFreq = new Label { Name = "lblCmdGetFreq", Text = loc.GetString("LabelCmdGetFrequency", "کامند دریافت فرکانس:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdGetFreq, 0, row);
            var txtCmdGetFreq = new TextBox { Name = "txtCmdGetFreq", Text = "/interface wireless print where name=\"{interface}\" value-name=frequency", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdGetFreq, 1, row++);

            // Command Get Interface Info
            var lblCmdGetInfo = new Label { Name = "lblCmdGetInfo", Text = loc.GetString("LabelCmdGetInfo", "کامند دریافت اطلاعات:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdGetInfo, 0, row);
            var txtCmdGetInfo = new TextBox { Name = "txtCmdGetInfo", Text = "/interface wireless print detail where name=\"{interface}\"", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdGetInfo, 1, row++);

            // Command Get Registration Table
            var lblCmdRegTable = new Label { Name = "lblCmdRegTable", Text = loc.GetString("LabelCmdRegTable", "کامند Registration Table:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdRegTable, 0, row);
            var txtCmdRegTable = new TextBox { Name = "txtCmdRegTable", Text = "/interface wireless registration-table print stat where interface=\"{interface}\"", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdRegTable, 1, row++);

            // Command Monitor
            var lblCmdMonitor = new Label { Name = "lblCmdMonitor", Text = loc.GetString("LabelCmdMonitor", "کامند Monitor:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdMonitor, 0, row);
            var txtCmdMonitor = new TextBox { Name = "txtCmdMonitor", Text = "/interface wireless monitor \"{interface}\" once", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdMonitor, 1, row++);

            // Command Set Frequency
            var lblCmdSetFreq = new Label { Name = "lblCmdSetFreq", Text = loc.GetString("LabelCmdSetFrequency", "کامند تنظیم فرکانس:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdSetFreq, 0, row);
            var txtCmdSetFreq = new TextBox { Name = "txtCmdSetFreq", Text = "/interface wireless set \"{interface}\" frequency={frequency}", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdSetFreq, 1, row++);

            // Command Set Wireless Protocol
            var lblCmdSetProtocol = new Label { Name = "lblCmdSetProtocol", Text = loc.GetString("LabelCmdSetProtocol", "کامند تنظیم Wireless Protocol:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdSetProtocol, 0, row);
            var txtCmdSetProtocol = new TextBox { Name = "txtCmdSetProtocol", Text = "/interface wireless set \"{interface}\" wireless-protocol={protocol}", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdSetProtocol, 1, row++);

            // Command Set Channel Width
            var lblCmdSetChannelWidth = new Label { Name = "lblCmdSetChannelWidth", Text = loc.GetString("LabelCmdSetChannelWidth", "کامند تنظیم Channel Width:"), TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            panel.Controls.Add(lblCmdSetChannelWidth, 0, row);
            var txtCmdSetChannelWidth = new TextBox { Name = "txtCmdSetChannelWidth", Text = "/interface wireless set \"{interface}\" channel-width={channelWidth}", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdSetChannelWidth, 1, row++);

            outerPanel.Controls.Add(panel);
            tab.Controls.Add(outerPanel);
        }

        private void CreateResultsAndTerminalTab(TabPage tab)
        {
            var loc = _localizationService;
            
            // Use SplitContainer to show terminal log (top) and results (bottom)
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Horizontal,
                SplitterDistance = 300, // Terminal log takes 300px, results take the rest
                SplitterWidth = 5
            };

            // Terminal Log Panel (top - Panel1)
            var terminalPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5), BackColor = Color.White };

            var terminalLabel = new Label
            {
                Name = "lblTerminalLog",
                Text = loc.GetString("LabelTerminalLog", "لاگ ترمینال:"),
                Dock = DockStyle.Top,
                Height = 25,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
            };

            _txtTerminalLog = new RichTextBox
            {
                Name = "txtTerminalLog",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new System.Drawing.Font("Consolas", 9F),
                BackColor = System.Drawing.Color.Black,
                ForeColor = System.Drawing.Color.LimeGreen
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(5)
            };

            var btnClear = new Button
            {
                Name = "btnClear",
                Text = loc.GetString("BtnClear", "🗑 پاک کردن"),
                Width = 110,
                Height = 28,
                BackColor = Color.FromArgb(198, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClear.FlatAppearance.BorderSize = 0;
            btnClear.MouseEnter += (s, e) => { btnClear.BackColor = Color.FromArgb(218, 60, 60); };
            btnClear.MouseLeave += (s, e) => { btnClear.BackColor = Color.FromArgb(198, 40, 40); };
            btnClear.Click += (s, e) => _txtTerminalLog?.Clear();

            buttonPanel.Controls.Add(btnClear);
            terminalPanel.Controls.Add(_txtTerminalLog);
            terminalPanel.Controls.Add(buttonPanel);
            terminalPanel.Controls.Add(terminalLabel);

            // Results Panel (bottom - Panel2)
            var resultsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5), BackColor = Color.White };
            
            var resultsLabel = new Label
            {
                Name = "lblScanResults",
                Text = loc.GetString("LabelScanResults", "نتایج اسکن:"),
                Dock = DockStyle.Top,
                Height = 25,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
            };

            // Container panel for filter and DataGridView to scroll together
            var gridContainer = new Panel
            {
                Dock = DockStyle.Fill
            };
            
            // Filter panel above DataGridView - will scroll with grid
            var filterPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40, // Increased height for better visibility
                BackColor = System.Drawing.Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle
            };

            _dgvResults = new DataGridView
            {
                Name = "dgvResults",
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            // Add columns with proper formatting
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Frequency", HeaderText = "فرکانس (MHz)", DataPropertyName = "Frequency", Width = 100 });
            
            var snrColumn = new DataGridViewTextBoxColumn { Name = "SNR", HeaderText = "SNR (dB)", DataPropertyName = "SignalToNoiseRatio", Width = 100 };
            _dgvResults.Columns.Add(snrColumn);
            
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Signal", HeaderText = "قدرت سیگنال (dBm)", DataPropertyName = "SignalStrength", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Noise", HeaderText = "نویز (dBm)", DataPropertyName = "NoiseFloor", Width = 100 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Power", HeaderText = "توان آنتن (dBm)", DataPropertyName = "AntennaPower", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Download", HeaderText = "سرعت دانلود (Mbps)", DataPropertyName = "DownloadSpeed", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Upload", HeaderText = "سرعت آپلود (Mbps)", DataPropertyName = "UploadSpeed", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "CCQ", HeaderText = "CCQ (%)", DataPropertyName = "CCQ", Width = 90 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "TxRate", HeaderText = "Tx Rate (Mbps)", DataPropertyName = "TxRate", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RxRate", HeaderText = "Rx Rate (Mbps)", DataPropertyName = "RxRate", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Band", HeaderText = "Band", DataPropertyName = "Band", Width = 100 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChannelWidth", HeaderText = "Channel Width", DataPropertyName = "ChannelWidth", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "WirelessProtocol", HeaderText = "Wireless Protocol", DataPropertyName = "WirelessProtocol", Width = 140 });
            
            // Remote Antenna columns
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteSignal", HeaderText = "سیگنال Remote (dBm)", DataPropertyName = "RemoteSignalStrength", Width = 140 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteSNR", HeaderText = "SNR Remote (dB)", DataPropertyName = "RemoteSignalToNoiseRatio", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteCCQ", HeaderText = "CCQ Remote (%)", DataPropertyName = "RemoteCCQ", Width = 110 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteTxRate", HeaderText = "Tx Rate Remote (Mbps)", DataPropertyName = "RemoteTxRate", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteRxRate", HeaderText = "Rx Rate Remote (Mbps)", DataPropertyName = "RemoteRxRate", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteIdentity", HeaderText = "Remote Identity", DataPropertyName = "RemoteIdentity", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteRadioName", HeaderText = "Remote Radio Name", DataPropertyName = "RemoteRadioName", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteTxCCQ", HeaderText = "Tx CCQ Remote (%)", DataPropertyName = "RemoteTxCCQ", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteRxCCQ", HeaderText = "Rx CCQ Remote (%)", DataPropertyName = "RemoteRxCCQ", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemotePThroughput", HeaderText = "P-Throughput Remote", DataPropertyName = "RemotePThroughput", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteSignalCh0", HeaderText = "Signal Ch0 Remote (dBm)", DataPropertyName = "RemoteSignalStrengthCh0", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteSignalCh1", HeaderText = "Signal Ch1 Remote (dBm)", DataPropertyName = "RemoteSignalStrengthCh1", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteTxSignalCh0", HeaderText = "Tx Signal Ch0 Remote (dBm)", DataPropertyName = "RemoteTxSignalStrengthCh0", Width = 160 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteTxSignalCh1", HeaderText = "Tx Signal Ch1 Remote (dBm)", DataPropertyName = "RemoteTxSignalStrengthCh1", Width = 160 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemotePacketsRx", HeaderText = "Packets Rx Remote", DataPropertyName = "RemotePacketsRx", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemotePacketsTx", HeaderText = "Packets Tx Remote", DataPropertyName = "RemotePacketsTx", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteBytesRx", HeaderText = "Bytes Rx Remote", DataPropertyName = "RemoteBytesRx", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteBytesTx", HeaderText = "Bytes Tx Remote", DataPropertyName = "RemoteBytesTx", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteUptime", HeaderText = "Uptime Remote", DataPropertyName = "RemoteUptime", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteLastActivity", HeaderText = "Last Activity Remote", DataPropertyName = "RemoteLastActivity", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteNstreme", HeaderText = "Nstreme Remote", DataPropertyName = "RemoteNstreme", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteNstremePlus", HeaderText = "Nstreme+ Remote", DataPropertyName = "RemoteNstremePlus", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteRouterOsVersion", HeaderText = "RouterOS Version Remote", DataPropertyName = "RemoteRouterOsVersion", Width = 170 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteLastIp", HeaderText = "Last IP Remote", DataPropertyName = "RemoteLastIp", Width = 130 });
            
            // Ping Test Results columns
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingSuccess", HeaderText = "Ping موفق", DataPropertyName = "PingSuccess", Width = 100 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingTime", HeaderText = "زمان Ping (ms)", DataPropertyName = "PingTime", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingMinTime", HeaderText = "حداقل Ping (ms)", DataPropertyName = "PingMinTime", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingMaxTime", HeaderText = "حداکثر Ping (ms)", DataPropertyName = "PingMaxTime", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingAverageTime", HeaderText = "میانگین Ping (ms)", DataPropertyName = "PingAverageTime", Width = 140 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingPacketsSent", HeaderText = "بسته‌های ارسالی", DataPropertyName = "PingPacketsSent", Width = 120 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingPacketsReceived", HeaderText = "بسته‌های دریافتی", DataPropertyName = "PingPacketsReceived", Width = 130 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingPacketsLost", HeaderText = "بسته‌های از دست رفته", DataPropertyName = "PingPacketsLost", Width = 140 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingLossPercentage", HeaderText = "درصد از دست رفتن (%)", DataPropertyName = "PingLossPercentage", Width = 150 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PingTestIpAddress", HeaderText = "آدرس IP تست Ping", DataPropertyName = "PingTestIpAddress", Width = 140 });
            
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "وضعیت", DataPropertyName = "Status", Width = 100 });
            _dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "ScanTime", HeaderText = "زمان اسکن", DataPropertyName = "ScanTime", Width = 150 });

            // Add cell formatting for better display
            _dgvResults.CellFormatting += (s, e) =>
            {
                if (e.Value == null)
                {
                    e.Value = "-";
                    e.FormattingApplied = true;
                }
                else
                {
                    var columnName = e.ColumnIndex >= 0 ? _dgvResults.Columns[e.ColumnIndex].DataPropertyName : "";
                    
                    // Check if this is the Frequency column - display as integer (no decimal places)
                    if (columnName == "Frequency")
                    {
                        var nullableDouble = e.Value as double?;
                        if (nullableDouble.HasValue && !nullableDouble.Value.Equals(double.NaN))
                        {
                            e.Value = ((int)Math.Round(nullableDouble.Value, 0)).ToString();
                            e.FormattingApplied = true;
                        }
                        else if (e.Value is double doubleValue && !double.IsNaN(doubleValue))
                        {
                            e.Value = ((int)Math.Round(doubleValue, 0)).ToString();
                            e.FormattingApplied = true;
                        }
                    }
                    // Format PingSuccess as "بله"/"خیر"
                    else if (columnName == "PingSuccess")
                    {
                        if (e.Value is bool boolValue)
                        {
                            e.Value = boolValue ? "بله" : "خیر";
                            e.FormattingApplied = true;
                        }
                        else
                        {
                            var nullableBool = e.Value as bool?;
                            if (nullableBool.HasValue)
                            {
                                e.Value = nullableBool.Value ? "بله" : "خیر";
                                e.FormattingApplied = true;
                            }
                        }
                    }
                    // Format Ping time columns as integers (no decimal places)
                    else if (columnName == "PingTime" || columnName == "PingMinTime" || 
                             columnName == "PingMaxTime" || columnName == "PingAverageTime")
                    {
                        if (e.Value is long longValue)
                        {
                            e.Value = longValue.ToString();
                            e.FormattingApplied = true;
                        }
                        else
                        {
                            var nullableLong = e.Value as long?;
                            if (nullableLong.HasValue)
                            {
                                e.Value = nullableLong.Value.ToString();
                                e.FormattingApplied = true;
                            }
                        }
                    }
                    // Format PingLossPercentage as percentage with 2 decimal places
                    else if (columnName == "PingLossPercentage")
                    {
                        var nullableDouble = e.Value as double?;
                        if (nullableDouble.HasValue && !nullableDouble.Value.Equals(double.NaN))
                        {
                            e.Value = nullableDouble.Value.ToString("F2") + "%";
                            e.FormattingApplied = true;
                        }
                        else if (e.Value is double doubleValue && !double.IsNaN(doubleValue))
                        {
                            e.Value = doubleValue.ToString("F2") + "%";
                            e.FormattingApplied = true;
                        }
                    }
                    else
                    {
                        // For other numeric columns, format with 2 decimal places
                        var nullableDouble = e.Value as double?;
                        if (nullableDouble.HasValue && !nullableDouble.Value.Equals(double.NaN))
                        {
                            e.Value = nullableDouble.Value.ToString("F2");
                            e.FormattingApplied = true;
                        }
                        else if (e.Value is double doubleValue && !double.IsNaN(doubleValue))
                        {
                            e.Value = doubleValue.ToString("F2");
                            e.FormattingApplied = true;
                        }
                        else if (e.Value is DateTime dateTime)
                        {
                            e.Value = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                            e.FormattingApplied = true;
                        }
                    }
                }
            };

            // Add row formatting to highlight base status
            _dgvResults.RowPrePaint += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < _dgvResults.Rows.Count)
                {
                    var row = _dgvResults.Rows[e.RowIndex];
                    if (row.DataBoundItem is FrequencyScanResult result)
                    {
                        if (result.Status == "base")
                        {
                            row.DefaultCellStyle.BackColor = System.Drawing.Color.LightBlue;
                            row.DefaultCellStyle.ForeColor = System.Drawing.Color.DarkBlue;
                            row.DefaultCellStyle.Font = new System.Drawing.Font(_dgvResults.Font, System.Drawing.FontStyle.Bold);
                        }
                        else if (result.Status == "موفق")
                        {
                            row.DefaultCellStyle.BackColor = System.Drawing.Color.White;
                            row.DefaultCellStyle.ForeColor = System.Drawing.Color.Black;
                        }
                        else if (result.Status == "خطا")
                        {
                            row.DefaultCellStyle.BackColor = System.Drawing.Color.LightCoral;
                            row.DefaultCellStyle.ForeColor = System.Drawing.Color.DarkRed;
                        }
                    }
                }
            };

            // Create filter textboxes for each column - using TableLayoutPanel for perfect alignment
            var filterTextBoxes = new Dictionary<string, TextBox>();
            var filterTableLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true, // Enable scroll to sync with DataGridView
                ColumnCount = _dgvResults.Columns.Count,
                RowCount = 1
            };
            
            // Set column styles to match DataGridView column widths
            for (int i = 0; i < _dgvResults.Columns.Count; i++)
            {
                filterTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _dgvResults.Columns[i].Width));
            }

            // Enable sorting for all columns BEFORE setting DataSource
            _dgvResults.AllowUserToOrderColumns = true;
            int colIndex = 0;
            foreach (DataGridViewColumn column in _dgvResults.Columns)
            {
                // Use Programmatic sort mode to allow custom sorting
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
                
                // Use custom header cell with sort icons
                var sortableHeader = new SortableHeaderCell();
                column.HeaderCell = sortableHeader;
                
                // Map column name to property name for filtering
                var propertyName = column.DataPropertyName;
                if (!string.IsNullOrEmpty(propertyName))
                {
                    _columnNameToPropertyMap![column.Name] = propertyName;
                }
                
                // Create filter textbox for this column - use property name as key
                var filterTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Height = 32, // Increased height
                    Margin = new Padding(2, 4, 2, 4), // Increased vertical margin
                    Tag = propertyName ?? column.Name, // Store property name
                    Font = new System.Drawing.Font("Tahoma", 9F) // Slightly larger font
                };
                filterTextBox.TextChanged += (s, e) => ApplyFilters();
                filterTextBoxes[propertyName ?? column.Name] = filterTextBox; // Use property name as key
                filterTableLayout.Controls.Add(filterTextBox, colIndex, 0);
                colIndex++;
            }

            filterPanel.Controls.Add(filterTableLayout);
            
            // Add filter label
            var filterLabel = new Label
            {
                Name = "lblFilter",
                Text = loc.GetString("LabelFilter", "فیلتر:") + ":",
                Dock = DockStyle.Left,
                Width = 50,
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                Font = new System.Drawing.Font("Tahoma", 8F, System.Drawing.FontStyle.Bold),
                Padding = new Padding(5, 0, 5, 0)
            };
            filterPanel.Controls.Add(filterLabel);
            
            // Use BindingSource for proper sorting support
            if (_bindingSource == null)
            {
                _bindingSource = new BindingSource();
            }
            _bindingSource.DataSource = _currentResults;
            
            // Set DataSource through BindingSource
            _dgvResults.DataSource = _bindingSource;
            
            // Ensure DataGridView is properly initialized
            _dgvResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _dgvResults.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            
            // Enable virtual mode for better performance with large datasets
            _dgvResults.VirtualMode = false;
            
            // Handle column header click for sorting
            _dgvResults.ColumnHeaderMouseClick += (s, e) =>
            {
                try
                {
                    if (e.ColumnIndex < 0 || _bindingSource == null || _currentResults == null) return;
                    
                    var column = _dgvResults.Columns[e.ColumnIndex];
                    var propertyName = column.DataPropertyName;
                    
                    if (string.IsNullOrEmpty(propertyName)) return;
                    
                    // Get current sortable header cell
                    var currentHeader = column.HeaderCell as SortableHeaderCell;
                    if (currentHeader == null) return;
                    
                    // Toggle sort direction
                    ListSortDirection direction = ListSortDirection.Ascending;
                    if (currentHeader.IsSorted && currentHeader.SortDirection == ListSortDirection.Ascending)
                    {
                        direction = ListSortDirection.Descending;
                    }
                    
                    // Clear sort indicators from all columns
                    foreach (DataGridViewColumn col in _dgvResults.Columns)
                    {
                        if (col.HeaderCell is SortableHeaderCell header)
                        {
                            header.IsSorted = false;
                            col.HeaderCell.Style.BackColor = System.Drawing.Color.White;
                        }
                        // Clear DataGridView sort glyph
                        col.HeaderCell.SortGlyphDirection = SortOrder.None;
                    }
                    
                    // Manual sort - BindingSource.Sort doesn't work well with BindingList
                    var property = typeof(FrequencyScanResult).GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (property != null)
                    {
                        // Get current list items
                        var items = _currentResults.ToList();
                        
                        // Sort the list based on property type
                        List<FrequencyScanResult> sortedList;
                        
                        // Helper function to get comparable value
                        // For null numeric values, use a sentinel value that sorts appropriately
                        IComparable GetComparableValue(object? value, Type propertyType)
                        {
                            if (value == null)
                            {
                                // For nullable numeric types, use MinValue so nulls sort to the end
                                if (propertyType == typeof(double?) || propertyType == typeof(int?) || 
                                    propertyType == typeof(float?) || propertyType == typeof(decimal?))
                                {
                                    return double.MaxValue; // Nulls go to end in ascending, start in descending
                                }
                                // For DateTime nullable
                                if (propertyType == typeof(DateTime?))
                                {
                                    return DateTime.MaxValue.Ticks;
                                }
                                // For strings, empty string
                                return "";
                            }
                            
                            // Handle numeric types
                            if (value is double d) return d;
                            var doubleNullable = value as double?;
                            if (doubleNullable.HasValue) return doubleNullable.Value;
                            if (value is int i) return (double)i;
                            var intNullable = value as int?;
                            if (intNullable.HasValue) return (double)intNullable.Value;
                            if (value is float f) return (double)f;
                            if (value is decimal dec) return (double)dec;
                            if (value is DateTime dt) return dt.Ticks;
                            var dateTimeNullable = value as DateTime?;
                            if (dateTimeNullable.HasValue) return dateTimeNullable.Value.Ticks;
                            
                            // Handle string types
                            return value.ToString() ?? "";
                        }
                        
                        if (direction == ListSortDirection.Ascending)
                        {
                            sortedList = items.OrderBy(x => GetComparableValue(property.GetValue(x), property.PropertyType)).ToList();
                        }
                        else
                        {
                            sortedList = items.OrderByDescending(x => GetComparableValue(property.GetValue(x), property.PropertyType)).ToList();
                        }
                        
                        // Clear and repopulate BindingList
                        _currentResults.Clear();
                        foreach (var item in sortedList)
                        {
                            _currentResults.Add(item);
                        }
                    }
                    
                    // Update current column header to show sort indicator
                    currentHeader.SortDirection = direction;
                    currentHeader.IsSorted = true;
                    column.HeaderCell.Style.BackColor = System.Drawing.Color.LightBlue;
                    
                    // Set DataGridView sort glyph
                    column.HeaderCell.SortGlyphDirection = direction == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;
                    
                    // Refresh to show sort icon
                    _dgvResults.InvalidateColumn(e.ColumnIndex);
                    _dgvResults.Refresh();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in sort: {ex.Message}");
                }
            };
            
            // BindingList automatically raises ListChanged events by default
            // No need to set RaiseListChangedEvents explicitly

            // Add filter and grid to container - they will scroll together
            gridContainer.Controls.Add(_dgvResults);
            gridContainer.Controls.Add(filterPanel);
            
            // Sync horizontal scroll between filter panel and DataGridView
            _dgvResults.Scroll += (s, e) =>
            {
                if (e.ScrollOrientation == ScrollOrientation.HorizontalScroll)
                {
                    // Sync filter panel scroll with DataGridView
                    try
                    {
                        var hScrollBar = _dgvResults.HorizontalScrollingOffset;
                        // Update filter panel position to match DataGridView scroll
                        filterTableLayout.AutoScroll = true;
                        filterTableLayout.HorizontalScroll.Value = Math.Max(0, Math.Min(hScrollBar, filterTableLayout.HorizontalScroll.Maximum));
                        filterTableLayout.PerformLayout();
                    }
                    catch
                    {
                        // Ignore errors during scroll sync
                    }
                }
            };
            
            // Also handle when DataGridView is resized or scrolled programmatically
            _dgvResults.Resize += (s, e) =>
            {
                try
                {
                    var hScrollBar = _dgvResults.HorizontalScrollingOffset;
                    filterTableLayout.HorizontalScroll.Value = Math.Max(0, Math.Min(hScrollBar, filterTableLayout.HorizontalScroll.Maximum));
                }
                catch
                {
                    // Ignore errors
                }
            };
            
            // Also sync when columns are resized
            _dgvResults.ColumnWidthChanged += (s, e) =>
            {
                // Update filter column widths to match DataGridView
                var column = e.Column;
                if (column != null)
                {
                    var columnIndex = _dgvResults.Columns.IndexOf(column);
                    if (columnIndex >= 0 && filterTableLayout.ColumnStyles.Count > columnIndex)
                    {
                        filterTableLayout.ColumnStyles[columnIndex].Width = column.Width;
                    }
                }
            };
            
            resultsPanel.Controls.Add(gridContainer);
            resultsPanel.Controls.Add(resultsLabel);
            
            // Store filter textboxes for later use
            _filterTextBoxes = filterTextBoxes;

            // Terminal log in Panel1 (top), Results in Panel2 (bottom)
            splitContainer.Panel1.Controls.Add(terminalPanel);
            splitContainer.Panel2.Controls.Add(resultsPanel);

            tab.Controls.Add(splitContainer);
        }

        /// <summary>
        /// کلاس برای نگهداری اطلاعات نمودار
        /// </summary>
        private class ChartInfo
        {
            public string? XProperty { get; set; }
            public string? YProperty { get; set; }
            public string? GroupByProperty { get; set; }
            public string? ValueProperty { get; set; }
            public string? Title { get; set; }
        }

        /// <summary>
        /// ایجاد تب نمودارها برای نمایش نمودارهای مقایسه‌ای نتایج اسکن
        /// این تب شامل نمودارهای مختلف برای تصمیم‌گیری بهترین فرکانس، کانال و پروتکل است
        /// </summary>
        /// <param name="tab">تب برای اضافه کردن نمودارها</param>
        private void CreateChartsTab(TabPage tab)
        {
            // استفاده از SplitContainer برای تقسیم نمودار بزرگ (بالا) و نمودارهای کوچک (پایین)
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical,
                SplitterDistance = 400, // نمودار بزرگ 400px ارتفاع دارد
                SplitterWidth = 5
            };

            // نمودار بزرگ در Panel1 (چپ)
            var largeChartPanel = CreateMultiSeriesChartPanel();
            splitContainer.Panel1.Controls.Add(largeChartPanel);

            // نمودارهای کوچک در Panel2 (راست)
            var smallChartsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(10),
                BackColor = Color.White
            };

            // تنظیم اندازه ستون‌ها (50% - 50%)
            smallChartsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            smallChartsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // تنظیم اندازه ردیف‌ها
            smallChartsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            smallChartsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            smallChartsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));

            var loc = _localizationService;

            // نمودار 1: SNR بر اساس فرکانس
            var chart1Panel = CreateChartPanel(loc.GetString("ChartSNRByFrequency", "SNR بر اساس فرکانس (dB)"), "Frequency", "SignalToNoiseRatio");
            smallChartsPanel.Controls.Add(chart1Panel, 0, 0);

            // نمودار 2: Signal Strength بر اساس فرکانس
            var chart2Panel = CreateChartPanel(loc.GetString("ChartSignalByFrequency", "قدرت سیگنال بر اساس فرکانس (dBm)"), "Frequency", "SignalStrength");
            smallChartsPanel.Controls.Add(chart2Panel, 1, 0);

            // نمودار 3: CCQ بر اساس فرکانس
            var chart3Panel = CreateChartPanel(loc.GetString("ChartCCQByFrequency", "CCQ بر اساس فرکانس (%)"), "Frequency", "CCQ");
            smallChartsPanel.Controls.Add(chart3Panel, 0, 1);

            // نمودار 4: Ping Time بر اساس فرکانس
            var chart4Panel = CreateChartPanel(loc.GetString("ChartPingByFrequency", "زمان Ping بر اساس فرکانس (ms)"), "Frequency", "PingAverageTime");
            smallChartsPanel.Controls.Add(chart4Panel, 1, 1);

            // نمودار 5: مقایسه WirelessProtocol
            var chart5Panel = CreateComparisonChartPanel(loc.GetString("ChartCompareWirelessProtocol", "مقایسه Wireless Protocol"), "WirelessProtocol", "SignalToNoiseRatio");
            smallChartsPanel.Controls.Add(chart5Panel, 0, 2);

            // نمودار 6: مقایسه ChannelWidth
            var chart6Panel = CreateComparisonChartPanel(loc.GetString("ChartCompareChannelWidth", "مقایسه Channel Width"), "ChannelWidth", "SignalToNoiseRatio");
            smallChartsPanel.Controls.Add(chart6Panel, 1, 2);

            splitContainer.Panel2.Controls.Add(smallChartsPanel);
            tab.Controls.Add(splitContainer);
        }

        /// <summary>
        /// ایجاد پنل نمودار برای نمایش یک نمودار خطی
        /// </summary>
        /// <param name="title">عنوان نمودار</param>
        /// <param name="xAxisProperty">نام property برای محور X</param>
        /// <param name="yAxisProperty">نام property برای محور Y</param>
        /// <returns>پنل حاوی نمودار</returns>
        private Panel CreateChartPanel(string title, string xAxisProperty, string yAxisProperty)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(240, 240, 240)
            };

            // Label برای نمایش مقدار
            var valueLabel = new Label
            {
                Text = "",
                Dock = DockStyle.Bottom,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Tahoma", 8F),
                BackColor = Color.FromArgb(250, 250, 250),
                ForeColor = Color.Blue,
                Padding = new Padding(5, 0, 5, 0)
            };

            var formsPlot = new ScottPlotWinForms.FormsPlot
            {
                Dock = DockStyle.Fill
            };

            // فعال‌سازی نمایش مختصات هنگام حرکت موس
            formsPlot.MouseMove += (s, e) => 
            {
                try
                {
                    var coordinates = formsPlot.Plot.GetCoordinates(e.X, e.Y);
                    var xLabel = GetPropertyDisplayName(xAxisProperty);
                    var yLabel = GetPropertyDisplayName(yAxisProperty);
                    
                    valueLabel.Text = $"{xLabel}: {coordinates.X:F2}  |  {yLabel}: {coordinates.Y:F2}";
                }
                catch 
                {
                    valueLabel.Text = "";
                }
            };

            formsPlot.MouseLeave += (s, e) =>
            {
                valueLabel.Text = "";
            };

            panel.Controls.Add(formsPlot);
            panel.Controls.Add(valueLabel);
            panel.Controls.Add(titleLabel);

            // ذخیره اطلاعات نمودار برای به‌روزرسانی بعدی
            formsPlot.Tag = new ChartInfo { XProperty = xAxisProperty, YProperty = yAxisProperty, Title = title };

            return panel;
        }

        /// <summary>
        /// ایجاد پنل نمودار بزرگ با چند منحنی
        /// محور X: ترکیب Frequency + WirelessProtocol + ChannelWidth
        /// محور Y: مقادیر مختلف با رنگ‌های مختلف
        /// </summary>
        /// <returns>پنل حاوی نمودار بزرگ</returns>
        private Panel CreateMultiSeriesChartPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            var loc = _localizationService;

            var titleLabel = new Label
            {
                Name = "lblMultiChartTitle",
                Text = loc.GetString("ChartMultiSeriesTitle", "نمودار جامع: ترکیب Frequency + Protocol + ChannelWidth"),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(240, 240, 240)
            };

            var formsPlot = new ScottPlotWinForms.FormsPlot
            {
                Dock = DockStyle.Fill
            };

            // فعال‌سازی نمایش مختصات هنگام حرکت موس
            formsPlot.MouseMove += (s, e) => 
            {
                try
                {
                    var coordinates = formsPlot.Plot.GetCoordinates(e.X, e.Y);
                    
                    if (_chartToolTip == null)
                    {
                        _chartToolTip = new ToolTip
                        {
                            IsBalloon = false,
                            UseAnimation = true,
                            UseFading = true,
                            AutoPopDelay = 5000,
                            InitialDelay = 100,
                            ReshowDelay = 100
                        };
                    }
                    
                    var tooltipText = $"X: {coordinates.X:F0}\nY: {coordinates.Y:F2}";
                    _chartToolTip.SetToolTip(formsPlot, tooltipText);
                }
                catch { }
            };

            panel.Controls.Add(formsPlot);
            panel.Controls.Add(titleLabel);

            // ذخیره اطلاعات نمودار برای به‌روزرسانی بعدی
            formsPlot.Tag = new ChartInfo { Title = "ChartMultiSeries" };

            return panel;
        }

        /// <summary>
        /// ایجاد پنل نمودار برای مقایسه مقادیر بر اساس یک property
        /// </summary>
        /// <param name="title">عنوان نمودار</param>
        /// <param name="groupByProperty">نام property برای گروه‌بندی</param>
        /// <param name="valueProperty">نام property برای مقایسه</param>
        /// <returns>پنل حاوی نمودار</returns>
        private Panel CreateComparisonChartPanel(string title, string groupByProperty, string valueProperty)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(240, 240, 240)
            };

            var formsPlot = new ScottPlotWinForms.FormsPlot
            {
                Dock = DockStyle.Fill
            };

            // فعال‌سازی نمایش مختصات هنگام حرکت موس
            formsPlot.MouseMove += (s, e) => 
            {
                try
                {
                    var coordinates = formsPlot.Plot.GetCoordinates(e.X, e.Y);
                    var xLabel = GetPropertyDisplayName(groupByProperty);
                    var yLabel = GetPropertyDisplayName(valueProperty);
                    
                    if (_chartToolTip == null)
                    {
                        _chartToolTip = new ToolTip
                        {
                            IsBalloon = false,
                            UseAnimation = true,
                            UseFading = true,
                            AutoPopDelay = 5000,
                            InitialDelay = 100,
                            ReshowDelay = 100
                        };
                    }
                    
                    var tooltipText = $"{xLabel}: {coordinates.X:F0}\n{yLabel}: {coordinates.Y:F2}";
                    _chartToolTip.SetToolTip(formsPlot, tooltipText);
                }
                catch { }
            };

            panel.Controls.Add(formsPlot);
            panel.Controls.Add(titleLabel);

            // ذخیره اطلاعات نمودار برای به‌روزرسانی بعدی
            formsPlot.Tag = new ChartInfo { GroupByProperty = groupByProperty, ValueProperty = valueProperty, Title = title };

            return panel;
        }

        /// <summary>
        /// به‌روزرسانی تمام نمودارها با داده‌های جدید
        /// این متد باید بعد از اضافه شدن نتایج جدید فراخوانی شود
        /// </summary>
        private void UpdateCharts()
        {
            if (_allResults == null || _allResults.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("UpdateCharts: _allResults is null or empty");
                return;
            }

            try
            {
                // پیدا کردن تب نمودارها
                var tabControl = this.Controls.OfType<TabControl>().FirstOrDefault();
                if (tabControl == null)
                {
                    System.Diagnostics.Debug.WriteLine("UpdateCharts: TabControl not found");
                    return;
                }

                var chartsTab = tabControl.TabPages.Cast<TabPage>()
                    .FirstOrDefault(t => t.Text.Contains("نمودارها"));

                if (chartsTab == null)
                {
                    System.Diagnostics.Debug.WriteLine("UpdateCharts: Charts tab not found");
                    return;
                }

                // پیدا کردن تمام FormsPlot controls
                // ابتدا SplitContainer را بررسی می‌کنیم (برای نمودار بزرگ)
                var splitContainer = chartsTab.Controls.OfType<SplitContainer>().FirstOrDefault();
                var chartPanels = new List<Panel>();

                if (splitContainer != null)
                {
                    // نمودار بزرگ در Panel1
                    var largeChartPanel = splitContainer.Panel1.Controls.OfType<Panel>()
                        .FirstOrDefault(p => p.Controls.OfType<ScottPlotWinForms.FormsPlot>().Any());
                    if (largeChartPanel != null)
                    {
                        chartPanels.Add(largeChartPanel);
                    }

                    // نمودارهای کوچک در Panel2 -> TableLayoutPanel
                    var smallChartsPanel = splitContainer.Panel2.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
                    if (smallChartsPanel != null)
                    {
                        var smallPanels = smallChartsPanel.Controls.OfType<Panel>()
                            .Where(p => p.Controls.OfType<ScottPlotWinForms.FormsPlot>().Any())
                            .ToList();
                        chartPanels.AddRange(smallPanels);
                    }
                }
                else
                {
                    // اگر SplitContainer وجود نداشت، TableLayoutPanel را بررسی می‌کنیم (ساختار قدیمی)
                    var mainPanel = chartsTab.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
                    if (mainPanel != null)
                    {
                        var panels = mainPanel.Controls.OfType<Panel>()
                            .Where(p => p.Controls.OfType<ScottPlotWinForms.FormsPlot>().Any())
                            .ToList();
                        chartPanels.AddRange(panels);
                    }
                }

                if (chartPanels == null || chartPanels.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("UpdateCharts: No chart panels found");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"UpdateCharts: Found {chartPanels.Count} chart panels, Total results: {_allResults.Count}");

                // فیلتر کردن نتایج معتبر (همه نتایج به جز "خطا" و "base")
                var validResults = _allResults
                    .Where(r => r.Status != "خطا" && r.Status != "base" && !string.IsNullOrEmpty(r.Status))
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"UpdateCharts: Valid results count: {validResults.Count}");

                if (validResults.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("UpdateCharts: No valid results to display");
                    // نمایش پیام در نمودارها که داده‌ای وجود ندارد
                    foreach (var chartPanel in chartPanels)
                    {
                        var formsPlot = chartPanel.Controls.OfType<ScottPlotWinForms.FormsPlot>().FirstOrDefault();
                        if (formsPlot != null)
                        {
                            formsPlot.Plot.Clear();
                            formsPlot.Plot.Title("هیچ داده معتبری برای نمایش وجود ندارد");
                            formsPlot.Refresh();
                        }
                    }
                    return;
                }

                foreach (var chartPanel in chartPanels)
                {
                    var formsPlot = chartPanel.Controls.OfType<ScottPlotWinForms.FormsPlot>().FirstOrDefault();
                    if (formsPlot == null || formsPlot.Tag == null)
                        continue;

                    var chartInfo = formsPlot.Tag as ChartInfo;
                    if (chartInfo == null)
                        continue;

                    formsPlot.Plot.Clear();

                    // اگر نمودار جامع است
                    if (chartInfo.Title == "نمودار جامع")
                    {
                        UpdateMultiSeriesChart(formsPlot, validResults);
                    }
                    // اگر نمودار مقایسه‌ای است
                    else if (!string.IsNullOrEmpty(chartInfo.GroupByProperty))
                    {
                        UpdateComparisonChart(formsPlot, validResults, chartInfo.GroupByProperty, chartInfo.ValueProperty ?? "", chartInfo.Title ?? "");
                    }
                    // اگر نمودار خطی است
                    else if (!string.IsNullOrEmpty(chartInfo.XProperty) && !string.IsNullOrEmpty(chartInfo.YProperty))
                    {
                        UpdateLineChart(formsPlot, validResults, chartInfo.XProperty, chartInfo.YProperty, chartInfo.Title ?? "");
                    }

                    formsPlot.Refresh();
                }

                System.Diagnostics.Debug.WriteLine("UpdateCharts: Charts updated successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating charts: {ex.Message}\n{ex.StackTrace}");
                ErrorHandler.ShowErrorWithSupport(ex, "به‌روزرسانی نمودارها", _txtTerminalLog);
            }
        }

        /// <summary>
        /// به‌روزرسانی نمودار خطی
        /// </summary>
        private void UpdateLineChart(ScottPlotWinForms.FormsPlot formsPlot, List<FrequencyScanResult> results, string xProperty, string yProperty, string title)
        {
            try
            {
                var xPropertyInfo = typeof(FrequencyScanResult).GetProperty(xProperty);
                var yPropertyInfo = typeof(FrequencyScanResult).GetProperty(yProperty);

                if (xPropertyInfo == null || yPropertyInfo == null)
                    return;

                var xValues = new List<double>();
                var yValues = new List<double>();

                foreach (var result in results.OrderBy(r => GetPropertyValue(r, xPropertyInfo)))
                {
                    var xValue = GetPropertyValue(result, xPropertyInfo);
                    var yValue = GetPropertyValue(result, yPropertyInfo);

                    if (xValue.HasValue && yValue.HasValue)
                    {
                        xValues.Add(xValue.Value);
                        yValues.Add(yValue.Value);
                    }
                }

                if (xValues.Count > 0 && yValues.Count > 0)
                {
                    var scatter = formsPlot.Plot.Add.Scatter(xValues.ToArray(), yValues.ToArray());
                    scatter.LineWidth = 2;
                    scatter.MarkerSize = 5;
                    formsPlot.Plot.Title(title);
                    formsPlot.Plot.Axes.Bottom.Label.Text = GetPropertyDisplayName(xProperty);
                    formsPlot.Plot.Axes.Left.Label.Text = GetPropertyDisplayName(yProperty);
                    formsPlot.Plot.ShowGrid();
                    
                    // فعال‌سازی Crosshair برای نمایش مقدار
                    var crosshair = formsPlot.Plot.Add.Crosshair(0, 0);
                    crosshair.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating line chart: {ex.Message}");
            }
        }

        /// <summary>
        /// به‌روزرسانی نمودار جامع با چند منحنی
        /// </summary>
        private void UpdateMultiSeriesChart(ScottPlotWinForms.FormsPlot formsPlot, List<FrequencyScanResult> results)
        {
            try
            {
                if (results.Count == 0)
                    return;

                // مرتب‌سازی نتایج بر اساس ترکیب Frequency + Protocol + ChannelWidth
                var sortedResults = results.OrderBy(r =>
                {
                    var freq = Math.Round(r.Frequency, 0);
                    var protocol = r.WirelessProtocol ?? "unknown";
                    var channelWidth = r.ChannelWidth ?? "unknown";
                    return $"{freq}-{protocol}-{channelWidth}";
                }).ToList();

                // ایجاد برچسب‌های محور X (ترکیب Frequency + Protocol + ChannelWidth)
                var xLabels = sortedResults.Select(r =>
                {
                    var freq = Math.Round(r.Frequency, 0);
                    var protocol = r.WirelessProtocol ?? "unknown";
                    var channelWidth = r.ChannelWidth ?? "unknown";
                    return $"{freq}-{protocol}-{channelWidth}";
                }).ToArray();

                var xPositions = Enumerable.Range(0, xLabels.Length).Select(i => (double)i).ToArray();

                // تعریف سری‌های مختلف با رنگ‌های مختلف
                var series = new[]
                {
                    new { Name = "NoiseFloor", Property = "NoiseFloor", Color = ScottPlot.Color.FromHex("#FF0000") }, // Red
                    new { Name = "CCQ", Property = "CCQ", Color = ScottPlot.Color.FromHex("#0000FF") }, // Blue
                    new { Name = "RemoteSignalStrength", Property = "RemoteSignalStrength", Color = ScottPlot.Color.FromHex("#00FF00") }, // Green
                    new { Name = "RemoteSignalToNoiseRatio", Property = "RemoteSignalToNoiseRatio", Color = ScottPlot.Color.FromHex("#FFA500") }, // Orange
                    new { Name = "RemoteTxRate", Property = "RemoteTxRate", Color = ScottPlot.Color.FromHex("#800080") }, // Purple
                    new { Name = "RemoteRxRate", Property = "RemoteRxRate", Color = ScottPlot.Color.FromHex("#A52A2A") }, // Brown
                    new { Name = "RemoteTxCCQ", Property = "RemoteTxCCQ", Color = ScottPlot.Color.FromHex("#FFC0CB") }, // Pink
                    new { Name = "RemoteRxCCQ", Property = "RemoteRxCCQ", Color = ScottPlot.Color.FromHex("#00FFFF") }, // Cyan
                    new { Name = "PingTime", Property = "PingTime", Color = ScottPlot.Color.FromHex("#FF00FF") } // Magenta
                };

                var propertyInfo = typeof(FrequencyScanResult);
                var legendItems = new List<string>();

                foreach (var serie in series)
                {
                    var prop = propertyInfo.GetProperty(serie.Property);
                    if (prop == null)
                        continue;

                    var yValues = new List<double?>();
                    foreach (var result in sortedResults)
                    {
                        var value = GetPropertyValue(result, prop);
                        yValues.Add(value);
                    }

                    // فقط اگر حداقل یک مقدار معتبر وجود داشته باشد
                    if (yValues.Any(v => v.HasValue))
                    {
                        // تبدیل به آرایه double (null ها را با NaN جایگزین می‌کنیم)
                        var yValuesArray = yValues.Select(v => v ?? double.NaN).ToArray();

                        var scatter = formsPlot.Plot.Add.Scatter(xPositions, yValuesArray);
                        scatter.LineWidth = 2;
                        scatter.MarkerSize = 4;
                        scatter.Color = serie.Color;
                        scatter.Label = serie.Name;

                        legendItems.Add(serie.Name);
                    }
                }

                // تنظیم برچسب‌های محور X
                formsPlot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(xPositions, xLabels);
                formsPlot.Plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
                formsPlot.Plot.Axes.Bottom.Label.Text = "ترکیب: Frequency-Protocol-ChannelWidth";

                // تنظیم محور Y
                formsPlot.Plot.Axes.Left.Label.Text = "مقدار";

                // نمایش راهنما (Legend)
                if (legendItems.Count > 0)
                {
                    formsPlot.Plot.ShowLegend();
                }

                formsPlot.Plot.Title("نمودار جامع: ترکیب Frequency + Protocol + ChannelWidth");
                formsPlot.Plot.ShowGrid();
                
                // فعال‌سازی Crosshair برای نمایش مقدار
                var crosshair = formsPlot.Plot.Add.Crosshair(0, 0);
                crosshair.IsVisible = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating multi-series chart: {ex.Message}");
            }
        }

        /// <summary>
        /// به‌روزرسانی نمودار مقایسه‌ای (ستونی)
        /// </summary>
        private void UpdateComparisonChart(ScottPlotWinForms.FormsPlot formsPlot, List<FrequencyScanResult> results, string groupByProperty, string valueProperty, string title)
        {
            try
            {
                var groupByPropertyInfo = typeof(FrequencyScanResult).GetProperty(groupByProperty);
                var valuePropertyInfo = typeof(FrequencyScanResult).GetProperty(valueProperty);

                if (groupByPropertyInfo == null || valuePropertyInfo == null)
                    return;

                // گروه‌بندی نتایج بر اساس groupByProperty و محاسبه میانگین valueProperty
                var grouped = results
                    .Where(r => GetPropertyValue(r, valuePropertyInfo).HasValue)
                    .GroupBy(r =>
                    {
                        var value = groupByPropertyInfo.GetValue(r);
                        return value?.ToString() ?? "نامشخص";
                    })
                    .Select(g => new
                    {
                        Group = g.Key,
                        AverageValue = g.Average(r => GetPropertyValue(r, valuePropertyInfo).Value),
                        Count = g.Count()
                    })
                    .OrderBy(x => x.Group)
                    .ToList();

                if (grouped.Count == 0)
                    return;

                var positions = new double[grouped.Count];
                var values = new double[grouped.Count];
                var labels = new string[grouped.Count];

                for (int i = 0; i < grouped.Count; i++)
                {
                    positions[i] = i;
                    values[i] = grouped[i].AverageValue;
                    labels[i] = grouped[i].Group;
                }

                var bar = formsPlot.Plot.Add.Bars(values);
                formsPlot.Plot.Title(title);
                formsPlot.Plot.Axes.Bottom.Label.Text = GetPropertyDisplayName(groupByProperty);
                formsPlot.Plot.Axes.Left.Label.Text = GetPropertyDisplayName(valueProperty);
                
                // تنظیم برچسب‌های محور X
                formsPlot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(positions, labels);
                formsPlot.Plot.ShowGrid();
                
                // فعال‌سازی Crosshair برای نمایش مقدار
                var crosshair = formsPlot.Plot.Add.Crosshair(0, 0);
                crosshair.IsVisible = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating comparison chart: {ex.Message}");
            }
        }

        /// <summary>
        /// دریافت مقدار property به صورت double?
        /// </summary>
        private double? GetPropertyValue(FrequencyScanResult result, System.Reflection.PropertyInfo propertyInfo)
        {
            try
            {
                var value = propertyInfo.GetValue(result);
                if (value == null)
                    return null;

                if (value is double d)
                    return d;
                var nullableDouble = value as double?;
                if (nullableDouble.HasValue)
                    return nullableDouble.Value;
                if (value is int i)
                    return i;
                var nullableInt = value as int?;
                if (nullableInt.HasValue)
                    return nullableInt.Value;
                if (value is long l)
                    return l;
                var nullableLong = value as long?;
                if (nullableLong.HasValue)
                    return nullableLong.Value;
                if (value is float f)
                    return f;
                var nullableFloat = value as float?;
                if (nullableFloat.HasValue)
                    return nullableFloat.Value;

                if (double.TryParse(value.ToString(), out double parsed))
                    return parsed;

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ToolTip مشترک برای تمام نمودارها
        private ToolTip? _chartToolTip;

        /// <summary>
        /// نمایش مختصات نمودار خطی هنگام حرکت موس
        /// </summary>
        private void ShowChartCoordinates(ScottPlotWinForms.FormsPlot formsPlot, MouseEventArgs e, string xProperty, string yProperty)
        {
            try
            {
                if (_chartToolTip == null)
                {
                    _chartToolTip = new ToolTip
                    {
                        IsBalloon = false,
                        UseAnimation = true,
                        UseFading = true,
                        AutoPopDelay = 5000,
                        InitialDelay = 100,
                        ReshowDelay = 100
                    };
                }

                var coordinates = formsPlot.Plot.GetCoordinates(e.X, e.Y);
                var xLabel = GetPropertyDisplayName(xProperty);
                var yLabel = GetPropertyDisplayName(yProperty);
                
                // نمایش مختصات در tooltip
                var tooltipText = $"{xLabel}: {coordinates.X:F2}\n{yLabel}: {coordinates.Y:F2}";
                _chartToolTip.SetToolTip(formsPlot, tooltipText);
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// نمایش مختصات نمودار جامع هنگام حرکت موس
        /// </summary>
        private void ShowMultiSeriesChartCoordinates(ScottPlotWinForms.FormsPlot formsPlot, MouseEventArgs e)
        {
            try
            {
                if (_chartToolTip == null)
                {
                    _chartToolTip = new ToolTip
                    {
                        IsBalloon = false,
                        UseAnimation = true,
                        UseFading = true,
                        AutoPopDelay = 5000,
                        InitialDelay = 100,
                        ReshowDelay = 100
                    };
                }

                var coordinates = formsPlot.Plot.GetCoordinates(e.X, e.Y);
                
                // نمایش مختصات
                var tooltipText = $"X: {coordinates.X:F0}\nY: {coordinates.Y:F2}";
                _chartToolTip.SetToolTip(formsPlot, tooltipText);
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// نمایش مختصات نمودار مقایسه‌ای هنگام حرکت موس
        /// </summary>
        private void ShowComparisonChartCoordinates(ScottPlotWinForms.FormsPlot formsPlot, MouseEventArgs e, string groupByProperty, string valueProperty)
        {
            try
            {
                if (_chartToolTip == null)
                {
                    _chartToolTip = new ToolTip
                    {
                        IsBalloon = false,
                        UseAnimation = true,
                        UseFading = true,
                        AutoPopDelay = 5000,
                        InitialDelay = 100,
                        ReshowDelay = 100
                    };
                }

                var coordinates = formsPlot.Plot.GetCoordinates(e.X, e.Y);
                var xLabel = GetPropertyDisplayName(groupByProperty);
                var yLabel = GetPropertyDisplayName(valueProperty);
                
                var tooltipText = $"{xLabel}: {coordinates.X:F0}\n{yLabel}: {coordinates.Y:F2}";
                _chartToolTip.SetToolTip(formsPlot, tooltipText);
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// دریافت نام نمایشی property (وابسته به زبان)
        /// </summary>
        private string GetPropertyDisplayName(string propertyName)
        {
            var loc = _localizationService;

            return propertyName switch
            {
                "Frequency" => loc.GetString("ColumnFrequency", "فرکانس (MHz)"),
                "SignalToNoiseRatio" => loc.GetString("ColumnSNR", "SNR (dB)"),
                "SignalStrength" => loc.GetString("ColumnSignal", "قدرت سیگنال (dBm)"),
                "CCQ" => loc.GetString("ColumnCCQ", "CCQ (%)"),
                "PingAverageTime" => loc.GetString("ColumnPingAverageTime", "میانگین Ping (ms)"),
                "WirelessProtocol" => loc.GetString("ColumnWirelessProtocol", "Wireless Protocol"),
                "ChannelWidth" => loc.GetString("ColumnChannelWidth", "Channel Width"),
                _ => propertyName
            };
        }

        private void CreateTerminalLogTab(TabPage tab)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var loc = _localizationService;
            var label = new Label 
            { 
                Name = "lblTerminalLogDetails",
                Text = loc.GetString("LabelTerminalLog", "داده‌های ارسالی و دریافتی ترمینال:"), 
                Dock = DockStyle.Top, 
                Height = 25,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
            };

            _txtTerminalLog = new RichTextBox
            {
                Name = "txtTerminalLog",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new System.Drawing.Font("Consolas", 9F),
                BackColor = System.Drawing.Color.Black,
                ForeColor = System.Drawing.Color.LimeGreen
            };

            var buttonPanel = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Bottom, 
                Height = 35,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(5)
            };

            var btnClear = new Button 
            { 
                Name = "btnClearTerminal",
                Text = loc.GetString("BtnClear", "🗑 پاک کردن"),
                Size = new System.Drawing.Size(110, 30),
                BackColor = Color.FromArgb(198, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClear.FlatAppearance.BorderSize = 0;
            btnClear.MouseEnter += (s, e) => { btnClear.BackColor = Color.FromArgb(218, 60, 60); };
            btnClear.MouseLeave += (s, e) => { btnClear.BackColor = Color.FromArgb(198, 40, 40); };
            btnClear.Click += (s, e) => 
            {
                if (_txtTerminalLog != null)
                    _txtTerminalLog.Clear();
            };

            buttonPanel.Controls.Add(btnClear);
            panel.Controls.Add(_txtTerminalLog);
            panel.Controls.Add(label);
            panel.Controls.Add(buttonPanel);

            tab.Controls.Add(panel);
        }

        private void CreateAboutTab(TabPage tab)
        {
            tab.BackColor = Color.White;
            tab.Padding = new Padding(10);

            var loc = _localizationService;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Name = "lblAboutTitle",
                Text = loc.GetString("AboutTitle", "درباره ابزار و توسعه‌دهنده"),
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 10)
            };

            var descriptionBox = new TextBox
            {
                Name = "txtAboutDescription",
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                Dock = DockStyle.Top,
                Height = 160,
                ScrollBars = ScrollBars.Vertical,
                Text = loc.GetString(
                    "AboutDescription",
                    "این برنامه برای اسکن و بهینه‌سازی فرکانس در روترهای MikroTik طراحی شده است تا بهترین کیفیت لینک Point-to-Point را پیدا کند.\r\n" +
                    "با اتصال امن SSH، ترکیب‌های مختلف فرکانس، پروتکل و Channel Width را تست می‌کند، نتایج را به‌صورت زنده نمایش می‌دهد و امکان ذخیره در فایل JSON را فراهم می‌کند.\r\n\r\n" +
                    "تمام رابط کاربری به زبان فارسی است و شامل فیلتر، مرتب‌سازی، لاگ ترمینال و مدیریت تنظیمات می‌باشد.")
            };

            var infoLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 10, 0, 10)
            };
            infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            void AddInfoRow(string titleKey, string valueKey)
            {
                var titleLabelLocal = new Label
                {
                    Text = loc.GetString(titleKey, titleKey),
                    TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
                };

                var valueLabel = new Label
                {
                    Text = loc.GetString(valueKey, valueKey),
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    MaximumSize = new Size(900, 0),
                    AutoEllipsis = true
                };

                var rowIndex = infoLayout.RowCount++;
                infoLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                infoLayout.Controls.Add(titleLabelLocal, 0, rowIndex);
                infoLayout.Controls.Add(valueLabel, 1, rowIndex);
            }

            var developerName = Environment.UserName;
            var projectLocation = AppDomain.CurrentDomain.BaseDirectory;

            AddInfoRow("AboutProjectName", "AboutProjectValue");
            AddInfoRow("AboutVersion", "Application.ProductVersion");
            AddInfoRow("AboutPlatform", "AboutPlatformValue");
            AddInfoRow("AboutDeveloper", "AboutDeveloperValue");
            AddInfoRow("AboutContactEmails", "AboutContactEmailsValue");
            AddInfoRow("AboutLocation", "AboutLocationValue");
            AddInfoRow("AboutPhone", "AboutPhoneValue");
            AddInfoRow("AboutSkills", "AboutSkillsValue");
            AddInfoRow("AboutExperience", "AboutExperienceValue");
            AddInfoRow("AboutEducation", "AboutEducationValue");
            AddInfoRow("AboutInstallPath", projectLocation);

            var footerLabel = new Label
            {
                Name = "lblAboutFooter",
                Text = loc.GetString("AboutFooter", "در صورت نیاز به پشتیبانی یا پیشنهاد، اطلاعات بالا را به‌روزرسانی کنید و با تیم توسعه در تماس باشید."),
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 10, 0, 0)
            };

            mainLayout.Controls.Add(titleLabel, 0, 0);
            mainLayout.Controls.Add(descriptionBox, 0, 1);
            mainLayout.Controls.Add(infoLayout, 0, 2);
            mainLayout.Controls.Add(footerLabel, 0, 3);

            tab.Controls.Add(mainLayout);
        }

        private ScanSettings GetSettingsFromForm()
        {
            // زبان فعلی را از LocalizationService یا ComboBox بخوان
            var currentLanguage = _localizationService.CurrentLanguage;
            if (_cmbLanguage != null && _cmbLanguage.SelectedItem != null)
            {
                try
                {
                    currentLanguage = ((dynamic)_cmbLanguage.SelectedItem).Code ?? currentLanguage;
                }
                catch
                {
                    // در صورت خطا، همان مقدار قبلی را نگه دار
                }
            }

            return new ScanSettings
            {
                RouterIpAddress = _txtRouterIp?.Text ?? "192.168.88.1",
                SshPort = (int)(_txtSshPort?.Value ?? 22),
                Username = _txtUsername?.Text ?? "admin",
                Password = _txtPassword?.Text ?? "",
                StartFrequency = (double)(_txtStartFreq?.Value ?? 2400),
                EndFrequency = (double)(_txtEndFreq?.Value ?? 2500),
                FrequencyStep = (double)(_txtFreqStep?.Value ?? 5),
                StabilizationTimeMinutes = (int)(_txtStabilizationTime?.Value ?? 2),
                InterfaceName = _txtInterface?.Text ?? "wlan1",
                PingTestIpAddress = _txtPingIp?.Text ?? "8.8.8.8",
                WirelessProtocols = (this.Controls.Find("txtWirelessProtocols", true).FirstOrDefault() as TextBox)?.Text ?? "nstreme\r\nnv2\r\n802.11",
                ChannelWidths = (this.Controls.Find("txtChannelWidths", true).FirstOrDefault() as TextBox)?.Text ?? "20/40mhz-eC\r\n20/40mhz-Ce\r\n20mhz\r\n10mhz",
                CommandGetFrequency = (this.Controls.Find("txtCmdGetFreq", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless print where name=\"{interface}\" value-name=frequency",
                CommandSetFrequency = (this.Controls.Find("txtCmdSetFreq", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless set \"{interface}\" frequency={frequency}",
                CommandSetWirelessProtocol = (this.Controls.Find("txtCmdSetProtocol", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless set \"{interface}\" wireless-protocol={protocol}",
                CommandSetChannelWidth = (this.Controls.Find("txtCmdSetChannelWidth", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless set \"{interface}\" channel-width={channelWidth}",
                CommandGetInterfaceInfo = (this.Controls.Find("txtCmdGetInfo", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless print detail where name=\"{interface}\"",
                CommandGetRegistrationTable = (this.Controls.Find("txtCmdRegTable", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless registration-table print stat where interface=\"{interface}\"",
                CommandMonitorInterface = (this.Controls.Find("txtCmdMonitor", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless monitor \"{interface}\" once",
                CommandValidateInterface = (this.Controls.Find("txtCmdValidateInterface", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless print",
                Language = currentLanguage
            };
        }

        /// <summary>
        /// بارگذاری تنظیمات از فایل و اعمال به فرم
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                
                // Apply to form
                if (_txtRouterIp != null) _txtRouterIp.Text = settings.RouterIpAddress;
                if (_txtSshPort != null) _txtSshPort.Value = settings.SshPort;
                if (_txtUsername != null) _txtUsername.Text = settings.Username;
                if (_txtPassword != null) _txtPassword.Text = settings.Password;
                if (_txtStartFreq != null) _txtStartFreq.Value = (decimal)settings.StartFrequency;
                if (_txtEndFreq != null) _txtEndFreq.Value = (decimal)settings.EndFrequency;
                if (_txtFreqStep != null) _txtFreqStep.Value = (decimal)settings.FrequencyStep;
                if (_txtStabilizationTime != null) _txtStabilizationTime.Value = settings.StabilizationTimeMinutes;
                if (_txtInterface != null) _txtInterface.Text = settings.InterfaceName;
                if (_txtPingIp != null) _txtPingIp.Text = settings.PingTestIpAddress;
                
                // Load WirelessProtocols and ChannelWidths
                var txtWirelessProtocols = this.Controls.Find("txtWirelessProtocols", true).FirstOrDefault() as TextBox;
                if (txtWirelessProtocols != null && !string.IsNullOrEmpty(settings.WirelessProtocols))
                {
                    txtWirelessProtocols.Text = settings.WirelessProtocols;
                }
                
                var txtChannelWidths = this.Controls.Find("txtChannelWidths", true).FirstOrDefault() as TextBox;
                if (txtChannelWidths != null && !string.IsNullOrEmpty(settings.ChannelWidths))
                {
                    txtChannelWidths.Text = settings.ChannelWidths;
                }
                
                // Load commands
                if (this.Controls.Find("txtCmdGetFreq", true).FirstOrDefault() is TextBox txtCmdGetFreq)
                    txtCmdGetFreq.Text = settings.CommandGetFrequency;
                if (this.Controls.Find("txtCmdSetFreq", true).FirstOrDefault() is TextBox txtCmdSetFreq)
                    txtCmdSetFreq.Text = settings.CommandSetFrequency;
                if (this.Controls.Find("txtCmdSetProtocol", true).FirstOrDefault() is TextBox txtCmdSetProtocol)
                    txtCmdSetProtocol.Text = settings.CommandSetWirelessProtocol;
                if (this.Controls.Find("txtCmdSetChannelWidth", true).FirstOrDefault() is TextBox txtCmdSetChannelWidth)
                    txtCmdSetChannelWidth.Text = settings.CommandSetChannelWidth;
                if (this.Controls.Find("txtCmdGetInfo", true).FirstOrDefault() is TextBox txtCmdGetInfo)
                    txtCmdGetInfo.Text = settings.CommandGetInterfaceInfo;
                if (this.Controls.Find("txtCmdRegTable", true).FirstOrDefault() is TextBox txtCmdRegTable)
                    txtCmdRegTable.Text = settings.CommandGetRegistrationTable;
                if (this.Controls.Find("txtCmdMonitor", true).FirstOrDefault() is TextBox txtCmdMonitor)
                    txtCmdMonitor.Text = settings.CommandMonitorInterface;
                if (this.Controls.Find("txtCmdValidateInterface", true).FirstOrDefault() is TextBox txtCmdValidateInterface)
                    txtCmdValidateInterface.Text = settings.CommandValidateInterface;
            }
            catch (Exception ex)
            {
                ErrorHandler.ShowErrorWithSupport(ex, "بارگذاری تنظیمات", _txtTerminalLog);
            }
        }

        /// <summary>
        /// ذخیره تنظیمات از فرم به فایل
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                var settings = GetSettingsFromForm();
                if (_settingsService.SaveSettings(settings))
                {
                    var loc = _localizationService;
                    MessageBox.Show(loc.GetString("MsgSettingsSaved", "تنظیمات ذخیره شد."), loc.GetString("MsgSuccess", "موفق"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var loc = _localizationService;
                    MessageBox.Show(loc.GetString("MsgErrorSavingSettings", "خطا در ذخیره تنظیمات."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.ShowErrorWithSupport(ex, "ذخیره تنظیمات", _txtTerminalLog);
            }
        }

        /// <summary>
        /// بازگردانی تنظیمات به مقادیر پیش‌فرض
        /// </summary>
        private void ResetToDefaults()
        {
            var loc = _localizationService;
            var result = MessageBox.Show(
                loc.GetString("MsgConfirmReset", "آیا مطمئن هستید که می‌خواهید تمام تنظیمات را به مقادیر پیش‌فرض بازگردانید؟"),
                loc.GetString("MsgConfirm", "تأیید"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            try
            {
                var defaultSettings = _settingsService.GetDefaultSettings();
                
                // Apply default values to form
                if (_txtRouterIp != null) _txtRouterIp.Text = defaultSettings.RouterIpAddress;
                if (_txtSshPort != null) _txtSshPort.Value = defaultSettings.SshPort;
                if (_txtUsername != null) _txtUsername.Text = defaultSettings.Username;
                if (_txtPassword != null) _txtPassword.Text = defaultSettings.Password;
                if (_txtStartFreq != null) _txtStartFreq.Value = (decimal)defaultSettings.StartFrequency;
                if (_txtEndFreq != null) _txtEndFreq.Value = (decimal)defaultSettings.EndFrequency;
                if (_txtFreqStep != null) _txtFreqStep.Value = (decimal)defaultSettings.FrequencyStep;
                if (_txtStabilizationTime != null) _txtStabilizationTime.Value = defaultSettings.StabilizationTimeMinutes;
                if (_txtInterface != null) _txtInterface.Text = defaultSettings.InterfaceName;
                if (_txtPingIp != null) _txtPingIp.Text = defaultSettings.PingTestIpAddress;

                // Reset WirelessProtocols and ChannelWidths
                var txtWirelessProtocols = this.Controls.Find("txtWirelessProtocols", true).FirstOrDefault() as TextBox;
                if (txtWirelessProtocols != null) txtWirelessProtocols.Text = defaultSettings.WirelessProtocols;

                var txtChannelWidths = this.Controls.Find("txtChannelWidths", true).FirstOrDefault() as TextBox;
                if (txtChannelWidths != null) txtChannelWidths.Text = defaultSettings.ChannelWidths;

                // Reset commands to defaults
                if (this.Controls.Find("txtCmdGetFreq", true).FirstOrDefault() is TextBox txtCmdGetFreq)
                    txtCmdGetFreq.Text = defaultSettings.CommandGetFrequency;
                if (this.Controls.Find("txtCmdSetFreq", true).FirstOrDefault() is TextBox txtCmdSetFreq)
                    txtCmdSetFreq.Text = defaultSettings.CommandSetFrequency;
                if (this.Controls.Find("txtCmdSetProtocol", true).FirstOrDefault() is TextBox txtCmdSetProtocol)
                    txtCmdSetProtocol.Text = defaultSettings.CommandSetWirelessProtocol;
                if (this.Controls.Find("txtCmdSetChannelWidth", true).FirstOrDefault() is TextBox txtCmdSetChannelWidth)
                    txtCmdSetChannelWidth.Text = defaultSettings.CommandSetChannelWidth;
                if (this.Controls.Find("txtCmdGetInfo", true).FirstOrDefault() is TextBox txtCmdGetInfo)
                    txtCmdGetInfo.Text = defaultSettings.CommandGetInterfaceInfo;
                if (this.Controls.Find("txtCmdRegTable", true).FirstOrDefault() is TextBox txtCmdRegTable)
                    txtCmdRegTable.Text = defaultSettings.CommandGetRegistrationTable;
                if (this.Controls.Find("txtCmdMonitor", true).FirstOrDefault() is TextBox txtCmdMonitor)
                    txtCmdMonitor.Text = defaultSettings.CommandMonitorInterface;
                if (this.Controls.Find("txtCmdValidateInterface", true).FirstOrDefault() is TextBox txtCmdValidateInterface)
                    txtCmdValidateInterface.Text = defaultSettings.CommandValidateInterface;

                MessageBox.Show(loc.GetString("MsgSettingsReset", "تنظیمات به مقادیر پیش‌فرض بازگردانده شد."), loc.GetString("MsgSuccess", "موفق"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ErrorHandler.ShowErrorWithSupport(ex, "بازگردانی تنظیمات", _txtTerminalLog);
            }
        }

        private async Task ConnectToRouterAsync()
        {
            if (_isConnected && _sshClient != null && _sshClient.IsConnected)
            {
                var loc = _localizationService;
                MessageBox.Show(loc.GetString("MsgAlreadyConnected", "در حال حاضر به روتر متصل هستید."), loc.GetString("MsgInfo", "اطلاع"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var settings = GetSettingsFromForm();
            
            if (string.IsNullOrWhiteSpace(settings.RouterIpAddress))
            {
                var loc = _localizationService;
                MessageBox.Show(loc.GetString("MsgEnterRouterIp", "لطفاً آدرس IP روتر را وارد کنید."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.Username))
            {
                var loc = _localizationService;
                MessageBox.Show(loc.GetString("MsgEnterUsername", "لطفاً نام کاربری را وارد کنید."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var btnConnect = this.Controls.Find("btnConnect", true).FirstOrDefault() as Button;
            var btnDisconnect = this.Controls.Find("btnDisconnect", true).FirstOrDefault() as Button;

            if (btnConnect != null) btnConnect.Enabled = false;
            if (_lblStatus != null) _lblStatus.Text = "در حال اتصال به روتر...";

            try
            {
                _sshClient = new MikroTikSshClient();
                
                // Forward terminal data events
                _sshClient.DataSent += (s, data) =>
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_txtTerminalLog != null)
                        {
                            AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] {data}\r\n");
                        }
                    });
                };

                _sshClient.DataReceived += (s, data) =>
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_txtTerminalLog != null)
                        {
                            AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] {data}\r\n");
                        }
                    });
                };

                var connected = await _sshClient.ConnectAsync(
                    settings.RouterIpAddress,
                    settings.SshPort,
                    settings.Username,
                    settings.Password
                );

                if (connected)
                {
                    _isConnected = true;
                    if (_lblStatus != null) _lblStatus.Text = "اتصال برقرار شد. در حال دریافت اطلاعات پایه...";
                    if (btnConnect != null) btnConnect.Enabled = false;
                    if (btnDisconnect != null) btnDisconnect.Enabled = true;
                    if (_btnStart != null) _btnStart.Enabled = true;
                    
                    // Validate interface name before proceeding
                    var interfaceValid = await ValidateInterfaceNameAsync(settings.InterfaceName);
                    if (!interfaceValid)
                    {
                        // Interface validation failed - disconnect and show error
                        _isConnected = false;
                        if (_lblStatus != null) _lblStatus.Text = "خطا: نام اینترفیس نامعتبر است.";
                        if (btnConnect != null) btnConnect.Enabled = true;
                        if (btnDisconnect != null) btnDisconnect.Enabled = false;
                        if (_btnStart != null) _btnStart.Enabled = false;
                        
                        var btnStatus = this.Controls.Find("btnStatus", true).FirstOrDefault() as Button;
                        if (btnStatus != null) btnStatus.Enabled = false;
                        
                        _sshClient?.Disconnect();
                        _sshClient?.Dispose();
                        _sshClient = null;
                        return;
                    }
                    
                    // Enable status button
                    var btnStatus2 = this.Controls.Find("btnStatus", true).FirstOrDefault() as Button;
                    if (btnStatus2 != null)
                    {
                        btnStatus2.Enabled = true;
                        btnStatus2.BackColor = Color.FromArgb(0, 150, 136);
                    }
                    
                    // Collect and display base status immediately after connection
                    await CollectAndDisplayBaseStatusAsync(settings);
                    
                    if (_lblStatus != null) _lblStatus.Text = "اتصال برقرار شد.";
                    var loc = _localizationService;
                    MessageBox.Show(loc.GetString("MsgConnectionSuccess", "اتصال به روتر با موفقیت برقرار شد و اطلاعات پایه دریافت شد."), loc.GetString("MsgSuccess", "موفق"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _isConnected = false;
                    if (_lblStatus != null) _lblStatus.Text = "خطا در اتصال به روتر.";
                    if (btnConnect != null) btnConnect.Enabled = true;
                    if (btnDisconnect != null) btnDisconnect.Enabled = false;
                    if (_btnStart != null) _btnStart.Enabled = false;
                    
                    var loc = _localizationService;
                    MessageBox.Show(loc.GetString("MsgConnectionError", "خطا در اتصال به روتر. لطفاً IP، پورت، نام کاربری و رمز عبور را بررسی کنید."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _sshClient?.Dispose();
                    _sshClient = null;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    _isConnected = false;
                    if (_lblStatus != null) _lblStatus.Text = "خطا در اتصال";
                    if (btnConnect != null) btnConnect.Enabled = true;
                    if (btnDisconnect != null) btnDisconnect.Enabled = false;
                    if (_btnStart != null) _btnStart.Enabled = false;
                    
                    var btnStatus = this.Controls.Find("btnStatus", true).FirstOrDefault() as Button;
                    if (btnStatus != null) btnStatus.Enabled = false;
                    
                    ErrorHandler.ShowErrorWithSupport(ex, "اتصال به روتر", _txtTerminalLog);
                    
                    _sshClient?.Dispose();
                    _sshClient = null;
                }
                catch
                {
                    // اگر حتی نمایش خطا هم خطا داد، حداقل اتصال را قطع کن
                    _isConnected = false;
                    _sshClient?.Dispose();
                    _sshClient = null;
                }
            }
        }

        private void DisconnectFromRouter()
        {
            try
            {
                _sshClient?.Disconnect();
                _sshClient?.Dispose();
                _sshClient = null;
                _isConnected = false;

                var btnConnect = this.Controls.Find("btnConnect", true).FirstOrDefault() as Button;
                var btnDisconnect = this.Controls.Find("btnDisconnect", true).FirstOrDefault() as Button;
                var btnStatus = this.Controls.Find("btnStatus", true).FirstOrDefault() as Button;

                if (btnConnect != null) btnConnect.Enabled = true;
                if (btnDisconnect != null) btnDisconnect.Enabled = false;
                if (_btnStart != null) _btnStart.Enabled = false;
                if (btnStatus != null)
                {
                    btnStatus.Enabled = false;
                    btnStatus.BackColor = Color.FromArgb(150, 150, 150);
                }
                if (_lblStatus != null) _lblStatus.Text = "اتصال قطع شد.";

                var loc = _localizationService;
                MessageBox.Show(loc.GetString("MsgDisconnected", "اتصال به روتر قطع شد."), loc.GetString("MsgInfo", "اطلاع"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ErrorHandler.ShowErrorWithSupport(ex, "قطع اتصال", _txtTerminalLog);
            }
        }

        /// <summary>
        /// اعتبارسنجی نام اینترفیس wireless
        /// این متد از ConnectionService برای بررسی معتبر بودن نام اینترفیس استفاده می‌کند
        /// </summary>
        /// <param name="interfaceName">نام اینترفیس مورد نظر برای بررسی</param>
        /// <returns>true اگر اینترفیس معتبر باشد، false در غیر این صورت</returns>
        private async Task<bool> ValidateInterfaceNameAsync(string interfaceName)
        {
            try
            {
                if (_sshClient == null || !_sshClient.IsConnected)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(interfaceName))
                {
                    MessageBox.Show(
                        "نام اینترفیس خالی است. لطفاً نام اینترفیس را در تنظیمات وارد کنید.",
                        "خطا: نام اینترفیس نامعتبر",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                if (_lblStatus != null) _lblStatus.Text = $"در حال بررسی نام اینترفیس '{interfaceName}'...";
                
                var settings = GetSettingsFromForm();
                var interfaceValidationResult = await _connectionService.ValidateInterfaceNameAsync(_sshClient, settings, interfaceName);

                if (!interfaceValidationResult.IsValid)
                {
                    if (!string.IsNullOrEmpty(interfaceValidationResult.ErrorMessage))
                    {
                        MessageBox.Show(
                            interfaceValidationResult.ErrorMessage,
                            "خطا: نام اینترفیس نامعتبر",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

                    if (_txtTerminalLog != null)
                    {
                        var availableInterfaces = interfaceValidationResult.AvailableInterfaces.Count > 0 
                            ? string.Join(", ", interfaceValidationResult.AvailableInterfaces) 
                            : "(هیچ اینترفیس wireless یافت نشد)";
                        AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] [VALIDATE] ❌ اینترفیس '{interfaceName}' یافت نشد. اینترفیس‌های موجود: {availableInterfaces}\r\n");
                    }

                    return false;
                }

                // Interface is valid
                if (_txtTerminalLog != null)
                {
                    AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] [VALIDATE] ✅ اینترفیس '{interfaceName}' معتبر است.\r\n");
                }

                if (_lblStatus != null) _lblStatus.Text = $"اینترفیس '{interfaceName}' معتبر است.";
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (_txtTerminalLog != null)
                    {
                        AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] [VALIDATE] ❌ خطا: {ex.Message}\r\n");
                    }
                    
                    ErrorHandler.ShowErrorWithSupport(ex, "بررسی نام اینترفیس", _txtTerminalLog);
                }
                catch
                {
                    // اگر نمایش خطا هم خطا داد، حداقل false برگردان
                }
                
                return false;
            }
        }

        /// <summary>
        /// Collects and displays base status information immediately after connection
        /// </summary>
        private async Task CollectAndDisplayBaseStatusAsync(ScanSettings settings)
        {
            try
            {
                if (_sshClient == null || !_sshClient.IsConnected)
                {
                    return;
                }

                // Check if we already have a base result (to avoid duplicates on reconnection)
                bool hasBaseResult = false;
                this.Invoke((MethodInvoker)delegate
                {
                    hasBaseResult = _currentResults.Any(r => r.Status == "base");
                    if (hasBaseResult)
                    {
                        // Remove old base result before adding new one
                        var oldBase = _currentResults.FirstOrDefault(r => r.Status == "base");
                        if (oldBase != null)
                        {
                            _currentResults.Remove(oldBase);
                        }
                    }
                });

                // Create a temporary scanner instance to use its GetCurrentStatusAsync method
                var tempScanner = new FrequencyScanner(settings, _sshClient, _jsonService);
                
                // Subscribe to status updates
                tempScanner.StatusUpdate += (s, msg) =>
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_lblStatus != null)
                        {
                            _lblStatus.Text = msg;
                        }
                    });
                };

                // Subscribe to terminal data
                tempScanner.TerminalData += (s, data) =>
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_txtTerminalLog != null)
                        {
                            AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] {data}\r\n");
                        }
                    });
                };

                // Get current status
                var baseResult = await tempScanner.GetCurrentStatusAsync();
                
                if (baseResult != null)
                {
                    baseResult.Status = "base";
                    baseResult.ScanTime = DateTime.Now;
                    
                    // Save base settings for restoration later
                    _baseSettings = new FrequencyScanResult
                    {
                        Frequency = baseResult.Frequency,
                        WirelessProtocol = baseResult.WirelessProtocol,
                        ChannelWidth = baseResult.ChannelWidth
                    };

                    // Add to results list
                    this.Invoke((MethodInvoker)delegate
                    {
                        _allResults.Add(baseResult);
                        _currentResults.Add(baseResult);
                        
                        // Refresh DataGridView
                        if (_dgvResults != null)
                        {
                            _dgvResults.Refresh();
                            _dgvResults.Update();
                            
                            // Scroll to the last row safely
                            try
                            {
                                if (_dgvResults.Rows.Count > 0)
                                {
                                    var lastRowIndex = _dgvResults.Rows.Count - 1;
                                    // Ensure the row index is valid
                                    if (lastRowIndex >= 0 && lastRowIndex < _dgvResults.Rows.Count)
                                    {
                                        // Use BeginInvoke to ensure DataGridView is fully rendered
                                        this.BeginInvoke((MethodInvoker)delegate
                                        {
                                            try
                                            {
                                                if (_dgvResults.Rows.Count > lastRowIndex)
                                                {
                                                    _dgvResults.FirstDisplayedScrollingRowIndex = lastRowIndex;
                                                }
                                            }
                                            catch
                                            {
                                                // Ignore scroll errors - not critical
                                            }
                                        });
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore scroll errors - not critical
                            }
                        }
                    });

                    // Save to JSON file (only if this is a new scan, not a reconnection)
                    if (!hasBaseResult)
                    {
                        _jsonService.StartNewScan();
                    }
                    _jsonService.SaveSingleResult(baseResult, settings);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_lblStatus != null)
                        {
                            _lblStatus.Text = "خطا در دریافت اطلاعات پایه";
                        }
                        ErrorHandler.ShowErrorWithSupport(ex, "دریافت اطلاعات پایه", _txtTerminalLog);
                    });
                }
                catch
                {
                    // اگر Invoke خطا داد، حداقل خطا را لاگ کن
                    ErrorHandler.ShowErrorWithSupport(ex, "دریافت اطلاعات پایه", _txtTerminalLog);
                }
            }
        }

        private async Task StartScanAsync()
        {
            if (!_isConnected || _sshClient == null || !_sshClient.IsConnected)
            {
                var loc = _localizationService;
                MessageBox.Show(loc.GetString("MsgConnectFirst", "لطفاً ابتدا به روتر متصل شوید."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Validate settings
            var settings = GetSettingsFromForm();
            var validation = SettingsValidator.Validate(settings);
            
            if (!validation.IsValid)
            {
                var errorMessage = string.Join("\n", validation.Errors);
                var loc = _localizationService;
                MessageBox.Show(string.Format(loc.GetString("MsgSettingsErrors", "لطفاً خطاهای زیر را برطرف کنید:\n\n{0}"), errorMessage), loc.GetString("MsgSettingsErrorTitle", "خطا در تنظیمات"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _currentResults.Clear();

            if (_btnStart != null) _btnStart.Enabled = false;
            if (_btnStop != null) _btnStop.Enabled = true;
            if (_progressBar != null)
            {
                _progressBar.Value = 0;
                _progressBar.Style = ProgressBarStyle.Continuous;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _scanner = new FrequencyScanner(settings, _sshClient, _jsonService);

            _scanner.ScanProgress += (s, result) =>
            {
                this.Invoke((MethodInvoker)delegate
                {
                    _allResults.Add(result);
                    _currentResults.Add(result);
                    
                    // Update charts with new data
                    UpdateCharts();
                    
                    // Force DataGridView to update and show new row
                    if (_dgvResults != null)
                    {
                        // Ensure DataSource is set through BindingSource
                        if (_bindingSource != null && _dgvResults.DataSource != _bindingSource)
                        {
                            _dgvResults.DataSource = _bindingSource;
                        }
                        
                        // Refresh and scroll to last row
                        _dgvResults.Refresh();
                        _dgvResults.Update();
                        
                        // Scroll to the last row to show the new result
                        try
                        {
                            if (_dgvResults.Rows.Count > 0)
                            {
                                var lastRowIndex = _dgvResults.Rows.Count - 1;
                                // Ensure the row index is valid
                                if (lastRowIndex >= 0 && lastRowIndex < _dgvResults.Rows.Count)
                                {
                                    // Use BeginInvoke to ensure DataGridView is fully rendered
                                    this.BeginInvoke((MethodInvoker)delegate
                                    {
                                        try
                                        {
                                            if (_dgvResults != null && _dgvResults.Rows.Count > lastRowIndex)
                                            {
                                                _dgvResults.FirstDisplayedScrollingRowIndex = lastRowIndex;
                                                _dgvResults.Rows[lastRowIndex].Selected = true;
                                            }
                                        }
                                        catch
                                        {
                                            // Ignore scroll errors - not critical
                                        }
                                    });
                                }
                            }
                        }
                        catch
                        {
                            // Ignore scroll errors - not critical
                        }
                    }
                });
            };

            _scanner.StatusUpdate += (s, message) =>
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (_lblStatus != null)
                        _lblStatus.Text = message;
                });
            };

            _scanner.ProgressChanged += (s, progress) =>
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (_progressBar != null)
                        _progressBar.Value = Math.Min(100, Math.Max(0, progress));
                });
            };

            _scanner.TerminalData += (s, data) =>
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (_txtTerminalLog != null)
                    {
                        _txtTerminalLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {data}\r\n");
                        _txtTerminalLog.SelectionStart = _txtTerminalLog.Text.Length;
                        _txtTerminalLog.ScrollToCaret();
                    }
                });
            };

            try
            {
                var results = await _scanner.StartScanAsync(_cancellationTokenSource.Token);
                
                // Save to JSON
                _jsonService.SaveScanResults(results, settings);
                
                if (_lblStatus != null)
                    _lblStatus.Text = $"اسکن کامل شد. {results.Count} نتیجه ذخیره شد.";
            }
            catch (Exception ex)
            {
                try
                {
                    if (_lblStatus != null)
                        _lblStatus.Text = "خطا در اسکن";
                    ErrorHandler.ShowErrorWithSupport(ex, "اجرای اسکن", _txtTerminalLog);
                }
                catch
                {
                    // اگر نمایش خطا هم خطا داد، حداقل وضعیت را به‌روز کن
                    if (_lblStatus != null)
                        _lblStatus.Text = "خطا در اسکن";
                }
            }
            finally
            {
                // Restore base settings after scan completes
                await RestoreBaseSettingsAsync();
                
                if (_btnStart != null) _btnStart.Enabled = true;
                if (_btnStop != null) _btnStop.Enabled = false;
                if (_progressBar != null) _progressBar.Value = 0;
            }
        }

        private async void StopScan()
        {
            _scanner?.StopScan();
            _cancellationTokenSource?.Cancel();
            
            // Restore base settings
            await RestoreBaseSettingsAsync();
            
            if (_btnStart != null) _btnStart.Enabled = true;
            if (_btnStop != null) _btnStop.Enabled = false;
            if (_lblStatus != null) _lblStatus.Text = "متوقف شد";
        }
        
        /// <summary>
        /// Restores router settings to base configuration
        /// </summary>
        private async Task RestoreBaseSettingsAsync()
        {
            if (_baseSettings == null || _sshClient == null || !_sshClient.IsConnected)
            {
                return;
            }

            try
            {
                var settings = GetSettingsFromForm();
                
                if (_lblStatus != null)
                {
                    _lblStatus.Text = "در حال بازگردانی تنظیمات به حالت اولیه...";
                }

                var restoreCommands = new List<string>();

                // Restore frequency (always restore if we have a base frequency)
                if (_baseSettings.Frequency > 0)
                {
                    var setFreqCommand = settings.CommandSetFrequency
                        .Replace("{interface}", settings.InterfaceName)
                        .Replace("{frequency}", _baseSettings.Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    restoreCommands.Add(setFreqCommand);
                }

                // Restore wireless-protocol (only if we have a value)
                if (!string.IsNullOrEmpty(_baseSettings.WirelessProtocol))
                {
                    var setProtocolCommand = settings.CommandSetWirelessProtocol
                        .Replace("{interface}", settings.InterfaceName)
                        .Replace("{protocol}", _baseSettings.WirelessProtocol);
                    restoreCommands.Add(setProtocolCommand);
                }

                // Restore channel-width (only if we have a value)
                if (!string.IsNullOrEmpty(_baseSettings.ChannelWidth))
                {
                    var setChannelWidthCommand = settings.CommandSetChannelWidth
                        .Replace("{interface}", settings.InterfaceName)
                        .Replace("{channelWidth}", _baseSettings.ChannelWidth);
                    restoreCommands.Add(setChannelWidthCommand);
                }

                // Execute all restore commands
                foreach (var command in restoreCommands)
                {
                    try
                    {
                        await _sshClient.SendCommandAsync(command, 5000);
                        if (_txtTerminalLog != null)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] Restore: {command}\r\n");
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but continue with other commands
                        if (_txtTerminalLog != null)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] Error restoring {command}: {ex.Message}\r\n");
                            });
                        }
                    }
                }

                if (_lblStatus != null)
                {
                    var protocolInfo = !string.IsNullOrEmpty(_baseSettings.WirelessProtocol) ? _baseSettings.WirelessProtocol : "unchanged";
                    var channelWidthInfo = !string.IsNullOrEmpty(_baseSettings.ChannelWidth) ? _baseSettings.ChannelWidth : "unchanged";
                    _lblStatus.Text = $"تنظیمات به حالت اولیه بازگردانده شد (Frequency: {_baseSettings.Frequency}, Protocol: {protocolInfo}, ChannelWidth: {channelWidthInfo}).";
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (_lblStatus != null)
                    {
                        _lblStatus.Text = "خطا در بازگردانی تنظیمات";
                    }
                    ErrorHandler.ShowErrorWithSupport(ex, "بازگردانی تنظیمات پایه", _txtTerminalLog);
                }
                catch
                {
                    // اگر نمایش خطا هم خطا داد، حداقل وضعیت را به‌روز کن
                    if (_lblStatus != null)
                        _lblStatus.Text = "خطا در بازگردانی تنظیمات";
                }
            }
        }
        
        /// <summary>
        /// Tests automatic reconnection for up to 1 minute
        /// </summary>
        private async Task TestReconnectionAsync()
        {
            if (_sshClient == null || !_sshClient.IsConnected)
            {
                var loc = _localizationService;
                MessageBox.Show(loc.GetString("MsgConnectFirst", "لطفاً ابتدا به روتر متصل شوید."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var settings = GetSettingsFromForm();
            var endTime = DateTime.Now.AddMinutes(1);
            var testCount = 0;
            var successCount = 0;
            var failCount = 0;

            if (_lblStatus != null)
            {
                _lblStatus.Text = "شروع تست اتصال مجدد (1 دقیقه)...";
            }

            try
            {
                while (DateTime.Now < endTime)
                {
                    testCount++;
                    
                    // Disconnect
                    _sshClient.Disconnect();
                    await Task.Delay(1000); // Wait 1 second
                    
                    // Try to reconnect automatically (by sending a command which should trigger auto-reconnect)
                    try
                    {
                        var testCommand = ":put \"reconnection-test\"";
                        var response = await _sshClient.SendCommandAsync(testCommand, 5000);
                        
                        if (_sshClient.IsConnected && !string.IsNullOrEmpty(response))
                        {
                            successCount++;
                            if (_txtTerminalLog != null)
                            {
                                AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] تست {testCount}: اتصال مجدد موفق ✓\r\n");
                            }
                        }
                        else
                        {
                            failCount++;
                            if (_txtTerminalLog != null)
                            {
                                AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] تست {testCount}: اتصال مجدد ناموفق ✗\r\n");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        if (_txtTerminalLog != null)
                        {
                            AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] تست {testCount}: خطا - {ex.Message}\r\n");
                        }
                    }
                    
                    // Wait a bit before next test
                    await Task.Delay(2000); // Wait 2 seconds between tests
                    
                    if (_lblStatus != null)
                    {
                        var remaining = (endTime - DateTime.Now).TotalSeconds;
                        var remainingSeconds = (int)Math.Max(0, remaining);
                        _lblStatus.Text = $"تست اتصال مجدد: {testCount} تست ({successCount} موفق، {failCount} ناموفق) - {remainingSeconds} ثانیه باقی مانده";
                    }
                }

                // Final summary
                var successRate = testCount > 0 ? (successCount * 100.0 / testCount) : 0;
                var summary = $"تست اتصال مجدد تکمیل شد:\nتعداد کل تست‌ها: {testCount}\nموفق: {successCount}\nناموفق: {failCount}\nنرخ موفقیت: {successRate:F1}%";
                
                if (_txtTerminalLog != null)
                {
                    AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] {summary}\r\n");
                }

                if (_lblStatus != null)
                {
                    var successRate2 = testCount > 0 ? (successCount * 100.0 / testCount) : 0;
                    _lblStatus.Text = $"تست اتصال مجدد: {successCount}/{testCount} موفق ({successRate2:F1}%)";
                }

                var loc = _localizationService;
                MessageBox.Show(summary, loc.GetString("MsgReconnectTestResult", "نتیجه تست اتصال مجدد"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                try
                {
                    if (_lblStatus != null)
                    {
                        _lblStatus.Text = "خطا در تست اتصال مجدد";
                    }
                    ErrorHandler.ShowErrorWithSupport(ex, "تست اتصال مجدد", _txtTerminalLog);
                }
                catch
                {
                    // اگر نمایش خطا هم خطا داد، حداقل وضعیت را به‌روز کن
                    if (_lblStatus != null)
                        _lblStatus.Text = "خطا در تست اتصال مجدد";
                }
            }
        }

        private void LoadPreviousResults()
        {
            try
            {
                var files = _jsonService.GetAvailableScanFiles();
                if (files.Count == 0)
                {
                    var loc = _localizationService;
                    MessageBox.Show(loc.GetString("MsgNoResultFiles", "هیچ فایل نتیجه‌ای یافت نشد."), loc.GetString("MsgInfo", "اطلاع"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Show file selection dialog
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                    dialog.InitialDirectory = System.IO.Path.Combine(Application.StartupPath, "ScanResults");
                    dialog.Title = "انتخاب فایل نتایج";

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        var results = _jsonService.LoadScanResults(dialog.FileName);
                        if (results.Count > 0)
                        {
                            _currentResults?.Clear();
                            _allResults?.Clear();
                            foreach (var result in results)
                            {
                                if (_allResults != null) _allResults.Add(result);
                                if (_currentResults != null) _currentResults.Add(result);
                            }
                            
                            // Ensure BindingSource is properly set
                            if (_bindingSource != null)
                            {
                                _bindingSource.DataSource = null;
                                _bindingSource.DataSource = _currentResults;
                            }
                            
                            // Ensure DataGridView DataSource is set through BindingSource
                            if (_dgvResults != null && _bindingSource != null)
                            {
                                _dgvResults.DataSource = null;
                                _dgvResults.DataSource = _bindingSource;
                                
                                // Ensure all columns have SortableHeaderCell
                                foreach (DataGridViewColumn column in _dgvResults.Columns)
                                {
                                    if (!(column.HeaderCell is SortableHeaderCell))
                                    {
                                        column.HeaderCell = new SortableHeaderCell();
                                    }
                                    column.SortMode = DataGridViewColumnSortMode.Programmatic;
                                }
                            }
                            
                            // Apply filters and refresh
                            ApplyFilters();
                            
                            // Update charts with loaded data
                            UpdateCharts();
                            
                            // Refresh DataGridView
                            if (_dgvResults != null)
                            {
                                _dgvResults.Refresh();
                                _dgvResults.Invalidate();
                            }
                            
                            var loc = _localizationService;
                            MessageBox.Show(string.Format(loc.GetString("MsgResultsLoaded", "{0} نتیجه بارگذاری شد."), results.Count), loc.GetString("MsgSuccess", "موفق"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            var loc = _localizationService;
                            MessageBox.Show(loc.GetString("MsgInvalidFile", "فایل انتخاب شده معتبر نیست یا خالی است."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.ShowErrorWithSupport(ex, "بارگذاری نتایج", _txtTerminalLog);
            }
        }

        /// <summary>
        /// Sets the application icon from Program.ApplicationIcon or creates a programmatic one
        /// </summary>
        private void SetApplicationIcon()
        {
            try
            {
                // Use icon from Program if available (set in Program.Main)
                if (Program.ApplicationIcon != null)
                {
                    this.Icon = Program.ApplicationIcon;
                    return;
                }
                
                // Fallback: Try to load from file directly
                var possiblePaths = new[]
                {
                    System.IO.Path.Combine(Application.StartupPath, "icon.ico"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "icon.ico"),
                    "icon.ico"
                };
                
                foreach (var iconPath in possiblePaths)
                {
                    if (System.IO.File.Exists(iconPath))
                    {
                        try
                        {
                            using (var iconStream = new System.IO.FileStream(iconPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                            {
                                this.Icon = new Icon(iconStream);
                                return;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting icon: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets current status and displays it in the results grid
        /// </summary>
        private async Task GetCurrentStatusAsync()
        {
            if (!_isConnected || _sshClient == null || !_sshClient.IsConnected)
            {
                var loc = _localizationService;
                MessageBox.Show(loc.GetString("MsgConnectFirst", "لطفاً ابتدا به روتر متصل شوید."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var settings = GetSettingsFromForm();
                
                if (_lblStatus != null)
                {
                    _lblStatus.Text = "در حال دریافت وضعیت فعلی...";
                }

                // Create a temporary scanner instance to use its GetCurrentStatusAsync method
                var tempScanner = new FrequencyScanner(settings, _sshClient, _jsonService);
                
                // Subscribe to status updates
                tempScanner.StatusUpdate += (s, msg) =>
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_lblStatus != null)
                        {
                            _lblStatus.Text = msg;
                        }
                    });
                };

                // Subscribe to terminal data
                tempScanner.TerminalData += (s, data) =>
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_txtTerminalLog != null)
                        {
                            AppendToTerminalLog($"[{DateTime.Now:HH:mm:ss}] {data}\r\n");
                        }
                    });
                };

                // Get current status
                var statusResult = await tempScanner.GetCurrentStatusAsync();
                
                if (statusResult != null)
                {
                    statusResult.Status = "وضعیت";
                    statusResult.ScanTime = DateTime.Now;
                    
                    // Add to results list
                    this.Invoke((MethodInvoker)delegate
                    {
                        _allResults.Add(statusResult);
                        _currentResults.Add(statusResult);
                        
                        // Refresh DataGridView
                        if (_dgvResults != null)
                        {
                            _dgvResults.Refresh();
                            _dgvResults.Update();
                            
                            // Scroll to the last row
                            try
                            {
                                if (_dgvResults.Rows.Count > 0)
                                {
                                    var lastRowIndex = _dgvResults.Rows.Count - 1;
                                    if (lastRowIndex >= 0 && lastRowIndex < _dgvResults.Rows.Count)
                                    {
                                        this.BeginInvoke((MethodInvoker)delegate
                                        {
                                            try
                                            {
                                                if (_dgvResults.Rows.Count > lastRowIndex)
                                                {
                                                    _dgvResults.FirstDisplayedScrollingRowIndex = lastRowIndex;
                                                    _dgvResults.Rows[lastRowIndex].Selected = true;
                                                }
                                            }
                                            catch
                                            {
                                                // Ignore scroll errors
                                            }
                                        });
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore scroll errors
                            }
                        }
                    });

                    if (_lblStatus != null)
                    {
                        _lblStatus.Text = "وضعیت فعلی دریافت و نمایش داده شد.";
                    }
                    
                    MessageBox.Show(
                        $"وضعیت فعلی دریافت شد:\n" +
                        $"فرکانس: {statusResult.Frequency} MHz\n" +
                        $"SNR: {statusResult.SignalToNoiseRatio?.ToString("F2") ?? "N/A"} dB\n" +
                        $"Signal: {statusResult.SignalStrength?.ToString("F2") ?? "N/A"} dBm\n" +
                        $"Noise: {statusResult.NoiseFloor?.ToString("F2") ?? "N/A"} dBm\n" +
                        $"CCQ: {statusResult.CCQ?.ToString("F2") ?? "N/A"}%\n" +
                        (statusResult.RemoteSignalStrength.HasValue ? 
                            $"Remote Signal: {statusResult.RemoteSignalStrength.Value:F2} dBm\n" : "") +
                        (statusResult.RemoteCCQ.HasValue ? 
                            $"Remote CCQ: {statusResult.RemoteCCQ.Value:F2}%\n" : ""),
                        "وضعیت فعلی",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    if (_lblStatus != null)
                    {
                        _lblStatus.Text = "خطا در دریافت وضعیت.";
                    }
                    var loc = _localizationService;
                    MessageBox.Show(loc.GetString("MsgStatusError", "خطا در دریافت وضعیت فعلی."), loc.GetString("MsgError", "خطا"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (_lblStatus != null)
                    {
                        _lblStatus.Text = "خطا در دریافت وضعیت";
                    }
                    ErrorHandler.ShowErrorWithSupport(ex, "دریافت وضعیت", _txtTerminalLog);
                }
                catch
                {
                    // اگر نمایش خطا هم خطا داد، حداقل وضعیت را به‌روز کن
                    if (_lblStatus != null)
                        _lblStatus.Text = "خطا در دریافت وضعیت";
                }
            }
        }

        /// <summary>
        /// اضافه کردن متن به لاگ ترمینال با رنگ مناسب
        /// خطاها با رنگ قرمز و سایر پیام‌ها با رنگ پیش‌فرض نمایش داده می‌شوند
        /// </summary>
        /// <param name="text">متن برای اضافه کردن</param>
        private void AppendToTerminalLog(string text)
        {
            if (_txtTerminalLog == null)
                return;

            try
            {
                // تعیین رنگ بر اساس محتوای متن
                Color textColor = System.Drawing.Color.LimeGreen; // رنگ پیش‌فرض
                
                // اگر متن شامل کلمات کلیدی خطا باشد، رنگ قرمز استفاده کن
                if (text.Contains("[ERROR]") || 
                    text.Contains("❌") || 
                    text.Contains("خطا") || 
                    text.Contains("Error") || 
                    text.Contains("error") ||
                    text.Contains("Exception") ||
                    text.Contains("exception") ||
                    text.Contains("failed") ||
                    text.Contains("Failed") ||
                    text.Contains("ناموفق") ||
                    text.Contains("✗"))
                {
                    textColor = System.Drawing.Color.Red;
                }
                else if (text.Contains("✅") || 
                         text.Contains("موفق") || 
                         text.Contains("success") || 
                         text.Contains("Success") ||
                         text.Contains("Connected successfully") ||
                         text.Contains("✓"))
                {
                    textColor = System.Drawing.Color.LimeGreen;
                }
                else if (text.Contains("[SENT]") || text.Contains(">"))
                {
                    textColor = System.Drawing.Color.Cyan;
                }
                else if (text.Contains("[RECEIVED]"))
                {
                    textColor = System.Drawing.Color.Yellow;
                }
                else if (text.Contains("[VALIDATE]"))
                {
                    textColor = System.Drawing.Color.Orange;
                }

                // اضافه کردن متن با رنگ مناسب
                _txtTerminalLog.SelectionStart = _txtTerminalLog.Text.Length;
                _txtTerminalLog.SelectionLength = 0;
                _txtTerminalLog.SelectionColor = textColor;
                _txtTerminalLog.AppendText(text);
                _txtTerminalLog.SelectionColor = _txtTerminalLog.ForeColor; // بازگشت به رنگ پیش‌فرض
                _txtTerminalLog.SelectionStart = _txtTerminalLog.Text.Length;
                _txtTerminalLog.ScrollToCaret();
            }
            catch
            {
                // اگر خطا در اضافه کردن متن رخ داد، از روش ساده استفاده کن
                try
                {
                    _txtTerminalLog.AppendText(text);
                    _txtTerminalLog.SelectionStart = _txtTerminalLog.Text.Length;
                    _txtTerminalLog.ScrollToCaret();
                }
                catch
                {
                    // Ignore errors
                }
            }
        }

        /// <summary>
        /// اعمال فیلترها بر روی نتایج نمایش داده شده در DataGridView
        /// این متد از DataFilterService برای فیلتر کردن نتایج استفاده می‌کند
        /// </summary>
        private void ApplyFilters()
        {
            if (_filterTextBoxes == null || _allResults == null || _currentResults == null)
                return;

            try
            {
                // Clear current results
                _currentResults.Clear();

                // Build filters dictionary
                var filters = new Dictionary<string, string>();
                foreach (var filterPair in _filterTextBoxes)
                {
                    var propertyName = filterPair.Key;
                    var filterText = filterPair.Value.Text?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(filterText))
                    {
                        filters[propertyName] = filterText;
                    }
                }

                // Apply filters using DataFilterService
                var filteredResults = _dataFilterService.ApplyFilters(_allResults, filters);

                // Add filtered results
                foreach (var result in filteredResults)
                {
                    _currentResults.Add(result);
                }
                
                // Reset bindings to refresh DataGridView
                if (_bindingSource != null)
                {
                    _bindingSource.ResetBindings(false);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                System.Diagnostics.Debug.WriteLine($"Error applying filters: {ex.Message}");
            }
        }

        /// <summary>
        /// به‌روزرسانی تمام متون فرم بر اساس زبان انتخابی
        /// </summary>
        private void UpdateAllTexts()
        {
            try
            {
                var loc = _localizationService;
                
                // Form title
                this.Text = loc.GetString("FormTitle", "اسکنر فرکانس میکروتیک");
                
                // Status label
                if (_lblStatus != null)
                {
                    _lblStatus.Text = loc.GetString("StatusReady", "آماده");
                }
                
                // Buttons
                if (_btnStart != null)
                {
                    _btnStart.Text = $"▶ {loc.GetString("BtnStartScan", "شروع اسکن")}";
                }
                if (_btnStop != null)
                {
                    _btnStop.Text = $"⏹ {loc.GetString("BtnStop", "توقف")}";
                }
                
                // Tab pages
                if (this.Controls.Count > 0)
                {
                    var tabControl = this.Controls.OfType<TabControl>().FirstOrDefault();
                    if (tabControl != null)
                    {
                        foreach (TabPage tab in tabControl.TabPages)
                        {
                            if (tab.Text.Contains("⚙️"))
                            {
                                tab.Text = loc.GetString("TabSettings", "⚙️ تنظیمات");
                            }
                            else if (tab.Text.Contains("📊"))
                            {
                                tab.Text = loc.GetString("TabResults", "📊 نتایج و لاگ");
                            }
                            else if (tab.Text.Contains("📈"))
                            {
                                tab.Text = loc.GetString("TabCharts", "📈 نمودارها");
                            }
                            else if (tab.Text.Contains("ℹ️"))
                            {
                                tab.Text = loc.GetString("TabAbout", "ℹ️ درباره ما");
                            }
                        }
                    }
                }
                
                // Update all labels and buttons in settings tab
                UpdateSettingsTabTexts();
                
                // Update results tab texts
                UpdateResultsTabTexts();
                
                // Update about tab texts
                UpdateAboutTabTexts();
                
                // Update DataGridView column headers
                UpdateDataGridViewHeaders();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating texts: {ex.Message}");
            }
        }

        /// <summary>
        /// به‌روزرسانی متون تب تنظیمات
        /// </summary>
        private void UpdateSettingsTabTexts()
        {
            var loc = _localizationService;
            
            // Update buttons
            var btnSave = this.Controls.Find("btnSave", true).FirstOrDefault() as Button;
            if (btnSave != null)
            {
                btnSave.Text = $"💾 {loc.GetString("BtnSaveSettings", "ذخیره تنظیمات")}";
            }
            
            var btnLoadResults = this.Controls.Find("btnLoadResults", true).FirstOrDefault() as Button;
            if (btnLoadResults != null)
            {
                btnLoadResults.Text = $"📂 {loc.GetString("BtnLoadResults", "بارگذاری نتایج قبلی")}";
            }
            
            var btnResetDefaults = this.Controls.Find("btnResetDefaults", true).FirstOrDefault() as Button;
            if (btnResetDefaults != null)
            {
                btnResetDefaults.Text = $"🔄 {loc.GetString("BtnResetDefaults", "بازگشت به پیش‌فرض")}";
            }
            
            // Update labels in settings tab
            var labelMap = new Dictionary<string, string>
            {
                { "lblRouterIp", "LabelRouterIp" },
                { "lblSshPort", "LabelSshPort" },
                { "lblUsername", "LabelUsername" },
                { "lblPassword", "LabelPassword" },
                { "lblStartFreq", "LabelStartFrequency" },
                { "lblEndFreq", "LabelEndFrequency" },
                { "lblFreqStep", "LabelFrequencyStep" },
                { "lblStabilizationTime", "LabelStabilizationTime" },
                { "lblInterface", "LabelInterfaceName" },
                { "lblPingIp", "LabelPingTestIp" },
                { "lblWirelessProtocols", "LabelWirelessProtocols" },
                { "lblChannelWidths", "LabelChannelWidths" },
                { "lblCommands", "LabelRouterOSCommands" },
                { "lblCmdValidateInterface", "LabelCmdValidateInterface" },
                { "lblCmdGetFreq", "LabelCmdGetFrequency" },
                { "lblCmdGetInfo", "LabelCmdGetInfo" },
                { "lblCmdRegTable", "LabelCmdRegTable" },
                { "lblCmdMonitor", "LabelCmdMonitor" },
                { "lblCmdSetFreq", "LabelCmdSetFrequency" },
                { "lblCmdSetProtocol", "LabelCmdSetProtocol" },
                { "lblCmdSetChannelWidth", "LabelCmdSetChannelWidth" }
            };
            
            foreach (var kvp in labelMap)
            {
                var label = this.Controls.Find(kvp.Key, true).FirstOrDefault() as Label;
                if (label != null)
                {
                    label.Text = loc.GetString(kvp.Value, label.Text);
                }
            }
        }

        /// <summary>
        /// به‌روزرسانی متون تب نتایج
        /// </summary>
        private void UpdateResultsTabTexts()
        {
            var loc = _localizationService;
            
            // Update terminal log label
            var lblTerminalLog = this.Controls.Find("lblTerminalLog", true).FirstOrDefault() as Label;
            if (lblTerminalLog != null)
            {
                lblTerminalLog.Text = loc.GetString("LabelTerminalLog", "لاگ ترمینال:");
            }
            
            // Update clear button
            var btnClear = this.Controls.Find("btnClear", true).FirstOrDefault() as Button;
            if (btnClear != null)
            {
                btnClear.Text = loc.GetString("BtnClear", "🗑 پاک کردن");
            }
            
            // Update scan results label
            var lblScanResults = this.Controls.Find("lblScanResults", true).FirstOrDefault() as Label;
            if (lblScanResults != null)
            {
                lblScanResults.Text = loc.GetString("LabelScanResults", "نتایج اسکن:");
            }
            
            // Update filter label
            var lblFilter = this.Controls.Find("lblFilter", true).FirstOrDefault() as Label;
            if (lblFilter != null)
            {
                lblFilter.Text = loc.GetString("LabelFilter", "فیلتر:") + ":";
            }
        }

        /// <summary>
        /// به‌روزرسانی متون تب درباره
        /// </summary>
        private void UpdateAboutTabTexts()
        {
            var loc = _localizationService;

            // پیدا کردن تب درباره
            var tabControl = this.Controls.OfType<TabControl>().FirstOrDefault();
            if (tabControl == null) return;

            var aboutTab = tabControl.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(t => t.Text.Contains("ℹ️") || t.Text.Contains("About") || t.Text.Contains("درباره"));

            if (aboutTab == null) return;

            // به‌روزرسانی عنوان
            var lblTitle = aboutTab.Controls.Find("lblAboutTitle", true).FirstOrDefault() as Label;
            if (lblTitle != null)
            {
                lblTitle.Text = loc.GetString("AboutTitle", "درباره ابزار و توسعه‌دهنده");
            }

            // به‌روزرسانی توضیحات
            var txtDescription = aboutTab.Controls.Find("txtAboutDescription", true).FirstOrDefault() as TextBox;
            if (txtDescription != null)
            {
                txtDescription.Text = loc.GetString(
                    "AboutDescription",
                    "این برنامه برای اسکن و بهینه‌سازی فرکانس در روترهای MikroTik طراحی شده است تا بهترین کیفیت لینک Point-to-Point را پیدا کند.\r\n" +
                    "با اتصال امن SSH، ترکیب‌های مختلف فرکانس، پروتکل و Channel Width را تست می‌کند، نتایج را به‌صورت زنده نمایش می‌دهد و امکان ذخیره در فایل JSON را فراهم می‌کند.\r\n\r\n" +
                    "تمام رابط کاربری به زبان فارسی است و شامل فیلتر، مرتب‌سازی، لاگ ترمینال و مدیریت تنظیمات می‌باشد.");
            }

            // به‌روزرسانی ردیف‌های اطلاعاتی (بر اساس عنوان)
            var labelMap = new Dictionary<string, string>
            {
                { "نام پروژه", "AboutProjectName" },
                { "نسخه برنامه", "AboutVersion" },
                { "پلتفرم", "AboutPlatform" },
                { "توسعه‌دهنده", "AboutDeveloper" },
                { "ایمیل‌های تماس", "AboutContactEmails" },
                { "محل فعالیت", "AboutLocation" },
                { "شماره تماس", "AboutPhone" },
                { "مهارت‌های کلیدی", "AboutSkills" },
                { "تجربه", "AboutExperience" },
                { "تحصیلات", "AboutEducation" },
                { "مسیر اجرا/نصب", "AboutInstallPath" }
            };

            foreach (var kvp in labelMap)
            {
                var labels = aboutTab.Controls.OfType<TableLayoutPanel>()
                    .SelectMany(p => p.Controls.Cast<Control>())
                    .OfType<Label>()
                    .Where(l => l.Text == kvp.Key)
                    .ToList();

                foreach (var lbl in labels)
                {
                    lbl.Text = loc.GetString(kvp.Value, kvp.Key);
                }
            }

            // به‌روزرسانی متن پاورقی
            var lblFooter = aboutTab.Controls.Find("lblAboutFooter", true).FirstOrDefault() as Label;
            if (lblFooter != null)
            {
                lblFooter.Text = loc.GetString("AboutFooter", "در صورت نیاز به پشتیبانی یا پیشنهاد، اطلاعات بالا را به‌روزرسانی کنید و با تیم توسعه در تماس باشید.");
            }
        }

        /// <summary>
        /// به‌روزرسانی هدرهای DataGridView
        /// </summary>
        private void UpdateDataGridViewHeaders()
        {
            var loc = _localizationService;
            if (_dgvResults != null)
            {
                foreach (DataGridViewColumn col in _dgvResults.Columns)
                {
                    var key = $"Column{col.Name}";
                    var translated = loc.GetString(key, col.HeaderText);
                    if (translated != key)
                    {
                        col.HeaderText = translated;
                    }
                }
            }
        }
    }
}

