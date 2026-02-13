using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// High-performance dictionary optimized for integer keys using open addressing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses linear probing with power-of-two table sizes for fast lookups.
    /// Designed for hot paths where standard Dictionary overhead is too high.
    /// </para>
    /// <para>
    /// Writes are not thread-safe and require external synchronization.
    /// Reads (TryGetValue, ContainsKey) are safe against concurrent Grow
    /// because mask is derived from the snapshotted entries array length.
    /// Grows automatically at 75% load factor.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The value type.</typeparam>
    internal sealed class FastIntDictionary<T>
    {
        private struct Entry
        {
            public int key;
            public T value;
            public bool hasValue;
        }

        private Entry[] entries;
        private int count;

        public FastIntDictionary(int capacity = 16)
        {
            var size = NextPowerOfTwo(capacity);
            entries = new Entry[size];
        }

        public int Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int key, out T value)
        {
            var e = entries;
            var m = e.Length - 1;
            var idx = key & m;

            while (e[idx].hasValue)
            {
                if (e[idx].key == key)
                {
                    value = e[idx].value;
                    return true;
                }
                idx = (idx + 1) & m;
            }

            value = default;
            return false;
        }

        public T this[int key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (TryGetValue(key, out var val))
                    return val;
                throw new KeyNotFoundException();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => AddOrUpdate(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(int key, T value)
        {
            if (count >= entries.Length * 3 / 4)
                Grow();

            var e = entries;
            var m = e.Length - 1;
            var idx = key & m;

            while (e[idx].hasValue)
            {
                if (e[idx].key == key)
                {
                    e[idx].value = value;
                    return;
                }
                idx = (idx + 1) & m;
            }

            e[idx].key = key;
            e[idx].value = value;
            e[idx].hasValue = true;
            count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(int key)
        {
            var e = entries;
            var m = e.Length - 1;
            var idx = key & m;

            while (e[idx].hasValue)
            {
                if (e[idx].key == key)
                    return true;
                idx = (idx + 1) & m;
            }

            return false;
        }

        public bool Remove(int key)
        {
            var e = entries;
            var m = e.Length - 1;
            var idx = key & m;

            while (e[idx].hasValue)
            {
                if (e[idx].key == key)
                {
                    count--;
                    var empty = idx;

                    while (true)
                    {
                        idx = (idx + 1) & m;

                        if (!e[idx].hasValue)
                        {
                            e[empty].hasValue = false;
                            e[empty].value = default;
                            return true;
                        }

                        var ideal = e[idx].key & m;

                        if ((empty <= idx) ? (ideal <= empty || ideal > idx) : (ideal <= empty && ideal > idx))
                        {
                            e[empty] = e[idx];
                            empty = idx;
                        }
                    }
                }
                idx = (idx + 1) & m;
            }

            return false;
        }

        public void Clear()
        {
            Array.Clear(entries, 0, entries.Length);
            count = 0;
        }

        public void ClearFast()
        {
            if (count == 0) return;
            var e = entries;
            for (var i = 0; i < e.Length; i++)
                e[i].hasValue = false;
            count = 0;
        }

        private void Grow()
        {
            var oldEntries = entries;
            var newSize = oldEntries.Length * 2;
            var newEntries = new Entry[newSize];
            var newMask = newSize - 1;

            for (var i = 0; i < oldEntries.Length; i++)
            {
                if (oldEntries[i].hasValue)
                {
                    var idx = oldEntries[i].key & newMask;
                    while (newEntries[idx].hasValue)
                        idx = (idx + 1) & newMask;
                    newEntries[idx] = oldEntries[i];
                }
            }

            entries = newEntries;
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }

        public Enumerator GetEnumerator() => new(this);

        public struct Enumerator
        {
            private readonly Entry[] entries;
            private int index;

            internal Enumerator(FastIntDictionary<T> dict)
            {
                entries = dict.entries;
                index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (++index < entries.Length)
                    if (entries[index].hasValue)
                        return true;
                return false;
            }

            public KeyValuePair<int, T> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new(entries[index].key, entries[index].value);
            }
        }
    }

}
