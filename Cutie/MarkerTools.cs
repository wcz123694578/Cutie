using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.ComponentModel;
using System.Linq;

namespace Cutie
{
    [McpServerToolType]
    public class MarkerTools
    {
        [McpServerTool, Description("List project timeline markers and regions. Times are milliseconds.")]
        public object ListMarkers()
        {
            var project = VegasToolSupport.Project;
            return new
            {
                markers = project.Markers.Select((marker, index) => new
                {
                    index, number = marker.Index, positionMs = marker.Position.ToMilliseconds(), label = marker.Label
                }).ToArray(),
                regions = project.Regions.Select((region, index) => new
                {
                    index, number = region.Index, positionMs = region.Position.ToMilliseconds(),
                    lengthMs = region.Length.ToMilliseconds(), label = region.Label
                }).ToArray()
            };
        }

        [McpServerTool, Description("Add a timeline marker at a position in milliseconds.")]
        public object AddMarker(double positionMs, string label = null)
        {
            return VegasToolSupport.Edit("Add marker", () =>
            {
                var marker = new Marker(VegasToolSupport.Time(positionMs), label ?? "");
                VegasToolSupport.Project.Markers.Add(marker);
                return new { index = VegasToolSupport.Project.Markers.IndexOf(marker), number = marker.Index,
                    positionMs = marker.Position.ToMilliseconds(), label = marker.Label };
            });
        }

        [McpServerTool, Description("Update a marker at its current zero-based index.")]
        public object UpdateMarker(int index, double? positionMs = null, string label = null)
        {
            return VegasToolSupport.Edit("Update marker", () =>
            {
                var marker = GetMarker(index);
                if (positionMs.HasValue) marker.Position = VegasToolSupport.Time(positionMs.Value);
                if (label != null) marker.Label = label;
                return new { index = VegasToolSupport.Project.Markers.IndexOf(marker), number = marker.Index,
                    positionMs = marker.Position.ToMilliseconds(), label = marker.Label };
            });
        }

        [McpServerTool, Description("Delete a marker at its current zero-based index.")]
        public object DeleteMarker(int index)
        {
            return VegasToolSupport.Edit("Delete marker", () =>
            {
                VegasToolSupport.Project.Markers.Remove(GetMarker(index));
                return new { deleted = true };
            });
        }

        [McpServerTool, Description("Add a timeline region. Start and length are milliseconds.")]
        public object AddRegion(double startMs, double lengthMs, string label = null)
        {
            if (lengthMs <= 0) throw new ArgumentException("Length must be positive.");
            return VegasToolSupport.Edit("Add region", () =>
            {
                var region = new Region(VegasToolSupport.Time(startMs), VegasToolSupport.Time(lengthMs), label ?? "");
                VegasToolSupport.Project.Regions.Add(region);
                return new { index = VegasToolSupport.Project.Regions.IndexOf(region), number = region.Index,
                    positionMs = region.Position.ToMilliseconds(),
                    lengthMs = region.Length.ToMilliseconds(), label = region.Label };
            });
        }

        [McpServerTool, Description("Update a region at its current zero-based index. Times are milliseconds.")]
        public object UpdateRegion(int index, double? startMs = null, double? lengthMs = null, string label = null)
        {
            if (lengthMs.HasValue && lengthMs.Value <= 0) throw new ArgumentException("Length must be positive.");
            return VegasToolSupport.Edit("Update region", () =>
            {
                var region = GetRegion(index);
                if (startMs.HasValue) region.Position = VegasToolSupport.Time(startMs.Value);
                if (lengthMs.HasValue) region.Length = VegasToolSupport.Time(lengthMs.Value);
                if (label != null) region.Label = label;
                return new { index = VegasToolSupport.Project.Regions.IndexOf(region), number = region.Index,
                    positionMs = region.Position.ToMilliseconds(),
                    lengthMs = region.Length.ToMilliseconds(), label = region.Label };
            });
        }

        [McpServerTool, Description("Delete a region at its current zero-based index.")]
        public object DeleteRegion(int index)
        {
            return VegasToolSupport.Edit("Delete region", () =>
            {
                VegasToolSupport.Project.Regions.Remove(GetRegion(index));
                return new { deleted = true };
            });
        }

        private static Marker GetMarker(int index)
        {
            var items = VegasToolSupport.Project.Markers;
            if (index < 0 || index >= items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return items[index];
        }

        private static Region GetRegion(int index)
        {
            var items = VegasToolSupport.Project.Regions;
            if (index < 0 || index >= items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return items[index];
        }
    }
}
