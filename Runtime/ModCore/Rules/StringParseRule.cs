using System;

namespace LightSide
{
    /// <summary>Matches literal string patterns and optionally replaces them.</summary>
    /// <remarks>
    /// Useful for custom shortcodes, emoji aliases, or text substitutions.
    /// Example: pattern ":)" with replacement "😊".
    /// </remarks>
    [Serializable]
    [TypeGroup("Utility", 3)]
    public sealed class StringParseRule : IParseRule
    {
        /// <summary>String patterns to match (case-sensitive).</summary>
        [UnityEngine.Tooltip("String patterns to match (case-sensitive).")]
        [EscapeTextArea(1, 3)]
        public string[] patterns;

        /// <summary>Whether to replace matched patterns with a custom string.</summary>
        [UnityEngine.Tooltip("Whether to replace matched patterns with a custom string.")]
        public bool hasReplacement;

        /// <summary>Replacement string (used when hasReplacement is true).</summary>
        [UnityEngine.Tooltip("Replacement string (used when hasReplacement is true).")]
        [EscapeTextArea(1, 3)]
        public string replacement;

        public int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            if (patterns == null || patterns.Length == 0)
                return index;

            for (var p = 0; p < patterns.Length; p++)
            {
                var pattern = patterns[p];
                if (string.IsNullOrEmpty(pattern))
                    continue;

                var patternLen = pattern.Length;
                if (index + patternLen > text.Length)
                    continue;

                var matched = true;
                for (var i = 0; i < patternLen; i++)
                {
                    if (text[index + i] != pattern[i])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    var matchEnd = index + patternLen;
                    results.Add(ParsedRange.SelfClosing(index, matchEnd, hasReplacement ? (replacement ?? string.Empty) : string.Empty));
                    return matchEnd;
                }
            }

            return index;
        }
    }
}
