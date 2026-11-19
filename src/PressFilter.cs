using System.Collections.Generic;

namespace CalibrationFixer
{
    // Generic so tests can run without the game's key type
    public sealed class PressFilter<TKey>
    {
        readonly HashSet<TKey> held = new HashSet<TKey>();

        public readonly Queue<long> Pending = new Queue<long>();

        public void Clear()
        {
            held.Clear();
            Pending.Clear();
        }

        public void Add(TKey key, bool pressed, long tick, bool collecting, long ignoreBefore)
        {
            if (!pressed) held.Remove(key);
            else if (held.Add(key) && collecting && tick > ignoreBefore) Pending.Enqueue(tick);
        }
    }
}
