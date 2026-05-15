using System;

namespace LightSide
{
    /// <summary>Parses italic markup tags: <![CDATA[<i>text</i>]]>.</summary>
    /// <seealso cref="ItalicModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class ItalicParseRule : TagParseRule
    {
        protected override string TagName => "i";
        protected override bool HasParameter => false;
    }
}
