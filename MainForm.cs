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

namespace Ntk.Mikrotik.Tools
{
    public partial class MainForm : Form
    {
        private FrequencyScanner? _scanner;
        private CancellationTokenSource? _cancellationTokenSource;
        private JsonDataService _jsonService;
        private BindingList<FrequencyScanResult> _currentResults;
        private MikroTikSshClient? _sshClient;
        private bool _isConnected = false;
        
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
        private Label? _lblStatus;
        private ProgressBar? _progressBar;
        private Button? _btnStart;
        private Button? _btnStop;
        private DataGridView? _dgvResults;
        private TextBox? _txtTerminalLog;

        public MainForm()
        {
            InitializeComponent();
            _jsonService = new JsonDataService();
            _currentResults = new BindingList<FrequencyScanResult>();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form
            this.Text = "اسکنر فرکانس میکروتیک";
            this.Size = new System.Drawing.Size(1400, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Top Panel for buttons and status
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 100, // Increased height to accommodate spacing
                Padding = new Padding(10)
            };

            // Status label
            _lblStatus = new Label
            {
                Text = "آماده",
                Dock = DockStyle.Top,
                Height = 30, // Increased height for better visibility
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 0) // Add top margin for spacing from progress bar
            };

            // Progress bar
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 25,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 5, 0, 0) // Add top margin for spacing from buttons
            };

            // Buttons panel
            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(5, 0, 5, 0)
            };

            _btnStart = new Button { Text = "شروع اسکن", Width = 100, Height = 25 };
            _btnStop = new Button { Text = "توقف", Width = 100, Height = 25, Enabled = false };
            
            var btnConnect = new Button { Text = "اتصال", Width = 100, Height = 25, Name = "btnConnect" };
            var btnDisconnect = new Button { Text = "قطع اتصال", Width = 100, Height = 25, Enabled = false, Name = "btnDisconnect" };

            // Add event handlers
            btnConnect.Click += async (s, e) => await ConnectToRouterAsync();
            btnDisconnect.Click += (s, e) => DisconnectFromRouter();
            _btnStart.Click += async (s, e) => await StartScanAsync();
            _btnStop.Click += (s, e) => StopScan();

            buttonsPanel.Controls.Add(_btnStop);
            buttonsPanel.Controls.Add(_btnStart);
            buttonsPanel.Controls.Add(btnDisconnect);
            buttonsPanel.Controls.Add(btnConnect);

            topPanel.Controls.Add(_lblStatus);
            topPanel.Controls.Add(_progressBar);
            topPanel.Controls.Add(buttonsPanel);

            // Tab Control
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Tahoma", 9F)
            };

            // Settings Tab
            var settingsTab = new TabPage("تنظیمات");
            CreateSettingsTab(settingsTab);
            tabControl.TabPages.Add(settingsTab);

            // Results and Terminal Tab (combined)
            var resultsTab = new TabPage("نتایج و لاگ");
            CreateResultsAndTerminalTab(resultsTab);
            tabControl.TabPages.Add(resultsTab);

            this.Controls.Add(tabControl);
            this.Controls.Add(topPanel);
            this.ResumeLayout(false);
        }

        private void CreateSettingsTab(TabPage tab)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 13,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

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
            _txtStartFreq = new NumericUpDown { Name = "txtStartFreq", Minimum = 1000, Maximum = 6000, Value = 2400, DecimalPlaces = 2, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtStartFreq, 1, row++);

            // End Frequency
            panel.Controls.Add(new Label { Text = "فرکانس پایان (MHz):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtEndFreq = new NumericUpDown { Name = "txtEndFreq", Minimum = 1000, Maximum = 6000, Value = 2500, DecimalPlaces = 2, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtEndFreq, 1, row++);

            // Frequency Step
            panel.Controls.Add(new Label { Text = "پرش فرکانس (MHz):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtFreqStep = new NumericUpDown { Name = "txtFreqStep", Minimum = 0.1m, Maximum = 100, Value = 1, DecimalPlaces = 2, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtFreqStep, 1, row++);

            // Stabilization Time
            panel.Controls.Add(new Label { Text = "زمان استیبل شدن (دقیقه):", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtStabilizationTime = new NumericUpDown { Name = "txtStabilizationTime", Minimum = 1, Maximum = 60, Value = 2, Dock = DockStyle.Fill };
            panel.Controls.Add(_txtStabilizationTime, 1, row++);

            // Interface Name
            panel.Controls.Add(new Label { Text = "نام اینترفیس:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            _txtInterface = new TextBox { Name = "txtInterface", Text = "wlan1", Dock = DockStyle.Fill };
            panel.Controls.Add(_txtInterface, 1, row++);

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

            // Command Get Frequency
            panel.Controls.Add(new Label { Text = "کامند دریافت فرکانس:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdGetFreq = new TextBox { Name = "txtCmdGetFreq", Text = "/interface wireless print where name=\"{interface}\" value-name=frequency", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdGetFreq, 1, row++);

            // Command Set Frequency
            panel.Controls.Add(new Label { Text = "کامند تنظیم فرکانس:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdSetFreq = new TextBox { Name = "txtCmdSetFreq", Text = "/interface wireless set \"{interface}\" frequency={frequency}", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdSetFreq, 1, row++);

            // Command Get Interface Info
            panel.Controls.Add(new Label { Text = "کامند دریافت اطلاعات:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdGetInfo = new TextBox { Name = "txtCmdGetInfo", Text = "/interface wireless print detail where name=\"{interface}\"", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdGetInfo, 1, row++);

            // Command Get Registration Table
            panel.Controls.Add(new Label { Text = "کامند Registration Table:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdRegTable = new TextBox { Name = "txtCmdRegTable", Text = "/interface wireless registration-table print detail where interface=\"{interface}\"", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdRegTable, 1, row++);

            // Command Monitor
            panel.Controls.Add(new Label { Text = "کامند Monitor:", TextAlign = System.Drawing.ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, row);
            var txtCmdMonitor = new TextBox { Name = "txtCmdMonitor", Text = "/interface wireless monitor \"{interface}\" once", Dock = DockStyle.Fill };
            panel.Controls.Add(txtCmdMonitor, 1, row++);

            // Buttons (only Save and Load Results in settings tab)
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
            var btnSave = new Button { Name = "btnSave", Text = "ذخیره تنظیمات", Size = new System.Drawing.Size(130, 35) };
            var btnLoadResults = new Button { Name = "btnLoadResults", Text = "بارگذاری نتایج قبلی", Size = new System.Drawing.Size(150, 35) };
            
            buttonPanel.Controls.Add(btnLoadResults);
            buttonPanel.Controls.Add(btnSave);
            
            btnLoadResults.Click += (s, e) => LoadPreviousResults();
            btnSave.Click += (s, e) => SaveSettings();
            
            panel.SetColumnSpan(buttonPanel, 2);
            panel.Controls.Add(buttonPanel, 0, row);

            tab.Controls.Add(panel);
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
            var terminalPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };

            var terminalLabel = new Label
            {
                Text = "لاگ ترمینال:",
                Dock = DockStyle.Top,
                Height = 25,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
            };

            _txtTerminalLog = new TextBox
            {
                Name = "txtTerminalLog",
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
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
                Text = "پاک کردن",
                Width = 100,
                Height = 25
            };
            btnClear.Click += (s, e) => _txtTerminalLog?.Clear();

            buttonPanel.Controls.Add(btnClear);
            terminalPanel.Controls.Add(_txtTerminalLog);
            terminalPanel.Controls.Add(buttonPanel);
            terminalPanel.Controls.Add(terminalLabel);

            // Results Panel (bottom - Panel2)
            var resultsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };
            
            var resultsLabel = new Label
            {
                Text = "نتایج اسکن:",
                Dock = DockStyle.Top,
                Height = 25,
                Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold)
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
                    var nullableDouble = e.Value as double?;
                    if (nullableDouble.HasValue && !nullableDouble.Value.Equals(double.NaN))
                    {
                        // Format numbers with 2 decimal places
                        e.Value = nullableDouble.Value.ToString("F2");
                        e.FormattingApplied = true;
                    }
                    else if (e.Value is double doubleValue && !double.IsNaN(doubleValue))
                    {
                        // Format numbers with 2 decimal places
                        e.Value = doubleValue.ToString("F2");
                        e.FormattingApplied = true;
                    }
                    else if (e.Value is DateTime dateTime)
                    {
                        e.Value = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                        e.FormattingApplied = true;
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

            // Enable sorting for all columns BEFORE setting DataSource
            _dgvResults.AllowUserToOrderColumns = true;
            foreach (DataGridViewColumn column in _dgvResults.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Automatic;
            }
            
            // Set DataSource and ensure proper binding
            _dgvResults.DataSource = _currentResults;
            
            // Ensure DataGridView is properly initialized
            _dgvResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _dgvResults.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            
            // Enable virtual mode for better performance with large datasets
            _dgvResults.VirtualMode = false;
            
            // BindingList automatically raises ListChanged events by default
            // No need to set RaiseListChangedEvents explicitly

            resultsPanel.Controls.Add(_dgvResults);
            resultsPanel.Controls.Add(resultsLabel);

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

            _txtTerminalLog = new TextBox
            {
                Name = "txtTerminalLog",
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
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
                Text = "پاک کردن", 
                Size = new System.Drawing.Size(100, 30) 
            };
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
                FrequencyStep = (double)(_txtFreqStep?.Value ?? 1),
                StabilizationTimeMinutes = (int)(_txtStabilizationTime?.Value ?? 2),
                InterfaceName = _txtInterface?.Text ?? "wlan1",
                WirelessProtocols = (this.Controls.Find("txtWirelessProtocols", true).FirstOrDefault() as TextBox)?.Text ?? "",
                ChannelWidths = (this.Controls.Find("txtChannelWidths", true).FirstOrDefault() as TextBox)?.Text ?? "",
                CommandGetFrequency = (this.Controls.Find("txtCmdGetFreq", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless print where name=\"{interface}\" value-name=frequency",
                CommandSetFrequency = (this.Controls.Find("txtCmdSetFreq", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless set \"{interface}\" frequency={frequency}",
                CommandGetInterfaceInfo = (this.Controls.Find("txtCmdGetInfo", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless print detail where name=\"{interface}\"",
                CommandGetRegistrationTable = (this.Controls.Find("txtCmdRegTable", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless registration-table print detail where interface=\"{interface}\"",
                CommandMonitorInterface = (this.Controls.Find("txtCmdMonitor", true).FirstOrDefault() as TextBox)?.Text ?? "/interface wireless monitor \"{interface}\" once"
            };
        }

        private void LoadSettings()
        {
            // Load from file if exists
            try
            {
                var settingsFile = System.IO.Path.Combine(Application.StartupPath, "settings.json");
                if (System.IO.File.Exists(settingsFile))
                {
                    var json = System.IO.File.ReadAllText(settingsFile);
                    var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<ScanSettings>(json);
                    
                    if (settings != null)
                    {
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
                        if (this.Controls.Find("txtCmdGetInfo", true).FirstOrDefault() is TextBox txtCmdGetInfo)
                            txtCmdGetInfo.Text = settings.CommandGetInterfaceInfo;
                        if (this.Controls.Find("txtCmdRegTable", true).FirstOrDefault() is TextBox txtCmdRegTable)
                            txtCmdRegTable.Text = settings.CommandGetRegistrationTable;
                        if (this.Controls.Find("txtCmdMonitor", true).FirstOrDefault() is TextBox txtCmdMonitor)
                            txtCmdMonitor.Text = settings.CommandMonitorInterface;
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = GetSettingsFromForm();
                var settingsFile = System.IO.Path.Combine(Application.StartupPath, "settings.json");
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(settingsFile, json);
                MessageBox.Show("تنظیمات ذخیره شد.", "موفق", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در ذخیره تنظیمات: {ex.Message}", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                            _txtTerminalLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {data}\r\n");
                            _txtTerminalLog.SelectionStart = _txtTerminalLog.Text.Length;
                            _txtTerminalLog.ScrollToCaret();
                        }
                    });
                };

                _sshClient.DataReceived += (s, data) =>
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
                _isConnected = false;
                if (_lblStatus != null) _lblStatus.Text = $"خطا: {ex.Message}";
                if (btnConnect != null) btnConnect.Enabled = true;
                if (btnDisconnect != null) btnDisconnect.Enabled = false;
                if (_btnStart != null) _btnStart.Enabled = false;
                
                MessageBox.Show($"خطا در اتصال: {ex.Message}", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _sshClient?.Dispose();
                _sshClient = null;
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

                if (btnConnect != null) btnConnect.Enabled = true;
                if (btnDisconnect != null) btnDisconnect.Enabled = false;
                if (_btnStart != null) _btnStart.Enabled = false;
                if (_lblStatus != null) _lblStatus.Text = "اتصال قطع شد.";

                MessageBox.Show("اتصال به روتر قطع شد.", "اطلاع", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در قطع اتصال: {ex.Message}", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                            _txtTerminalLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {data}\r\n");
                            _txtTerminalLog.SelectionStart = _txtTerminalLog.Text.Length;
                            _txtTerminalLog.ScrollToCaret();
                        }
                    });
                };

                // Get current status
                var baseResult = await tempScanner.GetCurrentStatusAsync();
                
                if (baseResult != null)
                {
                    baseResult.Status = "base";
                    baseResult.ScanTime = DateTime.Now;

                    // Add to results list
                    this.Invoke((MethodInvoker)delegate
                    {
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
                this.Invoke((MethodInvoker)delegate
                {
                    if (_lblStatus != null)
                    {
                        _lblStatus.Text = $"خطا در دریافت اطلاعات پایه: {ex.Message}";
                    }
                });
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
                    _currentResults.Add(result);
                    
                    // Force DataGridView to update and show new row
                    if (_dgvResults != null)
                    {
                        // Ensure DataSource is set
                        if (_dgvResults.DataSource == null)
                        {
                            _dgvResults.DataSource = _currentResults;
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
                if (_lblStatus != null)
                    _lblStatus.Text = $"خطا: {ex.Message}";
                MessageBox.Show($"خطا در اسکن: {ex.Message}", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_btnStart != null) _btnStart.Enabled = true;
                if (_btnStop != null) _btnStop.Enabled = false;
                if (_progressBar != null) _progressBar.Value = 0;
            }
        }

        private void StopScan()
        {
            _scanner?.StopScan();
            _cancellationTokenSource?.Cancel();
            
            if (_btnStart != null) _btnStart.Enabled = true;
            if (_btnStop != null) _btnStop.Enabled = false;
            if (_lblStatus != null) _lblStatus.Text = "متوقف شد";
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
                            _currentResults.Clear();
                            foreach (var result in results)
                            {
                                _currentResults.Add(result);
                            }
                            
                            // Force refresh to ensure DataGridView updates
                            if (_dgvResults != null)
                            {
                                _dgvResults.DataSource = null;
                                _dgvResults.DataSource = _currentResults;
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
                MessageBox.Show($"خطا در بارگذاری نتایج: {ex.Message}", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

