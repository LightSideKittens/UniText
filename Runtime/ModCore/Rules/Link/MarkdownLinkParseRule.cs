using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>Parses Markdown-style links: [link text](https://example.com).</summary>
    /// <seealso cref="LinkModifier"/>
    /// <seealso cref="LinkTagParseRule"/>
    [Serializable]
    [TypeGroup("Markdown", 1)]
    public sealed class MarkdownLinkParseRule : IParseRule
    {
        public int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '[') return index;

            var textStart = index + 1;
            var closeBracket = FindCloseBracket(text, textStart);
            if (closeBracket < 0) return index;

            if (closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
                return index;

            var urlStart = closeBracket + 2;
            var closeParen = FindCloseParen(text, urlStart);
            if (closeParen < 0) return index;

            var url = text.Slice(urlStart, closeParen - urlStart).Trim().ToString();
            if (string.IsNullOrEmpty(url)) return index;

            var fullEnd = closeParen + 1;

            results.Add(new ParsedRange(
                index,
                textStart,
                closeBracket,
                fullEnd,
                url
            ));

            return fullEnd;
        }

        private static int FindCloseBracket(ReadOnlySpan<char> text,int start)
        {
            var depth = 1;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
                else if (c == '\n' || c == '\r') return -1;
            }
            return -1;
        }

        private static int FindCloseParen(ReadOnlySpan<char> text,int start)
        {
            var depth = 1;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
                else if (c == '\n' || c == '\r') return -1;
            }
            return -1;
        }
    }

}
