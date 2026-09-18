using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Globalization;

namespace Cutie
{
    [McpServerToolType]
    public class EffectTools
    {
        [McpServerTool, Description("List enabled video or audio effects by plugin unique ID. Limit defaults to 100.")]
        [ToolExecution(ToolKind.Read)]
        public object ListPlugins(string type, int limit = 100)
        {
            if (type != "video" && type != "audio") throw new ArgumentException("Type must be video or audio.");
            if (limit < 1 || limit > 500) throw new ArgumentOutOfRangeException(nameof(limit));
            var root = type == "video" ? VegasContext.Current.VideoFX : VegasContext.Current.AudioFX;
            return Walk(root).Where(node => !node.IsContainer && !node.IsDisabled)
                .Take(limit).Select(node => new
                {
                    uniqueId = node.UniqueID, name = node.Name, group = node.IsOFX ? node.Group : "(非OFX插件)",
                    isOfx = node.IsOFX, isVideo = node.IsVideo, isAudio = node.IsAudio
                }).ToArray();
        }

        [McpServerTool, Description("List effects on a track or event. Event index is required for event target.")]
        [ToolExecution(ToolKind.Read)]
        public object ListEffects(string targetType, int trackIndex, int eventIndex = -1)
        {
            return ResolveEffects(targetType, trackIndex, eventIndex)
                .Select(effect => new
                {
                    index = effect.Index, name = effect.Description,
                    pluginId = effect.PlugIn?.UniqueID, bypass = effect.Bypass,
                    preset = effect.CurrentPreset?.Name, isOfx = effect.IsOFX
                }).ToArray();
        }

        [McpServerTool, Description("Add a video or audio effect to a track or event by plugin unique ID.")]
        [ToolExecution(ToolKind.Edit)]
        public object AddEffect(string targetType, int trackIndex, string pluginId, int eventIndex = -1)
        {
            if (string.IsNullOrWhiteSpace(pluginId)) throw new ArgumentException("Plugin ID is required.");
            return VegasToolSupport.Edit("Add effect", () =>
            {
                var effects = ResolveEffects(targetType, trackIndex, eventIndex);
                var video = targetType == "track" ? VegasToolSupport.Track(trackIndex).IsVideo()
                    : VegasToolSupport.Event(trackIndex, eventIndex).IsVideo();
                var root = video ? VegasContext.Current.VideoFX : VegasContext.Current.AudioFX;
                var plugin = Walk(root).FirstOrDefault(node => !node.IsContainer &&
                    string.Equals(node.UniqueID, pluginId, StringComparison.OrdinalIgnoreCase));
                if (plugin == null || plugin.IsDisabled) throw new ArgumentException("Compatible plugin ID was not found.");
                var effect = effects.AddEffect(plugin);
                return new { index = effect.Index, name = effect.Description, pluginId = effect.PlugIn?.UniqueID };
            });
        }

        [McpServerTool, Description("Set an effect's bypass state or named preset.")]
        [ToolExecution(ToolKind.Edit)]
        public object UpdateEffect(string targetType, int trackIndex, int effectIndex,
            int eventIndex = -1, bool? bypass = null, string presetName = null)
        {
            return VegasToolSupport.Edit("Update effect", () =>
            {
                var effect = GetEffect(targetType, trackIndex, eventIndex, effectIndex);
                if (bypass.HasValue) effect.Bypass = bypass.Value;
                if (presetName != null) effect.Preset = presetName;
                return new { index = effect.Index, name = effect.Description,
                    bypass = effect.Bypass, preset = effect.CurrentPreset?.Name };
            });
        }

        [McpServerTool, Description("Remove an effect by its current zero-based index.")]
        [ToolExecution(ToolKind.Edit)]
        public object RemoveEffect(string targetType, int trackIndex, int effectIndex, int eventIndex = -1)
        {
            return VegasToolSupport.Edit("Remove effect", () =>
            {
                var effects = ResolveEffects(targetType, trackIndex, eventIndex);
                if (effectIndex < 0 || effectIndex >= effects.Count) throw new ArgumentOutOfRangeException(nameof(effectIndex));
                effects.RemoveAt(effectIndex);
                return new { removed = true };
            });
        }

        [McpServerTool, Description("List OFX parameters for a track or event effect, including current values and choices.")]
        [ToolExecution(ToolKind.Read)]
        public object ListEffectParameters(string targetType, int trackIndex, int effectIndex, int eventIndex = -1)
        {
            var effect = GetEffect(targetType, trackIndex, eventIndex, effectIndex);
            if (!effect.IsOFX) throw new ArgumentException("Effect is not OFX.");
            return effect.OFXEffect.Parameters.Select(parameter => new
            {
                name = parameter.Name, label = parameter.Label,
                type = parameter.ParameterType.ToString(), enabled = parameter.Enabled,
                canAnimate = parameter.CanAnimate, value = ParameterValue(parameter)
            }).ToArray();
        }

