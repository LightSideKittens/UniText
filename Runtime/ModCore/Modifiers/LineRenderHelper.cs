using System;
using UnityEngine;


namespace LightSide
{
    public static class LineRenderHelper
    {
        [ThreadStatic] private static Glyph? cachedUnderscoreGlyph;
        [ThreadStatic] private static UniTextFont cachedUnderscoreFont;
        [ThreadStatic] private static int cachedFontProviderId;


        public static void DrawLine(UniTextFontProvider fontProvider, float startX, float endX, float baselineY, float lineYOffset, Color32 color)
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen == null || fontProvider == null)
                return;

            var currentFont = gen.font;
            if (currentFont == null)
                return;

            var maybeGlyph = GetUnderscoreGlyph(fontProvider, out var glyphFont);
            if (!maybeGlyph.HasValue) return;
            var underscoreGlyph = maybeGlyph.Value;
            if (underscoreGlyph.glyphRect.width == 0) return;

            if (glyphFont != currentFont)
                return;

            if (underscoreGlyph.atlasIndex != gen.CurrentAtlasIndex)
                return;

            gen.EnsureCapacity(12, 18);

            var scale = gen.scale;
            var xScaleVal = gen.xScale;
            float atlasSize = gen.atlasSize;
            var paddingPixels = gen.paddingPixels;
            var padding = gen.padding;
            var gradientScale = gen.gradientScale;
            var spreadRatio = gen.spreadRatio;

            var samplingPointSize = currentFont.FaceInfo.pointSize;

            var underscoreRect = underscoreGlyph.glyphRect;
            var underscoreMetrics = underscoreGlyph.metrics;

            var lineThickness = currentFont.FaceInfo.underlineThickness;

            var atlasToScreen = samplingPointSize > 0 ? gen.FontSize / samplingPointSize : scale;

            var paddingOffset = paddingPixels * atlasToScreen;

            var y = baselineY + lineYOffset;
            var start = new Vector3(startX - paddingOffset, y, 0);
            var end = new Vector3(endX + paddingOffset, y, 0);

            var segmentWidth = (underscoreRect.width * 0.5f + paddingPixels) * atlasToScreen;
            var fullWidth = (underscoreRect.width + paddingPixels * 2) * atlasToScreen;

            if (endX - startX < fullWidth) segmentWidth = (end.x - start.x) * 0.5f;

            var thickness = (lineThickness + padding) * scale;
            var paddingScaled = padding * scale;

            var verts = gen.Vertices;
            var uvs0 = gen.Uvs0;
            var uvs1 = gen.Uvs1;
            var colors = gen.Colors;
            var tris = gen.Triangles;

            var vertIdx = gen.vertexCount;
            var triIdx = gen.triangleCount;

            #region VERTICES (12 vertices = 3 quads)

            verts[vertIdx + 0] = start + new Vector3(0, -thickness, 0);
            verts[vertIdx + 1] = start + new Vector3(0, paddingScaled, 0);
            verts[vertIdx + 2] = verts[vertIdx + 1] + new Vector3(segmentWidth, 0, 0);
            verts[vertIdx + 3] = verts[vertIdx + 0] + new Vector3(segmentWidth, 0, 0);

            verts[vertIdx + 4] = verts[vertIdx + 3];
            verts[vertIdx + 5] = verts[vertIdx + 2];
            verts[vertIdx + 6] = end + new Vector3(-segmentWidth, paddingScaled, 0);
            verts[vertIdx + 7] = end + new Vector3(-segmentWidth, -thickness, 0);

            verts[vertIdx + 8] = verts[vertIdx + 7];
            verts[vertIdx + 9] = verts[vertIdx + 6];
            verts[vertIdx + 10] = end + new Vector3(0, paddingScaled, 0);
            verts[vertIdx + 11] = end + new Vector3(0, -thickness, 0);

            #endregion

            #region UV0 (texture coordinates)

            var uvBottom = (underscoreRect.y - paddingPixels) / atlasSize;
            var uvTop = (underscoreRect.y + underscoreRect.height + paddingPixels) / atlasSize;

            var uvLeft = (underscoreRect.x - paddingPixels) / atlasSize;
            var uvCenter = (underscoreRect.x + underscoreRect.width * 0.5f) / atlasSize;
            var uvRight = (underscoreRect.x + underscoreRect.width + paddingPixels) / atlasSize;

            var uv0 = new Vector4(uvLeft, uvBottom, gradientScale, xScaleVal);
            var uv1 = new Vector4(uvLeft, uvTop, gradientScale, xScaleVal);
            var uv2 = new Vector4(uvCenter, uvTop, gradientScale, xScaleVal);
            var uv3 = new Vector4(uvCenter, uvBottom, gradientScale, xScaleVal);

            var uv4 = new Vector4(uvCenter, uvTop, gradientScale, xScaleVal);
            var uv5 = new Vector4(uvCenter, uvBottom, gradientScale, xScaleVal);
            var uv6 = new Vector4(uvRight, uvTop, gradientScale, xScaleVal);
            var uv7 = new Vector4(uvRight, uvBottom, gradientScale, xScaleVal);

