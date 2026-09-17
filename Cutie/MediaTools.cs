using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Cutie
{
    [McpServerToolType]
    public class MediaTools
    {
        [McpServerTool, Description("List project media with IDs, paths, stream types, and duration in milliseconds.")]
        public object ListMedia()
        {
            return VegasToolSupport.Project.MediaPool.Cast<Media>()
                .Select(DescribeMedia).ToArray();
        }

        [McpServerTool, Description("Get media by its project media ID.")]
        public object GetMedia(uint mediaId)
        {
            var media = VegasToolSupport.FindMedia(mediaId);
            if (media == null) return new { found = false, mediaId };
            return DescribeMedia(media);
        }

        [McpServerTool, Description("Import a local media file into the project media pool.")]
        public object ImportMedia(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
                throw new ArgumentException("An existing absolute file path is required.", nameof(path));
            return VegasToolSupport.Edit("Import media", () =>
                DescribeMedia(VegasToolSupport.Project.MediaPool.AddMedia(Path.GetFullPath(path))));
        }

        [McpServerTool, Description("List selected tracks, events, and project media by their current indices or IDs.")]
        public object GetSelection()
        {
            var project = VegasToolSupport.Project;
            return new
            {
                tracks = project.Tracks.Where(track => track.Selected)
                    .Select(track => track.Index).ToArray(),
                events = project.Tracks.SelectMany(track => track.Events
                    .Where(item => item.Selected)
                    .Select(item => new { trackIndex = track.Index, eventIndex = item.Index }))
                    .ToArray(),
                mediaIds = project.MediaPool.GetSelectedMedia().Select(media => media.MediaID).ToArray()
            };
        }

        private static object DescribeMedia(Media media)
        {
            return new
            {
                mediaId = media.MediaID,
                path = media.FilePath,
                lengthMs = media.Length.ToMilliseconds(),
                offline = media.IsOffline(),
                useCount = media.UseCount,
                streams = media.Streams.Select(stream => new
                {
                    index = stream.Index,
                    type = stream.MediaType.ToString(),
                    lengthMs = stream.Length.ToMilliseconds(),
                    offline = stream.IsOffline
                }).ToArray()
            };
        }
    }
}
