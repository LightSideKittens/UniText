using System;

namespace LightSide
{
    /// <summary>Parses inline object tags: <![CDATA[<obj=objectName/>]]>.</summary>
    /// <remarks>
    /// Self-closing tag that inserts a placeholder character (U+FFFC) which is replaced
    /// with a registered inline object during rendering.
    /// </remarks>
    /// <seealso cref="ObjModifier"/>
    /// <seealso cref="InlineObject"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class ObjParseRule : TagParseRule
    {
        protected override string TagName => "obj";
        protected override bool HasParameter => true;
        protected override bool IsSelfClosing => true;
        protected override string InsertString => "\uFFFC";
    }
}
