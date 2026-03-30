using System;

namespace LightSide
{
    /// <summary>
    /// Interface for script-specific word segmenters that inject break opportunities
    /// into SA-class (Complex Context Dependent) text runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations segment a contiguous run of codepoints belonging to a single script
    /// and write <see cref="LineBreakType.Optional"/> at word boundaries.
    /// </para>
    /// <para>
    /// Register via <see cref="WordSegmentationProcessor.Register"/>.
    /// Registering a segmenter for a script that already has one replaces it.
    /// </para>
    /// </remarks>
    public interface IWordSegmenter
    {
        /// <summary>The Unicode script this segmenter handles.</summary>
        UnicodeScript Script { get; }

        /// <summary>
        /// Segments a span of codepoints and injects Optional break opportunities at word boundaries.
        /// </summary>
        /// <param name="codepoints">The full codepoint array of the text.</param>
        /// <param name="start">Start index of the SA run within codepoints.</param>
        /// <param name="length">Length of the SA run.</param>
        /// <param name="breaks">
        /// Break opportunities array to modify (length = codepoints.Length + 1).
        /// breaks[i+1] represents the break opportunity between codepoints[i] and codepoints[i+1].
        /// Must never downgrade an existing Optional or Mandatory break to None.
        /// </param>
        void Segment(ReadOnlySpan<int> codepoints, int start, int length, Span<LineBreakType> breaks);
    }
}
