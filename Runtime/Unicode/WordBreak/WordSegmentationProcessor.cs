using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Post-processes UAX#14 break opportunities for SA-class scripts
    /// using registered word segmenters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scans for contiguous runs of the same SA-class script and dispatches
    /// to the registered <see cref="IWordSegmenter"/> for that script.
    /// </para>
    /// <para>
    /// Call <see cref="Process"/> after <see cref="LineBreakAlgorithm.GetBreakOpportunities"/>
    /// and after script analysis to inject word boundary break opportunities.
    /// </para>
    /// </remarks>
    internal sealed class WordSegmentationProcessor
    {
        private readonly IWordSegmenter[] segmenters = new IWordSegmenter[256];
        private int registeredCount;

        /// <summary>Returns true if any segmenters are registered.</summary>
        public bool HasSegmenters
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => registeredCount > 0;
        }

        /// <summary>Registers a segmenter for its target script. Replaces any existing one.</summary>
        public void Register(IWordSegmenter segmenter)
        {
            if (segmenter == null) throw new ArgumentNullException(nameof(segmenter));
            var idx = (int)segmenter.Script;
            if (segmenters[idx] == null) registeredCount++;
            segmenters[idx] = segmenter;
        }

        /// <summary>Unregisters the segmenter for a specific script.</summary>
        public void Unregister(UnicodeScript script)
        {
            var idx = (int)script;
            if (segmenters[idx] != null)
            {
                segmenters[idx] = null;
                registeredCount--;
            }
        }

        /// <summary>Removes all registered segmenters.</summary>
        public void Clear()
        {
            Array.Clear(segmenters, 0, segmenters.Length);
            registeredCount = 0;
        }

        /// <summary>
        /// Scans for contiguous SA-class script runs and dispatches to registered segmenters.
        /// </summary>
        /// <param name="codepoints">Codepoint array.</param>
        /// <param name="scripts">Per-codepoint script array (from ScriptAnalyzer).</param>
        /// <param name="breaks">Break opportunities array (length = codepoints.Length + 1).</param>
        public void Process(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<UnicodeScript> scripts,
            Span<LineBreakType> breaks)
        {
            var length = codepoints.Length;
            if (length == 0) return;

            var i = 0;
            while (i < length)
            {
                var script = scripts[i];
                var segmenter = segmenters[(int)script];

                if (segmenter == null)
                {
                    i++;
                    continue;
                }

                var runStart = i;
                i++;
                while (i < length && scripts[i] == script)
                    i++;

                var runLength = i - runStart;
                if (runLength > 1) segmenter.Segment(codepoints, runStart, runLength, breaks);
            }
        }
    }
}
