using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Cutie.Launcher
{
    internal static class DebugLaunch
    {
        internal static int Run(string root, LocalSettings settings, Action<string> status)
        {
            using (new OleMessageFilter())
            using (var vs = VisualStudioSession.Connect(root))
            {
                vs.EnsureReady();
                settings.Validate();
                vs.SaveDocuments();
                var info = Path.GetTempFileName();
                try
                {
                    status("正在生成 Cutie（" + vs.Configuration + " | " + vs.Platform + "）…");
                    var build = new ProcessStartInfo(vs.MsBuildPath,
                        Quote(Path.Combine(root, "Cutie", "Cutie.csproj")) +
                        " /restore /t:Build /nologo /verbosity:minimal" +
                        " /p:Configuration=" + Quote(vs.Configuration) +
                        " /p:Platform=" + Quote(vs.Platform) +
                        " /p:CutieLaunchInfoFile=" + Quote(info))
                    {
                        WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                    };
                    using (var process = new Process { StartInfo = build })
                    {
                        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) status(e.Data); };
                        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) status(e.Data); };
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                            throw new InvalidOperationException("Cutie 生成失败，未启动 VEGAS。请查看上方生成信息；若 DLL 被占用，请先停止调试并退出占用它的 VEGAS。");
                    }
                    var target = File.ReadAllText(info).Trim();
                    if (!Path.IsPathRooted(target) || !File.Exists(target) ||
                        !string.Equals(Path.GetFileName(target), "Cutie.dll", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("生成未返回有效的 Cutie.dll 输出路径，已停止启动。");
                    var output = Path.GetDirectoryName(target);
                    status("生成完成：" + target);
                    status("正在启动：" + settings.Executable);
                    using (var vegas = Process.Start(new ProcessStartInfo(settings.Executable,
                        "-CMDMODULE:" + Quote(output)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(settings.Executable) }))
                    {
                        if (vegas == null) throw new InvalidOperationException("VEGAS 启动没有返回进程。");
                        var pid = vegas.Id;
                        status("正在附加 .NET Framework 调试器，进程 " + pid + "…");
                        try { vs.Attach(pid); }
                        catch (Exception e)
                        {
                            throw new InvalidOperationException("VEGAS 已启动（PID " + pid + "），但自动附加失败。进程保留，请在当前 VS 手动附加，或退出 VEGAS 后重试。\r\n" + e.Message, e);
                        }
                        status("调试器已附加。请在 VEGAS 的 工具 → 扩展 中打开 CutieCommand；插件断点现在可以命中。");
                        return pid;
                    }
                }
                finally { File.Delete(info); }
            }
        }

        internal static string Quote(string value)
        {
            // Windows CommandLineToArgvW quoting, including trailing backslashes.
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
