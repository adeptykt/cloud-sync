using CloudSyncShared;
using System.Text.Json;

namespace CloudSyncTray;

public partial class SettingsForm : Form
{
    private SyncConfig _config;
    private DataGridView _rulesGrid;
    private TextBox _txtServerUrl;
    private TextBox _txtUsername;
    private TextBox _txtPassword;
    private TextBox _txtSyncFolder;
    private NumericUpDown _numSyncInterval;
    private CheckBox _chkStartWithWindows;
    private CheckBox _chkShowNotifications;
    private Button _btnSave;
    private Button _btnCancel;
    private TabControl _tabControl;

    public event Action<SyncConfig> OnConfigSaved;

    public SettingsForm(SyncConfig config)
    {
        _config = JsonSerializer.Deserialize<SyncConfig>(JsonSerializer.Serialize(config)) ?? new SyncConfig();
        InitializeComponent();
        LoadConfigToUI();
    }

    private void InitializeComponent()
    {
        this.Text = "Настройки CloudSync Agent";
        this.Size = new System.Drawing.Size(600, 500);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        _tabControl = new TabControl { Dock = DockStyle.Fill, Padding = new Point(10, 10) };

        // Вкладка "Основные"
        var mainTab = new TabPage("Основные");
        
        var lblServerUrl = new Label { Text = "Адрес сервера:", Location = new Point(20, 20), Size = new Size(120, 25) };
        _txtServerUrl = new TextBox { Location = new Point(150, 20), Size = new Size(300, 25), Text = _config.ServerUrl };
        
        var lblUsername = new Label { Text = "Имя пользователя:", Location = new Point(20, 60), Size = new Size(120, 25) };
        _txtUsername = new TextBox { Location = new Point(150, 60), Size = new Size(300, 25), Text = _config.Username };
        
        var lblPassword = new Label { Text = "Пароль:", Location = new Point(20, 100), Size = new Size(120, 25) };
        _txtPassword = new TextBox { Location = new Point(150, 100), Size = new Size(300, 25), PasswordChar = '*', Text = _config.Password };
        
        var lblSyncFolder = new Label { Text = "Папка синхронизации:", Location = new Point(20, 140), Size = new Size(120, 25) };
        _txtSyncFolder = new TextBox { Location = new Point(150, 140), Size = new Size(300, 25), Text = _config.SyncFolder };
        var btnBrowse = new Button { Text = "Обзор...", Location = new Point(460, 140), Size = new Size(75, 25) };
        btnBrowse.Click += (s, e) => {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
                _txtSyncFolder.Text = dialog.SelectedPath;
        };
        
        var lblSyncInterval = new Label { Text = "Интервал синхронизации (сек):", Location = new Point(20, 180), Size = new Size(180, 25) };
        _numSyncInterval = new NumericUpDown { Location = new Point(210, 180), Size = new Size(60, 25), Minimum = 1, Maximum = 300, Value = _config.SyncIntervalSeconds };
        
        _chkStartWithWindows = new CheckBox { Text = "Запускать при старте Windows", Location = new Point(20, 220), Size = new Size(200, 25), Checked = _config.StartWithWindows };
        _chkShowNotifications = new CheckBox { Text = "Показывать уведомления", Location = new Point(20, 250), Size = new Size(200, 25), Checked = _config.ShowNotifications };
        
        mainTab.Controls.AddRange(new Control[] { lblServerUrl, _txtServerUrl, lblUsername, _txtUsername, 
            lblPassword, _txtPassword, lblSyncFolder, _txtSyncFolder, btnBrowse, lblSyncInterval, 
            _numSyncInterval, _chkStartWithWindows, _chkShowNotifications });

        // Вкладка "Правила порядка"
        var rulesTab = new TabPage("Правила порядка");
        
        _rulesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        
        _rulesGrid.Columns.Add("Enabled", "Вкл");
        _rulesGrid.Columns.Add("Name", "Название");
        _rulesGrid.Columns.Add("FilePattern", "Шаблон файла");
        _rulesGrid.Columns.Add("OrderType", "Тип порядка");
        _rulesGrid.Columns.Add("SequentialOrder", "Последовательность");
        _rulesGrid.Columns.Add("Description", "Описание");
        
        ((DataGridViewCheckBoxColumn)_rulesGrid.Columns["Enabled"]).TrueValue = true;
        ((DataGridViewCheckBoxColumn)_rulesGrid.Columns["Enabled"]).FalseValue = false;
        
        var btnAddRule = new Button { Text = "Добавить правило", Location = new Point(10, 10), Size = new Size(120, 30) };
        btnAddRule.Click += (s, e) => _rulesGrid.Rows.Add(true, "Новое правило", "*.flag", "DataBeforeFlag", "", "");
        
        var btnRemoveRule = new Button { Text = "Удалить", Location = new Point(140, 10), Size = new Size(80, 30) };
        btnRemoveRule.Click += (s, e) => {
            if (_rulesGrid.SelectedRows.Count > 0)
                _rulesGrid.Rows.RemoveAt(_rulesGrid.SelectedRows[0].Index);
        };
        
        rulesTab.Controls.Add(_rulesGrid);
        rulesTab.Controls.Add(btnAddRule);
        rulesTab.Controls.Add(btnRemoveRule);
        
        // Загружаем существующие правила
        foreach (var rule in _config.CustomRules)
        {
            _rulesGrid.Rows.Add(rule.Enabled, rule.Name, rule.FilePattern, rule.OrderType.ToString(), 
                string.Join(",", rule.SequentialOrder), rule.Description);
        }
        
        _tabControl.TabPages.Add(mainTab);
        _tabControl.TabPages.Add(rulesTab);
        
        // Кнопки внизу
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = SystemColors.Control };
        _btnSave = new Button { Text = "Сохранить", Location = new Point(400, 12), Size = new Size(80, 30), DialogResult = DialogResult.OK };
        _btnCancel = new Button { Text = "Отмена", Location = new Point(490, 12), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
        
        _btnSave.Click += OnSave;
        _btnCancel.Click += (s, e) => this.Close();
        
        panel.Controls.Add(_btnSave);
        panel.Controls.Add(_btnCancel);
        
        this.Controls.Add(_tabControl);
        this.Controls.Add(panel);
    }

