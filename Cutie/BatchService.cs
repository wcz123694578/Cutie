using Cutie.Models;
using Cutie.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cutie
{
    internal static class BatchService
    {
        private sealed class Job
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal BatchExecutor Executor;
            internal Task<BatchResult> Task;
            internal JObject Snapshot;
            internal string Label;
            internal int TimeoutSeconds;
            internal bool Finished;
        }
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Job> Jobs = new Dictionary<string, Job>();

        internal static string Start(BatchOperation[] operations, string label, string undoMode, string onError, string batchId, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > 128) throw new ArgumentException("Batch label must contain 1 to 128 characters.");
            if (timeoutSeconds < 1 || timeoutSeconds > 3600) throw new ArgumentException("completionTimeoutSeconds must be 1 to 3600.");
            batchId = batchId ?? Guid.NewGuid().ToString("N");
            if (!System.Text.RegularExpressions.Regex.IsMatch(batchId, "^[A-Za-z0-9_-]{1,64}$")) throw new ArgumentException("Invalid batchId.");
            if (operations != null && JArray.FromObject(operations).ToString().Length > 4 * 1024 * 1024)
                throw new ArgumentException("Batch arguments exceed 4 MiB of JSON characters.");
            var job = new Job { Executor = new BatchExecutor(ToolRegistry.Tools.Value, new VegasBatchHost(), operations, batchId, undoMode, onError),
                Label = label, TimeoutSeconds = timeoutSeconds };
            job.Executor.Result.Status = "queued";
            job.Snapshot = JObject.FromObject(job.Executor.Result);
            lock (Sync)
            {
                if (Jobs.ContainsKey(batchId)) throw new ArgumentException("batchId already exists; use get_batch_status, do not replay it.");
                if (Jobs.Values.Count(j => !j.Finished) >= 8) throw new InvalidOperationException("At most 8 batches may be queued or running.");
                foreach (var old in Jobs.Where(p => p.Value.Finished).Take(Math.Max(0, Jobs.Count - 31)).Select(p => p.Key).ToArray())
                { Jobs[old].Cancellation.Dispose(); Jobs.Remove(old); }
                Jobs.Add(batchId, job);
                job.Task = Task.Run(() => Run(job));
            }
            return batchId;
        }

        internal static async Task<BatchResult> Execute(BatchOperation[] operations, string label, string undoMode,
            string onError, string batchId, int timeoutSeconds)
        {
            var id = Start(operations, label, undoMode, onError, batchId, timeoutSeconds);
            Task<BatchResult> task;
            lock (Sync) task = Jobs[id].Task;
            return await task.ConfigureAwait(false);
        }

        internal static object Status(string batchId)
        {
            lock (Sync)
            {
                if (!Jobs.TryGetValue(batchId ?? "", out var job)) throw new ArgumentException("Unknown or expired batchId.");
                return job.Snapshot.DeepClone();
            }
        }
        internal static object Cancel(string batchId)
        {
            lock (Sync)
            {
                if (!Jobs.TryGetValue(batchId ?? "", out var job)) throw new ArgumentException("Unknown or expired batchId.");
                if (!job.Finished) job.Cancellation.Cancel();
                return new { batchId, cancellationRequested = !job.Finished, alreadyFinished = job.Finished };
            }
        }
        private static void Publish(Job job)
        {
            var snapshot = JObject.FromObject(job.Executor.Result);
            lock (Sync) job.Snapshot = snapshot;
        }

        private static async Task<BatchResult> Run(Job job)
        {
            var executor = job.Executor; var token = job.Cancellation.Token; var acquired = false;
            try
            {
                await ToolExecutor.Gate.WaitAsync(token).ConfigureAwait(false); acquired = true;
                executor.Result.Status = "running"; Publish(job);
                if (executor.Result.UndoMode == "single")
                {
                    await VegasContext.InvokeAsync(() =>
                    {
                        executor.ExecuteRange(0, executor.Operations.Length, job.Label, token, () => Publish(job));
                        return true;
                    }).ConfigureAwait(false);
                }
                else
                {
                    for (var start = 0; start < executor.Operations.Length && !executor.Stopped;)
                    {
                        token.ThrowIfCancellationRequested();
                        var index = start; var kind = executor.Kind(index); var count = 1;
                        // External calls form boundaries. Captures/references may touch VEGAS even for a parser step.
                        if (kind != ToolKind.External && kind != ToolKind.Background)
                            while (index + count < executor.Operations.Length && executor.Kind(index + count) != ToolKind.External &&
                                executor.Kind(index + count) != ToolKind.Background) count++;
                        var length = count;
                        Func<bool> execute = () => { executor.ExecuteRange(index, length, job.Label, token, () => Publish(job)); return true; };
                        await VegasContext.InvokeAsync(execute).ConfigureAwait(false);
                        if (kind == ToolKind.External && executor.Result.Steps[index].Status == "succeeded")
                        {
                            executor.Result.Steps[index].Status = "waiting";
                            Publish(job);
                            try
                            {
                                await WaitForCompletion(executor, index, job.TimeoutSeconds, token, () => Publish(job)).ConfigureAwait(false);
                                executor.Result.Steps[index].Status = "succeeded"; Publish(job);
                            }
                            catch (OperationCanceledException)
                            { executor.Result.Steps[index].Status = "cancelled"; throw; }
                            catch (Exception ex) { executor.FailCompletion(index, ex); Publish(job); }
                        }
                        start += count;
                    }
                }
                executor.Finish();
            }
            catch (Exception ex) { executor.Finish(ex); }
            finally
            {
                if (acquired) ToolExecutor.Gate.Release();
                Publish(job);
                lock (Sync) job.Finished = true;
            }
            return executor.Result;
        }

        private static async Task WaitForCompletion(BatchExecutor executor, int index, int timeoutSeconds, CancellationToken token, Action progress)
        {
            var name = executor.Operations[index].Tool.ToLowerInvariant();
            if (name != "open_project" && name != "render_project") return;
            var watch = Stopwatch.StartNew();
            do
            {
                token.ThrowIfCancellationRequested();
                var done = await VegasContext.InvokeAsync(() =>
                {
                    if (name == "open_project")
                    {
                        var expected = (string)executor.Result.Steps[index].Result["requestedPath"];
                        var request = (long)executor.Result.Steps[index].Result["openRequest"];
                        var opened = !string.IsNullOrEmpty(expected) && ProjectOutputTools.IsOpenComplete(request, expected);
                        if (opened)
                        {
                            var result = executor.Result.Steps[index].Result.DeepClone();
                            result["path"] = expected;
                            executor.CompleteExternal(index, result);
                        }
                        return opened;
                    }
                    var state = JToken.FromObject(new ProjectOutputTools().GetRenderStatus());
                    executor.Result.Steps[index].Result = state;
                    var status = (string)state["status"];
                    if (status == "Complete") { executor.CompleteExternal(index, state); return true; }
                    if (status == "Failed" || status == "Canceled" || status == "Cancelled")
                        throw new InvalidOperationException("Render ended with status: " + status);
                    return false;
                }).ConfigureAwait(false);
                progress();
                if (done) return;
                await Task.Delay(100, token).ConfigureAwait(false);
            } while (watch.Elapsed.TotalSeconds < timeoutSeconds);
            throw new TimeoutException("Timed out waiting for " + name + "; the operation may still be running. No dependent steps were executed.");
        }
    }
}
