using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.ComponentModel;
using System.Linq;

namespace Cutie.Tools
{
    [McpServerToolType]
    public class TimelineTools
    {
        [McpServerTool, Description("Get the VEGAS version and installation path.")]
        [ToolExecution(ToolKind.Read)]
        public object GetVegasInfo()
        {
            var vegas = VegasContext.Current;
            return new { version = vegas.Version, installationDirectory = vegas.InstallationDirectory };
        }

        [McpServerTool, Description("Get active project properties. Times are milliseconds.")]
        [ToolExecution(ToolKind.Read)]
        public object GetProjectInfo()
        {
            var project = VegasToolSupport.Project;
            return new { filePath = project.FilePath, isModified = project.IsModified,
                width = project.Video.Width, height = project.Video.Height,
                frameRate = project.Video.FrameRate, trackCount = project.Tracks.Count,
                lengthMs = project.Length.ToMilliseconds() };
        }

        [McpServerTool, Description("List current tracks. Indices can change after edits; list again before editing.")]
        [ToolExecution(ToolKind.Read)]
        public object ListTracks() => VegasToolSupport.Project.Tracks.Select(VegasToolSupport.DescribeTrack).ToArray();

        [McpServerTool, Description("List events on a track by its current zero-based index.")]
        [ToolExecution(ToolKind.Read)]
        public object ListEvents(int trackIndex) => VegasToolSupport.Track(trackIndex).Events.Select(VegasToolSupport.DescribeEvent).ToArray();

        [McpServerTool, Description("Get one event by current zero-based track and event indices.")]
        [ToolExecution(ToolKind.Read)]
        public object GetEvent(int trackIndex, int eventIndex) => VegasToolSupport.DescribeEvent(VegasToolSupport.Event(trackIndex, eventIndex));

        [McpServerTool, Description("Get cursor, selection, loop, and playback state. Times are milliseconds.")]
        [ToolExecution(ToolKind.Read)]
        public object GetTimelineState()
        {
            var t = VegasContext.Current.Transport;
            return new { cursorMs = t.CursorPosition.ToMilliseconds(),
                selectionStartMs = t.SelectionStart.ToMilliseconds(),
                selectionLengthMs = t.SelectionLength.ToMilliseconds(),
                isPlaying = t.IsPlaying, loopMode = t.LoopMode,
                loopStartMs = t.LoopRegionStart.ToMilliseconds(),
                loopLengthMs = t.LoopRegionLength.ToMilliseconds() };
        }

        [McpServerTool, Description("Create a video or audio track. Index -1 appends it.")]
        [ToolExecution(ToolKind.Edit)]
        public object CreateTrack(string type, string name, int index = -1)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Track name is required.");
            if (type != "video" && type != "audio") throw new ArgumentException("Type must be video or audio.");
            if (index < -1 || index > VegasToolSupport.Project.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return VegasToolSupport.Edit("Create track", () =>
            {
                Track track = type == "video" ? (Track)new VideoTrack(VegasToolSupport.Project, index, name)
                    : new AudioTrack(VegasToolSupport.Project, index, name);
                VegasToolSupport.Project.Tracks.Add(track);
                return VegasToolSupport.DescribeTrack(track);
            });
        }

        [McpServerTool, Description("Update a track. Omitted fields are unchanged.")]
        [ToolExecution(ToolKind.Edit)]
        public object UpdateTrack(int trackIndex, string name = null, bool? mute = null, bool? solo = null)
        {
            return VegasToolSupport.Edit("Update track", () =>
            {
                var track = VegasToolSupport.Track(trackIndex);
                if (name != null) track.Name = name;
                if (mute.HasValue) track.Mute = mute.Value;
                if (solo.HasValue) track.Solo = solo.Value;
                return VegasToolSupport.DescribeTrack(track);
            });
        }

        // [McpServerTool, Description("Move a track to a specified final zero-based index. Track indices can change after this operation; call list_tracks again. Moving tracks can change parent/child compositing relationships because VEGAS derives them from track order and nesting levels.")]
        // [ToolExecution(ToolKind.Edit)]
        // public object MoveTrack(int trackIndex, int destinationIndex)
        // {
        //     var tracks = VegasToolSupport.Project.Tracks;
        //     if (trackIndex < 0 || trackIndex >= tracks.Count)
        //         throw new ArgumentOutOfRangeException(nameof(trackIndex), "Track index is out of range. Call list_tracks again.");
        //     if (destinationIndex < 0 || destinationIndex >= tracks.Count)
        //         throw new ArgumentOutOfRangeException(nameof(destinationIndex), "Destination index is out of range. Call list_tracks again.");

