using System;

namespace LightSide
{
    /// <summary>
    /// Parses line-height markup tags: <![CDATA[<line-height=1.5>text</line-height>]]> or <![CDATA[<line-height=40>text</line-height>]]>.
    /// </summary>
    /// <seealso cref="LineHeightModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class LineHeightParseRule : TagParseRule
    {
        protected override string TagName => "line-height";
        protected override bool HasParameter => true;
    }

}
