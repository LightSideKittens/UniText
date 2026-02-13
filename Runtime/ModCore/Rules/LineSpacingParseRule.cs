using System;

namespace LightSide
{
    /// <summary>
    /// Parses line-spacing markup tags: <![CDATA[<line-spacing=10>text</line-spacing>]]> or <![CDATA[<line-spacing=-5>text</line-spacing>]]>.
    /// </summary>
    /// <remarks>
    /// This rule prefixes the parameter with "s:" to distinguish it from line-height in the modifier.
    /// </remarks>
    /// <seealso cref="LineHeightModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class LineSpacingParseRule : TagParseRule
    {
        protected override string TagName => "line-spacing";
        protected override bool HasParameter => true;

        public override int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            var countBefore = results.Count;
            var result = base.TryMatch(text, index, results);

            for (var i = countBefore; i < results.Count; i++)
            {
                ref var range = ref results[i];
                if (!string.IsNullOrEmpty(range.parameter))
                    range.parameter = "s:" + range.parameter;
            }

            return result;
        }
    }

}
