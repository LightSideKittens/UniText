using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies italic styling to text by shearing glyph vertices.
    /// </summary>
    /// <remarks>
    /// Usage: <c>&lt;i&gt;italic text&lt;/i&gt;</c>
    ///
    /// The italic effect is achieved by horizontally shearing the glyph vertices.
    /// The shear angle is determined by the font's ItalicStyle property (default 12 degrees).
    /// </remarks>
    /// <seealso cref="ItalicParseRule"/>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    public class ItalicModifier : GlyphModifier<byte>
    {
        protected override string AttributeKey => AttributeKeys.Italic;

        protected override Action GetOnGlyphCallback()
        {
            return OnGlyph;
        }

        protected override void DoApply(int start, int end, string parameter)
        {
            var cpCount = buffers.codepoints.count;
            attribute.buffer.data.SetFlagRange(start, Math.Min(end, cpCount));
        }

        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen.font.IsColor) return;

            var cluster = gen.currentCluster;
            if (!attribute.buffer.data.HasFlag(cluster))
                return;

            var italicStyle = gen.font?.ItalicStyle ?? 12f;
            var shearValue = italicStyle * 0.01f;

            var baseIdx = gen.vertexCount - 4;
            var verts = gen.Vertices;

            var blY = verts[baseIdx].y;
            var tlY = verts[baseIdx + 1].y;
            var midY = (tlY + blY) * 0.5f;

            var topShearX = shearValue * (tlY - midY);
            var bottomShearX = shearValue * (blY - midY);

            verts[baseIdx].x += bottomShearX;
            verts[baseIdx + 1].x += topShearX;
            verts[baseIdx + 2].x += topShearX;
            verts[baseIdx + 3].x += bottomShearX;
        }
    }
}
