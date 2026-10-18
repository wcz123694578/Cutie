using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Cutie
{
    // Logging must not wait for the VEGAS thread or perform file I/O on it.
    internal sealed class OperationDiagnostics : IDisposable
    {
        private static readonly ConcurrentQueue<string> Pending = new ConcurrentQueue<string>();
        private static readonly string Session = Guid.NewGuid().ToString("N");
        private static int _writing;
        private readonly object _sync = new object();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly string _name;
        private readonly Timer _timer;
        private string _stage;
        private long _stageStarted;
        private bool _disposed;

        internal static string LogPath { get; } = Path.Combine(Path.GetTempPath(), "Cutie", "diagnostics", Session + ".log");

        internal OperationDiagnostics(string name)
        {
            _name = name;
            Stage("started");
            _timer = new Timer(_ => ReportWaiting(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        internal void Stage(string stage)
        {
            lock (_sync)
            {
                if (_disposed) return;
                Write("stage_complete", _stage);
                _stage = stage;
                _stageStarted = _elapsed.ElapsedMilliseconds;
                Write("stage_enter", stage);
            }
        }

        internal void Failed(Exception error)
        {
            lock (_sync) Write("failed", _stage, error.GetType().FullName + ": " + error.Message);
        }

        internal void Note(string detail)
        {
            lock (_sync)
                if (!_disposed) Write("observation", _stage, detail);
        }

        private void ReportWaiting()
        {
            lock (_sync)
                if (!_disposed) Write("still_waiting", _stage);
        }

        private void Write(string eventName, string stage, string detail = null)
        {
            if (stage == null) return;
            var line = $"{DateTime.UtcNow:O} id={_id} tool={_name} event={eventName} stage={stage}" +
                $" totalMs={_elapsed.ElapsedMilliseconds} stageMs={_elapsed.ElapsedMilliseconds - _stageStarted}" +
                $" thread={Thread.CurrentThread.ManagedThreadId} apartment={Thread.CurrentThread.GetApartmentState()}" +
                (detail == null ? "" : " detail=" + detail.Replace('\r', ' ').Replace('\n', ' '));
            // Drop diagnostics rather than retaining unlimited data if the log disk is stuck.
            if (Pending.Count >= 1024) return;
            Pending.Enqueue(line);
            StartWriter();
        }

        private static void StartWriter()
        {
            if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(ignored =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    using (var writer = new StreamWriter(LogPath, true))
                        while (Pending.TryDequeue(out var line))
                        {
                            Trace.WriteLine("[Cutie] " + line);
                            writer.WriteLine(line);
                            writer.Flush();
                        }
                }
                catch (Exception error)
                {
                    Trace.WriteLine("[Cutie] Diagnostics unavailable: " + error.Message);
                    while (Pending.TryDequeue(out _)) { }
                }
                finally
                {
                    Interlocked.Exchange(ref _writing, 0);
                    if (!Pending.IsEmpty) StartWriter();
                }
            });
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                Write("finished", _stage);
                _disposed = true;
            }
            _timer.Dispose();
        }
    }
}
