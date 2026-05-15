using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Configuration for text layout and positioning.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="TextLayout"/> to control how text is positioned within the available bounds.
    /// Includes settings for maximum dimensions, spacing, and alignment.
    /// </remarks>
    public struct LayoutSettings
    {
        /// <summary>Maximum width for text layout. Use <see cref="TextProcessSettings.FloatMax"/> for unlimited.</summary>
        public float maxWidth;

        /// <summary>Maximum height for text layout. Use <see cref="TextProcessSettings.FloatMax"/> for unlimited.</summary>
        public float maxHeight;

        /// <summary>Additional spacing between lines (can be negative).</summary>
        public float lineSpacing;

        /// <summary>Fallback line height when font metrics are unavailable.</summary>
        public float defaultLineHeight;

        /// <summary>Horizontal text alignment within the layout bounds.</summary>
        public HorizontalAlignment horizontalAlignment;

        /// <summary>Vertical text alignment within the layout bounds.</summary>
        public VerticalAlignment verticalAlignment;

        /// <summary>Top edge metric for text box trimming.</summary>
        public TextOverEdge overEdge;

        /// <summary>Bottom edge metric for text box trimming.</summary>
        public TextUnderEdge underEdge;

        /// <summary>How extra leading from line-height is distributed relative to the content area.</summary>
        public LeadingDistribution leadingDistribution;

        /// <summary>
        /// Gets the default layout settings with unlimited dimensions and top-left alignment.
        /// </summary>
        public static LayoutSettings Default => new()
        {
            maxWidth = TextProcessSettings.FloatMax,
            maxHeight = TextProcessSettings.FloatMax,
            lineSpacing = 0,
            defaultLineHeight = 20,
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
            overEdge = TextOverEdge.Ascent,
            underEdge = TextUnderEdge.Descent,
            leadingDistribution = LeadingDistribution.HalfLeading
        };
    }


    /// <summary>
    /// Positions glyphs within the layout bounds based on line breaking results and alignment settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the final positioning stage of the text processing pipeline. It takes the output
    /// from <see cref="LineBreaker"/> (lines and runs) and produces <see cref="PositionedGlyph"/>
    /// data with final X/Y coordinates.
    /// </para>
    /// <para>
    /// Handles:
    /// <list type="bullet">
    /// <item>Horizontal alignment (left, center, right) with RTL awareness</item>
    /// <item>Vertical alignment (top, middle, bottom)</item>
    /// <item>Line spacing and margins</item>
    /// <item>Glyph scaling based on font size</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="LayoutSettings"/>
    public sealed class TextLayout
    {
        /// <summary>
        /// Delegate for modifying line height during layout.
        /// </summary>
        /// <param name="lineIndex">Zero-based index of the current line.</param>
        /// <param name="lineStartCluster">First codepoint cluster on this line.</param>
        /// <param name="lineEndCluster">One past the last codepoint cluster on this line.</param>
        /// <param name="lineAdvance">The Y advance for this line. Modify to change spacing.</param>
        public delegate void LineHeightDelegate(int lineIndex, int lineStartCluster, int lineEndCluster, ref float lineAdvance);

        private LayoutSettings settings;

        private float fontAscender;
        private float fontDescender;
        private float fontLineHeight;
        private float fontCapHeight;
        private float glyphScale = 1f;
        private float effectiveFirstLineHeight;
        private float effectiveLastLineHeight;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextLayout"/> class with default settings.
        /// </summary>
        public TextLayout()
        {
            settings = LayoutSettings.Default;
        }

        /// <summary>
        /// Sets font metrics used for line height and baseline calculations.
        /// </summary>
        /// <param name="ascender">Distance from baseline to top of tallest glyph.</param>
        /// <param name="descender">Distance from baseline to bottom of lowest glyph (typically negative).</param>
        /// <param name="lineHeight">Total line height from the font metrics.</param>
        /// <param name="glyphScaleFactor">Scale factor applied to all glyph positions (default 1.0).</param>
        /// <param name="capHeight">Cap height for visual vertical centering (0 to skip correction).</param>
        public void SetFontMetrics(float ascender, float descender, float lineHeight, float glyphScaleFactor = 1f, float capHeight = 0f)
        {
            fontAscender = ascender;
            fontDescender = descender;
            fontLineHeight = lineHeight;
            fontCapHeight = capHeight;
            glyphScale = glyphScaleFactor;
        }

        /// <summary>
        /// Sets the layout settings controlling dimensions and alignment.
        /// </summary>
        /// <param name="newSettings">The new layout settings to apply.</param>
        public void SetLayoutSettings(LayoutSettings newSettings)
        {
            settings = newSettings;
        }

        /// <summary>
        /// Sets the effective line heights after modifier callbacks, used for half-leading calculation.
        /// </summary>
        /// <param name="firstLineHeight">Effective height of the first line (0 = use base metrics).</param>
        /// <param name="lastLineHeight">Effective height of the last line (0 = use base metrics).</param>
        public void SetEffectiveLineHeights(float firstLineHeight, float lastLineHeight)
        {
            effectiveFirstLineHeight = firstLineHeight;
            effectiveLastLineHeight = lastLineHeight;
        }

        /// <summary>
        /// Positions all glyphs from the line breaking results into final screen coordinates.
        /// </summary>
        /// <param name="lines">The lines produced by line breaking.</param>
        /// <param name="runs">The shaped runs referenced by lines.</param>
        /// <param name="glyphs">The shaped glyphs referenced by runs.</param>
        /// <param name="perLineAdvances">Pre-computed Y advances for each line (from TextProcessor).</param>
        /// <param name="totalHeight">Pre-computed total text height (from TextProcessor).</param>
        /// <param name="result">Output array to receive positioned glyphs.</param>
        /// <param name="glyphCount">Returns the number of positioned glyphs written.</param>
        /// <param name="width">Returns the maximum line width encountered.</param>
        /// <param name="height">Returns the total text height.</param>
        /// <remarks>
        /// <para>
        /// The method iterates through all lines, applying horizontal alignment per-line
        /// and accounting for RTL paragraphs. Vertical positioning starts from the top
        /// and advances downward by line height plus spacing.
        /// </para>
        /// <para>
        /// Each glyph's final position combines the line's X offset, the glyph's advance
        /// within the run, and any glyph-specific offsets from shaping (e.g., diacritics).
        /// </para>
        /// </remarks>
        public void Layout(
            ReadOnlySpan<TextLine> lines,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<float> perLineAdvances,
            float totalHeight,
            PositionedGlyph[] result,
            ref int glyphCount,
            out float width,
            out float height)
        {
            glyphCount = 0;
            width = 0;
            height = 0;

            var lineCount = lines.Length;
            if (lineCount == 0)
                return;

            var computedLineHeight = fontLineHeight;
            if (computedLineHeight <= 0)
                computedLineHeight = fontAscender - fontDescender;
            if (computedLineHeight <= 0)
                computedLineHeight = settings.defaultLineHeight;

            var ascender = fontAscender;
            if (ascender <= 0) ascender = computedLineHeight * 0.8f;

            var contentArea = ascender - fontDescender;
            var firstLeading = MathF.Max(0, effectiveFirstLineHeight - contentArea);
            var topLeading = settings.leadingDistribution switch
            {
                LeadingDistribution.LeadingAbove => firstLeading,
                LeadingDistribution.LeadingBelow => 0f,
                _ => firstLeading * 0.5f
            };

            float topMetric = settings.overEdge switch
            {
                TextOverEdge.CapHeight when fontCapHeight > 0 => fontCapHeight,
                TextOverEdge.HalfLeading => ascender + topLeading,
                _ => ascender
            };

            var trimAmount = ComputeTrimAmount(ascender, fontDescender,
                fontCapHeight, settings.overEdge, settings.underEdge,
                settings.leadingDistribution,
                effectiveFirstLineHeight, effectiveLastLineHeight);

            var effectiveHeight = totalHeight - trimAmount;

            var y = ComputeTextStartY(effectiveHeight, settings) + topMetric;
            float maxLineWidth = 0;

            var availableWidth = settings.maxWidth;
            var hAlign = settings.horizontalAlignment;
            var hasFiniteWidth = !float.IsInfinity(availableWidth) && availableWidth > 0;

            for (var i = 0; i < lineCount; i++)
            {
                ref readonly var line = ref lines[i];
                var runStart = line.runStart;
                var runCount = line.runCount;
                var runEnd = runStart + runCount;

                var lineWidth = line.width * glyphScale;

                float x;
                var isRtlLine = (line.paragraphBaseLevel & 1) == 1;
                if (hasFiniteWidth)
                    x = ComputeLineStartX(lineWidth, isRtlLine, availableWidth, hAlign);
                else
                    x = 0;

                if (line.startMargin > 0 && hasFiniteWidth)
                {
                    var margin = line.startMargin * glyphScale;
                    if (isRtlLine)
                    {
                        if (hAlign == HorizontalAlignment.Left)
                            x -= margin;
                        else if (hAlign == HorizontalAlignment.Center) x = (availableWidth - margin - lineWidth) * 0.5f;
                    }
                    else
                    {
                        if (hAlign == HorizontalAlignment.Left)
                            x += margin;
                        else if (hAlign == HorizontalAlignment.Center)
                            x = margin + (availableWidth - margin - lineWidth) * 0.5f;
                    }
                }

                for (var r = runStart; r < runEnd; r++)
                {
                    ref readonly var run = ref runs[r];
                    var glyphStart = run.glyphStart;
                    var glyphLen = run.glyphCount;

                    var fontId = run.fontId;
                    var glyphEnd = glyphStart + glyphLen;

                    for (var g = glyphStart; g < glyphEnd; g++)
                    {
                        ref readonly var glyph = ref glyphs[g];
                        var glyphX = x + glyph.offsetX * glyphScale;
                        var advanceScaled = glyph.advanceX * glyphScale;

                        var boundsTop = y - ascender;
                        var boundsBottom = y - fontDescender;

                        result[glyphCount++] = new PositionedGlyph
                        {
                            glyphId = glyph.glyphId,
                            cluster = glyph.cluster,
                            x = glyphX,
                            y = y - glyph.offsetY * glyphScale,
                            fontId = fontId,
                            shapedGlyphIndex = g,
                            left = x,
                            right = x + advanceScaled,
                            top = boundsTop,
                            bottom = boundsBottom
                        };
                        x += advanceScaled;
                    }
                }

                if (lineWidth > maxLineWidth)
                    maxLineWidth = lineWidth;

                if (i < perLineAdvances.Length)
                    y += perLineAdvances[i];
            }

            width = maxLineWidth;
            height = effectiveHeight;
        }

        /// <summary>
        /// Computes the total height trim based on edge metrics and leading distribution.
        /// </summary>
        /// <param name="ascender">Font ascender value.</param>
        /// <param name="descender">Font descender value (typically negative).</param>
        /// <param name="capHeight">Font cap height (0 if unavailable).</param>
        /// <param name="overEdge">Top edge metric.</param>
        /// <param name="underEdge">Bottom edge metric.</param>
        /// <param name="distribution">How extra leading is distributed.</param>
        /// <param name="effectiveFirstLineHeight">Effective height of the first line (including modifier adjustments).</param>
        /// <param name="effectiveLastLineHeight">Effective height of the last line (including modifier adjustments).</param>
        /// <returns>The total amount to subtract from raw height to get effective height.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeTrimAmount(
            float ascender, float descender,
            float capHeight, TextOverEdge overEdge, TextUnderEdge underEdge,
            LeadingDistribution distribution,
            float effectiveFirstLineHeight, float effectiveLastLineHeight)
        {
            var contentArea = ascender - descender;
            var firstLeading = MathF.Max(0, effectiveFirstLineHeight - contentArea);
            var lastLeading = MathF.Max(0, effectiveLastLineHeight - contentArea);

            var topLeading = distribution switch
            {
                LeadingDistribution.LeadingAbove => firstLeading,
                LeadingDistribution.LeadingBelow => 0f,
                _ => firstLeading * 0.5f
            };

            var bottomLeading = distribution switch
            {
                LeadingDistribution.LeadingAbove => 0f,
                LeadingDistribution.LeadingBelow => lastLeading,
                _ => lastLeading * 0.5f
            };

            float topTrim = overEdge switch
            {
                TextOverEdge.CapHeight when capHeight > 0 => ascender - capHeight,
                TextOverEdge.HalfLeading => -topLeading,
                _ => 0f
            };

            float bottomTrim = underEdge switch
            {
                TextUnderEdge.Baseline => -descender,
                TextUnderEdge.HalfLeading => -bottomLeading,
                _ => 0f
            };

            return topTrim + bottomTrim;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeLineStartX(float lineWidth, bool isRtlLine, float availableWidth,
            HorizontalAlignment alignment)
        {
            return alignment switch
            {
                HorizontalAlignment.Left => isRtlLine ? availableWidth - lineWidth : 0,
                HorizontalAlignment.Right => isRtlLine ? 0 : availableWidth - lineWidth,
                _ => (availableWidth - lineWidth) * 0.5f
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ComputeTextStartY(float totalTextHeight, LayoutSettings settings)
        {
            var availableHeight = settings.maxHeight;
            if (float.IsInfinity(availableHeight) || availableHeight <= 0)
                return 0;

            return settings.verticalAlignment switch
            {
                VerticalAlignment.Middle => (availableHeight - totalTextHeight) * 0.5f
                    + (settings.overEdge == TextOverEdge.Ascent && fontCapHeight > 0
                        ? (fontCapHeight - fontAscender - fontDescender) * 0.5f : 0f),
                VerticalAlignment.Bottom => availableHeight - totalTextHeight,
                _ => 0
            };
        }
    }
}
