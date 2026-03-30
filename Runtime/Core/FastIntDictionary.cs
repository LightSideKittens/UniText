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
    /// Uses Struct-of-Arrays layout: keys and values are stored in separate arrays.
    /// Probing only touches the compact key array (4 bytes per slot), loading the value
    /// array only on key match. This dramatically reduces cache pressure for large value types.
    /// </para>
    /// <para>
    /// Key <c>-1</c> (0xFFFFFFFF) is reserved as the empty-slot sentinel and cannot be stored.
    /// Internally keys are stored as <c>key + 1</c> so that key <c>0</c> maps to stored value <c>1</c>
    /// and the stored value <c>0</c> unambiguously marks an empty slot.
    /// </para>
    /// <para>
    /// Writes are not thread-safe and require external synchronization.
    /// Reads (TryGetValue, ContainsKey) are safe against concurrent Grow
    /// because mask is derived from the snapshotted keys array length.
    /// Grows automatically at 75% load factor.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The value type.</typeparam>
    internal sealed class FastIntDictionary<T>
    {
        private int[] storedKeys;
        private T[] values;
        private int count;
        private int growThreshold;

        public FastIntDictionary(int capacity = 16)
        {
            var size = NextPowerOfTwo(capacity * 4 / 3 + 1);
            storedKeys = new int[size];
            values = new T[size];
            growThreshold = size * 3 / 4;
        }

        public int Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int key, out T value)
        {
            var k = storedKeys;
            var m = k.Length - 1;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    value = values[idx];
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
            if (count >= growThreshold)
                Grow();

            var k = storedKeys;
            var m = k.Length - 1;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    values[idx] = value;
                    return;
                }
                idx = (idx + 1) & m;
            }

            k[idx] = sk;
            values[idx] = value;
            count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(int key)
        {
            var k = storedKeys;
            var m = k.Length - 1;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                    return true;
                idx = (idx + 1) & m;
            }

            return false;
        }

        public bool Remove(int key)
        {
            var k = storedKeys;
            var v = values;
            var m = k.Length - 1;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    count--;
                    var empty = idx;

                    while (true)
                    {
                        idx = (idx + 1) & m;

                        if (k[idx] == 0)
                        {
                            k[empty] = 0;
                            v[empty] = default;
                            return true;
                        }

                        var ideal = (k[idx] - 1) & m;

                        if ((empty <= idx) ? (ideal <= empty || ideal > idx) : (ideal <= empty && ideal > idx))
                        {
                            k[empty] = k[idx];
                            v[empty] = v[idx];
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
            Array.Clear(storedKeys, 0, storedKeys.Length);
            Array.Clear(values, 0, values.Length);
            count = 0;
        }

        public void ClearFast()
        {
            if (count == 0) return;
            Array.Clear(storedKeys, 0, storedKeys.Length);
            count = 0;
        }

        private void Grow()
        {
            var oldKeys = storedKeys;
            var oldValues = values;
            var newSize = oldKeys.Length * 2;
            var newKeys = new int[newSize];
            var newValues = new T[newSize];
            var newMask = newSize - 1;

            for (var i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] != 0)
                {
                    var idx = (oldKeys[i] - 1) & newMask;
                    while (newKeys[idx] != 0)
                        idx = (idx + 1) & newMask;
                    newKeys[idx] = oldKeys[i];
                    newValues[idx] = oldValues[i];
                }
            }

            storedKeys = newKeys;
            values = newValues;
            growThreshold = newSize * 3 / 4;
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
            private readonly int[] storedKeys;
            private readonly T[] values;
            private int index;
            private int remaining;

            internal Enumerator(FastIntDictionary<T> dict)
            {
                storedKeys = dict.storedKeys;
                values = dict.values;
                index = -1;
                remaining = dict.count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (remaining <= 0) return false;
                while (++index < storedKeys.Length)
                {
                    if (storedKeys[index] != 0)
                    {
                        remaining--;
                        return true;
                    }
                }
                return false;
            }

            public KeyValuePair<int, T> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new(storedKeys[index] - 1, values[index]);
            }
        }
    }

    /// <summary>
    /// High-performance dictionary optimized for long keys using open addressing with Fibonacci hashing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses Struct-of-Arrays layout: keys and values are stored in separate arrays.
    /// Probing only touches the compact key array (8 bytes per slot), loading the value
    /// array only on key match. This dramatically reduces cache pressure for large value types.
    /// </para>
    /// <para>
    /// Key <c>-1</c> (0xFFFFFFFFFFFFFFFF) is reserved as the empty-slot sentinel and cannot be stored.
    /// Internally keys are stored as <c>key + 1</c> so that key <c>0</c> maps to stored value <c>1</c>
    /// and the stored value <c>0</c> unambiguously marks an empty slot.
    /// </para>
    /// <inheritdoc cref="FastIntDictionary{T}"/>
    /// </remarks>
    internal sealed class FastLongDictionary<T>
    {
        private long[] storedKeys;
        private T[] values;
        private int count;
        private int growThreshold;

        public FastLongDictionary(int capacity = 16)
        {
            var size = NextPowerOfTwo(capacity * 4 / 3 + 1);
            storedKeys = new long[size];
            values = new T[size];
            growThreshold = size * 3 / 4;
        }

        public int Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(long key, int mask) =>
            (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32) & mask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(long key, out T value)
        {
            var k = storedKeys;
            var m = k.Length - 1;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    value = values[idx];
                    return true;
                }
                idx = (idx + 1) & m;
            }

            value = default;
            return false;
        }

        public T this[long key]
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
        public void AddOrUpdate(long key, T value)
        {
            if (count >= growThreshold)
                Grow();

            var k = storedKeys;
            var m = k.Length - 1;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    values[idx] = value;
                    return;
                }
                idx = (idx + 1) & m;
            }

            k[idx] = sk;
            values[idx] = value;
            count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(long key, T value)
        {
            if (count >= growThreshold)
                Grow();

            var k = storedKeys;
            var m = k.Length - 1;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                    return false;
                idx = (idx + 1) & m;
            }

            k[idx] = sk;
            values[idx] = value;
            count++;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(long key)
        {
            var k = storedKeys;
            var m = k.Length - 1;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                    return true;
                idx = (idx + 1) & m;
            }

            return false;
        }

        public bool Remove(long key)
        {
            var k = storedKeys;
            var v = values;
            var m = k.Length - 1;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    count--;
                    var empty = idx;

                    while (true)
                    {
                        idx = (idx + 1) & m;

                        if (k[idx] == 0)
                        {
                            k[empty] = 0;
                            v[empty] = default;
                            return true;
                        }

                        var ideal = Hash(k[idx] - 1, m);

                        if ((empty <= idx) ? (ideal <= empty || ideal > idx) : (ideal <= empty && ideal > idx))
                        {
                            k[empty] = k[idx];
                            v[empty] = v[idx];
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
            Array.Clear(storedKeys, 0, storedKeys.Length);
            Array.Clear(values, 0, values.Length);
            count = 0;
        }

        public void ClearFast()
        {
            if (count == 0) return;
            Array.Clear(storedKeys, 0, storedKeys.Length);
            count = 0;
        }

        private void Grow()
        {
            var oldKeys = storedKeys;
            var oldValues = values;
            var newSize = oldKeys.Length * 2;
            var newKeys = new long[newSize];
            var newValues = new T[newSize];
            var newMask = newSize - 1;

            for (var i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] != 0)
                {
                    var idx = Hash(oldKeys[i] - 1, newMask);
                    while (newKeys[idx] != 0)
                        idx = (idx + 1) & newMask;
                    newKeys[idx] = oldKeys[i];
                    newValues[idx] = oldValues[i];
                }
            }

            storedKeys = newKeys;
            values = newValues;
            growThreshold = newSize * 3 / 4;
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
            private readonly long[] storedKeys;
            private readonly T[] values;
            private int index;
            private int remaining;

            internal Enumerator(FastLongDictionary<T> dict)
            {
                storedKeys = dict.storedKeys;
                values = dict.values;
                index = -1;
                remaining = dict.count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (remaining <= 0) return false;
                while (++index < storedKeys.Length)
                {
                    if (storedKeys[index] != 0)
                    {
                        remaining--;
                        return true;
                    }
                }
                return false;
            }

            public KeyValuePair<long, T> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new(storedKeys[index] - 1, values[index]);
            }
        }
    }
}
