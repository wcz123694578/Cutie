using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cutie
{
    internal static class ToolExecutor
    {
        // Shared by individual calls and entire batches. Control/status calls bypass this queue.
        internal static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        internal static async Task<object> ExecuteAsync(ToolRegistration tool, JObject arguments)
        {
            tool.Validate(arguments);
            if (tool.Kind == ToolKind.Control) return await Unwrap(tool.Invoke(arguments)).ConfigureAwait(false);
            if (tool.Kind == ToolKind.Background)
                return await Task.Run(() => tool.Invoke(arguments)).ConfigureAwait(false);
            await Gate.WaitAsync().ConfigureAwait(false);
            try { return await VegasContext.InvokeAsync(() => tool.Invoke(arguments)).ConfigureAwait(false); }
            finally { Gate.Release(); }
        }

        private static async Task<object> Unwrap(object value)
        {
            if (!(value is Task task)) return value;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }
    }
}
