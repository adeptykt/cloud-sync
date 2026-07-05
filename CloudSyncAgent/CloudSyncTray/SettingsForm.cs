using CloudSyncShared;
using Microsoft.Win32;
using System.Text.Json;

namespace CloudSyncTray;

public partial class SettingsForm : Form
{
    private SyncConfig _config;

    public event Action<SyncConfig> OnConfigSaved;

    public SettingsForm(SyncConfig config)
    {
        _config = JsonSerializer.Deserialize<SyncConfig>(JsonSerializer.Serialize(config)) ?? new SyncConfig();
        InitializeComponent();
        WireEvents();
        LoadConfigToUI();
        ConfigureRulesGrid();
    }

    private void WireEvents()
    {
        btnBrowse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
                txtSyncFolder.Text = dialog.SelectedPath;
        };

        btnAddRule.Click += (_, _) =>
            rulesGrid.Rows.Add(true, "Новое правило", "*.flag", "DataBeforeFlag", "", "");

        btnRemoveRule.Click += (_, _) =>
        {
            if (rulesGrid.SelectedRows.Count > 0)
                rulesGrid.Rows.RemoveAt(rulesGrid.SelectedRows[0].Index);
        };

        btnSave.Click += OnSave;
        btnCancel.Click += (_, _) => Close();
    }

    private void ConfigureRulesGrid()
    {
        rulesGrid.Columns.Clear();
        rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Вкл" });
        rulesGrid.Columns.Add("Name", "Название");
        rulesGrid.Columns.Add("FilePattern", "Шаблон файла");
        rulesGrid.Columns.Add("OrderType", "Тип порядка");
        rulesGrid.Columns.Add("SequentialOrder", "Последовательность");
        rulesGrid.Columns.Add("Description", "Описание");

        foreach (var rule in _config.CustomRules)
        {
            rulesGrid.Rows.Add(
                rule.Enabled,
                rule.Name,
                rule.FilePattern,
                rule.OrderType.ToString(),
                string.Join(",", rule.SequentialOrder),
                rule.Description);
        }
    }

    private void LoadConfigToUI()
    {
        txtServerUrl.Text = _config.ServerUrl;
        txtUsername.Text = _config.Username;
        txtPassword.Text = _config.Password;
        txtSyncFolder.Text = _config.SyncFolder;
        numSyncInterval.Value = _config.SyncIntervalSeconds;
        chkStartWithWindows.Checked = _config.StartWithWindows;
        chkShowNotifications.Checked = _config.ShowNotifications;
    }

    private void OnSave(object sender, EventArgs e)
    {
        _config.ServerUrl = txtServerUrl.Text;
        _config.Username = txtUsername.Text;
        _config.Password = txtPassword.Text;
        _config.SyncFolder = txtSyncFolder.Text;
        _config.SyncIntervalSeconds = (int)numSyncInterval.Value;
        _config.StartWithWindows = chkStartWithWindows.Checked;
        _config.ShowNotifications = chkShowNotifications.Checked;

        _config.CustomRules.Clear();
        foreach (DataGridViewRow row in rulesGrid.Rows)
        {
            if (row.IsNewRow)
                continue;

            var rule = new SyncRule
            {
                Enabled = (bool)(row.Cells["Enabled"].Value ?? true),
                Name = row.Cells["Name"].Value?.ToString() ?? "",
                FilePattern = row.Cells["FilePattern"].Value?.ToString() ?? "",
                Description = row.Cells["Description"].Value?.ToString() ?? ""
            };

            if (Enum.TryParse<SyncOrderType>(row.Cells["OrderType"].Value?.ToString(), out var orderType))
                rule.OrderType = orderType;

            var seqOrder = row.Cells["SequentialOrder"].Value?.ToString();
            if (!string.IsNullOrEmpty(seqOrder))
                rule.SequentialOrder = seqOrder.Split(',').ToList();

            if (!string.IsNullOrEmpty(rule.Name) && !string.IsNullOrEmpty(rule.FilePattern))
                _config.CustomRules.Add(rule);
        }

        SetAutoStart(_config.StartWithWindows);
        OnConfigSaved?.Invoke(_config);
        Close();
    }

    private static void SetAutoStart(bool enable)
    {
        var registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        var appPath = Application.ExecutablePath;

        if (enable)
            registryKey?.SetValue("CloudSyncAgent", appPath);
        else
            registryKey?.DeleteValue("CloudSyncAgent", false);
    }
}