        //     return VegasToolSupport.Edit("Move track", () =>
        //     {
        //         var track = VegasToolSupport.Track(trackIndex);
        //         if (trackIndex != destinationIndex)
        //         {
        //             tracks.Remove(track);
        //             tracks.Insert(destinationIndex, track);
        //         }

        //         return VegasToolSupport.DescribeTrack(track);
        //     });
        // }

        [McpServerTool, Description("Delete a track and its events by current zero-based index.")]
        [ToolExecution(ToolKind.Edit)]
        public object DeleteTrack(int trackIndex)
        {
            return VegasToolSupport.Edit("Delete track", () =>
            {
                var track = VegasToolSupport.Track(trackIndex);
                var name = track.Name;
                VegasToolSupport.Project.Tracks.Remove(track);
                return new { deleted = true, name };
            });
        }

        [McpServerTool, Description("Add a media stream to a matching track. Start and optional length are milliseconds.")]
        [ToolExecution(ToolKind.Edit)]
        public object AddEvent(int trackIndex, uint mediaId, double startMs, double? lengthMs = null)
        {
            var start = VegasToolSupport.Time(startMs);
            if (lengthMs.HasValue && lengthMs.Value <= 0) throw new ArgumentException("Length must be positive.");
            return VegasToolSupport.Edit("Add event", () =>
            {
                var track = VegasToolSupport.Track(trackIndex);
                var media = VegasToolSupport.Media(mediaId);
                MediaStream stream = track.IsVideo() ? (MediaStream)media.Streams.OfType<VideoStream>().FirstOrDefault()
                    : media.Streams.OfType<AudioStream>().FirstOrDefault();
                if (stream == null) throw new ArgumentException("Media has no stream matching the track type.");
                var length = lengthMs.HasValue ? VegasToolSupport.Time(lengthMs.Value) : stream.Length;
                TrackEvent item = track.IsVideo() ? (TrackEvent)((VideoTrack)track).AddVideoEvent(start, length)
                    : ((AudioTrack)track).AddAudioEvent(start, length);
                item.AddTake(stream);
                return VegasToolSupport.DescribeEvent(item);
            });
        }

        [McpServerTool, Description("Update an event. Times are milliseconds; omitted fields are unchanged.")]
        [ToolExecution(ToolKind.Edit)]
        public object UpdateEvent(int trackIndex, int eventIndex, double? startMs = null,
            double? lengthMs = null, string name = null, bool? mute = null,
            bool? loop = null, double? playbackRate = null,
            double? pitchSemis = null, double? sourceOffsetMs = null)
        {
            if (startMs.HasValue) VegasToolSupport.Time(startMs.Value);
            if (lengthMs.HasValue && lengthMs.Value <= 0) throw new ArgumentException("Length must be positive.");
            if (playbackRate.HasValue && playbackRate.Value <= 0) throw new ArgumentException("Playback rate must be positive.");
            if (pitchSemis.HasValue && (double.IsNaN(pitchSemis.Value) || double.IsInfinity(pitchSemis.Value)))
                throw new ArgumentOutOfRangeException(nameof(pitchSemis));
            if (sourceOffsetMs.HasValue) VegasToolSupport.Time(sourceOffsetMs.Value);
            return VegasToolSupport.Edit("Update event", () =>
            {
                var item = VegasToolSupport.Event(trackIndex, eventIndex);
                if (startMs.HasValue) item.Start = VegasToolSupport.Time(startMs.Value);
                if (lengthMs.HasValue) item.Length = VegasToolSupport.Time(lengthMs.Value);
                if (name != null) item.Name = name;
                if (mute.HasValue) item.Mute = mute.Value;
                if (loop.HasValue) item.Loop = loop.Value;
                if (playbackRate.HasValue) item.PlaybackRate = playbackRate.Value;
                if (pitchSemis.HasValue)
                {
                    if (!(item is AudioEvent audio)) throw new ArgumentException("Pitch is only available on audio events.");
                    audio.PitchSemis = pitchSemis.Value;
                }
                if (sourceOffsetMs.HasValue)
                {
                    if (item.ActiveTake == null) throw new ArgumentException("Event has no active take.");
                    item.ActiveTake.Offset = VegasToolSupport.Time(sourceOffsetMs.Value);
                }
                return VegasToolSupport.DescribeEvent(item);
            });
        }

