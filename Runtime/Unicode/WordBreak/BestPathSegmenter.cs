using System;

namespace LightSide
{
    /// <summary>
    /// Dictionary-based word segmenter using best-path (maximal matching) algorithm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the same approach as ICU's Thai word breaker: builds a DAG of all possible
    /// dictionary words at each position, then finds the path with the fewest words
    /// (= most natural segmentation) via dynamic programming.
    /// </para>
    /// <para>
    /// Generic: works for any SA-class script. Only the dictionary (trie) differs.
    /// </para>
    /// </remarks>
    internal sealed class BestPathSegmenter : IWordSegmenter
    {
        private readonly DoubleArrayTrie trie;
        private readonly UnicodeScript script;

        public UnicodeScript Script => script;

        public BestPathSegmenter(DoubleArrayTrie trie, UnicodeScript script)
        {
            this.trie = trie ?? throw new ArgumentNullException(nameof(trie));
            this.script = script;
        }

        /// <inheritdoc/>
        public void Segment(ReadOnlySpan<int> codepoints, int start, int length, Span<LineBreakType> breaks)
        {
            var bestCost = UniTextArrayPool<int>.Rent(length + 1);
            var bestPrev = UniTextArrayPool<int>.Rent(length + 1);

            try
            {
                SegmentCore(codepoints, start, length, breaks, bestCost, bestPrev);
            }
            finally
            {
                UniTextArrayPool<int>.Return(bestCost);
                UniTextArrayPool<int>.Return(bestPrev);
            }
        }

        private void SegmentCore(
            ReadOnlySpan<int> codepoints, int start, int length,
            Span<LineBreakType> breaks, int[] bestCost, int[] bestPrev)
        {
            const int infinity = int.MaxValue / 2;

            bestCost[0] = 0;
            bestPrev[0] = 0;
            for (var i = 1; i <= length; i++)
            {
                bestCost[i] = infinity;
                bestPrev[i] = -1;
            }

            for (var i = 0; i < length; i++)
            {
                if (bestCost[i] == infinity) continue;

                var cost = bestCost[i] + 1;

                var state = 0;
                for (var j = i; j < length; j++)
                {
                    state = trie.Traverse(state, codepoints[start + j]);
                    if (state < 0) break;

                    if (trie.IsWordEnd(state))
                    {
                        var end = j + 1;
                        if (cost < bestCost[end])
                        {
                            bestCost[end] = cost;
                            bestPrev[end] = i;
                        }
                    }
                }

                if (cost < bestCost[i + 1])
                {
                    bestCost[i + 1] = cost;
                    bestPrev[i + 1] = i;
                }
            }

            if (bestCost[length] == infinity)
                return;

            var pos = length;
            while (pos > 0)
            {
                var prev = bestPrev[pos];
                if (prev < 0) break;

                if (pos < length)
                {
                    var breakIdx = start + pos;
                    if (breaks[breakIdx] == LineBreakType.None)
                        breaks[breakIdx] = LineBreakType.Optional;
                }

                pos = prev;
            }
        }
    }
}
