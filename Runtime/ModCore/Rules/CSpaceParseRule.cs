using System;

namespace LightSide
{
    /// <summary>
    /// Parses character spacing markup tags: <![CDATA[<cspace=10>text</cspace>]]> or <![CDATA[<cspace=0.5em>text</cspace>]]>.
    /// </summary>
    /// <seealso cref="LetterSpacingModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class CSpaceParseRule : TagParseRule
    {
        protected override string TagName => "cspace";
        protected override bool HasParameter => true;
    }

}