        [McpServerTool, Description("Split an event at an offset in milliseconds relative to its start.")]
        [ToolExecution(ToolKind.Edit)]
        public object SplitEvent(int trackIndex, int eventIndex, double offsetMs)
        {
            return VegasToolSupport.Edit("Split event", () =>
            {
                var item = VegasToolSupport.Event(trackIndex, eventIndex);
                if (offsetMs <= 0 || offsetMs >= item.Length.ToMilliseconds()) throw new ArgumentOutOfRangeException(nameof(offsetMs));
                return VegasToolSupport.DescribeEvent(item.Split(VegasToolSupport.Time(offsetMs)));
            });
        }

        [McpServerTool, Description("Copy an event to a matching track at a start time in milliseconds.")]
        [ToolExecution(ToolKind.Edit)]
        public object CopyEvent(int trackIndex, int eventIndex, int destinationTrackIndex, double startMs)
        {
            return VegasToolSupport.Edit("Copy event", () =>
            {
                var item = VegasToolSupport.Event(trackIndex, eventIndex);
                var destination = VegasToolSupport.Track(destinationTrackIndex);
                if (item.IsVideo() != destination.IsVideo()) throw new ArgumentException("Destination track type does not match event.");
                return VegasToolSupport.DescribeEvent(item.Copy(destination, VegasToolSupport.Time(startMs)));
            });
        }

        [McpServerTool, Description("Delete an event by current zero-based track and event indices.")]
        [ToolExecution(ToolKind.Edit)]
        public object DeleteEvent(int trackIndex, int eventIndex)
        {
            return VegasToolSupport.Edit("Delete event", () =>
            {
                var item = VegasToolSupport.Event(trackIndex, eventIndex);
                var name = item.Name;
                item.Track.Events.Remove(item);
                return new { deleted = true, name };
            });
        }

        [McpServerTool, Description("Choose an event take by current zero-based index.")]
        [ToolExecution(ToolKind.Edit)]
        public object SetActiveTake(int trackIndex, int eventIndex, int takeIndex)
        {
            return VegasToolSupport.Edit("Set active take", () =>
            {
                var item = VegasToolSupport.Event(trackIndex, eventIndex);
                if (takeIndex < 0 || takeIndex >= item.Takes.Count) throw new ArgumentOutOfRangeException(nameof(takeIndex));
                item.ActiveTake = item.Takes[takeIndex];
                return VegasToolSupport.DescribeEvent(item);
            });
        }

        [McpServerTool, Description("Set an event's fade-in or fade-out length in milliseconds.")]
        [ToolExecution(ToolKind.Edit)]
        public object SetEventFade(int trackIndex, int eventIndex, string side, double lengthMs)
        {
            if (side != "in" && side != "out") throw new ArgumentException("Side must be in or out.");
            return VegasToolSupport.Edit("Set event fade", () =>
            {
                var item = VegasToolSupport.Event(trackIndex, eventIndex);
                if (lengthMs > item.Length.ToMilliseconds()) throw new ArgumentException("Fade exceeds event length.");
                var fade = side == "in" ? item.FadeIn : item.FadeOut;
                fade.Length = VegasToolSupport.Time(lengthMs);
                return VegasToolSupport.DescribeEvent(item);
            });
        }

        [McpServerTool, Description("Set the timeline cursor in milliseconds.")]
        [ToolExecution(ToolKind.External)]
        public object SetCursor(double positionMs)
        {
            VegasContext.Current.Transport.CursorPosition = VegasToolSupport.Time(positionMs);
            return GetTimelineState();
        }

        [McpServerTool, Description("Set selection start and length in milliseconds.")]
        [ToolExecution(ToolKind.External)]
        public object SetSelection(double startMs, double lengthMs)
        {
            var t = VegasContext.Current.Transport;
            t.SelectionStart = VegasToolSupport.Time(startMs);
            t.SelectionLength = VegasToolSupport.Time(lengthMs);
            return GetTimelineState();
        }

        [McpServerTool, Description("Set the timeline loop region and loop playback mode. Times are milliseconds.")]
        [ToolExecution(ToolKind.External)]
        public object SetLoop(double startMs, double lengthMs, bool enabled)
        {
            var t = VegasContext.Current.Transport;
            t.LoopRegionStart = VegasToolSupport.Time(startMs);
            t.LoopRegionLength = VegasToolSupport.Time(lengthMs);
            t.LoopMode = enabled;
            return GetTimelineState();
        }

        [McpServerTool, Description("Start VEGAS timeline playback.")]
        [ToolExecution(ToolKind.External)]
        public object Play()
        {
            VegasContext.Current.Transport.Play();
            return GetTimelineState();
        }

        [McpServerTool, Description("Stop VEGAS timeline playback.")]
        [ToolExecution(ToolKind.External)]
        public object StopPlayback()
        {
            VegasContext.Current.Transport.Stop();
            return GetTimelineState();
        }
    }
}
