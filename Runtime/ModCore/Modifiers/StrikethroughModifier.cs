using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Renders a horizontal line through the middle of the text (strikethrough effect).
    /// </summary>
    /// <remarks>
    /// Usage: <c>&lt;s&gt;strikethrough text&lt;/s&gt;</c>
    ///
    /// The line position is determined by the font's strikethroughOffset property,
    /// or calculated from the x-height if not available.
    /// Supports line breaks and color inheritance from the text.
    /// </remarks>
    /// <seealso cref="StrikethroughParseRule"/>
    [Serializable]
    [TypeGroup("Decoration", 1)]
    public class StrikethroughModifier : BaseLineModifier
    {
        protected override string AttributeKey => AttributeKeys.Strikethrough;

        protected override float GetLineOffset(FaceInfo faceInfo, float scale)
        {
            if (faceInfo.strikethroughOffset != 0)
                return faceInfo.strikethroughOffset * scale;

            var xHeightMid = faceInfo.meanLine > 0
                ? faceInfo.meanLine * 0.5f
                : faceInfo.ascentLine * 0.35f;
            return xHeightMid * scale;
        }

        protected override void SetStaticBuffer(byte[] buf)
        {
        }
    }

}
