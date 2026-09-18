using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Cutie
{
    [McpServerToolType]
    public class GeneratorTools
    {
        [McpServerTool, Description("List enabled VEGAS video media generators and their unique IDs.")]
        [ToolExecution(ToolKind.Read)]
        public object ListGenerators(int limit = 100)
        {
            if (limit < 1 || limit > 500) throw new ArgumentOutOfRangeException(nameof(limit));
            var found = new List<object>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in SafeWalk(VegasContext.Current.Generators))
            {
                try
                {
                    if (node.IsContainer || node.IsDisabled) continue;
                    var pluginId = node.UniqueID;
                    if (string.IsNullOrWhiteSpace(pluginId) || !seenIds.Add(pluginId)) continue;
                    found.Add(new { pluginId, name = node.Name,
                        group = node.Group, isOfx = node.IsOFX });
                    if (found.Count >= limit) break;
                }
                catch (COMException) { /* A broken installed generator must not hide other generators. */ }
            }
            return found.ToArray();
        }

        [McpServerTool, Description("List preset names for a VEGAS media generator by plugin ID.")]
        [ToolExecution(ToolKind.Read)]
        public object ListGeneratorPresets(string pluginId)
        {
            var node = FindGenerator(pluginId);
            return node.Presets.Select(preset => preset.Name).ToArray();
        }

        [McpServerTool, Description("Create a generated media event on an existing video track. Times are milliseconds; presetName is optional.")]
        [ToolExecution(ToolKind.Edit)]
        public object CreateGeneratedEvent(int trackIndex, string pluginId, double startMs,
            double lengthMs, string presetName = null)
        {
            if (lengthMs <= 0) throw new ArgumentOutOfRangeException(nameof(lengthMs));
            var start = VegasToolSupport.Time(startMs);
            var length = VegasToolSupport.Time(lengthMs);
            var node = FindGenerator(pluginId);
            return VegasToolSupport.Edit("Create generated media", () =>
            {
                var track = VegasToolSupport.Track(trackIndex) as VideoTrack;
                if (track == null) throw new ArgumentException("A video track is required.", nameof(trackIndex));
                Media media = string.IsNullOrWhiteSpace(presetName)
                    ? Media.CreateInstance(VegasToolSupport.Project, node)
                    : Media.CreateInstance(VegasToolSupport.Project, node, presetName);
                media.Length = length;
                var stream = media.GetVideoStreamByIndex(0);
                if (stream == null) throw new InvalidOperationException("Generator returned no video stream.");
                var item = track.AddVideoEvent(start, length);
                item.AddTake(stream);
                return new { mediaId = media.MediaID, generator = node.Name,
                    generatorId = node.UniqueID, eventInfo = VegasToolSupport.DescribeEvent(item) };
            });
        }

        [McpServerTool, Description("List OFX parameters on a generated media event, including values and animation support.")]
        [ToolExecution(ToolKind.Read)]
        public object ListGeneratorParameters(int trackIndex, int eventIndex)
        {
            var generator = GetGenerator(trackIndex, eventIndex);
            if (!generator.IsOFX) throw new NotSupportedException("This generator does not expose OFX parameters.");
            return generator.OFXEffect.Parameters.Select(parameter => new {
                name = parameter.Name, label = parameter.Label,
                type = parameter.ParameterType.ToString(), enabled = parameter.Enabled,
                canAnimate = parameter.CanAnimate,
                value = EffectTools.ParameterValue(parameter)
            }).ToArray();
        }

        [McpServerTool, Description("Set a generated media OFX parameter. Numeric vectors and RGB/RGBA colors use comma-separated components; optional atMs sets a keyframe.")]
        [ToolExecution(ToolKind.Edit)]
        public object SetGeneratorParameter(int trackIndex, int eventIndex,
            string parameterName, string value, double? atMs = null)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || value == null)
                throw new ArgumentException("Parameter name and value are required.");
            return VegasToolSupport.Edit("Set generator parameter", () =>
            {
                var generator = GetGenerator(trackIndex, eventIndex);
                if (!generator.IsOFX) throw new NotSupportedException("This generator does not expose OFX parameters.");
                var parameter = generator.OFXEffect.Parameters.FirstOrDefault(item =>
                    string.Equals(item.Name, parameterName, StringComparison.OrdinalIgnoreCase));
                if (parameter == null || !parameter.Enabled)
                    throw new ArgumentException("Enabled generator parameter was not found.");
                var time = atMs.HasValue ? VegasToolSupport.OfxTime(atMs.Value) : null;
                if (time != null && !parameter.CanAnimate)
                    throw new ArgumentException("Generator parameter cannot be animated.");
                if (time != null && !parameter.IsAnimated) parameter.IsAnimated = true;
                EffectTools.SetParameterValue(parameter, value, time);
                return new { name = parameter.Name, atMs,
                    value = EffectTools.ParameterValue(parameter) };
            });
        }

        private static Effect GetGenerator(int trackIndex, int eventIndex)
        {
            var media = VegasToolSupport.Event(trackIndex, eventIndex).ActiveTake?.Media;
            if (media == null || !media.IsGenerated() || media.Generator == null)
                throw new ArgumentException("Active event take is not generated media.");
            return media.Generator;
        }

        private static PlugInNode FindGenerator(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId)) throw new ArgumentException("Generator plugin ID is required.");
            PlugInNode node = null;
            try { node = VegasContext.Current.Generators.GetChildByName(pluginId); }
            catch (COMException) { }
            if (node == null)
                foreach (var item in SafeWalk(VegasContext.Current.Generators))
                {
                    try
                    {
                        if (!item.IsContainer && !item.IsDisabled &&
                            (string.Equals(item.UniqueID, pluginId, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(item.Name, pluginId, StringComparison.OrdinalIgnoreCase)))
                        { node = item; break; }
                    }
                    catch (COMException) { }
                }
            return node ?? throw new ArgumentException("Enabled video generator ID was not found.");
        }

        private static IEnumerable<PlugInNode> SafeWalk(PlugInNode root)
        {
            int count;
            try { count = root.Count; }
            catch (COMException) { count = 0; }
            for (var index = 0; index < count; index++)
            {
                PlugInNode node;
                try { node = root[index]; }
                catch (COMException) { continue; }
                yield return node;
                bool container;
                try { container = node.IsContainer; }
                catch (COMException) { continue; }
                if (container)
                    foreach (var child in SafeWalk(node)) yield return child;
            }
        }
    }
}
