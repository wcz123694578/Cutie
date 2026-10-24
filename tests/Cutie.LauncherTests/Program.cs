using Cutie.Launcher;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

public static class LauncherTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        _checks++;
    }
    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (InvalidOperationException) { _checks++; return; }
        throw new Exception(label);
    }
    public static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "Cutie Launcher Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Check(LocalSettings.Load(root) == null, "First run must have no settings");
            Check(!LocalSettings.IsSdk(root), "Empty SDK rejected");
            Check(VegasInstallation.IsVegasExecutable("vegas180.exe"), "18 executable");
            Check(VegasInstallation.IsVegasExecutable("vegas2026.exe"), "2026 executable");
            Check(!VegasInstallation.IsVegasExecutable("vegas2026-helper.exe"), "Helper executable rejected");
            Check(!VegasInstallation.IsVegasExecutable("vegas.dll"), "Non executable rejected");
            var sdk = Path.Combine(root, "SDK & tools");
            Directory.CreateDirectory(sdk);
            var exe = Path.Combine(sdk, "vegas2026.exe");
            File.WriteAllText(exe, "fixture");
            File.WriteAllText(Path.Combine(sdk, "ScriptPortal.Vegas.dll"), "fixture");
            var settings = new LocalSettings { Executable = exe, SdkDirectory = sdk };
            Reject(() => settings.Save(root), "Incomplete SDK rejected");
            File.WriteAllText(Path.Combine(sdk, "ScriptPortal.MediaSoftware.Skins.dll"), "fixture");
            settings.Save(root);
            var roundtrip = LocalSettings.Load(root);
            Check(roundtrip.Executable == exe && roundtrip.SdkDirectory == sdk, "XML escaped path roundtrip");
            settings.Save(root);
            Check(!File.Exists(Path.Combine(root, LocalSettings.FileName + ".tmp")), "Atomic replacement cleaned up");
            File.Delete(exe);
            Reject(() => settings.Validate(), "Stale executable rejected");
            File.WriteAllText(exe, "fixture");
            File.Delete(Path.Combine(sdk, "ScriptPortal.Vegas.dll"));
            Reject(() => settings.Validate(), "Stale SDK rejected");

            foreach (var value in new[] { @"C:\Program Files\VEGAS", @"D:\VEGAS\", "", "quoted\" value", @"C:\音频 & 视频\插件" })
            {
                var command = "dummy.exe -CMDMODULE:" + DebugLaunch.Quote(value);
                var argv = CommandLineToArgvW(command, out var count);
                try
                {
                    var argument = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, IntPtr.Size));
                    Check(count == 2 && argument == "-CMDMODULE:" + value, "CMDMODULE quoting roundtrip: " + value);
                }
                finally { LocalFree(argv); }
            }

            var fake = new FakeDte();
            var session = new VisualStudioSession();
            typeof(VisualStudioSession).GetField("_dte", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, fake);
            var current = Process.GetCurrentProcess().Id;
            fake.Debugger.DebuggedProcesses.Add(new FakeProcess(current, fake.Debugger));
            session.EnsureReady();
            var other = new FakeProcess(current + 100000, fake.Debugger);
            fake.Debugger.DebuggedProcesses.Add(other);
            Reject(() => session.EnsureReady(), "Other debug session rejected");
            fake.Debugger.DebuggedProcesses.Clear();
            fake.Debugger.LocalProcesses.Add(other);
            var target = new FakeProcess(current, fake.Debugger);
            fake.Debugger.LocalProcesses.Add(target);
            Check(typeof(FakeProcess).GetMethod("Attach2") == null, "Default process interface does not expose Attach2");
            session.Attach(current);
            Check(target.Attached && !other.Attached, "Attach only requested PID");
            Check(target.Engines.Single() == "{449EC4CC-30D2-4032-9256-EE18EB41B62B}", "Managed Framework engine selected");
            Check(fake.Debugger.DebuggedProcesses.Single().ProcessID == current, "Attachment verified");

            var installations = VegasInstallation.Scan();
            Check(installations.Select(i => i.Executable).Distinct(StringComparer.OrdinalIgnoreCase).Count() == installations.Count, "Scan deduplicates installations");
            Check(installations.SequenceEqual(installations.OrderBy(i => i.Version).ThenBy(i => i.Executable)), "Scan prefers oldest SDK");
            Console.WriteLine("PASS " + _checks + " checks; detected " + installations.Count + " VEGAS installations");
            foreach (var installation in installations) Console.WriteLine(installation);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { Directory.Delete(root, true); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
public sealed class FakeDte { public FakeDebugger Debugger { get; } = new FakeDebugger(); }
public sealed class FakeDebugger
{
    public List<FakeProcess> LocalProcesses { get; } = new List<FakeProcess>();
    public List<FakeProcess> DebuggedProcesses { get; } = new List<FakeProcess>();
}
[ComVisible(true), ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class FakeProcess : IVsProcess2
{
    public int ProcessID { get; }
    public bool Attached { get; private set; }
    public string[] Engines { get; private set; }
    private readonly FakeDebugger _debugger;
    public FakeProcess(int pid, FakeDebugger debugger) { ProcessID = pid; _debugger = debugger; }
    void IVsProcess2.Attach2(object engines) { Attached = true; Engines = (string[])engines; _debugger.DebuggedProcesses.Add(this); }
}