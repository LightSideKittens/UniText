using System;

namespace LightSide
{
    /// <summary>
    /// Parses size markup tags: <![CDATA[<size=24>text</size>]]>, <![CDATA[<size=150%>text</size>]]>,
    /// <![CDATA[<size=+10>text</size>]]>, or <![CDATA[<size=-5>text</size>]]>.
    /// </summary>
    /// <seealso cref="SizeModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class SizeParseRule : TagParseRule
    {
        protected override string TagName => "size";
        protected override bool HasParameter => true;
    }

}
