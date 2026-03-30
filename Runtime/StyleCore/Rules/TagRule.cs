using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Universal tag parse rule configured via a serialized tag name.
    /// Replaces all individual tag parse rule classes (BoldParseRule, ColorParseRule, etc.).
    /// </summary>
    /// <remarks>
    /// Parameters are always optional. Self-closing is syntax-driven via /&gt;.
    /// <list type="bullet">
    /// <item><c>&lt;tag&gt;text&lt;/tag&gt;</c> — range with no parameter</item>
    /// <item><c>&lt;tag=value&gt;text&lt;/tag&gt;</c> — range with parameter</item>
    /// <item><c>&lt;tag/&gt;</c> — self-closing, no parameter</item>
    /// <item><c>&lt;tag=value/&gt;</c> — self-closing with parameter</item>
    /// </list>
    /// When <see cref="defaultParameter"/> is set, tags without parameters use it as a fallback.
    /// Tags with partial parameters merge with the default (tag values take priority).
    /// </remarks>
    [Serializable]
    [TypeGroup("Tags", 0)]
    [TypeDescription("Activates the modifier using an XML-like tag with a configurable name.")]
    public sealed class TagRule : TagParseRule
    {
        [SerializeField] private string tagName = "tag";

        [DefaultParameter] public string defaultParameter;

        public TagRule() { }

        public TagRule(string tagName) => this.tagName = tagName;

        protected override string TagName => tagName;

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (string.IsNullOrEmpty(defaultParameter))
                return base.TryMatch(text, index, results);

            var countBefore = results.Count;
            var result = base.TryMatch(text, index, results);
            if (results.Count > countBefore)
                ApplyDefaults(results, countBefore);
            return result;
        }

        public override void Finalize(ReadOnlySpan<char> text, PooledList<ParsedRange> results)
        {
            if (string.IsNullOrEmpty(defaultParameter))
            {
                base.Finalize(text, results);
                return;
            }

            var countBefore = results.Count;
            base.Finalize(text, results);
            if (results.Count > countBefore)
                ApplyDefaults(results, countBefore);
        }

        private void ApplyDefaults(PooledList<ParsedRange> results, int fromIndex)
        {
            for (var i = fromIndex; i < results.Count; i++)
            {
                ref var range = ref results[i];
                if (string.IsNullOrEmpty(range.parameter))
                    range.parameter = defaultParameter;
                else
                    range.parameter = MergeParameters(range.parameter, defaultParameter);
            }
        }

        private static string MergeParameters(string fromText, string defaults)
        {
            var maxLen = fromText.Length + defaults.Length;
            Span<char> buf = maxLen <= 256 ? stackalloc char[maxLen] : new char[maxLen];
            var pos = 0;

            var textSpan = fromText.AsSpan();
            var defSpan = defaults.AsSpan();
            var firstGroup = true;

            while (textSpan.Length > 0 || defSpan.Length > 0)
            {
                var textGroup = NextToken(ref textSpan, ';');
                var defGroup = NextToken(ref defSpan, ';');

                if (!firstGroup) buf[pos++] = ';';
                firstGroup = false;

                if (textGroup.IsEmpty)
                {
                    defGroup.CopyTo(buf.Slice(pos));
                    pos += defGroup.Length;
                }
                else
                {
                    pos = MergeTokensInto(buf, pos, textGroup, defGroup);
                }
            }

            while (pos > 0 && buf[pos - 1] == ';') pos--;

            return new string(buf.Slice(0, pos));
        }

        private static int MergeTokensInto(Span<char> buf, int pos,
            ReadOnlySpan<char> text, ReadOnlySpan<char> defaults)
        {
            var firstToken = true;

            while (text.Length > 0 || defaults.Length > 0)
            {
                var textToken = NextToken(ref text, ',').Trim();
                var defToken = NextToken(ref defaults, ',').Trim();
                var chosen = textToken.Length > 0 ? textToken : defToken;

                if (!firstToken) buf[pos++] = ',';
                firstToken = false;

                chosen.CopyTo(buf.Slice(pos));
                pos += chosen.Length;
            }

            while (pos > 0 && buf[pos - 1] == ',') pos--;

            return pos;
        }

        private static ReadOnlySpan<char> NextToken(ref ReadOnlySpan<char> span, char separator)
        {
            if (span.IsEmpty) return default;

            var idx = span.IndexOf(separator);
            if (idx < 0)
            {
                var result = span;
                span = default;
                return result;
            }

            var token = span.Slice(0, idx);
            span = span.Slice(idx + 1);
            return token;
        }
    }
}
