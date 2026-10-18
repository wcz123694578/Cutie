using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Cutie
{
    // Opt-in observer on the calling VEGAS thread only. Never alters or posts a message.
    internal sealed class SnapshotMessageDiagnostics : IDisposable
    {
        private const int WhGetMessage = 3;
        private const int SnapshotCompletedMessage = 0x779; // Observed in VEGAS Pro 18's native wait loop.
        private readonly OperationDiagnostics _diagnostic;
        private readonly bool _traceMessages;
        private readonly HookProc _callback;
        private GCHandle _callbackRoot;
        private IntPtr _hook;
        private int _observations;

        internal SnapshotMessageDiagnostics(OperationDiagnostics diagnostic, bool traceMessages = true)
        {
            _diagnostic = diagnostic;
            _traceMessages = traceMessages;
            _callback = Observe;
            _callbackRoot = GCHandle.Alloc(_callback);
            _hook = SetWindowsHookEx(WhGetMessage, _callback, IntPtr.Zero, GetCurrentThreadId());
            if (_hook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _callbackRoot.Free();
                diagnostic.Note("snapshot_message_hook_unavailable win32=" + error);
                return;
            }
            diagnostic.Note("snapshot_message_hook_installed nativeThread=" + GetCurrentThreadId() +
                " vegasBase=0x" + GetModuleHandle(null).ToInt64().ToString("x") + " traceMessages=" + traceMessages);
        }

        private IntPtr Observe(int code, IntPtr removal, IntPtr message)
        {
            if (!_traceMessages) return CallNextHookEx(IntPtr.Zero, code, removal, message);
            int before = -1;
            if (code >= 0 && message != IntPtr.Zero)
                before = Marshal.ReadInt32(message, IntPtr.Size);
            // Always preserve the hook chain, and inspect its final message as well.
            var result = CallNextHookEx(IntPtr.Zero, code, removal, message);
            try
            {
                // MSG starts with HWND, then UINT message. Reading these fields leaves the message intact.
                int after = code >= 0 && message != IntPtr.Zero ? Marshal.ReadInt32(message, IntPtr.Size) : -1;
                if ((before == SnapshotCompletedMessage || after == SnapshotCompletedMessage) && _observations++ < 8)
                {
                    int parametersOffset = IntPtr.Size == 8 ? 16 : 8;
                    var frames = new IntPtr[48];
                    int count = CaptureStackBackTrace(0, (uint)frames.Length, frames, IntPtr.Zero);
                    _diagnostic.Note("snapshot_message_0x779 removed=" + (removal != IntPtr.Zero) +
                        " hwnd=0x" + Marshal.ReadIntPtr(message).ToInt64().ToString("x") +
                        " wParam=0x" + Marshal.ReadIntPtr(message, parametersOffset).ToInt64().ToString("x") +
                        " lParam=0x" + Marshal.ReadIntPtr(message, parametersOffset + IntPtr.Size).ToInt64().ToString("x") +
                        " messageBefore=0x" + before.ToString("x") + " messageAfter=0x" + after.ToString("x") +
                        " nativeStack=" + string.Join(",", frames.Take(count).Select(p => "0x" + p.ToInt64().ToString("x"))));
                }
            }
            catch (Exception) { /* Diagnostic failures must not escape a native hook callback. */ }
            return result;
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            if (UnhookWindowsHookEx(_hook))
            {
                _hook = IntPtr.Zero;
                _callbackRoot.Free();
                _diagnostic.Note("snapshot_message_hook_removed");
            }
            else
            {
                // Keep the delegate rooted if Windows still holds its function pointer.
                _diagnostic.Note("snapshot_message_hook_remove_failed win32=" + Marshal.GetLastWin32Error());
            }
            GC.KeepAlive(_callback);
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hook, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", EntryPoint = "RtlCaptureStackBackTrace")]
        private static extern ushort CaptureStackBackTrace(uint skip, uint capture, [Out] IntPtr[] frames, IntPtr hash);
    }
}
