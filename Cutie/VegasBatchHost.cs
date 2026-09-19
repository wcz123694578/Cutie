using Cutie.Tools;
using Newtonsoft.Json.Linq;
using ScriptPortal.Vegas;
using System;
using System.Linq;

namespace Cutie
{
    internal sealed class VegasBatchHost : IBatchHost
    {
        private sealed class Handle
        {
            internal Project Project;
            internal Track Track;
            internal TrackEvent Event;
            internal Effect Effect;
            internal Marker Marker;
            internal Region Region;
            internal string Kind;
        }
        public IDisposable BeginUndo(string label) => VegasToolSupport.BeginBatchUndo(label);
        public object Capture(string kind, JObject selector)
        {
            var handle = new Handle { Project = VegasToolSupport.Project, Kind = kind };
            if (kind == "marker") handle.Marker = handle.Project.Markers[(int)selector["index"]];
            else if (kind == "region") handle.Region = handle.Project.Regions[(int)selector["index"]];
            else
            {
                handle.Track = VegasToolSupport.Track((int)selector["trackIndex"]);
                if (kind == "event" || kind == "effect" && (string)selector["targetType"] == "event")
                    handle.Event = VegasToolSupport.Event(handle.Track.Index, (int)selector["eventIndex"]);
                if (kind == "effect")
                    handle.Effect = EffectTools.ResolveEffects((string)selector["targetType"], handle.Track.Index,
                        handle.Event?.Index ?? -1)[(int)selector["effectIndex"]];
            }
            return handle;
        }
        public JToken ResolveHandle(object value, string property)
        {
            var h = (Handle)value;
            if (!Equals(h.Project, VegasToolSupport.Project)) throw new InvalidOperationException("Handle belongs to a previous project.");
            var trackIndex = h.Track == null ? -1 : h.Project.Tracks.IndexOf(h.Track);
            if (h.Track != null && trackIndex < 0) throw new InvalidOperationException("Referenced track was deleted.");
            var eventIndex = h.Event == null ? -1 : h.Track.Events.IndexOf(h.Event);
            if (h.Event != null && eventIndex < 0) throw new InvalidOperationException("Referenced event was deleted or moved to another track.");
            var effectIndex = h.Effect == null ? -1 : EffectTools.ResolveEffects(h.Event == null ? "track" : "event", trackIndex, eventIndex).IndexOf(h.Effect);
            if (h.Effect != null && effectIndex < 0) throw new InvalidOperationException("Referenced effect was removed.");
            var index = h.Kind == "marker" ? h.Project.Markers.IndexOf(h.Marker)
                : h.Kind == "region" ? h.Project.Regions.IndexOf(h.Region) : -1;
            if ((h.Kind == "marker" || h.Kind == "region") && index < 0) throw new InvalidOperationException("Referenced marker or region was deleted.");
            switch (property)
            {
                case "trackIndex": if (trackIndex >= 0) return new JValue(trackIndex); break;
                case "eventIndex": if (eventIndex >= 0) return new JValue(eventIndex); break;
                case "effectIndex": if (effectIndex >= 0) return new JValue(effectIndex); break;
                case "targetType": if (h.Effect != null) return new JValue(h.Event == null ? "track" : "event"); break;
                case "index": if (index >= 0) return new JValue(index); break;
            }
            throw new ArgumentException("Property '" + property + "' is not available on " + h.Kind + " handles.");
        }
    }
}
