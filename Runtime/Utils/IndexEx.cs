using System;

namespace LightSide
{
    /// <summary>
    /// Extension methods for parsing <see cref="Index"/> from string representation.
    /// </summary>
    /// <remarks>
    /// Supports C# index syntax: "0" for start-based, "^1" for end-based indices.
    /// Used internally for markup parameter parsing.
    /// </remarks>
    /// <seealso cref="RangeEx"/>
    public static class IndexEx
    {
        /// <summary>
        /// Parses a string into a <see cref="Index"/>.
        /// </summary>
        /// <param name="text">String to parse (e.g., "0", "5", "^1", "^3").</param>
        /// <param name="index">The parsed index if successful.</param>
        /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
        public static bool TryParse(string text, out Index index)
        {
            index = default;

            if (string.IsNullOrEmpty(text))
                return false;

            var isFromEnd = text[0] == '^';
            var valueStart = isFromEnd ? 1 : 0;

            if (int.TryParse(text[valueStart..], out var value) && ((isFromEnd && value > 0) || (!isFromEnd && value > -1)))
            {
                index = new Index(value, isFromEnd);
                return true;
            }

            return false;
        }
    }
}
