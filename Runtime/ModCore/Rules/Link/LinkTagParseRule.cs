using System;

namespace LightSide
{
    /// <summary>Parses link markup tags: <![CDATA[<link=https://example.com>text</link>]]>.</summary>
    /// <seealso cref="LinkModifier"/>
    /// <seealso cref="MarkdownLinkParseRule"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class LinkTagParseRule : TagParseRule
    {
        protected override string TagName => "link";
        protected override bool HasParameter => true;
    }

}
