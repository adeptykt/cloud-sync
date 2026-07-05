namespace CloudSyncTray;

public partial class LogForm : Form
{
    private TextBox _txtLog;
    private Button _btnRefresh;
    private Button _btnClear;
    private System.Timers.Timer _refreshTimer;
    private string _logPath;

    public LogForm()
    {
        InitializeComponent();
        
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CloudSyncAgent",
            $"service_{DateTime.Now:yyyy-MM-dd}.log");
        
        _refreshTimer = new System.Timers.Timer(5000);
        _refreshTimer.Elapsed += (s, e) => RefreshLog();
        _refreshTimer.Start();
        
        RefreshLog();
    }

    private void InitializeComponent()
    {
        this.Text = "Лог CloudSync Agent";
        this.Size = new System.Drawing.Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9)
        };
        
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        
        _btnRefresh = new Button { Text = "Обновить", Location = new Point(10, 8), Size = new Size(80, 25) };
        _btnRefresh.Click += (s, e) => RefreshLog();
        
        _btnClear = new Button { Text = "Очистить", Location = new Point(100, 8), Size = new Size(80, 25) };
        _btnClear.Click += (s, e) => { _txtLog.Clear(); };
        
        var btnOpenFolder = new Button { Text = "Открыть папку", Location = new Point(690, 8), Size = new Size(90, 25) };
        btnOpenFolder.Click += (s, e) => {
            var dir = Path.GetDirectoryName(_logPath);
            if (Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
        };
        
        panel.Controls.Add(_btnRefresh);
        panel.Controls.Add(_btnClear);
        panel.Controls.Add(btnOpenFolder);
        
        this.Controls.Add(_txtLog);
        this.Controls.Add(panel);
    }

    private void RefreshLog()
    {
        try
        {
            if (File.Exists(_logPath))
            {
                var content = File.ReadAllText(_logPath);
                if (_txtLog.InvokeRequired)
                {
                    _txtLog.Invoke(new Action(() => {
                        _txtLog.Text = content;
                        _txtLog.SelectionStart = _txtLog.Text.Length;
                        _txtLog.ScrollToCaret();
                    }));
                }
                else
                {
                    _txtLog.Text = content;
                    _txtLog.SelectionStart = _txtLog.Text.Length;
                    _txtLog.ScrollToCaret();
                }
            }
        }
        catch { /* Игнорируем */ }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer?.Stop();
        base.OnFormClosed(e);
    }
}