using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;


namespace LightSide
{
    public static class GlyphRenderHelper
    {
        public static float DrawGlyph(UniTextFontProvider fontProvider, uint codepoint, float x, float baselineY, Color32 color)
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen == null || fontProvider == null)
                return 0f;

            var currentFont = gen.font;
            if (currentFont == null)
                return 0f;

            var maybeGlyph = GetGlyph(fontProvider, codepoint, out var glyphFont);
            if (maybeGlyph == null) return 0f;
            var glyph = maybeGlyph.Value;

            if (glyphFont != currentFont)
                return glyph.metrics.horizontalAdvance * gen.scale;

            if (glyph.atlasIndex != gen.CurrentAtlasIndex)
                return glyph.metrics.horizontalAdvance * gen.scale;

            var glyphRect = glyph.glyphRect;
            var metrics = glyph.metrics;

            if (glyphRect.width == 0 || glyphRect.height == 0)
                return metrics.horizontalAdvance * gen.scale;

            gen.EnsureCapacity(4, 6);

            var scale = gen.scale;
            var xScaleVal = gen.xScale;
            var padding = gen.padding;
            var padding2 = gen.padding2;
            var paddingPixels = gen.paddingPixels;
            var gradientScale = gen.gradientScale;
            var spreadRatio = gen.spreadRatio;
            var invAtlasSize = gen.invAtlasSize;

            var bearingXScaled = (metrics.horizontalBearingX - padding) * scale;
            var bearingYScaled = (metrics.horizontalBearingY + padding) * scale;
            var heightScaled = (metrics.height + padding2) * scale;
            var widthScaled = (metrics.width + padding2) * scale;

            var tlX = x + bearingXScaled;
            var tlY = baselineY + bearingYScaled;
            var blY = tlY - heightScaled;
            var trX = tlX + widthScaled;

            var uvBLx = (glyphRect.x - paddingPixels) * invAtlasSize;
            var uvBLy = (glyphRect.y - paddingPixels) * invAtlasSize;
            var uvTLy = (glyphRect.y + glyphRect.height + paddingPixels) * invAtlasSize;
            var uvTRx = (glyphRect.x + glyphRect.width + paddingPixels) * invAtlasSize;

            var verts = gen.Vertices;
            var uvData = gen.Uvs0;
            var uv1Data = gen.Uvs1;
            var cols = gen.Colors;
            var tris = gen.Triangles;

            var vertIdx = gen.vertexCount;
            var triIdx = gen.triangleCount;

            var i0 = vertIdx;
            var i1 = vertIdx + 1;
            var i2 = vertIdx + 2;
            var i3 = vertIdx + 3;

            ref var v0 = ref verts[i0];
            v0.x = tlX;
            v0.y = blY;
            v0.z = 0;
            ref var v1 = ref verts[i1];
            v1.x = tlX;
            v1.y = tlY;
            v1.z = 0;
            ref var v2 = ref verts[i2];
            v2.x = trX;
            v2.y = tlY;
            v2.z = 0;
            ref var v3 = ref verts[i3];
            v3.x = trX;
            v3.y = blY;
            v3.z = 0;

            ref var uv0 = ref uvData[i0];
            uv0.x = uvBLx;
            uv0.y = uvBLy;
            uv0.z = gradientScale;
            uv0.w = xScaleVal;
            ref var uv1 = ref uvData[i1];
            uv1.x = uvBLx;
            uv1.y = uvTLy;
            uv1.z = gradientScale;
            uv1.w = xScaleVal;
            ref var uv2 = ref uvData[i2];
            uv2.x = uvTRx;
            uv2.y = uvTLy;
            uv2.z = gradientScale;
            uv2.w = xScaleVal;
            ref var uv3 = ref uvData[i3];
            uv3.x = uvTRx;
            uv3.y = uvBLy;
            uv3.z = gradientScale;
            uv3.w = xScaleVal;

            cols[i0] = color;
            cols[i1] = color;
            cols[i2] = color;
            cols[i3] = color;

            var uv1Val = new Vector4(spreadRatio, 0, 0, 0);
            uv1Data[i0] = uv1Val;
            uv1Data[i1] = uv1Val;
            uv1Data[i2] = uv1Val;
            uv1Data[i3] = uv1Val;

