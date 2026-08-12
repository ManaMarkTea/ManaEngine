using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using System.Runtime.CompilerServices;

namespace Mana.Code
{
    // Which body handles belong to character capsules, so the narrow phase can give their contacts
    // different material properties from everything else (see DemoNarrowPhaseCallbacks -- wall
    // contacts get zero friction so characters slide down instead of clinging).
    //
    // Reads happen on narrow-phase worker threads. The backing array is never mutated in place: every
    // register/unregister publishes a whole new array, so a reader either sees the old snapshot or the
    // new one and never a torn state. No lock needed on the read path, which is the hot one.
    public sealed class ManaCharacterRegistry
    {
        private int[] handles = Array.Empty<int>();
        private readonly object writeLock = new();

        public void Register(BodyHandle handle)
        {
            lock (writeLock)
            {
                if (Array.IndexOf(handles, handle.Value) >= 0)
                {
                    return;
                }

                var updated = new int[handles.Length + 1];
                Array.Copy(handles, updated, handles.Length);
                updated[^1] = handle.Value;
                handles = updated;
            }
        }

        public void Unregister(BodyHandle handle)
        {
            lock (writeLock)
            {
                var index = Array.IndexOf(handles, handle.Value);
                if (index < 0)
                {
                    return;
                }

                var updated = new int[handles.Length - 1];
                Array.Copy(handles, updated, index);
                Array.Copy(handles, index + 1, updated, index, handles.Length - index - 1);
                handles = updated;
            }
        }

        // Linear scan on purpose: a scene has single-digit characters, so this beats a hash lookup and
        // needs no synchronisation.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(CollidableReference collidable)
        {
            if (collidable.Mobility == CollidableMobility.Static)
            {
                return false;
            }

            var snapshot = handles;
            var value = collidable.BodyHandle.Value;
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Involves(CollidablePair pair)
        {
            return Contains(pair.A) || Contains(pair.B);
        }
    }
}
