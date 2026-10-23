using System.Runtime.CompilerServices;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Remembers whether a scene has unsaved changes, instead of working it out on every ask: for half a second while the
    /// editor is in use, and from a second after the last activity until the next one.
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
        private static long _lastActivityMs;

        /// <summary>How long an answer is reused while the editor is being used, in milliseconds.</summary>
        public const long LifetimeMilliseconds = 500;

        /// <summary>
        /// How long after the last activity the answer is still rechecked. Past it nothing can have changed the scene,
        /// so the answer stands until the next activity: watching a level of a thousand lamps captured it to JSON twice a
        /// second for nothing, a 16 ms, 24 MB hitch each time.
        /// </summary>
        public const long SettleMilliseconds = 1000;

        /// <summary>
        /// Something that may change a scene happened -- a click, a key, a scroll, an undo step. The editor calls this
        /// on input and history changes; the answer is then rechecked for a moment.
        /// </summary>
        public static void Touch(long? nowMilliseconds = null)
        {
            _lastActivityMs = nowMilliseconds ?? Environment.TickCount64;
        }

        /// <summary>
        /// The cached answer for <paramref name="scene"/> when it is fresh, else <paramref name="compute"/>'s
        /// result (which is then cached). The times are for tests; leave them out to use the clock and the editor's
        /// last activity.
        /// </summary>
        public static bool IsDirty(object scene, Func<bool> compute, long? nowMilliseconds = null, long? lastActivityMilliseconds = null)
        {
            var now = nowMilliseconds ?? Environment.TickCount64;
            var activity = lastActivityMilliseconds ?? _lastActivityMs;
            var entry = Entries.GetOrCreateValue(scene);
            if (entry.CheckedAtMs != long.MinValue && now >= entry.CheckedAtMs)
            {
                // Worked out well after the last activity: nothing has happened since that could change the answer.
                if (entry.CheckedAtMs - activity >= SettleMilliseconds)
                {
                    return entry.Dirty;
                }

                if (now - entry.CheckedAtMs < LifetimeMilliseconds)
                {
                    return entry.Dirty;
                }
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
