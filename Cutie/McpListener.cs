using Cutie.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cutie
{
    public class McpToolCalledEventArgs : EventArgs
    {
        public string Message { get; set; }
    }

    public class McpListener : IDisposable
    {

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly SettingsService _settingsService;

        public event EventHandler<McpToolCalledEventArgs> ToolCalled;

        public McpListener(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Start()
        {
            if (_listener != null && _listener.IsListening)
                return;
            var settings = _settingsService.GetSettings();
            var prefix = $"http://localhost:{settings.McpServerPort}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleContext(context));
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { /* 可记录日志 */ }
            }
        }

        private void HandleContext(HttpListenerContext context)
        {
            try
            {
                var req = context.Request;
                string body = null;
                if (req.HasEntityBody)
                {
                    using (var reader = new System.IO.StreamReader(req.InputStream, req.ContentEncoding))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                var msg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {req.HttpMethod} {req.Url.PathAndQuery} {body}";
                ToolCalled?.Invoke(this, new McpToolCalledEventArgs { Message = msg });

                var resp = context.Response;
                var buffer = Encoding.UTF8.GetBytes("OK");
                resp.ContentLength64 = buffer.Length;
                resp.OutputStream.Write(buffer, 0, buffer.Length);
                resp.Close();
            }
            catch { /* 忽略或记录 */ }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch { }
            finally
            {
                _listener = null;
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
