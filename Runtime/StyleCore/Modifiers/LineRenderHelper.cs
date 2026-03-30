using System;
using UnityEngine;


namespace LightSide
{
    public static class LineRenderHelper
    {
        [ThreadStatic] private static Glyph? cachedUnderscoreGlyph;
        [ThreadStatic] private static UniTextFont cachedUnderscoreFont;
        [ThreadStatic] private static int cachedFontProviderId;
        [ThreadStatic] private static long cachedVarHash;


        public static void DrawLine(UniTextFontProvider fontProvider, float startX, float endX,
            float baselineY, float lineYOffset, Color32 color, long varHash48)
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen == null || fontProvider == null)
                return;

            var maybeGlyph = GetUnderscoreGlyph(fontProvider, varHash48, out var glyphFont);
            if (!maybeGlyph.HasValue) return;
            var underscoreGlyph = maybeGlyph.Value;

            var atlas = GlyphAtlas.GetInstance(gen.RenderMode);
            if (!atlas.TryGetEntry(varHash48, underscoreGlyph.index, out var entry) || entry.encodedTile < 0)
                return;

            gen.TrackGlyphKey(GlyphAtlas.MakeKey(varHash48, underscoreGlyph.index));

            var metrics = underscoreGlyph.metrics;
            var upem = (float)glyphFont.UnitsPerEm;
            var scale = gen.FontSize * glyphFont.FontScale / upem;

            var glyphH = metrics.height / upem;
            if (glyphH < 1e-6f) return;
            var glyphW = metrics.width / upem;
            var aspect = glyphW / glyphH;

            var glyphHeightLocal = metrics.height * scale;
            var halfH = Math.Max(glyphHeightLocal, gen.FontSize * 0.02f);

            var y = baselineY + lineYOffset;

            var uvHalfSpan = glyphHeightLocal > 1e-6f ? halfH / glyphHeightLocal : 1f;
            var uvBottom = 0.5f - uvHalfSpan;
            var uvTop = 0.5f + uvHalfSpan;

            gen.EnsureCapacity(4, 6);

            var verts = gen.Vertices;
            var uvData = gen.Uvs0;
            var uv1Data = gen.Uvs1;
            var cols = gen.Colors;
            var tris = gen.Triangles;

            var vertIdx = gen.vertexCount;
            var triIdx = gen.triangleCount;

            var tileIdx = (float)(entry.encodedTile + entry.pageIndex * GlyphAtlas.PageStride);
            var centerX = aspect * 0.5f;

            verts[vertIdx] = new Vector3(startX, y - halfH, 0);
            verts[vertIdx + 1] = new Vector3(startX, y + halfH, 0);
            verts[vertIdx + 2] = new Vector3(endX, y + halfH, 0);
            verts[vertIdx + 3] = new Vector3(endX, y - halfH, 0);

            uvData[vertIdx] = new Vector4(centerX, uvBottom, tileIdx, glyphH);
            uvData[vertIdx + 1] = new Vector4(centerX, uvTop, tileIdx, glyphH);
            uvData[vertIdx + 2] = new Vector4(centerX, uvTop, tileIdx, glyphH);
            uvData[vertIdx + 3] = new Vector4(centerX, uvBottom, tileIdx, glyphH);

            var uv1Val = new Vector2(aspect, 0);
            uv1Data[vertIdx] = uv1Val;
            uv1Data[vertIdx + 1] = uv1Val;
            uv1Data[vertIdx + 2] = uv1Val;
            uv1Data[vertIdx + 3] = uv1Val;

            cols[vertIdx] = color;
            cols[vertIdx + 1] = color;
            cols[vertIdx + 2] = color;
            cols[vertIdx + 3] = color;

            var localI0 = vertIdx - gen.CurrentSegmentVertexStart;
            tris[triIdx] = localI0;
            tris[triIdx + 1] = localI0 + 1;
            tris[triIdx + 2] = localI0 + 2;
            tris[triIdx + 3] = localI0 + 2;
            tris[triIdx + 4] = localI0 + 3;
            tris[triIdx + 5] = localI0;

            gen.vertexCount += 4;
            gen.triangleCount += 6;
        }


        private static Glyph? GetUnderscoreGlyph(UniTextFontProvider fontProvider, long varHash48, out UniTextFont font)
        {
            var providerId = fontProvider.GetHashCode();

            if (cachedUnderscoreGlyph.HasValue && cachedFontProviderId == providerId && cachedVarHash == varHash48)
            {
                font = cachedUnderscoreFont;
                return cachedUnderscoreGlyph;
            }

            cachedUnderscoreGlyph = null;
            cachedUnderscoreFont = null;
            cachedFontProviderId = providerId;
            cachedVarHash = varHash48;

            const uint underscoreCodepoint = '_';

            var fontId = fontProvider.FindFontForCodepoint((int)underscoreCodepoint);
            font = fontProvider.GetFontAsset(fontId);

            var glyphIndex = font.GetGlyphIndexForUnicode(underscoreCodepoint);
            if (glyphIndex == 0)
                return null;

            var glyphLookup = font.GlyphLookupTable;
            if (glyphLookup != null && glyphLookup.TryGetValue(UniTextFont.GlyphKey(varHash48, glyphIndex), out var glyph))
            {
                cachedUnderscoreGlyph = glyph;
                cachedUnderscoreFont = font;
            }

            return cachedUnderscoreGlyph;
        }
    }

}