            uvs0[vertIdx + 0] = uv0;
            uvs0[vertIdx + 1] = uv1;
            uvs0[vertIdx + 2] = uv2;
            uvs0[vertIdx + 3] = uv3;

            var halfPixelOffset = 0.5f / atlasSize;
            uvs0[vertIdx + 4] = new Vector4(uvCenter - halfPixelOffset, uvBottom, gradientScale, xScaleVal);
            uvs0[vertIdx + 5] = new Vector4(uvCenter - halfPixelOffset, uvTop, gradientScale, xScaleVal);
            uvs0[vertIdx + 6] = new Vector4(uvCenter + halfPixelOffset, uvTop, gradientScale, xScaleVal);
            uvs0[vertIdx + 7] = new Vector4(uvCenter + halfPixelOffset, uvBottom, gradientScale, xScaleVal);

            uvs0[vertIdx + 8] = uv5;
            uvs0[vertIdx + 9] = uv4;
            uvs0[vertIdx + 10] = uv6;
            uvs0[vertIdx + 11] = uv7;

            #endregion

            #region UV1 (spreadRatio for effect normalization + normalized X position along line)

            var totalWidth = end.x - start.x;
            if (totalWidth < 0.001f) totalWidth = 1f;

            var maxUvX_Left = (verts[vertIdx + 2].x - start.x) / totalWidth;
            var minUvX_Mid = (verts[vertIdx + 4].x - start.x) / totalWidth;
            var maxUvX_Mid = (verts[vertIdx + 6].x - start.x) / totalWidth;
            var minUvX_Right = (verts[vertIdx + 8].x - start.x) / totalWidth;

            uvs1[vertIdx + 0] = new Vector4(spreadRatio, 0, 0, 0);
            uvs1[vertIdx + 1] = new Vector4(spreadRatio, 0, 1, 0);
            uvs1[vertIdx + 2] = new Vector4(spreadRatio, maxUvX_Left, 1, 0);
            uvs1[vertIdx + 3] = new Vector4(spreadRatio, maxUvX_Left, 0, 0);

            uvs1[vertIdx + 4] = new Vector4(spreadRatio, minUvX_Mid, 0, 0);
            uvs1[vertIdx + 5] = new Vector4(spreadRatio, minUvX_Mid, 1, 0);
            uvs1[vertIdx + 6] = new Vector4(spreadRatio, maxUvX_Mid, 1, 0);
            uvs1[vertIdx + 7] = new Vector4(spreadRatio, maxUvX_Mid, 0, 0);

            uvs1[vertIdx + 8] = new Vector4(spreadRatio, minUvX_Right, 0, 0);
            uvs1[vertIdx + 9] = new Vector4(spreadRatio, minUvX_Right, 1, 0);
            uvs1[vertIdx + 10] = new Vector4(spreadRatio, 1, 1, 0);
            uvs1[vertIdx + 11] = new Vector4(spreadRatio, 1, 0, 0);

            #endregion

            for (var i = 0; i < 12; i++) colors[vertIdx + i] = color;

            var relIdx = vertIdx - gen.CurrentSegmentVertexStart;
            tris[triIdx + 0] = relIdx + 0;
            tris[triIdx + 1] = relIdx + 1;
            tris[triIdx + 2] = relIdx + 2;
            tris[triIdx + 3] = relIdx + 2;
            tris[triIdx + 4] = relIdx + 3;
            tris[triIdx + 5] = relIdx + 0;

            tris[triIdx + 6] = relIdx + 4;
            tris[triIdx + 7] = relIdx + 5;
            tris[triIdx + 8] = relIdx + 6;
            tris[triIdx + 9] = relIdx + 6;
            tris[triIdx + 10] = relIdx + 7;
            tris[triIdx + 11] = relIdx + 4;

            tris[triIdx + 12] = relIdx + 8;
            tris[triIdx + 13] = relIdx + 9;
            tris[triIdx + 14] = relIdx + 10;
            tris[triIdx + 15] = relIdx + 10;
            tris[triIdx + 16] = relIdx + 11;
            tris[triIdx + 17] = relIdx + 8;

            gen.vertexCount += 12;
            gen.triangleCount += 18;
        }


        private static Glyph? GetUnderscoreGlyph(UniTextFontProvider fontProvider, out UniTextFont font)
        {
            var providerId = fontProvider.GetHashCode();

            if (cachedUnderscoreGlyph.HasValue && cachedFontProviderId == providerId)
            {
                font = cachedUnderscoreFont;
                return cachedUnderscoreGlyph;
            }

            cachedUnderscoreGlyph = null;
            cachedUnderscoreFont = null;
            cachedFontProviderId = providerId;

            const uint underscoreCodepoint = '_';

            var fontId = fontProvider.FindFontForCodepoint((int)underscoreCodepoint);
            font = fontProvider.GetFontAsset(fontId);

            var charTable = font.CharacterLookupTable;
            if (charTable != null && charTable.TryGetValue(underscoreCodepoint, out var character) &&
                character != null)
            {
                cachedUnderscoreGlyph = character.glyph;
                cachedUnderscoreFont = font;
            }

            return cachedUnderscoreGlyph;
        }
    }

}
