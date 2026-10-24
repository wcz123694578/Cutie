namespace Cutie.Launcher
{
    partial class LauncherForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
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
            this.mainLayout = new System.Windows.Forms.TableLayoutPanel();
            this.instructionLabel = new System.Windows.Forms.Label();
            this.hostLabel = new System.Windows.Forms.Label();
            this.hostComboBox = new System.Windows.Forms.ComboBox();
            this.browseHostButton = new System.Windows.Forms.Button();
            this.sdkLabel = new System.Windows.Forms.Label();
            this.sdkComboBox = new System.Windows.Forms.ComboBox();
            this.browseSdkButton = new System.Windows.Forms.Button();
            this.startButton = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();
            this.logTextBox = new System.Windows.Forms.TextBox();
            this.mainLayout.SuspendLayout();
            this.SuspendLayout();
            // 
            // mainLayout
            // 
            this.mainLayout.ColumnCount = 3;
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 135F));
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 270F));
            this.mainLayout.Controls.Add(this.instructionLabel, 0, 0);
            this.mainLayout.Controls.Add(this.hostLabel, 0, 1);
            this.mainLayout.Controls.Add(this.hostComboBox, 1, 1);
            this.mainLayout.Controls.Add(this.browseHostButton, 2, 1);
            this.mainLayout.Controls.Add(this.sdkLabel, 0, 2);
            this.mainLayout.Controls.Add(this.sdkComboBox, 1, 2);
            this.mainLayout.Controls.Add(this.browseSdkButton, 2, 2);
            this.mainLayout.Controls.Add(this.startButton, 2, 3);
            this.mainLayout.Controls.Add(this.statusLabel, 0, 4);
            this.mainLayout.Controls.Add(this.logTextBox, 0, 5);
            this.mainLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainLayout.Location = new System.Drawing.Point(0, 0);
            this.mainLayout.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.mainLayout.Name = "mainLayout";
            this.mainLayout.Padding = new System.Windows.Forms.Padding(24, 24, 24, 24);
            this.mainLayout.RowCount = 6;
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 54F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.Size = new System.Drawing.Size(1197, 900);
            this.mainLayout.TabIndex = 0;
            // 
            // instructionLabel
            // 
            this.mainLayout.SetColumnSpan(this.instructionLabel, 3);
            this.instructionLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.instructionLabel.Location = new System.Drawing.Point(24, 24);
            this.instructionLabel.Margin = new System.Windows.Forms.Padding(0);
            this.instructionLabel.Name = "instructionLabel";
            this.instructionLabel.Size = new System.Drawing.Size(1149, 60);
            this.instructionLabel.TabIndex = 0;
            this.instructionLabel.Text = "每次 F5 先选择调试版本，再点击“生成并启动 VEGAS”。\r\n上次配置会自动填入；切换版本直接使用下方列表。";
            this.instructionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // hostLabel
            // 
            this.hostLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.hostLabel.Location = new System.Drawing.Point(28, 84);
            this.hostLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.hostLabel.Name = "hostLabel";
            this.hostLabel.Size = new System.Drawing.Size(127, 48);
            this.hostLabel.TabIndex = 1;
            this.hostLabel.Text = "调试版本";
            this.hostLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // hostComboBox
            // 
            this.hostComboBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.hostComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.hostComboBox.DropDownWidth = 900;
            this.hostComboBox.FormattingEnabled = true;
            this.hostComboBox.Location = new System.Drawing.Point(163, 92);
            this.hostComboBox.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.hostComboBox.Name = "hostComboBox";
            this.hostComboBox.Size = new System.Drawing.Size(736, 26);
            this.hostComboBox.TabIndex = 2;
            // 
            // browseHostButton
            // 
            this.browseHostButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.browseHostButton.Location = new System.Drawing.Point(907, 90);
            this.browseHostButton.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.browseHostButton.Name = "browseHostButton";
            this.browseHostButton.Size = new System.Drawing.Size(262, 36);
            this.browseHostButton.TabIndex = 3;
            this.browseHostButton.Text = "添加 VEGAS…";
            this.browseHostButton.UseVisualStyleBackColor = true;
            this.browseHostButton.Click += new System.EventHandler(this.BrowseHostButton_Click);
            // 
            // sdkLabel
            // 
            this.sdkLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.sdkLabel.Location = new System.Drawing.Point(28, 132);
            this.sdkLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.sdkLabel.Name = "sdkLabel";
            this.sdkLabel.Size = new System.Drawing.Size(127, 48);
            this.sdkLabel.TabIndex = 4;
            this.sdkLabel.Text = "编译 SDK";
            this.sdkLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // sdkComboBox
            // 
            this.sdkComboBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.sdkComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.sdkComboBox.DropDownWidth = 900;
            this.sdkComboBox.FormattingEnabled = true;
            this.sdkComboBox.Location = new System.Drawing.Point(163, 140);
            this.sdkComboBox.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.sdkComboBox.Name = "sdkComboBox";
            this.sdkComboBox.Size = new System.Drawing.Size(736, 26);
            this.sdkComboBox.TabIndex = 5;
            // 
            // browseSdkButton
            // 
            this.browseSdkButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.browseSdkButton.Location = new System.Drawing.Point(907, 138);
            this.browseSdkButton.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.browseSdkButton.Name = "browseSdkButton";
            this.browseSdkButton.Size = new System.Drawing.Size(262, 36);
            this.browseSdkButton.TabIndex = 6;
            this.browseSdkButton.Text = "选择 SDK 目录…";
            this.browseSdkButton.UseVisualStyleBackColor = true;
            this.browseSdkButton.Click += new System.EventHandler(this.BrowseSdkButton_Click);
            // 
            // startButton
            // 
            this.startButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startButton.Location = new System.Drawing.Point(907, 190);
            this.startButton.Margin = new System.Windows.Forms.Padding(4, 10, 4, 8);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(262, 30);
            this.startButton.TabIndex = 7;
            this.startButton.Text = "生成并启动 VEGAS";
            this.startButton.UseVisualStyleBackColor = true;
            this.startButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // statusLabel
            // 
            this.mainLayout.SetColumnSpan(this.statusLabel, 3);
            this.statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusLabel.Location = new System.Drawing.Point(28, 228);
            this.statusLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(1141, 54);
            this.statusLabel.TabIndex = 8;
            this.statusLabel.Text = "请选择调试版本，然后点击“生成并启动 VEGAS”。";
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // logTextBox
            // 
            this.mainLayout.SetColumnSpan(this.logTextBox, 3);
            this.logTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logTextBox.Location = new System.Drawing.Point(28, 286);
            this.logTextBox.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.logTextBox.Multiline = true;
            this.logTextBox.Name = "logTextBox";
            this.logTextBox.ReadOnly = true;
            this.logTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.logTextBox.Size = new System.Drawing.Size(1141, 586);
            this.logTextBox.TabIndex = 9;
            this.logTextBox.WordWrap = false;
            // 
            // LauncherForm
            // 
            this.AcceptButton = this.startButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(144F, 144F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(1197, 900);
            this.Controls.Add(this.mainLayout);
            this.Font = new System.Drawing.Font("新宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.MinimumSize = new System.Drawing.Size(1219, 722);
            this.Name = "LauncherForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "VEGAS 调试启动器";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.LauncherForm_FormClosing);
            this.Shown += new System.EventHandler(this.LauncherForm_Shown);
            this.mainLayout.ResumeLayout(false);
            this.mainLayout.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel mainLayout;
        private System.Windows.Forms.Label instructionLabel;
        private System.Windows.Forms.Label hostLabel;
        private System.Windows.Forms.Label sdkLabel;
        private System.Windows.Forms.ComboBox hostComboBox;
        private System.Windows.Forms.ComboBox sdkComboBox;
        private System.Windows.Forms.Button browseHostButton;
        private System.Windows.Forms.Button browseSdkButton;
        private System.Windows.Forms.Button startButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.TextBox logTextBox;
    }
}