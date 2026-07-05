using System.Diagnostics;
using System.Runtime.InteropServices;
using CloudSyncShared;
using Microsoft.Win32;
using System.Text.Json;

namespace CloudSyncTray;

public class TrayContext : ApplicationContext
{
    private NotifyIcon _trayIcon;
    private SyncConfig _config;
    private string _configPath;
    private System.Timers.Timer _statusTimer;
    private SettingsForm _settingsForm;
    private LogForm _logForm;

    public TrayContext()
    {
        // Путь к конфигу
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CloudSyncAgent", "config.json");
        
        LoadConfig();
        
        // Создаём иконку в трее
        _trayIcon = new NotifyIcon
        {
            Icon = GetAppIcon(),
            Text = "CloudSync Agent",
            Visible = true
        };
        
        // Создаём контекстное меню
        _trayIcon.ContextMenuStrip = new ContextMenuStrip();
        _trayIcon.ContextMenuStrip.Items.Add("Настройки", null, OnOpenSettings);
        _trayIcon.ContextMenuStrip.Items.Add("Показать лог", null, OnShowLog);
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Запустить синхронизацию", null, OnSyncNow);
        _trayIcon.ContextMenuStrip.Items.Add("Остановить синхронизацию", null, OnStopSync);
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("Открыть папку", null, OnOpenFolder);
        _trayIcon.ContextMenuStrip.Items.Add("-");
        _trayIcon.ContextMenuStrip.Items.Add("О программе", null, OnAbout);
        _trayIcon.ContextMenuStrip.Items.Add("Выход", null, OnExit);
        
        // Таймер для обновления статуса
        _statusTimer = new System.Timers.Timer(5000);
        _statusTimer.Elapsed += (s, e) => UpdateStatus();
        _statusTimer.Start();
        
        // Проверяем, запущена ли служба
        CheckServiceStatus();
    }

    private void LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<SyncConfig>(json) ?? new SyncConfig();
        }
        else
        {
            _config = new SyncConfig();
            SaveConfig();
        }
    }

    private void SaveConfig()
    {
        var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    private void UpdateStatus()
    {
        var status = GetServiceStatus();
        _trayIcon.Icon = GetAppIcon(status == "Running");
        _trayIcon.Text = $"CloudSync Agent\nСтатус: {status}\nПапка: {_config.SyncFolder}";
    }

    private string GetServiceStatus()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("CloudSyncAgent");
            return sc.Status.ToString();
        }
        catch
        {
            return "Не запущена";
        }
    }

    private void CheckServiceStatus()
    {
        var status = GetServiceStatus();
        if (status != "Running")
        {
            _trayIcon.ShowBalloonTip(3000, "CloudSync Agent", 
                "Служба синхронизации не запущена. Запустите её в настройках.", 
                ToolTipIcon.Warning);
        }
    }

    private void OnOpenSettings(object sender, EventArgs e)
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_config);
            _settingsForm.OnConfigSaved += (newConfig) =>
            {
                _config = newConfig;
                SaveConfig();
                RestartService();
            };
            _settingsForm.Show();
        }
        else
        {
            _settingsForm.BringToFront();
        }
    }

    private void OnShowLog(object sender, EventArgs e)
    {
        if (_logForm == null || _logForm.IsDisposed)
        {
            _logForm = new LogForm();
            _logForm.Show();
        }
        else
        {
            _logForm.BringToFront();
        }
    }

    private void OnSyncNow(object sender, EventArgs e)
    {
        // Сигнализируем службе запустить синхронизацию
        _trayIcon.ShowBalloonTip(1000, "CloudSync Agent", "Запущена синхронизация...", ToolTipIcon.Info);
    }

    private void OnStopSync(object sender, EventArgs e)
    {
        using var sc = new System.ServiceProcess.ServiceController("CloudSyncAgent");
        if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
        {
            sc.Stop();
            sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            _trayIcon.ShowBalloonTip(1000, "CloudSync Agent", "Синхронизация остановлена", ToolTipIcon.Info);
        }
    }

    private void OnOpenFolder(object sender, EventArgs e)
    {
        if (Directory.Exists(_config.SyncFolder))
        {
            Process.Start("explorer.exe", _config.SyncFolder);
        }
        else
        {
            Directory.CreateDirectory(_config.SyncFolder);
            Process.Start("explorer.exe", _config.SyncFolder);
        }
    }

    private void OnAbout(object sender, EventArgs e)
    {
        MessageBox.Show(
            "CloudSync Agent v2.0\n\nОблачная синхронизация файлов\nс поддержкой порядка загрузки\n\n© 2024",
            "О программе",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnExit(object sender, EventArgs e)
    {
        _statusTimer.Stop();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void RestartService()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("CloudSyncAgent");
            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                sc.Start();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                _trayIcon.ShowBalloonTip(3000, "CloudSync Agent", "Служба перезапущена с новыми настройками", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка перезапуска службы: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Icon GetAppIcon(bool isRunning = true)
    {
        using var bitmap = new System.Drawing.Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(isRunning ? Color.FromArgb(0, 120, 215) : Color.Gray);
        g.FillEllipse(Brushes.White, 3, 3, 10, 10);
        return Icon.FromHandle(bitmap.GetHicon());
    }
}