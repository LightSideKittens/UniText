using System;

namespace LightSide
{
    /// <summary>
    /// Interface for custom text parsing rules that identify modifier application ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parse rules scan text for markup (XML tags, Markdown, custom markers) and produce
    /// <see cref="ParsedRange"/> entries that specify where modifiers should be applied.
    /// </para>
    /// <para>
    /// Rules are matched in priority order (highest first). Use higher priority for explicit
    /// markup rules (tags, Markdown) and lower priority for auto-detection rules (raw URLs).
    /// </para>
    /// </remarks>
    /// <seealso cref="TagParseRule"/>
    /// <seealso cref="AttributeParser"/>
    public interface IParseRule
    {
        /// <summary>
        /// Gets the matching priority. Higher values are matched first.
        /// Default is 0. Use positive values for explicit markup, negative for auto-detection.
        /// </summary>
        int Priority => 0;

        /// <summary>
        /// Attempts to match a pattern starting at the specified index.
        /// </summary>
        /// <param name="text">The text to scan.</param>
        /// <param name="index">Starting character index.</param>
        /// <param name="results">List to add parsed ranges to.</param>
        /// <returns>Index after the match, or same index if no match.</returns>
        int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results);

        /// <summary>Called after parsing completes to finalize any pending ranges (e.g., unclosed tags).</summary>
        void Finalize(ReadOnlySpan<char> text, PooledList<ParsedRange> results) { }

        /// <summary>Resets the rule state for a new parse operation.</summary>
        void Reset() { }
    }
}
