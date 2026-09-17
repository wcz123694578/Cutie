using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Cutie
{
    [McpServerToolType]
    public class KeyframeTools
    {
        [McpServerTool, Description("List interpolation types supported by VEGAS 18 for OFX and video motion keyframes.")]
        public object ListKeyframeTypes() => new
        {
            ofx = Enum.GetNames(typeof(OFXInterpolationType)),
            videoMotion = Enum.GetNames(typeof(VideoKeyframeType))
        };

        [McpServerTool, Description("List event Pan/Crop keyframes, including position, interpolation and bounds.")]
        public object ListPanCropKeyframes(int trackIndex, int eventIndex)
        {
            var video = VideoEvent(trackIndex, eventIndex);
            return video.VideoMotion.Keyframes.Select((frame, index) => new
            {
                index, positionMs = frame.Position.ToMilliseconds(), type = frame.Type.ToString(),
                smoothness = frame.Smoothness, bounds = DescribeBounds(frame.Bounds)
            }).ToArray();
        }

        [McpServerTool, Description("Create or update an event Pan/Crop keyframe. Position is relative to event start in milliseconds; moveX/moveY translate the crop rectangle in project pixels.")]
        public object SetPanCropKeyframe(int trackIndex, int eventIndex, double atMs,
            string interpolation, float moveX = 0, float moveY = 0, float smoothness = 0)
        {
            var type = ParseVideoInterpolation(interpolation);
            var time = VegasToolSupport.Time(atMs);
            return VegasToolSupport.Edit("Set Pan/Crop keyframe", () =>
            {
                var video = VideoEvent(trackIndex, eventIndex);
                if (atMs > video.Length.ToMilliseconds()) throw new ArgumentOutOfRangeException(nameof(atMs));
                var frames = video.VideoMotion.Keyframes;
                var frame = frames.FirstOrDefault(item => Math.Abs(item.Position.ToMilliseconds() - atMs) < 0.6);
                if (frame == null)
                {
                    frame = new VideoMotionKeyframe(VegasToolSupport.Project, time);
                    frames.Add(frame);
                }
                frame.Type = type;
                frame.Smoothness = smoothness;
                if (moveX != 0 || moveY != 0) frame.MoveBy(new VideoMotionVertex(moveX, moveY));
                return new { positionMs = frame.Position.ToMilliseconds(), type = frame.Type.ToString(),
                    smoothness = frame.Smoothness, bounds = DescribeBounds(frame.Bounds) };
            });
        }

        [McpServerTool, Description("List track motion keyframes, including position, interpolation, and x/y coordinates.")]
        public object ListTrackMotionKeyframes(int trackIndex)
        {
            var track = VideoTrack(trackIndex);
            TrackMotion motion;
            using (var undo = new UndoBlock(VegasToolSupport.Project, "Read track motion keyframes"))
            {
                motion = track.TrackMotion;
            }
            return motion.MotionKeyframes.Select(frame => new
            {
                index = frame.Index,
                positionMs = frame.Position.ToMilliseconds(),
                type = frame.Type.ToString(),
                smoothness = frame.Smoothness,
                x = frame.PositionX,
                y = frame.PositionY
            }).ToArray();
        }

        [McpServerTool, Description("Create or update a track motion keyframe at timeline milliseconds, with interpolation and optional x/y coordinates in VEGAS native units.")]
        public object SetTrackMotionKeyframe(int trackIndex, double atMs, string interpolation,
            double? x = null, double? y = null, double? smoothness = null)
        {
            var type = ParseVideoInterpolation(interpolation);
            var time = VegasToolSupport.Time(atMs);
            return VegasToolSupport.Edit("Set track motion keyframe", () =>
            {
                TrackMotion motion;
                try { motion = VideoTrack(trackIndex).TrackMotion; }
                catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x8000FFFF)
                {
                    throw new NotSupportedException("VEGAS returned E_UNEXPECTED while opening TrackMotion; no keyframe was changed.", ex);
                }
                var frame = motion.MotionKeyframes.FirstOrDefault(item =>
                    Math.Abs(item.Position.ToMilliseconds() - atMs) < 0.6)
                    ?? motion.InsertMotionKeyframe(time);
                frame.Type = type;
                if (x.HasValue) frame.PositionX = x.Value;
                if (y.HasValue) frame.PositionY = y.Value;
                if (smoothness.HasValue) frame.Smoothness = smoothness.Value;
                return new { index = frame.Index, positionMs = frame.Position.ToMilliseconds(),
                    type = frame.Type.ToString(), x = frame.PositionX, y = frame.PositionY,
                    smoothness = frame.Smoothness };
            });
        }

        private static VideoEvent VideoEvent(int trackIndex, int eventIndex) =>
            VegasToolSupport.Event(trackIndex, eventIndex) as VideoEvent
            ?? throw new ArgumentException("Target event must be video.");

        private static VideoTrack VideoTrack(int trackIndex) =>
            VegasToolSupport.Track(trackIndex) as VideoTrack
            ?? throw new ArgumentException("Target track must be video.");

        private static object DescribeBounds(VideoMotionBounds bounds) => new
        {
            topLeft = new { x = bounds.TopLeft.X, y = bounds.TopLeft.Y },
            topRight = new { x = bounds.TopRight.X, y = bounds.TopRight.Y },
            bottomRight = new { x = bounds.BottomRight.X, y = bounds.BottomRight.Y },
            bottomLeft = new { x = bounds.BottomLeft.X, y = bounds.BottomLeft.Y }
        };

        private static VideoKeyframeType ParseVideoInterpolation(string interpolation)
        {
            if (!Enum.TryParse(interpolation, true, out VideoKeyframeType parsed) ||
                !Enum.IsDefined(typeof(VideoKeyframeType), parsed))
                throw new ArgumentException("Unsupported video interpolation type.", nameof(interpolation));
            return parsed;
        }

        [McpServerTool, Description("List an OFX parameter's keyframes, values and interpolation types. timecodeValue is the raw VEGAS OFX timecode reading, which VEGAS 18 may expose as frame count. Target is generator, event or track.")]
        public object ListOfxKeyframes(string targetType, int trackIndex, int eventIndex,
            string parameterName, int effectIndex = -1, double? sampleAtMs = null)
        {
            var parameter = FindParameter(targetType, trackIndex, eventIndex, effectIndex, parameterName);
            var frames = Frames(parameter).Select((frame, index) => new
            {
                index,
                timecodeValue = frame.Time.ToMilliseconds(),
                interpolation = frame.Interpolation.ToString(),
                value = FrameValue(frame)
            }).ToArray();
            object sampleValue = null;
            if (sampleAtMs.HasValue)
            {
                var time = VegasToolSupport.OfxTime(sampleAtMs.Value);
                var method = parameter.GetType().GetMethod("GetValueAtTime", new[] { typeof(Timecode) });
                sampleValue = method?.Invoke(parameter, new object[] { time });
            }
            return new { parameterName = parameter.Name, parameterType = parameter.ParameterType.ToString(),
                keyframes = frames, sampleAtMs, sampleValue };
        }

        [McpServerTool, Description("Set the interpolation type of an existing OFX parameter keyframe at an exact time in milliseconds.")]
        public object SetOfxKeyframeInterpolation(string targetType, int trackIndex, int eventIndex,
            string parameterName, double atMs, string interpolation, int effectIndex = -1)
        {
            var parsed = ParseInterpolation(interpolation);
            VegasToolSupport.Time(atMs);
            return VegasToolSupport.Edit("Set OFX keyframe interpolation", () =>
            {
                var parameter = FindParameter(targetType, trackIndex, eventIndex, effectIndex, parameterName);
                var frames = Frames(parameter);
                var frameNumber = Math.Round(atMs * VegasToolSupport.Project.Video.FrameRate / 1000.0);
                var toleranceMs = 1000.0 / VegasToolSupport.Project.Video.FrameRate;
                var frame = frames.FirstOrDefault(item =>
                    Math.Abs(item.Time.ToMilliseconds() - frameNumber) < 0.6)
                    ?? frames.FirstOrDefault(item =>
                        Math.Abs(item.Time.ToMilliseconds() - atMs) <= toleranceMs);
                if (frame == null)
                    throw new ArgumentException("Keyframe near " + atMs + " ms was not found. Existing times: " +
                        string.Join(", ", frames.Select(item => item.Time.ToMilliseconds())), nameof(atMs));
                frame.Interpolation = parsed;
                return new { parameterName = parameter.Name, requestedAtMs = atMs,
                    timecodeValue = frame.Time.ToMilliseconds(),
                    interpolation = frame.Interpolation.ToString(), value = FrameValue(frame) };
            });
        }

        [McpServerTool, Description("Set OFX keyframe interpolation by stable zero-based keyframe index. List keyframes first to obtain indices.")]
        public object SetOfxKeyframeInterpolationByIndex(string targetType, int trackIndex,
            int eventIndex, string parameterName, int keyframeIndex, string interpolation,
            int effectIndex = -1)
        {
            var parsed = ParseInterpolation(interpolation);
            return VegasToolSupport.Edit("Set OFX keyframe interpolation", () =>
            {
                var parameter = FindParameter(targetType, trackIndex, eventIndex, effectIndex, parameterName);
                var frames = Frames(parameter);
                if (keyframeIndex < 0 || keyframeIndex >= frames.Length)
                    throw new ArgumentOutOfRangeException(nameof(keyframeIndex));
                var frame = frames[keyframeIndex];
                frame.Interpolation = parsed;
                return new { parameterName = parameter.Name, keyframeIndex,
                    timecodeValue = frame.Time.ToMilliseconds(), interpolation = frame.Interpolation.ToString(),
                    value = FrameValue(frame) };
            });
        }

        private static OFXInterpolationType ParseInterpolation(string interpolation)
        {
            if (!Enum.TryParse(interpolation, true, out OFXInterpolationType parsed) ||
                !Enum.IsDefined(typeof(OFXInterpolationType), parsed) ||
                parsed == OFXInterpolationType.Unknown)
                throw new ArgumentException("Unsupported OFX interpolation type.", nameof(interpolation));
            return parsed;
        }

        private static OFXParameter FindParameter(string targetType, int trackIndex,
            int eventIndex, int effectIndex, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                throw new ArgumentException("Parameter name is required.", nameof(parameterName));
            Effect effect;
            if (targetType == "generator")
            {
                var media = VegasToolSupport.Event(trackIndex, eventIndex).ActiveTake?.Media;
                if (media == null || !media.IsGenerated())
                    throw new ArgumentException("Event is not generated media.");
                effect = media.Generator;
            }
            else
            {
                Effects effects;
                if (targetType == "track") effects = VegasToolSupport.Track(trackIndex).Effects;
                else if (targetType == "event")
                {
                    var item = VegasToolSupport.Event(trackIndex, eventIndex);
                    effects = item.IsVideo() ? ((VideoEvent)item).Effects : ((AudioEvent)item).Effects;
                }
                else throw new ArgumentException("Target must be generator, track or event.");
                if (effectIndex < 0 || effectIndex >= effects.Count)
                    throw new ArgumentOutOfRangeException(nameof(effectIndex));
                effect = effects[effectIndex];
            }
            if (effect == null || !effect.IsOFX)
                throw new ArgumentException("Target has no OFX effect or generator.");
            return effect.OFXEffect.Parameters.FirstOrDefault(item =>
                string.Equals(item.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("OFX parameter was not found.", nameof(parameterName));
        }

        private static OFXKeyframe[] Frames(OFXParameter parameter)
        {
            var property = parameter.GetType().GetProperty("Keyframes");
            var collection = property?.GetValue(parameter) as IEnumerable;
            if (collection == null)
                throw new NotSupportedException("This OFX parameter has no keyframe collection.");
            return collection.Cast<OFXKeyframe>().ToArray();
        }

        private static object FrameValue(OFXKeyframe frame)
        {
            var valueProperty = frame.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name == "Value" && property.GetIndexParameters().Length == 0)
                .OrderBy(property => property.DeclaringType == frame.GetType() ? 0 : 1)
                .FirstOrDefault();
            var value = valueProperty?.GetValue(frame);
            if (value is OFXChoice choice) return new { selected = choice.Name, index = choice.Index };
            if (value is OFXColor color) return new { r = color.R, g = color.G, b = color.B, a = color.A };
            if (value is OFXDouble2D xy) return new { x = xy.X, y = xy.Y };
            return value;
        }
    }
}
