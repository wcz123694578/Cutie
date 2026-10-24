using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Cutie.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                var root = FindRepository(AppDomain.CurrentDomain.BaseDirectory);
                string key;
                using (var hash = SHA256.Create())
                    key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "");
                using (var mutex = new Mutex(true, "Local\\Cutie.Launcher." + key, out var created))
                {
                    if (!created)
                    {
                        MessageBox.Show("此工程的启动器已打开，请使用原来的窗口。", "Cutie 启动器");
                        return;
                    }
                    try { Application.Run(new LauncherForm(root)); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error) { MessageBox.Show(error.Message, "Cutie 启动器", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        internal static string FindRepository(string directory)
        {
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "Cutie.slnx")) &&
                    File.Exists(Path.Combine(current.FullName, "Cutie", "Cutie.csproj")))
                    return current.FullName;
            throw new InvalidOperationException("找不到 Cutie 解决方案。请在解决方案中将 Cutie.Launcher 设为启动项目。不要单独复制启动器运行。");
        }
    }
}
