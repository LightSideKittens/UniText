using System;

namespace LightSide
{
    /// <summary>Parses bold markup tags: <![CDATA[<b>text</b>]]>.</summary>
    /// <seealso cref="BoldModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class BoldParseRule : TagParseRule
    {
        protected override string TagName => "b";
        protected override bool HasParameter => false;
    }
}
