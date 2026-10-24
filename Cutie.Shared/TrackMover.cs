using ScriptPortal.Vegas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cutie.Shared
{
    public static class TrackMover
    {
        private static NativeTrackMove _native;

        /// <summary>Moves a contiguous block to its final zero-based starting index.</summary>
        /// <remarks>
        /// Call on the VEGAS UI thread inside the caller's undo scope.
        /// Existing compositing parents and nesting levels are preserved.
        /// Include complete compositing subtrees and track groups in the block.
        /// </remarks>
        public static void Move(Project project, int first, int count, int finalIndex)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            var total = project.Tracks.Count;
            if (first < 0 || first >= total) throw new ArgumentOutOfRangeException(nameof(first));
            if (count < 1 || count > total - first) throw new ArgumentOutOfRangeException(nameof(count));
            if (finalIndex < 0 || finalIndex > total - count) throw new ArgumentOutOfRangeException(nameof(finalIndex));
            if (first == finalIndex) return;

            var tracks = new Track[total];
            var selected = new bool[total];
            for (var i = 0; i < total; i++)
            {
                tracks[i] = project.Tracks[i];
                selected[i] = tracks[i].Selected;
            }

            var levels = tracks.OfType<VideoTrack>()
                .ToDictionary(track => track, track => track.CompositeNestingLevel);
            var reordered = tracks.ToList();
            var block = reordered.GetRange(first, count);
            reordered.RemoveRange(first, count);
            reordered.InsertRange(finalIndex, block);
            var parents = CompositeParents(tracks, levels);
            var newParents = CompositeParents(reordered, levels);
            if (parents.Any(pair => !ReferenceEquals(pair.Value, newParents[pair.Key])))
                throw new InvalidOperationException("Move would change compositing parents. Include the complete subtree and choose a destination within the same parent.");

            var native = _native ?? (_native = NativeTrackMove.Resolve(project.Tracks[first]));
            var gap = finalIndex > first ? finalIndex + count : finalIndex;
            try
            {
                for (var i = 0; i < total; i++)
                    tracks[i].Selected = i >= first && i < first + count;

                native.Move(gap);
                // Native dragging can promote a nested parent; restore levels before selection.
                foreach (var track in reordered.OfType<VideoTrack>())
                    if (track.CompositeNestingLevel != levels[track])
                        track.CompositeNestingLevel = levels[track];
            }
            finally
            {
                for (var i = 0; i < total; i++)
                    tracks[i].Selected = selected[i];
            }
        }

        private static Dictionary<VideoTrack, VideoTrack> CompositeParents(
            IEnumerable<Track> tracks, Dictionary<VideoTrack, int> levels)
        {
            var result = new Dictionary<VideoTrack, VideoTrack>();
            var ancestors = new List<VideoTrack>();
            foreach (var track in tracks)
            {
                if (!(track is VideoTrack video))
                {
                    ancestors.Clear();
                    continue;
                }
                var level = levels[video];
                if (level < 0 || level > ancestors.Count)
                    throw new InvalidOperationException("Move would split a compositing subtree.");
                if (ancestors.Count > level)
                    ancestors.RemoveRange(level, ancestors.Count - level);
                result.Add(video, level == 0 ? null : ancestors[level - 1]);
                ancestors.Add(video);
            }
            return result;
        }
    }
}