            var segmentStart = gen.CurrentSegmentVertexStart;
            tris[triIdx] = i0 - segmentStart;
            tris[triIdx + 1] = i1 - segmentStart;
            tris[triIdx + 2] = i2 - segmentStart;
            tris[triIdx + 3] = i2 - segmentStart;
            tris[triIdx + 4] = i3 - segmentStart;
            tris[triIdx + 5] = i0 - segmentStart;

            gen.vertexCount += 4;
            gen.triangleCount += 6;

            return metrics.horizontalAdvance * scale;
        }

        public static float DrawString(UniTextFontProvider fontProvider, string text, float x, float baselineY, Color32 color)
        {
            if (string.IsNullOrEmpty(text) || fontProvider == null)
                return 0f;

            var totalWidth = 0f;
            var currentX = x;

            for (var i = 0; i < text.Length; i++)
            {
                uint codepoint = text[i];

                if (char.IsHighSurrogate((char)codepoint) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codepoint = (uint)char.ConvertToUtf32((char)codepoint, text[i + 1]);
                    i++;
                }

                var advance = DrawGlyph(fontProvider, codepoint, currentX, baselineY, color);
                currentX += advance;
                totalWidth += advance;
            }

            return totalWidth;
        }


        public static float MeasureString(UniTextFontProvider fontProvider, string text)
        {
            if (string.IsNullOrEmpty(text) || fontProvider == null)
                return 0f;

            var totalWidth = 0f;

            for (var i = 0; i < text.Length; i++)
            {
                uint codepoint = text[i];

                if (char.IsHighSurrogate((char)codepoint) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codepoint = (uint)char.ConvertToUtf32((char)codepoint, text[i + 1]);
                    i++;
                }

                var maybeGlyph = GetGlyph(fontProvider, codepoint, out var font);
                if (maybeGlyph.HasValue)
                {
                    var glyph = maybeGlyph.Value;
                    var upem = font.UnitsPerEm;
                    var scale = upem > 0 ? fontProvider.FontSize * font.FontScale / upem : 1f;
                    totalWidth += glyph.metrics.horizontalAdvance * scale;
                }
            }

            return totalWidth;
        }


        public static float DrawString(UniTextFontProvider fontProvider, StringBuilder sb, float x, float baselineY, Color32 color)
        {
            if (sb == null || sb.Length == 0 || fontProvider == null)
                return 0f;

            var totalWidth = 0f;
            var currentX = x;
            var len = sb.Length;

            for (var i = 0; i < len; i++)
            {
                uint codepoint = sb[i];

                if (char.IsHighSurrogate((char)codepoint) && i + 1 < len && char.IsLowSurrogate(sb[i + 1]))
                {
                    codepoint = (uint)char.ConvertToUtf32((char)codepoint, sb[i + 1]);
                    i++;
                }

                var advance = DrawGlyph(fontProvider, codepoint, currentX, baselineY, color);
                currentX += advance;
                totalWidth += advance;
            }

            return totalWidth;
        }


        public static float MeasureString(UniTextFontProvider fontProvider, StringBuilder sb)
        {
            if (sb == null || sb.Length == 0 || fontProvider == null)
                return 0f;

            var totalWidth = 0f;
            var len = sb.Length;

            for (var i = 0; i < len; i++)
            {
                uint codepoint = sb[i];

                if (char.IsHighSurrogate((char)codepoint) && i + 1 < len && char.IsLowSurrogate(sb[i + 1]))
                {
                    codepoint = (uint)char.ConvertToUtf32((char)codepoint, sb[i + 1]);
                    i++;
                }

                var maybeGlyph = GetGlyph(fontProvider, codepoint, out var font);
                if (maybeGlyph.HasValue)
                {
                    var glyph = maybeGlyph.Value;
                    var upem = font.UnitsPerEm;
                    var scale = upem > 0 ? fontProvider.FontSize * font.FontScale / upem : 1f;
                    totalWidth += glyph.metrics.horizontalAdvance * scale;
                }
            }

            return totalWidth;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Glyph? GetGlyph(UniTextFontProvider fontProvider, uint codepoint, out UniTextFont font)
        {
            var fontId = fontProvider.FindFontForCodepoint((int)codepoint);
            font = fontProvider.GetFontAsset(fontId);

            if (!Shaper.TryGetGlyphInfo(font, codepoint, fontProvider.FontSize, out var glyphId, out _))
                return null;

            var glyphLookup = font.GlyphLookupTable;
            if (glyphLookup != null && glyphLookup.TryGetValue(glyphId, out var glyph))
                return glyph;

            return null;
        }
    }

}
