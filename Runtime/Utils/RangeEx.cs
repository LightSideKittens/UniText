using System;

namespace LightSide
{
    /// <summary>
    /// Extension methods for parsing <see cref="Range"/> from string representation.
    /// </summary>
    /// <remarks>
    /// Supports C# range syntax: "0..5", "..5", "2..", "^3..^1".
    /// Used internally for markup parameter parsing.
    /// </remarks>
    /// <seealso cref="IndexEx"/>
    public static class RangeEx
    {
        /// <summary>
        /// Parses a string into a <see cref="Range"/>.
        /// </summary>
        /// <param name="text">String to parse (e.g., "0..5", "..10", "5..", "^3..^1").</param>
        /// <param name="range">The parsed range if successful.</param>
        /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
        public static bool TryParse(string text, out Range range)
        {
            range = default;

            if (string.IsNullOrEmpty(text))
                return false;

            var rangeSeparatorIndex = text.IndexOf("..");

            if (rangeSeparatorIndex == -1)
            {
                return false;
            }

            var startText = text[..rangeSeparatorIndex];
            var endText = text[(rangeSeparatorIndex + 2)..];

            Index start;

            if (string.IsNullOrEmpty(startText))
            {
                start = Index.Start;
            }
            else if(!IndexEx.TryParse(startText, out start))
            {
                return false;
            }

            Index end;

            if (string.IsNullOrEmpty(endText))
            {
                end = Index.End;
            }
            else if(!IndexEx.TryParse(endText, out end))
            {
                return false;
            }

            range = new Range(start, end);
            return true;
        }

        /// <summary>
        /// Converts a <see cref="Range"/> to absolute start/end indices for a given length.
        /// </summary>
        /// <param name="count">Total length of the collection.</param>
        /// <param name="range">The range to convert.</param>
        /// <returns>A tuple of (start, end) absolute indices.</returns>
        public static (int start, int end) Range(int count, Range range)
        {
            var start = range.Start.GetOffset(count);
            var end = range.End.GetOffset(count);

            return (start, end);
        }
    }
}
