using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Renders an underline below text using the font's underline metrics.
    /// </summary>
    /// <remarks>
    /// No parameter. The underline position is determined by the font's underlineOffset property.
    /// Supports line breaks and color inheritance from the text.
    /// </remarks>
    [Serializable]
    [TypeGroup("Decoration", 1)]
    [TypeDescription("Draws a line beneath the text.")]
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
