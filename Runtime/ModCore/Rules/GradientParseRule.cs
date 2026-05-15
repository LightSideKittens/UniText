using System;

namespace LightSide
{
    /// <summary>
    /// Parses gradient markup tags: <![CDATA[<gradient=name>text</gradient>]]> or <![CDATA[<gradient=name,angle>text</gradient>]]>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parameter specifies the gradient name and optional angle:
    /// <list type="bullet">
    /// <item><c>&lt;gradient=rainbow&gt;</c> - uses 0° angle (left-to-right)</item>
    /// <item><c>&lt;gradient=rainbow,45&gt;</c> - uses 45° angle</item>
    /// <item><c>&lt;gradient=rainbow,90&gt;</c> - uses 90° angle (bottom-to-top)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Gradient names must be defined in <see cref="UniTextGradients"/> referenced by <see cref="UniTextSettings"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="GradientModifier"/>
    /// <seealso cref="UniTextGradients"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    public sealed class GradientParseRule : TagParseRule
    {
        protected override string TagName => "gradient";
        protected override bool HasParameter => true;
    }
}
