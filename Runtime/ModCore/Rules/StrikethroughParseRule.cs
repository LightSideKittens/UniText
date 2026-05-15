using System;

namespace LightSide
{
    /// <summary>Parses strikethrough markup tags: <![CDATA[<s>text</s>]]>.</summary>
    /// <seealso cref="StrikethroughModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class StrikethroughParseRule : TagParseRule
    {
        protected override string TagName => "s";
        protected override bool HasParameter => false;
    }
}
