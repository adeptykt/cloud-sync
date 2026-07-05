using System.Windows.Forms;

namespace CloudSyncTray;

partial class LogForm
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
        this.txtLog = new System.Windows.Forms.TextBox();
        this.panelButtons = new System.Windows.Forms.Panel();
        this.btnOpenFolder = new System.Windows.Forms.Button();
        this.btnClear = new System.Windows.Forms.Button();
        this.btnRefresh = new System.Windows.Forms.Button();
        this.panelButtons.SuspendLayout();
        this.SuspendLayout();
        // 
        // txtLog
        // 
        this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
        this.txtLog.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.txtLog.Location = new System.Drawing.Point(0, 0);
        this.txtLog.Multiline = true;
        this.txtLog.Name = "txtLog";
        this.txtLog.ReadOnly = true;
        this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        this.txtLog.Size = new System.Drawing.Size(800, 560);
        this.txtLog.TabIndex = 0;
        // 
        // panelButtons
        // 
        this.panelButtons.Controls.Add(this.btnOpenFolder);
        this.panelButtons.Controls.Add(this.btnClear);
        this.panelButtons.Controls.Add(this.btnRefresh);
        this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.panelButtons.Location = new System.Drawing.Point(0, 560);
        this.panelButtons.Name = "panelButtons";
        this.panelButtons.Size = new System.Drawing.Size(800, 40);
        this.panelButtons.TabIndex = 1;
        // 
        // btnOpenFolder
        // 
        this.btnOpenFolder.Location = new System.Drawing.Point(690, 8);
        this.btnOpenFolder.Name = "btnOpenFolder";
        this.btnOpenFolder.Size = new System.Drawing.Size(100, 25);
        this.btnOpenFolder.TabIndex = 2;
        this.btnOpenFolder.Text = "Открыть папку";
        this.btnOpenFolder.UseVisualStyleBackColor = true;
        // 
        // btnClear
        // 
        this.btnClear.Location = new System.Drawing.Point(100, 8);
        this.btnClear.Name = "btnClear";
        this.btnClear.Size = new System.Drawing.Size(80, 25);
        this.btnClear.TabIndex = 1;
        this.btnClear.Text = "Очистить";
        this.btnClear.UseVisualStyleBackColor = true;
        // 
        // btnRefresh
        // 
        this.btnRefresh.Location = new System.Drawing.Point(10, 8);
        this.btnRefresh.Name = "btnRefresh";
        this.btnRefresh.Size = new System.Drawing.Size(80, 25);
        this.btnRefresh.TabIndex = 0;
        this.btnRefresh.Text = "Обновить";
        this.btnRefresh.UseVisualStyleBackColor = true;
        // 
        // LogForm
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(800, 600);
        this.Controls.Add(this.txtLog);
        this.Controls.Add(this.panelButtons);
        this.Name = "LogForm";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Text = "Лог CloudSync Agent";
        this.panelButtons.ResumeLayout(false);
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.TextBox txtLog;
    private System.Windows.Forms.Panel panelButtons;
    private System.Windows.Forms.Button btnRefresh;
    private System.Windows.Forms.Button btnClear;
    private System.Windows.Forms.Button btnOpenFolder;
}