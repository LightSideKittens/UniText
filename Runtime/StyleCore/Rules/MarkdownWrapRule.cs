using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>Parses symmetric open/close markers in text (e.g., **text**, ~~text~~).</summary>
    /// <remarks>
    /// The marker string is configurable — any string can be used as a marker with any modifier.
    /// When <see cref="defaultParameter"/> is set, matched ranges use it as the parameter value.
    /// </remarks>
    [Serializable]
    [TypeGroup("Markdown", 1)]
    [TypeDescription("Activates the modifier using symmetric wrap markers, e.g. **text**.")]
    public sealed class MarkdownWrapRule : IParseRule
    {
        /// <summary>The symmetric marker string that wraps the affected text range.</summary>
        [UnityEngine.Tooltip("The symmetric marker string that wraps the affected text range (e.g. **, ~~, ++, or any custom string).")]
        public string marker = "**";

        [DefaultParameter] public string defaultParameter;

        private readonly Stack<(int tagStart, int tagEnd)> openMarkers = new(8);

        public int Priority => marker != null ? marker.Length : 0;

        public void Reset()
        {
            openMarkers.Clear();
        }

        public int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (string.IsNullOrEmpty(marker))
                return index;

            var len = marker.Length;
            if (index + len > text.Length)
                return index;

            for (var i = 0; i < len; i++)
            {
                if (text[index + i] != marker[i])
                    return index;
            }

            var afterMarker = index + len;

            if (openMarkers.Count > 0)
            {
                var open = openMarkers.Pop();
                var param = string.IsNullOrEmpty(defaultParameter) ? null : defaultParameter;
                results.Add(new ParsedRange(open.tagStart, open.tagEnd, index, afterMarker, param));
                return afterMarker;
            }

            openMarkers.Push((index, afterMarker));
            return afterMarker;
        }

        public void Finalize(ReadOnlySpan<char> text, PooledList<ParsedRange> results)
        {
            openMarkers.Clear();
        }
    }
}
