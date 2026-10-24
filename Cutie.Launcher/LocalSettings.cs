using System;
using System.IO;
using System.Xml.Linq;

namespace Cutie.Launcher
{
    internal sealed class LocalSettings
    {
        internal const string FileName = "Vegas.local.props";
        internal string Executable { get; set; }
        internal string SdkDirectory { get; set; }

        internal static LocalSettings Load(string root)
        {
            var path = Path.Combine(root, FileName);
            if (!File.Exists(path)) return null;
            var xml = XDocument.Load(path);
            var properties = xml.Root?.Element("PropertyGroup");
            return new LocalSettings
            {
                Executable = (string)properties?.Element("VegasExecutable"),
                SdkDirectory = (string)properties?.Element("VegasSdkDirectory")
            };
        }

        internal void Validate()
        {
            if (!File.Exists(Executable) || !VegasInstallation.IsVegasExecutable(Executable))
                throw new InvalidOperationException("请选择存在的 VEGAS 主程序（例如 vegas180.exe 或 vegas2026.exe）。");
            if (!IsSdk(SdkDirectory))
                throw new InvalidOperationException("编译 SDK 目录必须同时包含 ScriptPortal.Vegas.dll 和 ScriptPortal.MediaSoftware.Skins.dll。");
            // MSBuild interprets these as property expansion or property-list delimiters.
            foreach (var path in new[] { Executable, SdkDirectory })
                if (path.IndexOfAny(new[] { '$', '@', '%', ';', '\r', '\n' }) >= 0)
                    throw new InvalidOperationException("安装路径包含 MSBuild 特殊字符，请选择不含 $、@、%、分号或换行的目录。");
        }

        internal static bool IsSdk(string path) => !string.IsNullOrWhiteSpace(path) &&
            File.Exists(Path.Combine(path, "ScriptPortal.Vegas.dll")) &&
            File.Exists(Path.Combine(path, "ScriptPortal.MediaSoftware.Skins.dll"));

        internal void Save(string root)
        {
            Validate();
            var xml = new XDocument(new XElement("Project", new XElement("PropertyGroup",
                new XElement("VegasSdkDirectory", Path.GetFullPath(SdkDirectory)),
                new XElement("VegasExecutable", Path.GetFullPath(Executable)))));
            var path = Path.Combine(root, FileName);
            var temporary = path + ".tmp";
            xml.Save(temporary);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
