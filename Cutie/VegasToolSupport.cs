using ScriptPortal.Vegas;
using System;
using System.Linq;

namespace Cutie
{
    internal static class VegasToolSupport
    {
        public static Project Project => VegasContext.Current?.Project
            ?? throw new InvalidOperationException("No active VEGAS project.");

        public static Track Track(int index)
        {
            var tracks = Project.Tracks;
            if (index < 0 || index >= tracks.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Track index is out of range. Call list_tracks again.");
            return tracks[index];
        }

        public static TrackEvent Event(int trackIndex, int eventIndex)
        {
            var events = Track(trackIndex).Events;
            if (eventIndex < 0 || eventIndex >= events.Count)
                throw new ArgumentOutOfRangeException(nameof(eventIndex), "Event index is out of range. Call list_events again.");
            return events[eventIndex];
        }

        public static Media Media(uint mediaId)
        {
            return FindMedia(mediaId)
                ?? throw new ArgumentException("Media ID was not found. Call list_media again.", nameof(mediaId));
        }

        public static Media FindMedia(uint mediaId)
        {
            return Project.MediaPool.Cast<Media>()
                .FirstOrDefault(media => media.MediaID == mediaId);
        }

        public static Timecode Time(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds), "Time must be a nonnegative number of milliseconds.");
            return Timecode.FromMilliseconds(milliseconds);
        }

        // VEGAS 18 OFX SetValueAtTime/GetValueAtTime interpret the numeric Timecode
        // value as a frame index. Passing ordinary milliseconds places keyframes
        // frameRate times too late. Keep the public MCP API in milliseconds.
        public static Timecode OfxTime(double milliseconds)
        {
            Time(milliseconds);
            var frame = Math.Round(milliseconds * Project.Video.FrameRate / 1000.0);
            return Timecode.FromMilliseconds(frame);
        }

        public static T Edit<T>(string label, Func<T> action)
        {
            using (var undo = new UndoBlock(Project, label))
            {
                try
                {
                    return action();
                }
                catch
                {
                    undo.Cancel = true;
                    throw;
                }
            }
        }

        public static object DescribeTrack(Track track)
        {
            return new
            {
                index = track.Index,
                name = track.Name,
                type = track.IsVideo() ? "video" : "audio",
                eventCount = track.Events.Count,
                mute = track.Mute,
                solo = track.Solo,
                selected = track.Selected,
                lengthMs = track.Length.ToMilliseconds()
            };
        }

        public static object DescribeEvent(TrackEvent item)
        {
            return new
            {
                trackIndex = item.Track.Index,
                eventIndex = item.Index,
                name = item.Name,
                type = item.IsVideo() ? "video" : "audio",
                startMs = item.Start.ToMilliseconds(),
                lengthMs = item.Length.ToMilliseconds(),
                endMs = item.End.ToMilliseconds(),
                playbackRate = item.PlaybackRate,
                pitchSemis = item is AudioEvent audio ? (double?)audio.PitchSemis : null,
                mute = item.Mute,
                locked = item.Locked,
                loop = item.Loop,
                selected = item.Selected,
                fadeInMs = item.FadeIn.Length.ToMilliseconds(),
                fadeOutMs = item.FadeOut.Length.ToMilliseconds(),
                activeMediaId = item.ActiveTake?.Media?.MediaID,
                takes = item.Takes.Select(take => new
                {
                    index = take.Index,
                    name = take.Name,
                    mediaId = take.Media?.MediaID,
                    mediaPath = take.MediaPath,
                    active = take.IsActive,
                    offsetMs = take.Offset.ToMilliseconds()
                }).ToArray()
            };
        }
    }
}
