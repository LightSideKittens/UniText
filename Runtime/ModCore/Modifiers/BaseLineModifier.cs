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
            public Color32 color;
        }

        private const float LineBreakThreshold = 5f;

        protected PooledArrayAttribute<byte> flagsAttribute;

        private LineSegment[] lineSegments;
        private int lineSegmentsCapacity;
        private int lineSegmentCount;


        protected abstract string AttributeKey { get; }

        protected abstract float GetLineOffset(FaceInfo faceInfo, float scale);
        protected abstract void SetStaticBuffer(byte[] buffer);
        
        protected sealed override void OnEnable()
        {
            flagsAttribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<byte>>(AttributeKey);
            var cpCount = buffers.codepoints.count;
            flagsAttribute.EnsureCountAndClear(cpCount);
            SetStaticBuffer(flagsAttribute.buffer.data);

            if (lineSegments == null)
            {
                lineSegments = UniTextArrayPool<LineSegment>.Rent(64);
                lineSegmentsCapacity = 64;
            }
            lineSegmentCount = 0;

            uniText.Rebuilding += OnRebuilding;
            uniText.MeshGenerator.OnAfterGlyphsPerFont += OnAfterGlyphs;
        }

        protected sealed override void OnDisable()
        {
            uniText.Rebuilding -= OnRebuilding;
            uniText.MeshGenerator.OnAfterGlyphsPerFont -= OnAfterGlyphs;
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

        protected void EnsureBufferCapacity(int required)
        {
            if (flagsAttribute == null || flagsAttribute.buffer.Capacity < required)
            {
                flagsAttribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<byte>>(AttributeKey);
                flagsAttribute.EnsureCountAndClear(required);
            }
        }

        private void OnRebuilding()
        {
            flagsAttribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKey);
            SetStaticBuffer(flagsAttribute?.buffer.data);
        }

        private void AddSegment(float startX, float endX, float baselineY, Color32 color)
        {
            if (lineSegmentCount >= lineSegmentsCapacity)
            {
                var newCap = lineSegmentsCapacity * 2;
                var newBuffer = UniTextArrayPool<LineSegment>.Rent(newCap);
                lineSegments.AsSpan(0, lineSegmentCount).CopyTo(newBuffer);
                UniTextArrayPool<LineSegment>.Return(lineSegments);
                lineSegments = newBuffer;
                lineSegmentsCapacity = newCap;
            }

            lineSegments[lineSegmentCount] = new LineSegment
            {
                startX = startX,
                endX = endX,
                baselineY = baselineY,
                color = color
            };
            lineSegmentCount++;
        }

        private void OnAfterGlyphs()
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen == null) return;

            var currentFont = gen.font;
            if (currentFont == null) return;

            var fontProvider = uniText.FontProvider;
            var underscoreFontId = fontProvider.FindFontForCodepoint('_');
            var underscoreFont = fontProvider.GetFontAsset(underscoreFontId);

            if (underscoreFont != currentFont) return;

            var flagsBuffer = flagsAttribute?.buffer.data;
            if (flagsBuffer == null || !flagsBuffer.HasAnyFlags())
                return;

            lineSegmentCount = 0;

            var scale = gen.scale;
            var offsetX = gen.offsetX;
            var offsetY = gen.offsetY;
            var defaultColor = gen.defaultColor;

            var allGlyphs = buffers.positionedGlyphs.data;
            var glyphCount = buffers.positionedGlyphs.count;
            if (glyphCount == 0) return;

            var glyphLookup = currentFont.GlyphLookupTable;

            float lineStartX = 0, lineEndX = 0, lineBaselineY = 0;
            float rowBaselineY = 0;
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
                        glyphLookup.TryGetValue((uint)glyph.glyphId, out var fontGlyph))
                    {
                        glyphWidth = fontGlyph.metrics.horizontalAdvance * scale;
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
                            AddSegment(lineStartX, lineEndX, lineBaselineY, lineColor);
                            lineStartX = left;
                            lineEndX = right;
                            rowBaselineY = baselineY;
                            lineBaselineY = baselineY;
                            lineColor = glyphColor;
                        }
                        else if (colorChanged)
                        {
                            AddSegment(lineStartX, lineEndX, lineBaselineY, lineColor);
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
                    AddSegment(lineStartX, lineEndX, lineBaselineY, lineColor);
                    hasActiveLine = false;
                }
            }

            if (hasActiveLine)
                AddSegment(lineStartX, lineEndX, lineBaselineY, lineColor);

            if (lineSegmentCount == 0)
                return;

            var lineOffset = GetLineOffset(currentFont.FaceInfo, scale);
            for (var i = 0; i < lineSegmentCount; i++)
            {
                ref var seg = ref lineSegments[i];
                LineRenderHelper.DrawLine(fontProvider, seg.startX, seg.endX, seg.baselineY, lineOffset, seg.color);
            }
        }
    }

}
