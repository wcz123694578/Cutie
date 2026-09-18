using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Cutie
{
    // No VEGAS dependency: plan validation and ordered execution are tested with a fake host.
    internal sealed class BatchExecutor
    {
        private readonly IReadOnlyDictionary<string, ToolRegistration> _tools;
        private readonly IBatchHost _host;
        private readonly string _onError;
        private readonly Dictionary<string, JToken> _results = new Dictionary<string, JToken>();
        private readonly Dictionary<string, object> _handles = new Dictionary<string, object>();
        internal BatchOperation[] Operations { get; }
        internal BatchResult Result { get; }
        internal bool Stopped { get; private set; }

        internal BatchExecutor(IReadOnlyDictionary<string, ToolRegistration> tools, IBatchHost host,
            BatchOperation[] operations, string batchId, string undoMode, string onError)
        {
            _tools = tools; _host = host; _onError = onError;
            if (undoMode != "single" && undoMode != "staged") throw new ArgumentException("undoMode must be single or staged.");
            if (onError != "stop" && onError != "continue") throw new ArgumentException("onError must be stop or continue.");
            if (operations == null || operations.Length == 0 || operations.Length > 1000)
                throw new ArgumentException("A batch must contain 1 to 1000 operations.");
            // Freeze the plan so asynchronous callers cannot mutate a running batch.
            Operations = JArray.FromObject(operations).ToObject<BatchOperation[]>();
            var steps = new HashSet<string>(); var handles = new HashSet<string>();
            foreach (var operation in Operations)
            {
                if (operation == null || !ValidId(operation.Id) || steps.Contains(operation.Id))
                    throw new ArgumentException("Step IDs must be unique and contain 1-64 letters, digits, underscores or hyphens.");
                if (operation.Tool == null || !_tools.TryGetValue(operation.Tool, out var tool))
                    throw new ArgumentException("Unknown tool: " + operation.Tool);
                if (tool.Kind == ToolKind.Control) throw new ArgumentException("Batch control tools cannot be nested: " + tool.Name);
                if (undoMode == "single" && tool.Kind == ToolKind.External)
                    throw new ArgumentException(tool.Name + " cannot share one UndoBlock. Use undoMode: staged.");
                operation.Arguments = operation.Arguments ?? new JObject();
                BatchReferences.Validate(operation.Arguments, steps, handles);
                tool.Validate(operation.Arguments, true);
                steps.Add(operation.Id);
                var addedHandles = new HashSet<string>();
                foreach (var capture in operation.Capture ?? new BatchCapture[0])
                {
                    if (capture == null || !ValidId(capture.Name) || handles.Contains(capture.Name) || !addedHandles.Add(capture.Name))
                        throw new ArgumentException("Handle names must be valid and unique.");
                    ValidateCapture(capture);
                    BatchReferences.Validate(capture.Selector, steps, handles);
                }
                handles.UnionWith(addedHandles);
            }
            Result = new BatchResult { BatchId = batchId, UndoMode = undoMode, Status = "running" };
            Result.Steps.AddRange(Operations.Select(op => new BatchStepResult { Id = op.Id, Tool = op.Tool, Status = "not_executed" }));
        }

        internal ToolKind Kind(int index) => _tools[Operations[index].Tool].Kind;

        internal void ExecuteRange(int start, int count, string label, CancellationToken cancellation, Action progress)
        {
            if (Stopped) return;
            IDisposable undo = null;
            try
            {
                // Acquire lazily, after cancellation has been checked, including for TrackMotion reads.
                if (!cancellation.IsCancellationRequested && Enumerable.Range(start, count).Any(i => Kind(i) == ToolKind.Edit))
                    undo = _host.BeginUndo(label);
                for (var i = start; i < start + count && !Stopped; i++)
                {
                    if (cancellation.IsCancellationRequested) { Result.Status = "cancelled"; Stopped = true; break; }
                    var op = Operations[i]; var step = Result.Steps[i]; var clock = Stopwatch.StartNew();
                    try
                    {
                        var args = (JObject)BatchReferences.Resolve(op.Arguments, _results, _handles, _host);
                        var value = _tools[op.Tool].Invoke(args);
                        if (value is System.Threading.Tasks.Task) throw new InvalidOperationException("Async tools require an explicit batch completion adapter.");
                        step.Result = value == null ? JValue.CreateNull() : JToken.FromObject(value);
                        _results.Add(op.Id, step.Result);
                        var pending = new Dictionary<string, object>();
                        foreach (var capture in op.Capture ?? new BatchCapture[0])
                        {
                            var selector = (JObject)BatchReferences.Resolve(capture.Selector, _results, _handles, _host);
                            ValidateCapture(new BatchCapture { Kind = capture.Kind, Selector = selector });
                            pending.Add(capture.Name, _host.Capture(capture.Kind, selector));
                        }
                        foreach (var pair in pending) _handles.Add(pair.Key, pair.Value);
                        step.Status = "succeeded";
                    }
                    catch (Exception ex)
                    {
                        _results.Remove(op.Id);
                        step.Status = ex is BatchDependencyException ? "skipped" : "failed";
                        step.Error = ex.Message; step.ErrorType = ex.GetType().Name;
                        if (_onError == "stop") Stopped = true;
                    }
                    finally { step.ElapsedMs = clock.ElapsedMilliseconds; progress?.Invoke(); }
                }
            }
            finally
            {
                // Deliberately commit partial edits. One Undo must remain available after failure/cancellation.
                undo?.Dispose();
            }
        }

        internal void FailCompletion(int index, Exception error)
        {
            var step = Result.Steps[index];
            step.Status = "failed"; step.Error = error.Message; step.ErrorType = error.GetType().Name;
            _results.Remove(step.Id);
            // Unknown project/render state makes continuing unsafe, regardless of onError.
            Stopped = true;
        }

        internal void CompleteExternal(int index, JToken result)
        {
            Result.Steps[index].Result = result;
            _results[Operations[index].Id] = result;
        }

        internal void Finish(Exception error = null)
        {
            if (error != null) { Result.Error = error.Message; Result.Status = error is OperationCanceledException ? "cancelled" : "failed"; }
            else if (Result.Status != "cancelled") Result.Status = Result.Steps.All(s => s.Status == "succeeded") ? "completed" : "failed";
        }

        private static bool ValidId(string id) => id != null && Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$");
        private static void ValidateCapture(BatchCapture capture)
        {
            var required = capture.Kind == "track" ? new[] { "trackIndex" }
                : capture.Kind == "event" ? new[] { "trackIndex", "eventIndex" }
                : capture.Kind == "effect" ? new[] { "targetType", "trackIndex", "effectIndex" }
                : capture.Kind == "marker" || capture.Kind == "region" ? new[] { "index" }
                : throw new ArgumentException("Handle kind must be track, event, effect, marker or region.");
            if (capture.Selector == null || required.Any(key => capture.Selector[key] == null))
                throw new ArgumentException("Capture selector is missing required fields for " + capture.Kind);
            var allowed = new HashSet<string>(required);
            if (capture.Kind == "effect") allowed.Add("eventIndex");
            if (capture.Selector.Properties().Any(p => !allowed.Contains(p.Name))) throw new ArgumentException("Unknown capture selector field.");
            foreach (var field in capture.Selector.Properties())
            {
                if (BatchReferences.IsReference(field.Value)) continue;
                if (field.Name == "targetType")
                {
                    if (field.Value.Type != JTokenType.String || ((string)field.Value != "track" && (string)field.Value != "event"))
                        throw new ArgumentException("Effect capture targetType must be track or event.");
                }
                else if (field.Value.Type != JTokenType.Integer || (long)field.Value < 0 || (long)field.Value > int.MaxValue)
                    throw new ArgumentException("Capture indices must be nonnegative 32-bit integers.");
            }
            if (capture.Kind == "effect" && capture.Selector["targetType"]?.Type == JTokenType.String &&
                (string)capture.Selector["targetType"] == "event" && capture.Selector["eventIndex"] == null)
                throw new ArgumentException("Event effect capture requires eventIndex.");
        }
    }
}
