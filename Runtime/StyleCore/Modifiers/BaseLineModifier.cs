using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that render horizontal lines across text (underline, strikethrough).
    /// </summary>
    /// <remarks>
    /// Subclasses define the vertical offset of the line relative to the baseline.
    /// The line automatically breaks across multiple lines and respects color changes.
    /// </remarks>
    /// <seealso cref="UnderlineModifier"/>
    /// <seealso cref="StrikethroughModifier"/>
    [Serializable]
    public abstract class BaseLineModifier : BaseModifier
    {
        protected struct LineSegment
        {
            public float startX;
            public float endX;
            public float baselineY;
            public long varHash48;
            public Color32 color;
        }

        private const float LineBreakThreshold = 5f;

        protected PooledArrayAttribute<byte> flagsAttribute;

        private LineSegment[] lineSegments;
        private int lineSegmentsCapacity;
        private int lineSegmentCount;

        private bool segmentsComputed;
        private float underscoreScale;
        private UniTextFont cachedUnderscoreFont;


        protected abstract string AttributeKey { get; }

        protected abstract float GetLineOffset(FaceInfo faceInfo, float scale);
        protected abstract void SetStaticBuffer(byte[] buffer);

        protected sealed override void OnEnable()
        {
            buffers.PrepareAttribute(ref flagsAttribute, AttributeKey);
            SetStaticBuffer(flagsAttribute.buffer.data);

            if (lineSegments == null)
            {
                lineSegments = UniTextArrayPool<LineSegment>.Rent(64);
                lineSegmentsCapacity = 64;
            }
            lineSegmentCount = 0;
            segmentsComputed = false;

            uniText.Rebuilding += OnRebuilding;
            uniText.MeshGenerator.onAfterPage += OnAfterPage;
        }

        protected sealed override void OnDisable()
        {
            uniText.Rebuilding -= OnRebuilding;
            uniText.MeshGenerator.onAfterPage -= OnAfterPage;
        }

        protected sealed override void OnDestroy()
        {
            SetStaticBuffer(null);
            buffers?.ReleaseAttributeData(AttributeKey);
            flagsAttribute = null;

            if (lineSegments != null)
            {
                UniTextArrayPool<LineSegment>.Return(lineSegments);
                lineSegments = null;
            }
        }

        protected sealed override void OnApply(int start, int end, string parameter)
        {
            var cpCount = buffers.codepoints.count;
            flagsAttribute.buffer.data.SetFlagRange(start, Math.Min(end, cpCount));

            buffers.virtualCodepoints.Add('_');
        }

        private void OnRebuilding()
        {
            flagsAttribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKey);
            SetStaticBuffer(flagsAttribute?.buffer.data);
            segmentsComputed = false;
        }

        private void AddSegment(float startX, float endX, float baselineY, long varHash48, Color32 color)
        {
            UniTextArrayPool<LineSegment>.GrowDouble(ref lineSegments, ref lineSegmentsCapacity, lineSegmentCount);

            lineSegments[lineSegmentCount] = new LineSegment
            {
                startX = startX,
                endX = endX,
                baselineY = baselineY,
                varHash48 = varHash48,
                color = color
            };
            lineSegmentCount++;
        }

        private void OnAfterPage()
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen == null) return;

            if (!segmentsComputed)
            {
                ComputeLineSegments(gen);
                segmentsComputed = true;
            }

            if (lineSegmentCount == 0) return;

            var fontProvider = uniText.FontProvider;
            var lineOffset = GetLineOffset(cachedUnderscoreFont.FaceInfo, underscoreScale);
            for (var i = 0; i < lineSegmentCount; i++)
            {
                ref var seg = ref lineSegments[i];
                LineRenderHelper.DrawLine(fontProvider, seg.startX, seg.endX, seg.baselineY, lineOffset, seg.color, seg.varHash48);
            }
        }

        private void ComputeLineSegments(UniTextMeshGenerator gen)
        {
            lineSegmentCount = 0;

            var fontProvider = uniText.FontProvider;
            var underscoreFontId = fontProvider.FindFontForCodepoint('_');
            cachedUnderscoreFont = fontProvider.GetFontAsset(underscoreFontId);

            var upem = (float)cachedUnderscoreFont.UnitsPerEm;
            underscoreScale = gen.FontSize * cachedUnderscoreFont.FontScale / upem;

            var underscoreGlyphIndex = cachedUnderscoreFont.GetGlyphIndexForUnicode('_');
            if (underscoreGlyphIndex == 0)
                return;

            var defaultVarHash = cachedUnderscoreFont.DefaultVarHash48;

            var flagsBuffer = flagsAttribute?.buffer.data;
            if (flagsBuffer == null || !flagsBuffer.HasAnyFlags())
                return;

            var offsetX = gen.offsetX;
            var offsetY = gen.offsetY;
            var defaultColor = gen.defaultColor;

            var allGlyphs = buffers.positionedGlyphs.data;
            var glyphCount = buffers.positionedGlyphs.count;
            if (glyphCount == 0) return;

            var glyphLookup = cachedUnderscoreFont.GlyphLookupTable;
            var underscoreFontHash = cachedUnderscoreFont.FontDataHash;

            float lineStartX = 0, lineEndX = 0, lineBaselineY = 0;
            float rowBaselineY = 0;
            long lineVarHash = 0;
            Color32 lineColor = default;
            var hasActiveLine = false;

            for (var i = 0; i < glyphCount; i++)
            {
                ref readonly var glyph = ref allGlyphs[i];

                if (glyph.cluster < 0 || glyph.cluster >= flagsBuffer.Length)
                    continue;

                var hasFlag = flagsBuffer.HasFlag(glyph.cluster);
                if (!hasFlag && !hasActiveLine) continue;

                var baselineY = offsetY - glyph.y;

                float left, right;
                if (glyph.right > glyph.left)
                {
                    left = offsetX + glyph.left;
                    right = offsetX + glyph.right;
                }
                else
                {
                    var glyphX = offsetX + glyph.x;
                    float glyphWidth = 0;

                    if (i + 1 < glyphCount)
                    {
                        ref readonly var nextGlyph = ref allGlyphs[i + 1];
                        var yDiff = nextGlyph.y - glyph.y;
                        if (yDiff < 0) yDiff = -yDiff;

                        if (yDiff < LineBreakThreshold)
                        {
                            glyphWidth = (offsetX + nextGlyph.x) - glyphX;
                            if (glyphWidth < 0) glyphWidth = -glyphWidth;
                        }
                    }

                    if (glyphWidth < 1f && glyphLookup != null &&
                        glyphLookup.TryGetValue(cachedUnderscoreFont.GlyphKey((uint)glyph.glyphId), out var fontGlyph))
                    {
                        glyphWidth = fontGlyph.metrics.horizontalAdvance * underscoreScale;
                    }

                    left = glyphX;
                    right = glyphX + glyphWidth;
                }

                if (hasFlag)
                {
                    var glyphColor = ColorModifier.TryGetColor(buffers, glyph.cluster, out var customColor) ? customColor : defaultColor;
                    glyphColor.a = defaultColor.a;

                    if (!hasActiveLine)
                    {
                        lineStartX = left;
                        lineEndX = right;
                        rowBaselineY = baselineY;
                        lineBaselineY = baselineY;
                        lineVarHash = ResolveLineVarHash(fontProvider, glyph.fontId, underscoreFontHash, defaultVarHash);
                        lineColor = glyphColor;
                        hasActiveLine = true;
                    }
                    else
                    {
                        var yDiff = baselineY - rowBaselineY;
                        if (yDiff < 0) yDiff = -yDiff;

                        var colorChanged = lineColor.r != glyphColor.r || lineColor.g != glyphColor.g ||
                                           lineColor.b != glyphColor.b || lineColor.a != glyphColor.a;

                        if (yDiff > LineBreakThreshold)
                        {
                            AddSegment(lineStartX, lineEndX, lineBaselineY, lineVarHash, lineColor);
                            lineStartX = left;
                            lineEndX = right;
                            rowBaselineY = baselineY;
                            lineBaselineY = baselineY;
                            lineVarHash = ResolveLineVarHash(fontProvider, glyph.fontId, underscoreFontHash, defaultVarHash);
                            lineColor = glyphColor;
                        }
                        else if (colorChanged)
                        {
                            AddSegment(lineStartX, lineEndX, lineBaselineY, lineVarHash, lineColor);
                            lineStartX = left;
                            lineEndX = right;
                            lineBaselineY = rowBaselineY;
                            lineColor = glyphColor;
                        }
                        else
                        {
                            if (left < lineStartX) lineStartX = left;
                            if (right > lineEndX) lineEndX = right;
                        }
                    }
                }
                else if (hasActiveLine)
                {
                    AddSegment(lineStartX, lineEndX, lineBaselineY, lineVarHash, lineColor);
                    hasActiveLine = false;
                }
            }

            if (hasActiveLine)
                AddSegment(lineStartX, lineEndX, lineBaselineY, lineVarHash, lineColor);
        }

        /// <summary>
        /// Resolves varHash48 for a line segment. If the text glyph's font matches the
        /// underscore font (same base font), uses the text's variation directly.
        /// Otherwise finds a companion variation of the underscore font with matching axes.
        /// </summary>
        private long ResolveLineVarHash(UniTextFontProvider fontProvider, int glyphFontId,
            int underscoreFontHash, long defaultVarHash)
        {
            var glyphFont = fontProvider.GetFontAsset(glyphFontId);
            if (glyphFont == null) return defaultVarHash;

            if (glyphFont.FontDataHash == underscoreFontHash)
                return buffers.ResolveVarHash48(glyphFontId, glyphFont);

            var companion = buffers.FindCompanionVarHash(glyphFontId, underscoreFontHash);
            return companion != 0 ? companion : defaultVarHash;
        }
    }

}
