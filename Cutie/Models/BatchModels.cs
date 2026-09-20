using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Cutie.Models
{
    public sealed class BatchOperation
    {
        [JsonProperty("id", Required = Required.Always)] public string Id { get; set; }
        [JsonProperty("tool", Required = Required.Always)] public string Tool { get; set; }
        [JsonProperty("arguments")] public JObject Arguments { get; set; } = new JObject();
        [JsonProperty("capture")] public BatchCapture[] Capture { get; set; } = new BatchCapture[0];
    }
    public sealed class BatchCapture
    {
        [JsonProperty("name", Required = Required.Always)] public string Name { get; set; }
        [JsonProperty("kind", Required = Required.Always)] public string Kind { get; set; }
        [JsonProperty("selector", Required = Required.Always)] public JObject Selector { get; set; }
    }
    public sealed class BatchStepResult
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("tool")] public string Tool { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)] public JToken Result { get; set; }
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error { get; set; }
        [JsonProperty("errorType", NullValueHandling = NullValueHandling.Ignore)] public string ErrorType { get; set; }
        [JsonProperty("elapsedMs")] public long ElapsedMs { get; set; }
    }
    public sealed class BatchResult
    {
        [JsonProperty("batchId")] public string BatchId { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("undoMode")] public string UndoMode { get; set; }
        [JsonProperty("rollbackPerformed")] public bool RollbackPerformed => false;
        [JsonProperty("steps")] public List<BatchStepResult> Steps { get; } = new List<BatchStepResult>();
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error { get; set; }
    }
    internal interface IBatchHost
    {
        IDisposable BeginUndo(string label);
        object Capture(string kind, JObject selector);
        JToken ResolveHandle(object handle, string property);
    }
}
