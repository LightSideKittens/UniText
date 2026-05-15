using System;

namespace LightSide
{
    /// <summary>Parses uppercase markup tags: <![CDATA[<upper>text</upper>]]>.</summary>
    /// <seealso cref="UppercaseModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class UppercaseParseRule : TagParseRule
    {
        protected override string TagName => "upper";
        protected override bool HasParameter => false;
    }
}
