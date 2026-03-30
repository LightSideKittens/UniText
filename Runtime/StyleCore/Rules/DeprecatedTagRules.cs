using System;

namespace LightSide
{
    [Serializable, HideFromTypeSelector] public sealed class BoldParseRule : TagParseRule { protected override string TagName => "b"; }
    [Serializable, HideFromTypeSelector] public sealed class ItalicParseRule : TagParseRule { protected override string TagName => "i"; }
    [Serializable, HideFromTypeSelector] public sealed class ColorParseRule : TagParseRule { protected override string TagName => "color"; }
    [Serializable, HideFromTypeSelector] public sealed class SizeParseRule : TagParseRule { protected override string TagName => "size"; }
    [Serializable, HideFromTypeSelector] public sealed class UnderlineParseRule : TagParseRule { protected override string TagName => "u"; }
    [Serializable, HideFromTypeSelector] public sealed class StrikethroughParseRule : TagParseRule { protected override string TagName => "s"; }
    [Serializable, HideFromTypeSelector] public sealed class CSpaceParseRule : TagParseRule { protected override string TagName => "cspace"; }
    [Serializable, HideFromTypeSelector] public sealed class LineSpacingParseRule : TagParseRule { protected override string TagName => "line-spacing"; }
    [Serializable, HideFromTypeSelector] public sealed class LineHeightParseRule : TagParseRule { protected override string TagName => "line-height"; }
    [Serializable, HideFromTypeSelector] public sealed class OutlineParseRule : TagParseRule { protected override string TagName => "outline"; }
    [Serializable, HideFromTypeSelector] public sealed class ShadowParseRule : TagParseRule { protected override string TagName => "shadow"; }
    [Serializable, HideFromTypeSelector] public sealed class ObjParseRule : TagParseRule { protected override string TagName => "obj"; }
    [Serializable, HideFromTypeSelector] public sealed class EllipsisTagRule : TagParseRule { protected override string TagName => "ellipsis"; }
    [Serializable, HideFromTypeSelector] public sealed class UppercaseParseRule : TagParseRule { protected override string TagName => "upper"; }
    [Serializable, HideFromTypeSelector] public sealed class GradientParseRule : TagParseRule { protected override string TagName => "gradient"; }
    [Serializable, HideFromTypeSelector] public sealed class LinkTagParseRule : TagParseRule { protected override string TagName => "link"; }
}
