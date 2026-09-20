using System.Runtime.CompilerServices;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Remembers whether a scene has unsaved changes for a moment, instead of working it out on every ask.
    /// <para>
    /// Working it out means capturing the whole scene to JSON and comparing it with what was last saved. The
    /// editor asked on every frame, from two places, and on the sample course that was several milliseconds of
    /// serialising -- about a third of the editor's frame. The answer changes when a person edits something, so
    /// a few times a second is plenty; the "Unsaved changes" label being half a second late is not noticed,
    /// and a frame that is 8ms cheaper is.
    /// </para>
    /// </summary>
    public static class SceneDirtyCache
    {
        private sealed class Entry
        {
            public long CheckedAtMs = long.MinValue;
            public bool Dirty;
        }

        private static readonly ConditionalWeakTable<object, Entry> Entries = new();

        /// <summary>How long an answer is reused, in milliseconds.</summary>
        public const long LifetimeMilliseconds = 500;

        /// <summary>
        /// The cached answer for <paramref name="scene"/> when it is fresh, else <paramref name="compute"/>'s
        /// result (which is then cached). <paramref name="nowMilliseconds"/> is for tests; leave it out to use the clock.
        /// </summary>
        public static bool IsDirty(object scene, Func<bool> compute, long? nowMilliseconds = null)
        {
            var now = nowMilliseconds ?? Environment.TickCount64;
            var entry = Entries.GetOrCreateValue(scene);
            if (entry.CheckedAtMs != long.MinValue && now - entry.CheckedAtMs < LifetimeMilliseconds && now >= entry.CheckedAtMs)
            {
                return entry.Dirty;
            }

            entry.Dirty = compute();
            entry.CheckedAtMs = now;
            return entry.Dirty;
        }

        /// <summary>Forgets the answer for a scene, so the next ask is worked out afresh -- after a save, say.</summary>
        public static void Invalidate(object scene)
        {
            if (Entries.TryGetValue(scene, out var entry))
            {
                entry.CheckedAtMs = long.MinValue;
            }
        }
    }
}
