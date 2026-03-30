using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Read-only double-array trie for fast dictionary lookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Binary format (little-endian):
    /// <code>
    /// [4 bytes] stateCount
    /// [4 bytes] codepointBase  (first codepoint in alphabet, e.g. 0x0E00 for Thai)
    /// [4 bytes] codepointRange (number of codepoints in alphabet)
    /// [stateCount * 4 bytes] base array (negative value indicates word-end)
    /// [stateCount * 4 bytes] check array
    /// </code>
    /// </para>
    /// <para>
    /// Transition: for codepoint c, index = c - codepointBase.
    /// Next state = |base[current]| + index. Valid if check[next] == current.
    /// Word-end: base[state] &lt; 0 (actual base = ~base[state]).
    /// </para>
    /// <para>
    /// Thread-safe after construction (read-only).
    /// </para>
    /// </remarks>
    internal sealed class DoubleArrayTrie
    {
        private int[] baseArray;
        private int[] checkArray;
        private int codepointBase;
        private int codepointRange;
        private int stateCount;

        /// <summary>Number of states in the trie.</summary>
        public int StateCount => stateCount;

        /// <summary>Loads the trie from binary data.</summary>
        public void Load(byte[] data)
        {
            if (data == null || data.Length < 12)
                throw new ArgumentException("Invalid trie data: too short.", nameof(data));

            var span = data.AsSpan();
            stateCount = ReadInt32(span, 0);
            codepointBase = ReadInt32(span, 4);
            codepointRange = ReadInt32(span, 8);

            var headerSize = 12;
            var expectedSize = headerSize + stateCount * 8;
            if (data.Length < expectedSize)
                throw new ArgumentException(
                    $"Invalid trie data: expected {expectedSize} bytes, got {data.Length}.", nameof(data));

            baseArray = new int[stateCount];
            checkArray = new int[stateCount];

            var offset = headerSize;
            Buffer.BlockCopy(data, offset, baseArray, 0, stateCount * 4);
            offset += stateCount * 4;
            Buffer.BlockCopy(data, offset, checkArray, 0, stateCount * 4);
        }

        /// <summary>
        /// Attempts to traverse from the given state on the given codepoint.
        /// </summary>
        /// <param name="state">Current state (0 = root).</param>
        /// <param name="codepoint">Input codepoint.</param>
        /// <returns>Next state, or -1 if no transition exists.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Traverse(int state, int codepoint)
        {
            var index = codepoint - codepointBase;
            if ((uint)index >= (uint)codepointRange) return -1;

            var b = baseArray[state];
            if (b < 0) b = ~b;

            var next = b + index;
            if ((uint)next >= (uint)stateCount) return -1;
            if (checkArray[next] != state) return -1;

            return next;
        }

        /// <summary>Returns true if the given state is a word-end state.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsWordEnd(int state)
        {
            return baseArray[state] < 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
        {
            return data[offset]
                 | (data[offset + 1] << 8)
                 | (data[offset + 2] << 16)
                 | (data[offset + 3] << 24);
        }
    }
}
