using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;

namespace Cutie.Launcher
{
    internal sealed class VegasInstallation
    {
        internal string Executable { get; }
        internal string Directory => Path.GetDirectoryName(Executable);
        internal Version Version { get; }
        private string Label { get; }

        internal VegasInstallation(string executable)
        {
            Executable = Path.GetFullPath(executable);
            var info = FileVersionInfo.GetVersionInfo(Executable);
            Version = new Version(Math.Max(0, info.FileMajorPart), Math.Max(0, info.FileMinorPart),
                Math.Max(0, info.FileBuildPart), Math.Max(0, info.FilePrivatePart));
            Label = "VEGAS " + (string.IsNullOrWhiteSpace(info.ProductVersion) ? Version.ToString() : info.ProductVersion);
        }

        public override string ToString() => Label + " — " + Executable;

        internal static bool IsVegasExecutable(string path) => Regex.IsMatch(Path.GetFileName(path) ?? "",
            @"^vegas\d*(?:\.\d+)*\.exe$", RegexOptions.IgnoreCase);

        internal static List<VegasInstallation> Scan()
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (var registry = RegistryKey.OpenBaseKey(hive, view))
                        using (var uninstall = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                            if (uninstall != null)
                                foreach (var name in uninstall.GetSubKeyNames())
                                    using (var item = uninstall.OpenSubKey(name))
                                        if (((string)item?.GetValue("DisplayName") ?? "").IndexOf("VEGAS", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            var location = (string)item.GetValue("InstallLocation");
                                            if (!string.IsNullOrWhiteSpace(location)) directories.Add(location.Trim('"'));
                                            var icon = (string)item.GetValue("DisplayIcon");
                                            if (!string.IsNullOrWhiteSpace(icon))
                                            {
                                                var match = Regex.Match(icon, "^\\\"(?<path>[^\\\"]+\\.exe)\\\"|^(?<path>.+?\\.exe)(?:,|$)", RegexOptions.IgnoreCase);
                                                if (match.Success) directories.Add(Path.GetDirectoryName(match.Groups["path"].Value));
                                            }
                                        }
                    }
                    catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException || e is IOException) { }
                }

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
                foreach (var folder in new[] { "Program Files", "Program Files (x86)" })
                    foreach (var vendor in new[] { "VEGAS", "BorisFX", "Sony" })
                        foreach (var directory in SafeDirectories(Path.Combine(drive.Name, folder, vendor)))
                            if (Path.GetFileName(directory).IndexOf("VEGAS", StringComparison.OrdinalIgnoreCase) >= 0)
                                directories.Add(directory);

            var result = new Dictionary<string, VegasInstallation>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
                foreach (var executable in SafeFiles(directory).Where(IsVegasExecutable))
                    try { result[executable] = new VegasInstallation(executable); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { }
            return result.Values.OrderBy(i => i.Version).ThenBy(i => i.Executable).ToList();
        }

        private static string[] SafeDirectories(string path)
        {
            try { return System.IO.Directory.Exists(path) ? System.IO.Directory.GetDirectories(path) : Array.Empty<string>(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        private static string[] SafeFiles(string path)
        {
            try { return System.IO.Directory.Exists(path) ? System.IO.Directory.GetFiles(path, "*.exe") : Array.Empty<string>(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { return Array.Empty<string>(); }
        }
    }
}
