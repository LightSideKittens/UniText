using System;

namespace LightSide
{
    /// <summary>Parses ellipsis region tags: <![CDATA[<ellipsis=position>text</ellipsis>]]>.</summary>
    /// <remarks>
    /// Marks a region where text truncation with ellipsis should occur.
    /// The parameter specifies the ellipsis position (0-1): 0=start, 0.5=middle, 1=end.
    /// </remarks>
    /// <seealso cref="EllipsisModifier"/>
    [Serializable]
    public sealed class EllipsisTagRule : TagParseRule
    {
        protected override string TagName => "ellipsis";
        protected override bool HasParameter => true;
    }

}
