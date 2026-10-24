using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace Cutie.Launcher
{
    internal sealed class VisualStudioSession : IDisposable
    {
        private dynamic _dte;
        // Visual Studio's .NET Framework 4.x managed debug engine (also supports CLR 2).
        private const string ManagedEngine = "{449EC4CC-30D2-4032-9256-EE18EB41B62B}";
        internal string MsBuildPath { get; private set; }
        internal string Configuration { get; private set; }
        internal string Platform { get; private set; }

        internal static VisualStudioSession Connect(string root)
        {
            var ancestors = Ancestors();
            GetRunningObjectTable(0, out var table);
            CreateBindCtx(0, out var context);
            IEnumMoniker enumerator = null;
            object found = null;
            try
            {
                table.EnumRunning(out enumerator);
                var monikers = new IMoniker[1];
                while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    try
                    {
                        moniker.GetDisplayName(context, null, out var name);
                        var colon = name.LastIndexOf(':');
                        if (!name.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase) ||
                            colon < 0 || !int.TryParse(name.Substring(colon + 1), out var pid) || !ancestors.Contains(pid))
                            continue;
                        table.GetObject(moniker, out found);
                        break;
                    }
                    catch (COMException) { /* Unrelated ROT entries may disappear or deny access. */ }
                    finally { Marshal.ReleaseComObject(moniker); }
                }
            }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                Marshal.ReleaseComObject(context);
                Marshal.ReleaseComObject(table);
            }
            if (found == null)
                throw new InvalidOperationException("无法找到启动本工程的 Visual Studio。请在 VS 中将 Cutie.Launcher 设为启动项目后按 F5；VS 和启动器需使用相同权限运行。");
            var session = new VisualStudioSession { _dte = found };
            try
            {
                var solution = (string)session._dte.Solution.FullName;
                if (!string.Equals(Path.GetDirectoryName(solution), root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("启动器所属 VS 打开的不是当前 Cutie 解决方案，已停止启动。");
                session.MsBuildPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName((string)session._dte.FullName),
                    "..", "..", "MSBuild", "Current", "Bin", "MSBuild.exe"));
                if (!File.Exists(session.MsBuildPath)) throw new FileNotFoundException("此 Visual Studio 中找不到 MSBuild。", session.MsBuildPath);
                dynamic project = FindProject(session._dte.Solution.Projects, Path.Combine(root, "Cutie", "Cutie.csproj"));
                if (project == null) throw new InvalidOperationException("当前解决方案没有加载 Cutie 工程。");
                dynamic configuration = project.ConfigurationManager.ActiveConfiguration;
                session.Configuration = (string)configuration.ConfigurationName;
                session.Platform = ((string)configuration.PlatformName).Replace(" ", "");
                return session;
            }
            catch { session.Dispose(); throw; }
        }

        internal void EnsureReady()
        {
            foreach (dynamic process in _dte.Debugger.DebuggedProcesses)
                if ((int)process.ProcessID != Process.GetCurrentProcess().Id)
                    throw new InvalidOperationException("当前 VS 正在调试其他进程。请先停止之前的 VEGAS 调试，再运行启动器。");
        }

        internal void SaveDocuments() => _dte.Documents.SaveAll();

        internal void Attach(int pid)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(30))
            {
                using (var process = Process.GetProcessById(pid))
                    if (process.HasExited) throw new InvalidOperationException("VEGAS 已退出，无法附加调试器。");
                foreach (dynamic process in _dte.Debugger.LocalProcesses)
                {
                    if ((int)process.ProcessID != pid) continue;
                    ((IVsProcess2)(object)process).Attach2(new[] { ManagedEngine });
                    foreach (dynamic debugged in _dte.Debugger.DebuggedProcesses)
                        if ((int)debugged.ProcessID == pid) return;
                    throw new InvalidOperationException("VS 未确认附加到 VEGAS，未报告调试成功。");
                }
                Thread.Sleep(250);
            }
            throw new TimeoutException("等待 VS 识别 VEGAS 进程超时。请在 VS 中手动检查“附加到进程”。");
        }

        private static object FindProject(dynamic projects, string path)
        {
            foreach (dynamic project in projects)
            {
                if (string.Equals((string)project.Kind, "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (dynamic item in project.ProjectItems)
                    {
                        dynamic sub = item.SubProject;
                        if (sub == null) continue;
                        var result = FindProject(new object[] { sub }, path);
                        if (result != null) return result;
                    }
                }
                else if (string.Equals((string)project.FullName, path, StringComparison.OrdinalIgnoreCase)) return project;
            }
            return null;
        }

        private static HashSet<int> Ancestors()
        {
            var parents = new Dictionary<int, int>();
            var snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (Process32First(snapshot, ref entry))
                    do { parents[(int)entry.ProcessId] = (int)entry.ParentProcessId; }
                    while (Process32Next(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
            var result = new HashSet<int>();
            var current = Process.GetCurrentProcess().Id;
            while (parents.TryGetValue(current, out current) && current > 0 && result.Add(current)) { }
            return result;
        }

        public void Dispose()
        {
            if (_dte != null) { Marshal.ReleaseComObject(_dte); _dte = null; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            internal uint Size, Usage, ProcessId;
            internal UIntPtr DefaultHeap;
            internal uint ModuleId, Threads, ParentProcessId;
            internal int Priority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExeFile;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("ole32.dll", PreserveSig = false)] private static extern void GetRunningObjectTable(int reserved, out IRunningObjectTable table);
        [DllImport("ole32.dll", PreserveSig = false)] private static extern void CreateBindCtx(int reserved, out IBindCtx context);
    }

    // Query Process2 explicitly: the default EnvDTE.Process IDispatch has no Attach2.
    // GUID, DISPID and VARIANT parameter match the VS EnvDTE80 type library.
    [ComImport, Guid("95AC1923-6EAA-427C-B43E-6274A8CA6C95"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    internal interface IVsProcess2
    {
        [DispId(1001)]
        void Attach2([In, MarshalAs(UnmanagedType.Struct)] object engines);
    }
    // VS may briefly reject automation calls while processing build/debug events.
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleMessageFilter
    {
        [PreserveSig] int HandleInComingCall(int type, IntPtr caller, int tickCount, IntPtr info);
        [PreserveSig] int RetryRejectedCall(IntPtr callee, int tickCount, int rejectType);
        [PreserveSig] int MessagePending(IntPtr callee, int tickCount, int type);
    }

    internal sealed class OleMessageFilter : IOleMessageFilter, IDisposable
    {
        private readonly IOleMessageFilter _previous;
        internal OleMessageFilter() { Marshal.ThrowExceptionForHR(CoRegisterMessageFilter(this, out _previous)); }
        public int HandleInComingCall(int type, IntPtr caller, int tickCount, IntPtr info) => 0;
        public int RetryRejectedCall(IntPtr callee, int tickCount, int rejectType) => rejectType == 2 && tickCount < 10000 ? 100 : -1;
        public int MessagePending(IntPtr callee, int tickCount, int type) => 2;
        public void Dispose() { CoRegisterMessageFilter(_previous, out _); }
        [DllImport("ole32.dll")] private static extern int CoRegisterMessageFilter(IOleMessageFilter filter, out IOleMessageFilter old);
    }
}