    private void LoadConfigToUI()
    {
        _txtServerUrl.Text = _config.ServerUrl;
        _txtUsername.Text = _config.Username;
        _txtPassword.Text = _config.Password;
        _txtSyncFolder.Text = _config.SyncFolder;
        _numSyncInterval.Value = _config.SyncIntervalSeconds;
        _chkStartWithWindows.Checked = _config.StartWithWindows;
        _chkShowNotifications.Checked = _config.ShowNotifications;
    }

    private void OnSave(object sender, EventArgs e)
    {
        _config.ServerUrl = _txtServerUrl.Text;
        _config.Username = _txtUsername.Text;
        _config.Password = _txtPassword.Text;
        _config.SyncFolder = _txtSyncFolder.Text;
        _config.SyncIntervalSeconds = (int)_numSyncInterval.Value;
        _config.StartWithWindows = _chkStartWithWindows.Checked;
        _config.ShowNotifications = _chkShowNotifications.Checked;
        
        // Сохраняем правила
        _config.CustomRules.Clear();
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.IsNewRow) continue;
            
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
        
        // Настройка автозапуска в Windows
        SetAutoStart(_config.StartWithWindows);
        
        OnConfigSaved?.Invoke(_config);
        this.Close();
    }

    private void SetAutoStart(bool enable)
    {
        var registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        var appPath = Application.ExecutablePath;
        
        if (enable)
        {
            registryKey?.SetValue("CloudSyncAgent", appPath);
        }
        else
        {
            registryKey?.DeleteValue("CloudSyncAgent", false);
        }
    }
}