        [McpServerTool, Description("Set a Boolean, numeric, string, or choice OFX parameter. Value is text; optional atMs sets a keyframe.")]
        [ToolExecution(ToolKind.Edit)]
        public object SetEffectParameter(string targetType, int trackIndex, int effectIndex,
            string parameterName, string value, int eventIndex = -1, double? atMs = null)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || value == null)
                throw new ArgumentException("Parameter name and value are required.");
            return VegasToolSupport.Edit("Set effect parameter", () =>
            {
                var effect = GetEffect(targetType, trackIndex, eventIndex, effectIndex);
                if (!effect.IsOFX) throw new ArgumentException("Effect is not OFX.");
                var parameter = effect.OFXEffect.Parameters.FirstOrDefault(item =>
                    string.Equals(item.Name, parameterName, StringComparison.OrdinalIgnoreCase));
                if (parameter == null || !parameter.Enabled) throw new ArgumentException("Enabled parameter was not found.");
                var time = atMs.HasValue ? VegasToolSupport.OfxTime(atMs.Value) : null;
                if (time != null && !parameter.CanAnimate) throw new ArgumentException("Parameter cannot be animated.");
                if (time != null && !parameter.IsAnimated) parameter.IsAnimated = true;
                SetParameterValue(parameter, value, time);
                return new { name = parameter.Name, value = ParameterValue(parameter), atMs };
            });
        }

        internal static object ParameterValue(OFXParameter parameter)
        {
            if (parameter is OFXBooleanParameter b) return b.Value;
            if (parameter is OFXDoubleParameter d) return d.Value;
            if (parameter is OFXIntegerParameter i) return i.Value;
            if (parameter is OFXStringParameter s) return s.Value;
            if (parameter is OFXCustomParameter custom) return custom.Value;
            if (parameter is OFXDouble2DParameter xy) return new { x = xy.Value.X, y = xy.Value.Y };
            if (parameter is OFXRGBParameter rgb) return new { r = rgb.Value.R, g = rgb.Value.G, b = rgb.Value.B };
            if (parameter is OFXRGBAParameter rgba) return new { r = rgba.Value.R, g = rgba.Value.G, b = rgba.Value.B, a = rgba.Value.A };
            if (parameter is OFXChoiceParameter c)
                return new { selected = c.Value?.Name, choices = c.Choices.Select(x => x.Name).ToArray() };
            return null;
        }

        internal static void SetParameterValue(OFXParameter parameter, string value, Timecode time)
        {
            if (parameter is OFXBooleanParameter b)
            {
                if (!bool.TryParse(value, out var parsed)) throw new ArgumentException("Expected true or false.");
                if (time == null) b.Value = parsed; else b.SetValueAtTime(time, parsed);
            }
            else if (parameter is OFXDoubleParameter d)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                    double.IsNaN(parsed) || double.IsInfinity(parsed)) throw new ArgumentException("Expected a finite number.");
                if (time == null) d.Value = parsed; else d.SetValueAtTime(time, parsed);
            }
            else if (parameter is OFXIntegerParameter i)
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    throw new ArgumentException("Expected an integer.");
                if (time == null) i.Value = parsed; else i.SetValueAtTime(time, parsed);
            }
            else if (parameter is OFXStringParameter s)
            {
                if (time == null) s.Value = value; else s.SetValueAtTime(time, value);
            }
            else if (parameter is OFXCustomParameter custom)
            {
                if (time == null) custom.Value = value; else custom.SetValueAtTime(time, value);
            }
            else if (parameter is OFXDouble2DParameter xy)
            {
                var parts = ParseNumbers(value, 2);
                var parsed = new OFXDouble2D { X = parts[0], Y = parts[1] };
                if (time == null) xy.Value = parsed; else xy.SetValueAtTime(time, parsed);
            }
            else if (parameter is OFXRGBParameter rgb)
            {
                var parts = ParseNumbers(value, 3);
                var parsed = new OFXColor(parts[0], parts[1], parts[2]);
                if (time == null) rgb.Value = parsed; else rgb.SetValueAtTime(time, parsed);
            }
            else if (parameter is OFXRGBAParameter rgba)
            {
                var parts = ParseNumbers(value, 4);
                var parsed = new OFXColor(parts[0], parts[1], parts[2], parts[3]);
                if (time == null) rgba.Value = parsed; else rgba.SetValueAtTime(time, parsed);
            }
            else if (parameter is OFXChoiceParameter c)
            {
                OFXChoice choice = null;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var choiceIndex))
                    choice = c.Choices.FirstOrDefault(item => item.Index == choiceIndex);
                else
                    choice = c.Choices.FirstOrDefault(item => string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
                if (choice == null) throw new ArgumentException("Choice name or index was not found.");
                if (time == null) c.Value = choice; else c.SetValueAtTime(time, choice);
            }
            else throw new NotSupportedException("This OFX parameter type needs a dedicated value format.");
        }

        private static double[] ParseNumbers(string value, int count)
        {
            var parts = value.Split(',');
            if (parts.Length != count) throw new ArgumentException("Expected " + count + " comma-separated numbers.");
            var result = new double[count];
            for (var index = 0; index < count; index++)
                if (!double.TryParse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]) ||
                    double.IsNaN(result[index]) || double.IsInfinity(result[index]))
                    throw new ArgumentException("Expected finite comma-separated numbers.");
            return result;
        }

        private static Effect GetEffect(string targetType, int trackIndex, int eventIndex, int effectIndex)
        {
            var effects = ResolveEffects(targetType, trackIndex, eventIndex);
            if (effectIndex < 0 || effectIndex >= effects.Count) throw new ArgumentOutOfRangeException(nameof(effectIndex));
            return effects[effectIndex];
        }

        internal static Effects ResolveEffects(string targetType, int trackIndex, int eventIndex)
        {
            if (targetType == "track") return VegasToolSupport.Track(trackIndex).Effects;
            if (targetType == "event")
            {
                var item = VegasToolSupport.Event(trackIndex, eventIndex);
                return item.IsVideo() ? ((VideoEvent)item).Effects : ((AudioEvent)item).Effects;
            }
            throw new ArgumentException("Target type must be track or event.");
        }

        private static IEnumerable<PlugInNode> Walk(PlugInNode root)
        {
            foreach (var node in root)
            {
                yield return node;
                if (node.IsContainer)
                    foreach (var child in Walk(node)) yield return child;
            }
        }
    }
}
