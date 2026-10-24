using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cutie.Launcher
{
    public partial class LauncherForm : Form
    {
        private readonly string _root;
        private bool _busy;

        public LauncherForm()
        {
            InitializeComponent();
        }

        internal LauncherForm(string root) : this()
        {
            _root = root;
        }

        private async void LauncherForm_Shown(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_root)) return;
            SetBusy(true);
            statusLabel.Text = "正在扫描已安装的 VEGAS…";
            try
            {
                Log(statusLabel.Text);
                var installations = await Task.Run(() => VegasInstallation.Scan());
                if (IsDisposed || Disposing) return;
                foreach (var installation in installations) AddInstallation(installation);
                if (hostComboBox.Items.Count > 0) hostComboBox.SelectedIndex = 0;
                if (sdkComboBox.Items.Count > 0) sdkComboBox.SelectedIndex = 0;
                var settings = LocalSettings.Load(_root);
                if (settings != null)
                {
                    if (File.Exists(settings.Executable) && VegasInstallation.IsVegasExecutable(settings.Executable))
                    {
                        AddInstallation(new VegasInstallation(settings.Executable));
                        hostComboBox.SelectedItem = hostComboBox.Items.Cast<VegasInstallation>().First(i => string.Equals(i.Executable, settings.Executable, StringComparison.OrdinalIgnoreCase));
                    }
                    if (!string.IsNullOrWhiteSpace(settings.SdkDirectory))
                    {
                        AddSdk(settings.SdkDirectory);
                        sdkComboBox.SelectedItem = sdkComboBox.Items.Cast<string>().First(i => string.Equals(i, settings.SdkDirectory, StringComparison.OrdinalIgnoreCase));
                    }
                    settings.Validate();
                }
                Log(settings == null ? "扫描完成，请选择版本。点击“生成并启动 VEGAS”后才会启动进程。" : "已载入上次配置。可直接切换调试版本；点击“生成并启动 VEGAS”后才会启动进程。");
            }
            catch (Exception error) { Log("请重新选择配置：" + error.Message); }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                    statusLabel.Text = "请选择调试版本，然后点击“生成并启动 VEGAS”。";
                }
            }
        }

        private void LauncherForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_busy) return;
            e.Cancel = true;
            MessageBox.Show(this, "操作进行中，请等待扫描、生成或附加完成。", Text);
        }

        private void BrowseHostButton_Click(object sender, EventArgs e) => BrowseHost();

        private void BrowseSdkButton_Click(object sender, EventArgs e) => BrowseSdk();

        private async void StartButton_Click(object sender, EventArgs e) => await StartDebugging();

        private void AddInstallation(VegasInstallation installation)
        {
            if (!hostComboBox.Items.Cast<VegasInstallation>().Any(i => string.Equals(i.Executable, installation.Executable, StringComparison.OrdinalIgnoreCase)))
                hostComboBox.Items.Add(installation);
            if (LocalSettings.IsSdk(installation.Directory)) AddSdk(installation.Directory);
        }

        private void AddSdk(string path)
        {
            if (!sdkComboBox.Items.Cast<string>().Any(i => string.Equals(i, path, StringComparison.OrdinalIgnoreCase))) sdkComboBox.Items.Add(path);
        }

        private void BrowseHost()
        {
            using (var dialog = new OpenFileDialog { Title = "选择 VEGAS 主程序", Filter = "VEGAS 主程序 (vegas*.exe)|vegas*.exe", CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try
                    {
                        if (!VegasInstallation.IsVegasExecutable(dialog.FileName)) throw new InvalidOperationException("请选择 VEGAS 主程序。");
                        var installation = new VegasInstallation(dialog.FileName);
                        AddInstallation(installation);
                        hostComboBox.SelectedItem = hostComboBox.Items.Cast<VegasInstallation>().First(i => string.Equals(i.Executable, installation.Executable, StringComparison.OrdinalIgnoreCase));
                        if (sdkComboBox.SelectedIndex < 0 && sdkComboBox.Items.Count > 0) sdkComboBox.SelectedIndex = 0;
                    }
                    catch (Exception e) { MessageBox.Show(this, e.Message, Text); }
        }

        private void BrowseSdk()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择包含 ScriptPortal.Vegas.dll 和 ScriptPortal.MediaSoftware.Skins.dll 的 VEGAS SDK 目录", ShowNewFolderButton = false })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (!LocalSettings.IsSdk(dialog.SelectedPath)) { MessageBox.Show(this, "此目录缺少所需的两个 ScriptPortal DLL。", Text); return; }
                    AddSdk(dialog.SelectedPath);
                    sdkComboBox.SelectedItem = sdkComboBox.Items.Cast<string>().First(i => string.Equals(i, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase));
                }
        }

        private async Task StartDebugging()
        {
            if (_busy) return;
            try
            {
                var settings = new LocalSettings { Executable = (hostComboBox.SelectedItem as VegasInstallation)?.Executable, SdkDirectory = sdkComboBox.SelectedItem as string };
                settings.Save(_root);
                Log("本机配置已保存。切换 SDK 不代表自动保证旧版兼容，请在目标版本测试。");
                SetBusy(true);
                statusLabel.Text = "正在生成并启动 VEGAS，随后附加调试器…";
                var completion = new TaskCompletionSource<int>();
                var thread = new Thread(() =>
                {
                    try { completion.SetResult(DebugLaunch.Run(_root, settings, Log)); }
                    catch (Exception e) { completion.SetException(e); }
                }) { IsBackground = true, Name = "Cutie VS automation" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                var pid = await completion.Task;
                SetBusy(false);
                startButton.Enabled = false;
                statusLabel.Text = "调试器已附加到 VEGAS（PID " + pid + "）。";
                hostComboBox.Enabled = sdkComboBox.Enabled = browseHostButton.Enabled = browseSdkButton.Enabled = false;
                Log("需要再次启动时，请先在 VS 停止本次调试，再按 F5。关闭此窗口不会主动关闭 VEGAS。");
            }
            catch (Exception e)
            {
                SetBusy(false);
                statusLabel.Text = "启动未完成，请查看下方日志。";
                Log("启动未完成：" + e.Message);
                MessageBox.Show(this, e.Message, "启动未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            hostComboBox.Enabled = sdkComboBox.Enabled = browseHostButton.Enabled = browseSdkButton.Enabled = startButton.Enabled = !busy;
        }

        private void Log(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), message); return; }
            logTextBox.AppendText(message + Environment.NewLine);
        }
    }
}
