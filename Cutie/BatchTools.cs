using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Cutie
{
    [McpServerToolType]
    public sealed class BatchTools
    {
        [McpServerTool, ToolExecution(ToolKind.Control)]
        [Description("Execute 1-1000 ordered tool calls. Defaults to one UndoBlock and stop on error. single rejects external operations before any edits; staged supports every non-control tool. Use start_batch for long work. References: {$ref:'step#/field'}, {$handle:'name',property:'trackIndex'}. Partial edits remain undoable; no automatic rollback.")]
        public Task<BatchResult> ExecuteBatch(BatchOperation[] operations, string label = "Cutie batch", string undoMode = "single",
            string onError = "stop", string batchId = null, int completionTimeoutSeconds = 120)
            => BatchService.Execute(operations, label, undoMode, onError, batchId, completionTimeoutSeconds);

        [McpServerTool, ToolExecution(ToolKind.Control)]
        [Description("Start an ordered batch and return batchId immediately. Poll get_batch_status; cancel_batch stops between operations. Same arguments and undo semantics as execute_batch.")]
        public object StartBatch(BatchOperation[] operations, string label = "Cutie batch", string undoMode = "single",
            string onError = "stop", string batchId = null, int completionTimeoutSeconds = 120)
            => new { batchId = BatchService.Start(operations, label, undoMode, onError, batchId, completionTimeoutSeconds), status = "accepted" };

        [McpServerTool, ToolExecution(ToolKind.Control), Description("Read batch status and per-step results without waiting for the VEGAS editing queue. Completed results are retained in bounded process memory.")]
        public object GetBatchStatus(string batchId) => BatchService.Status(batchId);

        [McpServerTool, ToolExecution(ToolKind.Control), Description("Request cancellation between batch operations. Does not interrupt a COM call, undo completed edits, or abort a render already started.")]
        public object CancelBatch(string batchId) => BatchService.Cancel(batchId);

        [McpServerTool, ToolExecution(ToolKind.Control), Description("List every tool's execution kind and support for single-undo or staged batches.")]
        public object ListBatchCapabilities()
        {
            var result = new System.Collections.Generic.List<object>();
            foreach (var tool in ToolRegistry.Tools.Value.Values)
                result.Add(new { name = tool.Name, kind = tool.Kind.ToString(),
                    single = tool.Kind != ToolKind.External && tool.Kind != ToolKind.Control,
                    staged = tool.Kind != ToolKind.Control });
            return result;
        }
    }
}
