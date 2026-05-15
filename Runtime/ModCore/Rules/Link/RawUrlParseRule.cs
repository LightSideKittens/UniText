using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>Auto-detects URLs in plain text and converts them to clickable links.</summary>
    /// <remarks>
    /// <para>
    /// Recognizes schemes: http://, https://, ftp://, ftps://, file://, mailto:, tel:, and www. prefixes.
    /// </para>
    /// <para>
    /// Uses negative priority (-100) to run after explicit markup rules (tags, Markdown),
    /// so URLs inside other markup are not detected.
    /// </para>
    /// </remarks>
    /// <seealso cref="LinkModifier"/>
    [Serializable]
    [TypeGroup("Auto-detect", 2)]
    public sealed class RawUrlParseRule : IParseRule
    {
        /// <summary>Low priority ensures this runs after explicit markup rules.</summary>
        public int Priority => -100;

        public int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            if (index > 0 && IsWordChar(text[index - 1]))
                return index;

            var c = ToLowerAscii(text[index]);

            if (c == 'h')
            {
                if (MatchesScheme(text, index, 'h', 't', 't', 'p', 's', ':', '/', '/'))
                    return TryAddUrl(text, index, index + 8, results);
                if (MatchesScheme(text, index, 'h', 't', 't', 'p', ':', '/', '/'))
                    return TryAddUrl(text, index, index + 7, results);
            }
            else if (c == 'f')
            {
                if (MatchesScheme(text, index, 'f', 't', 'p', 's', ':', '/', '/'))
                    return TryAddUrl(text, index, index + 7, results);
                if (MatchesScheme(text, index, 'f', 'i', 'l', 'e', ':', '/', '/'))
                    return TryAddUrl(text, index, index + 7, results);
                if (MatchesScheme(text, index, 'f', 't', 'p', ':', '/', '/'))
                    return TryAddUrl(text, index, index + 6, results);
            }
            else if (c == 'm')
            {
                if (MatchesScheme(text, index, 'm', 'a', 'i', 'l', 't', 'o', ':'))
                    return TryAddUrl(text, index, index + 7, results);
            }
            else if (c == 't')
            {
                if (MatchesScheme(text, index, 't', 'e', 'l', ':'))
                    return TryAddUrl(text, index, index + 4, results);
            }
            else if (c == 'w')
            {
                if (MatchesScheme(text, index, 'w', 'w', 'w', '.'))
                {
                    var urlEnd = FindUrlEnd(text, index + 4);
                    if (urlEnd > index + 4)
                    {
                        var url = "https://" + text.Slice(index, urlEnd - index).ToString();
                        results.Add(new ParsedRange(index, urlEnd, url));
                        return urlEnd;
                    }
                }
            }

            return index;
        }

        private static int TryAddUrl(ReadOnlySpan<char> text,int start, int afterScheme, PooledList<ParsedRange> results)
        {
            var urlEnd = FindUrlEnd(text, afterScheme);
            if (urlEnd > afterScheme)
            {
                results.Add(new ParsedRange(start, urlEnd, text.Slice(start, urlEnd - start).ToString()));
                return urlEnd;
            }
            return start;
        }

        private static int FindUrlEnd(ReadOnlySpan<char> text,int start)
        {
            var i = start;
            var parenDepth = 0;

            while (i < text.Length)
            {
                var c = text[i];
                if (c <= ' ') break;

                if (c == '.' || c == ',' || c == ';' || c == ':' || c == '!' || c == '?')
                    if (i + 1 >= text.Length || text[i + 1] <= ' ')
                        break;

                if (c == '(')
                {
                    parenDepth++;
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    if (parenDepth > 0)
                    {
                        parenDepth--;
                        i++;
                        continue;
                    }
                    break;
                }

                if (c == '"' || c == '\'' || c == '<' || c == '>' || c == '`' || c == '[' || c == ']') break;
                if (!IsValidUrlChar(c)) break;

                i++;
            }

            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char ToLowerAscii(char c)
        {
            return (c >= 'A' && c <= 'Z') ? (char)(c | 0x20) : c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesScheme(ReadOnlySpan<char> text,int index, char c0, char c1, char c2, char c3)
        {
            if (index + 4 > text.Length) return false;
            return ToLowerAscii(text[index]) == c0 &&
                   ToLowerAscii(text[index + 1]) == c1 &&
                   ToLowerAscii(text[index + 2]) == c2 &&
                   ToLowerAscii(text[index + 3]) == c3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesScheme(ReadOnlySpan<char> text,int index, char c0, char c1, char c2, char c3, char c4, char c5)
        {
            if (index + 6 > text.Length) return false;
            return ToLowerAscii(text[index]) == c0 &&
                   ToLowerAscii(text[index + 1]) == c1 &&
                   ToLowerAscii(text[index + 2]) == c2 &&
                   ToLowerAscii(text[index + 3]) == c3 &&
                   ToLowerAscii(text[index + 4]) == c4 &&
                   ToLowerAscii(text[index + 5]) == c5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesScheme(ReadOnlySpan<char> text,int index, char c0, char c1, char c2, char c3, char c4, char c5, char c6)
        {
            if (index + 7 > text.Length) return false;
            return ToLowerAscii(text[index]) == c0 &&
                   ToLowerAscii(text[index + 1]) == c1 &&
                   ToLowerAscii(text[index + 2]) == c2 &&
                   ToLowerAscii(text[index + 3]) == c3 &&
                   ToLowerAscii(text[index + 4]) == c4 &&
                   ToLowerAscii(text[index + 5]) == c5 &&
                   ToLowerAscii(text[index + 6]) == c6;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesScheme(ReadOnlySpan<char> text,int index, char c0, char c1, char c2, char c3, char c4, char c5, char c6, char c7)
        {
            if (index + 8 > text.Length) return false;
            return ToLowerAscii(text[index]) == c0 &&
                   ToLowerAscii(text[index + 1]) == c1 &&
                   ToLowerAscii(text[index + 2]) == c2 &&
                   ToLowerAscii(text[index + 3]) == c3 &&
                   ToLowerAscii(text[index + 4]) == c4 &&
                   ToLowerAscii(text[index + 5]) == c5 &&
                   ToLowerAscii(text[index + 6]) == c6 &&
                   ToLowerAscii(text[index + 7]) == c7;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidUrlChar(char c)
        {
            if ((c | 0x20) >= 'a' && (c | 0x20) <= 'z') return true;
            if (c >= '0' && c <= '9') return true;
            switch (c)
            {
                case '-':
                case '.':
                case '_':
                case '~':
                case '!':
                case '$':
                case '&':
                case '\'':
                case '*':
                case '+':
                case ',':
                case ';':
                case '=':
                case ':':
                case '@':
                case '/':
                case '?':
                case '#':
                case '%':
                case '(':
                case ')':
                    return true;
            }

            if (c >= 0x00A0 && c <= 0xD7FF) return true;
            if (c >= 0xF900 && c <= 0xFDCF) return true;
            if (c >= 0xFDF0 && c <= 0xFFEF) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWordChar(char c)
        {
            return ((c | 0x20) >= 'a' && (c | 0x20) <= 'z') || (c >= '0' && c <= '9') || c == '_';
        }
    }
}
