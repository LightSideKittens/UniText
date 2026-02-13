using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Renders an underline below text using the font's underline metrics.
    /// </summary>
    /// <remarks>
    /// Usage: <c>&lt;u&gt;underlined text&lt;/u&gt;</c>
    ///
    /// The underline position is determined by the font's underlineOffset property.
    /// Supports line breaks and color inheritance from the text.
    /// </remarks>
    /// <seealso cref="UnderlineParseRule"/>
    [Serializable]
    [TypeGroup("Decoration", 1)]
    public class UnderlineModifier : BaseLineModifier
    {
        protected override string AttributeKey => AttributeKeys.Underline;

        protected override float GetLineOffset(FaceInfo faceInfo, float scale)
        {
            return faceInfo.underlineOffset * scale;
        }

        protected override void SetStaticBuffer(byte[] buf)
        {
        }
    }

}
