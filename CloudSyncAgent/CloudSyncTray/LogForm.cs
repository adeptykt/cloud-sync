using System.Diagnostics;

namespace CloudSyncTray;

public partial class LogForm : Form
{
    private readonly System.Timers.Timer _refreshTimer;
    private readonly string _logPath;

    public LogForm()
    {
        InitializeComponent();

        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CloudSyncAgent",
            $"service_{DateTime.Now:yyyy-MM-dd}.log");

        btnRefresh.Click += (_, _) => RefreshLog();
        btnClear.Click += (_, _) => txtLog.Clear();
        btnOpenFolder.Click += (_, _) =>
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
        };

        _refreshTimer = new System.Timers.Timer(5000);
        _refreshTimer.Elapsed += (_, _) => RefreshLog();
        _refreshTimer.Start();

        RefreshLog();
    }

    private void RefreshLog()
    {
        try
        {
            if (!File.Exists(_logPath))
                return;

            var content = File.ReadAllText(_logPath);
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(() =>
                {
                    txtLog.Text = content;
                    txtLog.SelectionStart = txtLog.Text.Length;
                    txtLog.ScrollToCaret();
                });
            }
            else
            {
                txtLog.Text = content;
                txtLog.SelectionStart = txtLog.Text.Length;
                txtLog.ScrollToCaret();
            }
        }
        catch
        {
            // ignore log read errors
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        base.OnFormClosed(e);
    }
}
