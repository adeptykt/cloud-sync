using System.Windows.Forms;

namespace CloudSyncTray;

partial class SettingsForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this.tabControl = new System.Windows.Forms.TabControl();
        this.mainTab = new System.Windows.Forms.TabPage();
        this.lblServerUrl = new System.Windows.Forms.Label();
        this.txtServerUrl = new System.Windows.Forms.TextBox();
        this.lblUsername = new System.Windows.Forms.Label();
        this.txtUsername = new System.Windows.Forms.TextBox();
        this.lblPassword = new System.Windows.Forms.Label();
        this.txtPassword = new System.Windows.Forms.TextBox();
        this.lblSyncFolder = new System.Windows.Forms.Label();
        this.txtSyncFolder = new System.Windows.Forms.TextBox();
        this.btnBrowse = new System.Windows.Forms.Button();
        this.lblSyncInterval = new System.Windows.Forms.Label();
        this.numSyncInterval = new System.Windows.Forms.NumericUpDown();
        this.chkStartWithWindows = new System.Windows.Forms.CheckBox();
        this.chkShowNotifications = new System.Windows.Forms.CheckBox();
        this.rulesTab = new System.Windows.Forms.TabPage();
        this.rulesGrid = new System.Windows.Forms.DataGridView();
        this.btnAddRule = new System.Windows.Forms.Button();
        this.btnRemoveRule = new System.Windows.Forms.Button();
        this.panelButtons = new System.Windows.Forms.Panel();
        this.btnSave = new System.Windows.Forms.Button();
        this.btnCancel = new System.Windows.Forms.Button();
        this.tabControl.SuspendLayout();
        this.mainTab.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.numSyncInterval)).BeginInit();
        this.rulesTab.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.rulesGrid)).BeginInit();
        this.panelButtons.SuspendLayout();
        this.SuspendLayout();
        // 
        // tabControl
        // 
        this.tabControl.Controls.Add(this.mainTab);
        this.tabControl.Controls.Add(this.rulesTab);
        this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
        this.tabControl.Location = new System.Drawing.Point(0, 0);
        this.tabControl.Name = "tabControl";
        this.tabControl.Padding = new System.Drawing.Point(10, 10);
        this.tabControl.Size = new System.Drawing.Size(600, 450);
        this.tabControl.TabIndex = 0;
        // 
        // mainTab
        // 
        this.mainTab.Controls.Add(this.lblServerUrl);
        this.mainTab.Controls.Add(this.txtServerUrl);
        this.mainTab.Controls.Add(this.lblUsername);
        this.mainTab.Controls.Add(this.txtUsername);
        this.mainTab.Controls.Add(this.lblPassword);
        this.mainTab.Controls.Add(this.txtPassword);
        this.mainTab.Controls.Add(this.lblSyncFolder);
        this.mainTab.Controls.Add(this.txtSyncFolder);
        this.mainTab.Controls.Add(this.btnBrowse);
        this.mainTab.Controls.Add(this.lblSyncInterval);
        this.mainTab.Controls.Add(this.numSyncInterval);
        this.mainTab.Controls.Add(this.chkStartWithWindows);
        this.mainTab.Controls.Add(this.chkShowNotifications);
        this.mainTab.Location = new System.Drawing.Point(4, 29);
        this.mainTab.Name = "mainTab";
        this.mainTab.Padding = new System.Windows.Forms.Padding(20);
        this.mainTab.Size = new System.Drawing.Size(592, 417);
        this.mainTab.TabIndex = 0;
        this.mainTab.Text = "Основные";
        this.mainTab.UseVisualStyleBackColor = true;
        // 
        // lblServerUrl
        // 
        this.lblServerUrl.AutoSize = true;
        this.lblServerUrl.Location = new System.Drawing.Point(20, 20);
        this.lblServerUrl.Name = "lblServerUrl";
        this.lblServerUrl.Size = new System.Drawing.Size(120, 20);
        this.lblServerUrl.TabIndex = 0;
        this.lblServerUrl.Text = "Адрес сервера:";
        // 
        // txtServerUrl
        // 
        this.txtServerUrl.Location = new System.Drawing.Point(150, 17);
        this.txtServerUrl.Name = "txtServerUrl";
        this.txtServerUrl.Size = new System.Drawing.Size(350, 27);
        this.txtServerUrl.TabIndex = 1;
        // 
        // lblUsername
        // 
        this.lblUsername.AutoSize = true;
        this.lblUsername.Location = new System.Drawing.Point(20, 60);
        this.lblUsername.Name = "lblUsername";
        this.lblUsername.Size = new System.Drawing.Size(124, 20);
        this.lblUsername.TabIndex = 2;
        this.lblUsername.Text = "Имя пользователя:";
        // 
        // txtUsername
        // 
        this.txtUsername.Location = new System.Drawing.Point(150, 57);
        this.txtUsername.Name = "txtUsername";
        this.txtUsername.Size = new System.Drawing.Size(350, 27);
        this.txtUsername.TabIndex = 3;
        // 
        // lblPassword
        // 
        this.lblPassword.AutoSize = true;
        this.lblPassword.Location = new System.Drawing.Point(20, 100);
        this.lblPassword.Name = "lblPassword";
        this.lblPassword.Size = new System.Drawing.Size(67, 20);
        this.lblPassword.TabIndex = 4;
        this.lblPassword.Text = "Пароль:";
        // 
        // txtPassword
        // 
        this.txtPassword.Location = new System.Drawing.Point(150, 97);
        this.txtPassword.Name = "txtPassword";
        this.txtPassword.Size = new System.Drawing.Size(350, 27);
        this.txtPassword.TabIndex = 5;
        this.txtPassword.UseSystemPasswordChar = true;
        // 
        // lblSyncFolder
        // 
        this.lblSyncFolder.AutoSize = true;
        this.lblSyncFolder.Location = new System.Drawing.Point(20, 140);
        this.lblSyncFolder.Name = "lblSyncFolder";
        this.lblSyncFolder.Size = new System.Drawing.Size(122, 20);
        this.lblSyncFolder.TabIndex = 6;
        this.lblSyncFolder.Text = "Папка синхронизации:";
        // 
        // txtSyncFolder
        // 
        this.txtSyncFolder.Location = new System.Drawing.Point(150, 137);
        this.txtSyncFolder.Name = "txtSyncFolder";
        this.txtSyncFolder.Size = new System.Drawing.Size(350, 27);
        this.txtSyncFolder.TabIndex = 7;
        // 
        // btnBrowse
        // 
        this.btnBrowse.Location = new System.Drawing.Point(506, 137);
        this.btnBrowse.Name = "btnBrowse";
        this.btnBrowse.Size = new System.Drawing.Size(75, 27);
        this.btnBrowse.TabIndex = 8;
        this.btnBrowse.Text = "Обзор...";
        this.btnBrowse.UseVisualStyleBackColor = true;
        // 
        // lblSyncInterval
        // 
        this.lblSyncInterval.AutoSize = true;
        this.lblSyncInterval.Location = new System.Drawing.Point(20, 180);
        this.lblSyncInterval.Name = "lblSyncInterval";
        this.lblSyncInterval.Size = new System.Drawing.Size(190, 20);
        this.lblSyncInterval.TabIndex = 9;
        this.lblSyncInterval.Text = "Интервал синхронизации (сек):";
        // 
        // numSyncInterval
        // 
        this.numSyncInterval.Location = new System.Drawing.Point(220, 178);
        this.numSyncInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        this.numSyncInterval.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
        this.numSyncInterval.Name = "numSyncInterval";
        this.numSyncInterval.Size = new System.Drawing.Size(80, 27);
        this.numSyncInterval.TabIndex = 10;
        this.numSyncInterval.Value = new decimal(new int[] { 5, 0, 0, 0 });
        // 
        // chkStartWithWindows
        // 
        this.chkStartWithWindows.AutoSize = true;
        this.chkStartWithWindows.Location = new System.Drawing.Point(20, 220);
        this.chkStartWithWindows.Name = "chkStartWithWindows";
        this.chkStartWithWindows.Size = new System.Drawing.Size(228, 24);
        this.chkStartWithWindows.TabIndex = 11;
        this.chkStartWithWindows.Text = "Запускать при старте Windows";
        this.chkStartWithWindows.UseVisualStyleBackColor = true;
        // 
        // chkShowNotifications
        // 
        this.chkShowNotifications.AutoSize = true;
        this.chkShowNotifications.Location = new System.Drawing.Point(20, 250);
        this.chkShowNotifications.Name = "chkShowNotifications";
        this.chkShowNotifications.Size = new System.Drawing.Size(210, 24);
        this.chkShowNotifications.TabIndex = 12;
        this.chkShowNotifications.Text = "Показывать уведомления";
        this.chkShowNotifications.UseVisualStyleBackColor = true;
        // 
        // rulesTab
        // 
        this.rulesTab.Controls.Add(this.rulesGrid);
        this.rulesTab.Controls.Add(this.btnAddRule);
        this.rulesTab.Controls.Add(this.btnRemoveRule);
        this.rulesTab.Location = new System.Drawing.Point(4, 29);
        this.rulesTab.Name = "rulesTab";
        this.rulesTab.Padding = new System.Windows.Forms.Padding(10);
        this.rulesTab.Size = new System.Drawing.Size(592, 417);
        this.rulesTab.TabIndex = 1;
        this.rulesTab.Text = "Правила порядка";
        this.rulesTab.UseVisualStyleBackColor = true;
        // 
        // rulesGrid
        // 
        this.rulesGrid.AllowUserToAddRows = true;
        this.rulesGrid.AllowUserToDeleteRows = true;
        this.rulesGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
        this.rulesGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this.rulesGrid.Dock = System.Windows.Forms.DockStyle.Fill;
        this.rulesGrid.Location = new System.Drawing.Point(10, 40);
        this.rulesGrid.Name = "rulesGrid";
        this.rulesGrid.RowHeadersWidth = 51;
        this.rulesGrid.RowTemplate.Height = 29;
        this.rulesGrid.Size = new System.Drawing.Size(572, 367);
        this.rulesGrid.TabIndex = 0;
        // 
        // btnAddRule
        // 
        this.btnAddRule.Location = new System.Drawing.Point(10, 10);
        this.btnAddRule.Name = "btnAddRule";
        this.btnAddRule.Size = new System.Drawing.Size(150, 27);
        this.btnAddRule.TabIndex = 1;
        this.btnAddRule.Text = "Добавить правило";
        this.btnAddRule.UseVisualStyleBackColor = true;
        // 
        // btnRemoveRule
        // 
        this.btnRemoveRule.Location = new System.Drawing.Point(166, 10);
        this.btnRemoveRule.Name = "btnRemoveRule";
        this.btnRemoveRule.Size = new System.Drawing.Size(80, 27);
        this.btnRemoveRule.TabIndex = 2;
        this.btnRemoveRule.Text = "Удалить";
        this.btnRemoveRule.UseVisualStyleBackColor = true;
        // 
        // panelButtons
        // 
        this.panelButtons.Controls.Add(this.btnSave);
        this.panelButtons.Controls.Add(this.btnCancel);
        this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.panelButtons.Location = new System.Drawing.Point(0, 450);
        this.panelButtons.Name = "panelButtons";
        this.panelButtons.Size = new System.Drawing.Size(600, 50);
        this.panelButtons.TabIndex = 1;
        // 
        // btnSave
        // 
        this.btnSave.Location = new System.Drawing.Point(400, 12);
        this.btnSave.Name = "btnSave";
        this.btnSave.Size = new System.Drawing.Size(90, 27);
        this.btnSave.TabIndex = 0;
        this.btnSave.Text = "Сохранить";
        this.btnSave.UseVisualStyleBackColor = true;
        // 
        // btnCancel
        // 
        this.btnCancel.Location = new System.Drawing.Point(496, 12);
        this.btnCancel.Name = "btnCancel";
        this.btnCancel.Size = new System.Drawing.Size(90, 27);
        this.btnCancel.TabIndex = 1;
        this.btnCancel.Text = "Отмена";
        this.btnCancel.UseVisualStyleBackColor = true;
        // 
        // SettingsForm
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(600, 500);
        this.Controls.Add(this.tabControl);
        this.Controls.Add(this.panelButtons);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Name = "SettingsForm";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Text = "Настройки CloudSync Agent";
        this.tabControl.ResumeLayout(false);
        this.mainTab.ResumeLayout(false);
        this.mainTab.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.numSyncInterval)).EndInit();
        this.rulesTab.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.rulesGrid)).EndInit();
        this.panelButtons.ResumeLayout(false);
        this.ResumeLayout(false);
    }

    #endregion

    private System.Windows.Forms.TabControl tabControl;
    private System.Windows.Forms.TabPage mainTab;
    private System.Windows.Forms.TabPage rulesTab;
    private System.Windows.Forms.Label lblServerUrl;
    private System.Windows.Forms.TextBox txtServerUrl;
    private System.Windows.Forms.Label lblUsername;
    private System.Windows.Forms.TextBox txtUsername;
    private System.Windows.Forms.Label lblPassword;
    private System.Windows.Forms.TextBox txtPassword;
    private System.Windows.Forms.Label lblSyncFolder;
    private System.Windows.Forms.TextBox txtSyncFolder;
    private System.Windows.Forms.Button btnBrowse;
    private System.Windows.Forms.Label lblSyncInterval;
    private System.Windows.Forms.NumericUpDown numSyncInterval;
    private System.Windows.Forms.CheckBox chkStartWithWindows;
    private System.Windows.Forms.CheckBox chkShowNotifications;
    private System.Windows.Forms.DataGridView rulesGrid;
    private System.Windows.Forms.Button btnAddRule;
    private System.Windows.Forms.Button btnRemoveRule;
    private System.Windows.Forms.Panel panelButtons;
    private System.Windows.Forms.Button btnSave;
    private System.Windows.Forms.Button btnCancel;
}