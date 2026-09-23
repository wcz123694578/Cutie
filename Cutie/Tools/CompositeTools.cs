using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.ComponentModel;
using System.Linq;

namespace Cutie.Tools
{
    [McpServerToolType]
    public class CompositeTools
    {
        [McpServerTool, Description("List video tracks with their VEGAS parent/child compositing nesting and composite settings. Call this before editing because track indices and inferred parentTrackIndex can change after timeline edits.")]
        [ToolExecution(ToolKind.Read)]
        public object ListCompositeTracks() => VegasToolSupport.Project.Tracks
            .OfType<VideoTrack>()
            .Select(VegasToolSupport.DescribeTrack)
            .ToArray();

        [McpServerTool, Description("Set a video track's parent/child compositing nesting level. Video tracks only. Call list_composite_tracks first. Set nestingLevel to 0 to remove it from a parent compositing group. For nestingLevel above 0, the target cannot be the first track, must immediately follow a video track, and cannot be more than one level deeper than that preceding video track. VEGAS derives the parent from track order and nesting levels; this tool never reorders tracks.")]
        [ToolExecution(ToolKind.Edit)]
        public object SetCompositeNestingLevel(int trackIndex, int nestingLevel)
        {
            if (nestingLevel < 0) throw new ArgumentOutOfRangeException(nameof(nestingLevel));
            ValidateCompositeNestingLevel(trackIndex, nestingLevel);
            return VegasToolSupport.Edit("Set composite nesting level", () =>
            {
                var track = VideoTrack(trackIndex);
                track.CompositeNestingLevel = nestingLevel;
                return VegasToolSupport.DescribeTrack(track);
            });
        }

        [McpServerTool, Description("Update a video track's composite level or blend modes. Video tracks only. Call list_composite_tracks first. Omitted values are unchanged; compositeLevel must be between 0 and 1. compositeMode and parentCompositeMode must be VEGAS CompositeMode names, such as SrcAlpha or Screen.")]
        [ToolExecution(ToolKind.Edit)]
        public object SetTrackComposite(int trackIndex, double? compositeLevel = null,
            string compositeMode = null, string parentCompositeMode = null)
        {
            if (compositeLevel.HasValue && (compositeLevel.Value < 0 || compositeLevel.Value > 1))
                throw new ArgumentOutOfRangeException(nameof(compositeLevel));
            var mode = compositeMode == null ? (CompositeMode?)null : ParseCompositeMode(compositeMode);
            var parentMode = parentCompositeMode == null ? (CompositeMode?)null : ParseCompositeMode(parentCompositeMode);
            return VegasToolSupport.Edit("Set track composite", () =>
            {
                var track = VideoTrack(trackIndex);
                if (compositeLevel.HasValue) track.CompositeLevel = (float)compositeLevel.Value;
                if (mode.HasValue) track.CompositeMode = mode.Value;
                if (parentMode.HasValue) track.ParentCompositeMode = parentMode.Value;
                return VegasToolSupport.DescribeTrack(track);
            });
        }

        [McpServerTool, Description("List Composite envelope keyframes for a video track. Video tracks only. Values control the track's animated composite level; use min and max from this result before creating or updating keyframes.")]
        [ToolExecution(ToolKind.Read)]
        public object ListCompositeKeyframes(int trackIndex)
        {
            var track = VideoTrack(trackIndex);
            var envelope = track.Envelopes.FindByType(EnvelopeType.Composite);
            if (envelope == null) return new { hasEnvelope = false, keyframes = Array.Empty<object>() };
            return new
            {
                hasEnvelope = true,
                min = envelope.Min,
                max = envelope.Max,
                neutral = envelope.Neutral,
                keyframes = envelope.Points.Select(point => new
                {
                    index = point.Index,
                    positionMs = point.X.ToMilliseconds(),
                    value = point.Y,
                    interpolation = point.Curve.ToString()
                }).ToArray()
            };
        }

        [McpServerTool, Description("Create or update a Composite envelope keyframe for a video track. Video tracks only. atMs is timeline milliseconds. Call list_composite_keyframes first to obtain the allowed value range; a keyframe within 0.6 ms of atMs is updated, otherwise a new keyframe is created.")]
        [ToolExecution(ToolKind.Edit)]
        public object SetCompositeKeyframe(int trackIndex, double atMs, double value,
            string interpolation = "Linear")
        {
            var time = VegasToolSupport.Time(atMs);
            var curve = ParseCurveType(interpolation);
            return VegasToolSupport.Edit("Set composite keyframe", () =>
            {
                var track = VideoTrack(trackIndex);
                var envelope = track.Envelopes.FindByType(EnvelopeType.Composite);
                if (envelope == null)
                {
                    envelope = new Envelope(EnvelopeType.Composite);
                    track.Envelopes.Add(envelope);
                }
                if (value < envelope.Min || value > envelope.Max)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Value must be between " + envelope.Min + " and " + envelope.Max + ".");

                var point = envelope.Points.FirstOrDefault(item =>
                    Math.Abs(item.X.ToMilliseconds() - atMs) < 0.6);
                if (point == null)
                {
                    point = new EnvelopePoint(time, value, curve);
                    envelope.Points.Add(point);
                }
                else
                {
                    point.Y = value;
                    point.Curve = curve;
                }
                return new
                {
                    index = point.Index,
                    positionMs = point.X.ToMilliseconds(),
                    value = point.Y,
                    interpolation = point.Curve.ToString()
                };
            });
        }

        private static VideoTrack VideoTrack(int trackIndex) =>
            VegasToolSupport.Track(trackIndex) as VideoTrack
            ?? throw new ArgumentException("Target track must be video.");

        private static void ValidateCompositeNestingLevel(int trackIndex, int nestingLevel)
        {
            var track = VideoTrack(trackIndex);
            if (nestingLevel == 0) return;

            if (trackIndex == 0)
                throw new ArgumentException("A child compositing track must follow a parent video track.", nameof(trackIndex));

            var precedingTrack = VegasToolSupport.Track(trackIndex - 1) as VideoTrack;
            if (precedingTrack == null)
                throw new ArgumentException("A child compositing track must immediately follow a video track.", nameof(trackIndex));

            if (nestingLevel > precedingTrack.CompositeNestingLevel + 1)
                throw new ArgumentException("nestingLevel cannot be more than one level deeper than the preceding video track.",
                    nameof(nestingLevel));
        }

        private static CompositeMode ParseCompositeMode(string value)
        {
            if (!Enum.TryParse(value, true, out CompositeMode mode) || mode == CompositeMode.Invalid)
                throw new ArgumentException("Unknown composite mode: " + value, nameof(value));
            return mode;
        }

        private static CurveType ParseCurveType(string value)
        {
            if (!Enum.TryParse(value, true, out CurveType curve) || !Enum.IsDefined(typeof(CurveType), curve))
                throw new ArgumentException("Unknown interpolation type: " + value, nameof(value));
            return curve;
        }
    }
}
