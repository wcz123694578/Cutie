using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ScriptPortal.Vegas;

namespace Cutie
{
    [McpServerToolType]
    public class MidiTools
    {
        [McpServerTool, Description("Place MIDI notes from one 1-based channel onto an existing VEGAS audio or video track. Audio events are pitch-shifted relative to baseNote; video events rotate source offsets. Times are milliseconds. selection is highest, lowest, or all notes at each onset.")]
        [ToolExecution(ToolKind.Edit)]
        public object PlaceMidiNotes(string midiPath, uint mediaId, int trackIndex, int channel,
            double fromMs, double toMs, int baseNote = 55, string selection = "highest",
            int everyNth = 1, int maxEvents = 200, double maxLengthMs = 220)
        {
            if (string.IsNullOrWhiteSpace(midiPath) || !Path.IsPathRooted(midiPath) || !File.Exists(midiPath))
                throw new ArgumentException("An existing absolute MIDI file path is required.", nameof(midiPath));
            if (channel < 1 || channel > 16) throw new ArgumentOutOfRangeException(nameof(channel));
            if (fromMs < 0 || toMs <= fromMs) throw new ArgumentOutOfRangeException(nameof(toMs));
            if (baseNote < 0 || baseNote > 127) throw new ArgumentOutOfRangeException(nameof(baseNote));
            if (everyNth < 1 || maxEvents < 1 || maxEvents > 1000 || maxLengthMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEvents));
            if (selection != "highest" && selection != "lowest" && selection != "all")
                throw new ArgumentException("Selection must be highest, lowest, or all.", nameof(selection));

            var midi = MidiFile.Read(Path.GetFullPath(midiPath));
            var tempoMap = midi.GetTempoMap();
            var candidates = midi.GetNotes().Where(note => (int)note.Channel + 1 == channel)
                .Select(note => new
                {
                    note,
                    startMs = note.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds,
                    lengthMs = note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds
                })
                .Where(item => item.startMs >= fromMs && item.startMs < toMs)
                .GroupBy(item => item.note.Time)
                .OrderBy(group => group.Key)
                .SelectMany(group => selection == "all" ? group.OrderByDescending(item => item.note.Velocity)
                    : selection == "lowest" ? group.OrderBy(item => item.note.NoteNumber).Take(1)
                    : group.OrderByDescending(item => item.note.NoteNumber).Take(1))
                .Where((item, index) => index % everyNth == 0)
                .Take(maxEvents).ToArray();

            return VegasToolSupport.Edit("Place MIDI notes", () =>
            {
                var track = VegasToolSupport.Track(trackIndex);
                var media = VegasToolSupport.Media(mediaId);
                var stream = track.IsVideo() ? (MediaStream)media.Streams.OfType<VideoStream>().FirstOrDefault()
                    : media.Streams.OfType<AudioStream>().FirstOrDefault();
                if (stream == null) throw new ArgumentException("Media has no stream matching the track type.");
                var placed = new System.Collections.Generic.List<object>();
                for (var i = 0; i < candidates.Length; i++)
                {
                    var item = candidates[i];
                    var duration = Math.Max(33.4, Math.Min(maxLengthMs, item.lengthMs));
                    var offset = track.IsVideo() ? (i % 4) * 180.0 : 0.0;
                    if (offset + duration > stream.Length.ToMilliseconds())
                        duration = Math.Max(33.4, stream.Length.ToMilliseconds() - offset);
                    TrackEvent ev = track.IsVideo()
                        ? (TrackEvent)((VideoTrack)track).AddVideoEvent(VegasToolSupport.Time(item.startMs), VegasToolSupport.Time(duration))
                        : ((AudioTrack)track).AddAudioEvent(VegasToolSupport.Time(item.startMs), VegasToolSupport.Time(duration));
                    var take = ev.AddTake(stream);
                    if (offset > 0) take.Offset = VegasToolSupport.Time(offset);
                    if (ev is AudioEvent audio) audio.PitchSemis = (int)item.note.NoteNumber - baseNote;
                    placed.Add(new { eventIndex = ev.Index, startMs = item.startMs,
                        lengthMs = duration, note = (int)item.note.NoteNumber,
                        velocity = (int)item.note.Velocity,
                        pitchSemis = ev is AudioEvent ? (int?)((int)item.note.NoteNumber - baseNote) : null,
                        sourceOffsetMs = offset });
                }
                return new { trackIndex, channel, count = placed.Count, events = placed.ToArray() };
            });
        }

        [McpServerTool, Description("Analyze a Standard MIDI File without changing the VEGAS project. Returns channel summaries and paged notes with tempo-aware millisecond times. Channels are 1-based.")]
        [ToolExecution(ToolKind.Background)]
        public object AnalyzeMidi(string path, int offset = 0, int limit = 256, int? channel = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
                throw new ArgumentException("An existing absolute MIDI file path is required.", nameof(path));
            if (offset < 0 || limit < 1 || limit > 2000) throw new ArgumentOutOfRangeException(nameof(limit));
            if (channel.HasValue && (channel < 1 || channel > 16)) throw new ArgumentOutOfRangeException(nameof(channel));

            var fullPath = Path.GetFullPath(path);
            var midi = MidiFile.Read(fullPath);
            var tempoMap = midi.GetTempoMap();
            var tracks = midi.GetTrackChunks().ToArray();
            var notes = tracks.SelectMany((track, trackIndex) => track.GetNotes().Select(note => new
            {
                trackIndex,
                channel = (int)note.Channel + 1,
                note = (int)note.NoteNumber,
                velocity = (int)note.Velocity,
                startTick = note.Time,
                lengthTicks = note.Length,
                startMs = note.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds,
                lengthMs = note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds
            })).OrderBy(note => note.startTick).ThenBy(note => note.trackIndex).ToArray();

            var programs = tracks.SelectMany((track, trackIndex) => track.GetTimedEvents()
                .Where(item => item.Event is ProgramChangeEvent)
                .Select(item => new
                {
                    trackIndex,
                    channel = (int)((ProgramChangeEvent)item.Event).Channel + 1,
                    program = (int)((ProgramChangeEvent)item.Event).ProgramNumber,
                    atTick = item.Time,
                    atMs = item.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds
                })).OrderBy(item => item.atTick).ToArray();

            var selected = channel.HasValue ? notes.Where(note => note.channel == channel.Value).ToArray() : notes;
            var channels = notes.GroupBy(note => note.channel).OrderBy(group => group.Key).Select(group => new
            {
                channel = group.Key,
                noteCount = group.Count(),
                firstMs = group.Min(note => note.startMs),
                lastMs = group.Max(note => note.startMs + note.lengthMs),
                minNote = group.Min(note => note.note),
                maxNote = group.Max(note => note.note),
                programs = programs.Where(item => item.channel == group.Key).ToArray()
            }).ToArray();
            return new
            {
                path = fullPath,
                trackCount = tracks.Length,
                noteCount = notes.Length,
                selectedNoteCount = selected.Length,
                offset,
                limit,
                channels,
                notes = selected.Skip(offset).Take(limit).ToArray()
            };
        }
    }
}
