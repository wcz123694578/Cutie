using Cutie.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cutie
{
    internal sealed class BatchDependencyException : Exception
    { public BatchDependencyException(string message) : base(message) { } }

    internal static class BatchReferences
    {
        internal static bool IsReference(JToken token) => token is JObject obj && (obj["$ref"] != null || obj["$handle"] != null);

        internal static void Validate(JToken token, ISet<string> steps, ISet<string> handles)
        {
            if (token is JObject obj)
            {
                if (IsReference(obj))
                {
                    if (obj["$ref"] != null)
                    {
                        if (obj.Count != 1 || obj["$ref"].Type != JTokenType.String)
                            throw new ArgumentException("A result reference must contain only a string $ref.");
                        var parts = Parse((string)obj["$ref"]);
                        if (!steps.Contains(parts[0])) throw new ArgumentException("Unknown or forward step reference: " + parts[0]);
                    }
                    else
                    {
                        if (obj.Count != 2 || obj["$handle"].Type != JTokenType.String || obj["property"]?.Type != JTokenType.String)
                            throw new ArgumentException("A handle reference requires $handle and property strings.");
                        if (!handles.Contains((string)obj["$handle"])) throw new ArgumentException("Unknown or forward handle reference.");
                    }
                    return;
                }
                foreach (var p in obj.Properties()) Validate(p.Value, steps, handles);
            }
            else if (token is JArray array) foreach (var child in array) Validate(child, steps, handles);
        }

        internal static JToken Resolve(JToken token, IDictionary<string, JToken> results,
            IDictionary<string, object> handles, IBatchHost host)
        {
            if (token is JObject obj)
            {
                if (obj["$ref"] != null)
                {
                    var parts = Parse((string)obj["$ref"]);
                    if (!results.TryGetValue(parts[0], out var value))
                        throw new BatchDependencyException("Referenced step did not succeed: " + parts[0]);
                    foreach (var key in parts.Skip(1))
                    {
                        if (value is JObject map) value = map[key];
                        else if (value is JArray items && int.TryParse(key, out var index) && index >= 0 && index < items.Count) value = items[index];
                        else value = null;
                        if (value == null) throw new ArgumentException("Result reference path was not found: " + obj["$ref"]);
                    }
                    return value.DeepClone();
                }
                if (obj["$handle"] != null)
                {
                    if (!handles.TryGetValue((string)obj["$handle"], out var handle))
                        throw new BatchDependencyException("Handle was not captured: " + obj["$handle"]);
                    return host.ResolveHandle(handle, (string)obj["property"]);
                }
                var copy = new JObject();
                foreach (var p in obj.Properties()) copy[p.Name] = Resolve(p.Value, results, handles, host);
                return copy;
            }
            if (token is JArray array) return new JArray(array.Select(t => Resolve(t, results, handles, host)));
            return token?.DeepClone() ?? JValue.CreateNull();
        }

        private static string[] Parse(string reference)
        {
            var hash = reference?.IndexOf('#') ?? -1;
            if (hash < 1 || (reference.Length > hash + 1 && reference[hash + 1] != '/'))
                throw new ArgumentException("Use JSON Pointer references such as step#/eventInfo/eventIndex or step#.");
            var result = new List<string> { reference.Substring(0, hash) };
            if (reference.Length > hash + 1)
                result.AddRange(reference.Substring(hash + 2).Split('/').Select(x => x.Replace("~1", "/").Replace("~0", "~")));
            return result.ToArray();
        }
    }
}
