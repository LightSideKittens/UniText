using System;

namespace LightSide
{
    /// <summary>Parses underline markup tags: <![CDATA[<u>text</u>]]>.</summary>
    /// <seealso cref="UnderlineModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class UnderlineParseRule : TagParseRule
    {
        protected override string TagName => "u";
        protected override bool HasParameter => false;
    }
}
