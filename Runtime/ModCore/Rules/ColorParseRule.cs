using System;

namespace LightSide
{
    /// <summary>Parses color markup tags: <![CDATA[<color=#FF0000>text</color>]]> or <![CDATA[<color=red>text</color>]]>.</summary>
    /// <seealso cref="ColorModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class ColorParseRule : TagParseRule
    {
        protected override string TagName => "color";
        protected override bool HasParameter => true;
    }
}
