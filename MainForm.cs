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
        private BindingList<FrequencyScanResult> _currentResults;
        private BindingSource? _bindingSource;
        private List<FrequencyScanResult> _allResults; // Store all results for filtering
        private MikroTikSshClient? _sshClient;
        private bool _isConnected = false;
        
        // Base settings to restore after scan
        private FrequencyScanResult? _baseSettings;
        
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
            _currentResults = new BindingList<FrequencyScanResult>();
            _allResults = new List<FrequencyScanResult>();
            _bindingSource = new BindingSource();
            _columnNameToPropertyMap = new Dictionary<string, string>();
            
            try
            {
                InitializeComponent();
                LoadSettings();
            }
            catch (Exception ex)
            {
                // اگر حتی ساخت فرم هم خطا داد، خطا را نمایش بده
                try
                {
                    var errorDetails = $"خطا در راه‌اندازی برنامه:\n\n{ex.Message}";
                    
                    if (ex.InnerException != null)
                    {
                        errorDetails += $"\n\nخطای داخلی: {ex.InnerException.Message}";
                    }
                    
                    errorDetails += $"\n\nنوع خطا: {ex.GetType().Name}";
                    
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        errorDetails += $"\n\nجزئیات فنی:\n{ex.StackTrace.Substring(0, Math.Min(500, ex.StackTrace.Length))}...";
                    }
                    
                    errorDetails += "\n\n⚠️ لطفاً این پیام را به پشتیبانی اطلاع دهید.";
                    
                    MessageBox.Show(
                        errorDetails,
                        "خطای راه‌اندازی",
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
            this.Text = "اسکنر فرکانس میکروتیک";
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
                Text = "آماده",
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

            // Helper method to create styled button with icon
            Button CreateStyledButton(string text, string icon, Color backColor, int width = 110, int height = 38)
            {
                return new Button
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
            }

            _btnStart = CreateStyledButton("شروع اسکن", "▶", Color.FromArgb(46, 125, 50), 110, 38);
            _btnStop = CreateStyledButton("توقف", "⏹", Color.FromArgb(198, 40, 40), 110, 38);
            _btnStop.Enabled = false;
            _btnStop.BackColor = Color.FromArgb(150, 150, 150);
            
            var btnConnect = CreateStyledButton("اتصال", "🔌", Color.FromArgb(25, 118, 210), 110, 38);
            btnConnect.Name = "btnConnect";
            
            var btnDisconnect = CreateStyledButton("قطع اتصال", "🔌❌", Color.FromArgb(198, 40, 40), 120, 38);
            btnDisconnect.Enabled = false;
            btnDisconnect.BackColor = Color.FromArgb(150, 150, 150);
            btnDisconnect.Name = "btnDisconnect";
            
            var btnTestReconnect = CreateStyledButton("تست اتصال مجدد", "🔄", Color.FromArgb(123, 31, 162), 140, 38);
            btnTestReconnect.Name = "btnTestReconnect";
            
            var btnStatus = CreateStyledButton("وضعیت", "📊", Color.FromArgb(0, 150, 136), 110, 38);
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
            var settingsTab = new TabPage("⚙️ تنظیمات");
            settingsTab.Tag = (Color.FromArgb(25, 118, 210), Color.White); // (BackColor, ForeColor)
            CreateSettingsTab(settingsTab);
            tabControl.TabPages.Add(settingsTab);

            // Results and Terminal Tab (combined)
            var resultsTab = new TabPage("📊 نتایج و لاگ");
            resultsTab.Tag = (Color.FromArgb(46, 125, 50), Color.White); // (BackColor, ForeColor)
            CreateResultsAndTerminalTab(resultsTab);
            tabControl.TabPages.Add(resultsTab);

            // About Tab
            var aboutTab = new TabPage("ℹ️ درباره ما");
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

                    // Draw background
                    var bgColor = isSelected ? backColor : Color.FromArgb(245, 245, 245);
                    e.Graphics.FillRectangle(new System.Drawing.SolidBrush(bgColor), rect);

                    // Draw border for selected tab
                    if (isSelected)
                    {
                        e.Graphics.DrawRectangle(new System.Drawing.Pen(backColor, 2), rect);
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
                btn.FlatAppearance.BorderSize = 0;
                btn.MouseEnter += (s, e) => { btn.BackColor = Color.FromArgb(Math.Min(255, backColor.R + 20), Math.Min(255, backColor.G + 20), Math.Min(255, backColor.B + 20)); };
                btn.MouseLeave += (s, e) => { btn.BackColor = backColor; };
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

            // Router IP
            panel.Controls.Add(new Label { Text = "آدرس IP روتر:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtRouterIp = new TextBox { Name = "txtRouterIp", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtRouterIp, 1, row++);

            // SSH Port
            panel.Controls.Add(new Label { Text = "پورت SSH:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtSshPort = new NumericUpDown { Name = "txtSshPort", Minimum = 1, Maximum = 65535, Value = 22, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtSshPort, 1, row++);

            // Username
            panel.Controls.Add(new Label { Text = "نام کاربری:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtUsername = new TextBox { Name = "txtUsername", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtUsername, 1, row++);

            // Password
            panel.Controls.Add(new Label { Text = "رمز عبور:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtPassword = new TextBox { Name = "txtPassword", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtPassword, 1, row++);

            // Start Frequency
            panel.Controls.Add(new Label { Text = "فرکانس شروع (MHz):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtStartFreq = new NumericUpDown { Name = "txtStartFreq", Minimum = 1000, Maximum = 6000, Value = 2400, DecimalPlaces = 0, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtStartFreq, 1, row++);

            // End Frequency
            panel.Controls.Add(new Label { Text = "فرکانس پایان (MHz):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtEndFreq = new NumericUpDown { Name = "txtEndFreq", Minimum = 1000, Maximum = 6000, Value = 2500, DecimalPlaces = 0, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtEndFreq, 1, row++);

            // Frequency Step
            panel.Controls.Add(new Label { Text = "پرش فرکانس (MHz):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtFreqStep = new NumericUpDown { Name = "txtFreqStep", Minimum = 1, Maximum = 100, Value = 5, DecimalPlaces = 0, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtFreqStep, 1, row++);

            // Stabilization Time
            panel.Controls.Add(new Label { Text = "زمان استیبل شدن (دقیقه):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtStabilizationTime = new NumericUpDown { Name = "txtStabilizationTime", Minimum = 1, Maximum = 60, Value = 2, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtStabilizationTime, 1, row++);

            // Interface Name
            panel.Controls.Add(new Label { Text = "نام اینترفیس:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtInterface = new TextBox { Name = "txtInterface", Text = "wlan1", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtInterface, 1, row++);

            // Ping Test IP Address
            panel.Controls.Add(new Label { Text = "آدرس IP تست پینگ:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtPingIp = new TextBox { Name = "txtPingIp", Text = "8.8.8.8", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtPingIp, 1, row++);

            // Wireless Protocols (multiple, comma or newline separated)
            panel.Controls.Add(new Label { Text = "Wireless Protocols\n(جدا شده با کاما یا خط جدید):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtWirelessProtocols = new TextBox { Name = "txtWirelessProtocols", Multiline = true, Height = 60, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            panel.Controls.Add(txtWirelessProtocols, 1, row++);

            // Channel Widths (multiple, comma or newline separated)
            panel.Controls.Add(new Label { Text = "Channel Widths\n(جدا شده با کاما یا خط جدید):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtChannelWidths = new TextBox { Name = "txtChannelWidths", Multiline = true, Height = 60, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            panel.Controls.Add(txtChannelWidths, 1, row++);

            // Commands Section
            var lblCommands = new Label { Text = "کامندهای RouterOS (پیشرفته):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold) };
            panel.SetColumnSpan(lblCommands, 2);
            panel.Controls.Add(lblCommands, 0, row++);

            // Command Validate Interface (اول باید چک شود)
            panel.Controls.Add(new Label { Text = "کامند اعتبارسنجی اینترفیس:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdValidateInterface = new TextBox { Name = "txtCmdValidateInterface", Text = "/interface wireless print", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdValidateInterface, 1, row++);

            // Command Get Frequency
            panel.Controls.Add(new Label { Text = "کامند دریافت فرکانس:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdGetFreq = new TextBox { Name = "txtCmdGetFreq", Text = "/interface wireless print where name=\"{interface}\" value-name=frequency", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdGetFreq, 1, row++);

            // Command Get Interface Info
            panel.Controls.Add(new Label { Text = "کامند دریافت اطلاعات:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdGetInfo = new TextBox { Name = "txtCmdGetInfo", Text = "/interface wireless print detail where name=\"{interface}\"", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdGetInfo, 1, row++);

            // Command Get Registration Table
            panel.Controls.Add(new Label { Text = "کامند Registration Table:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdRegTable = new TextBox { Name = "txtCmdRegTable", Text = "/interface wireless registration-table print stat where interface=\"{interface}\"", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdRegTable, 1, row++);

            // Command Monitor
            panel.Controls.Add(new Label { Text = "کامند Monitor:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdMonitor = new TextBox { Name = "txtCmdMonitor", Text = "/interface wireless monitor \"{interface}\" once", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdMonitor, 1, row++);

            // Command Set Frequency
            panel.Controls.Add(new Label { Text = "کامند تنظیم فرکانس:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdSetFreq = new TextBox { Name = "txtCmdSetFreq", Text = "/interface wireless set \"{interface}\" frequency={frequency}", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdSetFreq, 1, row++);

            // Command Set Wireless Protocol
            panel.Controls.Add(new Label { Text = "کامند تنظیم Wireless Protocol:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdSetProtocol = new TextBox { Name = "txtCmdSetProtocol", Text = "/interface wireless set \"{interface}\" wireless-protocol={protocol}", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdSetProtocol, 1, row++);

            // Command Set Channel Width
            panel.Controls.Add(new Label { Text = "کامند تنظیم Channel Width:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdSetChannelWidth = new TextBox { Name = "txtCmdSetChannelWidth", Text = "/interface wireless set \"{interface}\" channel-width={channelWidth}", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdSetChannelWidth, 1, row++);

            outerPanel.Controls.Add(panel);
            tab.Controls.Add(outerPanel);
        }

        private void CreateResultsAndTerminalTab(TabPage tab)
        {
            // Use SplitContainer to show terminal log (top) and results (bottom)
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 300, // Terminal log takes 300px, results take the rest
                SplitterWidth = 5
            };

            // Terminal Log Panel (top - Panel1)
            var terminalPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5), BackColor = Color.White };

            var terminalLabel = new Label
            {
                Text = "لاگ ترمینال:",
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
                Text = "🗑 پاک کردن",
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
                Text = "نتایج اسکن:",
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
                Text = "فیلتر:",
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

        private void CreateTerminalLogTab(TabPage tab)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var label = new Label 
            { 
                Text = "داده‌های ارسالی و دریافتی ترمینال:", 
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
                Text = "🗑 پاک کردن",
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
                Text = "درباره ابزار و توسعه‌دهنده",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 10)
            };

            var descriptionBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                Dock = DockStyle.Top,
                Height = 160,
                ScrollBars = ScrollBars.Vertical,
                Text =
                    "این برنامه برای اسکن و بهینه‌سازی فرکانس در روترهای MikroTik طراحی شده است تا بهترین کیفیت لینک Point-to-Point را پیدا کند.\r\n" +
                    "با اتصال امن SSH، ترکیب‌های مختلف فرکانس، پروتکل و Channel Width را تست می‌کند، نتایج را به‌صورت زنده نمایش می‌دهد و امکان ذخیره در فایل JSON را فراهم می‌کند.\r\n\r\n" +
                    "تمام رابط کاربری به زبان فارسی است و شامل فیلتر، مرتب‌سازی، لاگ ترمینال و مدیریت تنظیمات می‌باشد."
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

            void AddInfoRow(string title, string value)
            {
                var titleLabelLocal = new Label
                {
                    Text = title,
                    TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
                };

                var valueLabel = new Label
                {
                    Text = value,
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

            AddInfoRow("نام پروژه", "اسکنر فرکانس میکروتیک (Ntk.Mikrotik.Tools)");
            AddInfoRow("نسخه برنامه", Application.ProductVersion);
            AddInfoRow("پلتفرم", ".NET 8.0 - Windows Forms");
            AddInfoRow("توسعه‌دهنده", "Alireza Karavi");
            AddInfoRow("ایمیل‌های تماس", "info@alikaravi.com | karavi.alireza@gmail.com");
            AddInfoRow("محل فعالیت", "Dubai, UAE");
            AddInfoRow("شماره تماس", "(00971) 504504324");
            AddInfoRow("مهارت‌های کلیدی", "C# (WinForms, WebForms, MVC), .NET Core, Angular, Microservices, GraphQL, SignalR, SQL Server, MongoDB, MySQL, Docker, ESXi, Mikrotik, VoIP/Asterisk, Zabbix, WordPress، هوش مصنوعی و چت‌بات");
            AddInfoRow("تجربه", "بنیان‌گذار NTK (2005-اکنون)، مدیر پروژه در Arad (2015-اکنون)، مدیر پروژه در Arad ITC هند (2020-اکنون)، Founder Karavi Co. کانادا (2022-اکنون)");
            AddInfoRow("تحصیلات", "Master AI & Robotics (IAU Isfahan, 2024-2025) | MBA (University of Tehran, 2022-2024) | BSc Industrial Engineering (IAU Najafabad, 1999-2002)");
            AddInfoRow("مسیر اجرا/نصب", projectLocation);

            var footerLabel = new Label
            {
                Text = "در صورت نیاز به پشتیبانی یا پیشنهاد، اطلاعات بالا را به‌روزرسانی کنید و با تیم توسعه در تماس باشید.",
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
                CommandValidateInterface = (this.Controls.Find("txtCmdValidateInterface", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless print"
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
                    MessageBox.Show("تنظیمات ذخیره شد.", "موفق", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("خطا در ذخیره تنظیمات.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var result = MessageBox.Show(
                "آیا مطمئن هستید که می‌خواهید تمام تنظیمات را به مقادیر پیش‌فرض بازگردانید؟\nتمام مقادیر فعلی از دست خواهند رفت.",
                "تأیید بازگشت به پیش‌فرض",
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

                MessageBox.Show("تنظیمات به مقادیر پیش‌فرض بازگردانده شد.", "موفق", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                MessageBox.Show("در حال حاضر به روتر متصل هستید.", "اطلاع", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var settings = GetSettingsFromForm();
            
            if (string.IsNullOrWhiteSpace(settings.RouterIpAddress))
            {
                MessageBox.Show("لطفاً آدرس IP روتر را وارد کنید.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.Username))
            {
                MessageBox.Show("لطفاً نام کاربری را وارد کنید.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    MessageBox.Show("اتصال به روتر با موفقیت برقرار شد و اطلاعات پایه دریافت شد.", "موفق", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _isConnected = false;
                    if (_lblStatus != null) _lblStatus.Text = "خطا در اتصال به روتر.";
                    if (btnConnect != null) btnConnect.Enabled = true;
                    if (btnDisconnect != null) btnDisconnect.Enabled = false;
                    if (_btnStart != null) _btnStart.Enabled = false;
                    
                    MessageBox.Show("خطا در اتصال به روتر. لطفاً IP، پورت، نام کاربری و رمز عبور را بررسی کنید.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                MessageBox.Show("اتصال به روتر قطع شد.", "اطلاع", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                MessageBox.Show("لطفاً ابتدا به روتر متصل شوید.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Validate settings
            var settings = GetSettingsFromForm();
            var validation = SettingsValidator.Validate(settings);
            
            if (!validation.IsValid)
            {
                var errorMessage = string.Join("\n", validation.Errors);
                MessageBox.Show($"لطفاً خطاهای زیر را برطرف کنید:\n\n{errorMessage}", "خطا در تنظیمات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show("لطفاً ابتدا به روتر متصل شوید.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

                MessageBox.Show(summary, "نتیجه تست اتصال مجدد", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    MessageBox.Show("هیچ فایل نتیجه‌ای یافت نشد.", "اطلاع", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                            
                            // Refresh DataGridView
                            if (_dgvResults != null)
                            {
                                _dgvResults.Refresh();
                                _dgvResults.Invalidate();
                            }
                            
                            MessageBox.Show($"{results.Count} نتیجه بارگذاری شد.", "موفق", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("فایل انتخاب شده معتبر نیست یا خالی است.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show("لطفاً ابتدا به روتر متصل شوید.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    MessageBox.Show("خطا در دریافت وضعیت فعلی.", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
    }
}

