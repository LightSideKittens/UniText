using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Configuration settings for text processing operations.
    /// </summary>
    /// <remarks>
    /// This struct encapsulates all parameters needed by <see cref="TextProcessor"/> to process,
    /// shape, and lay out Unicode text. It combines layout settings with text-specific options
    /// like font size and base direction.
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="LayoutSettings"/>
    public struct TextProcessSettings
    {
        /// <summary>
        /// Maximum float value used to represent unlimited width or height.
        /// </summary>
        public const float FloatMax = 32767f;

        /// <summary>
        /// Layout settings including alignment, max dimensions, and spacing.
        /// </summary>
        public LayoutSettings layout;

        /// <summary>
        /// Font size in points for text rendering.
        /// </summary>
        public float fontSize;

        /// <summary>
        /// Base paragraph direction for bidirectional text processing.
        /// </summary>
        /// <remarks>
        /// When set to <see cref="TextDirection.Auto"/>, the direction is determined
        /// automatically from the text content using the Unicode BiDi algorithm (UAX #9).
        /// </remarks>
        public TextDirection baseDirection;

        /// <summary>
        /// Gets or sets a value indicating whether word wrapping is enabled.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to wrap text at word boundaries when exceeding
        /// <see cref="MaxWidth"/>; otherwise, <see langword="false"/>.
        /// </value>
        public bool enableWordWrap;

        /// <summary>
        /// Gets or sets the maximum width for text layout.
        /// </summary>
        /// <value>
        /// The maximum width in pixels. Use <see cref="FloatMax"/> for unlimited width.
        /// </value>
        public float MaxWidth
        {
            get => layout.maxWidth;
            set => layout.maxWidth = value;
        }

        /// <summary>
        /// Gets or sets the maximum height for text layout.
        /// </summary>
        /// <value>
        /// The maximum height in pixels. Use <see cref="FloatMax"/> or
        /// <see cref="float.PositiveInfinity"/> for unlimited height.
        /// </value>
        public float MaxHeight
        {
            get => layout.maxHeight;
            set => layout.maxHeight = value;
        }

        /// <summary>
        /// Gets or sets the horizontal text alignment.
        /// </summary>
        public HorizontalAlignment HorizontalAlignment
        {
            get => layout.horizontalAlignment;
            set => layout.horizontalAlignment = value;
        }

        /// <summary>
        /// Gets or sets the vertical text alignment.
        /// </summary>
        public VerticalAlignment VerticalAlignment
        {
            get => layout.verticalAlignment;
            set => layout.verticalAlignment = value;
        }

        /// <summary>
        /// Gets or sets the additional spacing between lines.
        /// </summary>
        /// <value>
        /// Extra spacing in pixels added between text lines. Default is 0.
        /// </value>
        public float LineSpacing
        {
            get => layout.lineSpacing;
            set => layout.lineSpacing = value;
        }

        /// <summary>
        /// Gets or sets the top edge metric for text box trimming.
        /// </summary>
        public TextOverEdge OverEdge
        {
            get => layout.overEdge;
            set => layout.overEdge = value;
        }

        /// <summary>
        /// Gets or sets the bottom edge metric for text box trimming.
        /// </summary>
        public TextUnderEdge UnderEdge
        {
            get => layout.underEdge;
            set => layout.underEdge = value;
        }

        /// <summary>
        /// Gets or sets how extra leading from line-height is distributed.
        /// </summary>
        public LeadingDistribution LeadingDistribution
        {
            get => layout.leadingDistribution;
            set => layout.leadingDistribution = value;
        }
    }

    /// <summary>
    /// Processes Unicode text through script analysis, BiDi reordering, shaping, and layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TextProcessor"/> is the main entry point for the text processing pipeline.
    /// It orchestrates multiple Unicode algorithms to produce correctly shaped and positioned glyphs.
    /// </para>
    /// <para>
    /// <b>Processing pipeline:</b>
    /// </para>
    /// <list type="number">
    /// <item><description>Parsing — converts UTF-16 to codepoints</description></item>
    /// <item><description>Script analysis (UAX #24) — identifies script per codepoint</description></item>
    /// <item><description>BiDi algorithm (UAX #9) — determines text direction and reordering</description></item>
    /// <item><description>Itemization — splits text into runs by script, direction, and font</description></item>
    /// <item><description>Shaping — converts codepoints to positioned glyphs via HarfBuzz</description></item>
    /// <item><description>Line breaking (UAX #14) — determines line break opportunities</description></item>
    /// <item><description>Layout — positions glyphs according to alignment settings</description></item>
    /// </list>
    /// <para>
    /// <b>Performance:</b> The processor caches intermediate results. Use invalidation methods
    /// only when necessary to avoid redundant processing.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var buffers = new UniTextBuffers();
    /// var processor = new TextProcessor(buffers);
    /// processor.SetFontProvider(fontProvider);
    ///
    /// var settings = new TextProcessSettings
    /// {
    ///     fontSize = 24f,
    ///     MaxWidth = 400f,
    ///     enableWordWrap = true
    /// };
    ///
    /// processor.EnsureFirstPass(text, settings);
    /// processor.EnsureLines(settings.MaxWidth, settings.fontSize, settings.enableWordWrap);
    /// processor.EnsurePositions(settings);
    ///
    /// // Access results
    /// var glyphs = processor.PositionedGlyphs;
    /// </code>
    /// </example>
    /// <seealso cref="UniTextBuffers"/>
    /// <seealso cref="TextProcessSettings"/>
    /// <seealso href="https://unicode.org/reports/tr9/">UAX #9: Unicode Bidirectional Algorithm</seealso>
    /// <seealso href="https://unicode.org/reports/tr14/">UAX #14: Unicode Line Breaking Algorithm</seealso>
    /// <seealso href="https://unicode.org/reports/tr24/">UAX #24: Unicode Script Property</seealso>
    public sealed class TextProcessor
    {
        private static BidiEngine BidiEngine => SharedPipelineComponents.BidiEngine;
        private static ScriptAnalyzer ScriptAnalyzer => SharedPipelineComponents.ScriptAnalyzer;
        private static GraphemeBreaker GraphemeBreaker => SharedPipelineComponents.GraphemeBreaker;
        private static Shaper Shaper => SharedPipelineComponents.Shaper;
        private static LineBreaker LineBreaker => SharedPipelineComponents.LineBreaker;
        private static TextLayout Layout => SharedPipelineComponents.Layout;

        /// <summary>
        /// The buffer container holding all intermediate and final processing results.
        /// </summary>
        public readonly UniTextBuffers buf;

        private UniTextFontProvider fontProvider;
        private float resultWidth;
        private float resultHeight;

        private bool hasValidFirstPassData;
        private bool hasValidGlyphsInAtlas;

        internal UniTextFontProvider FontProviderForAtlas => fontProvider;
        internal bool HasValidGlyphsInAtlas { get => hasValidGlyphsInAtlas; set => hasValidGlyphsInAtlas = value; }

        private float lastLinesWidth = -1;
        private float lastLinesFontSize = -1;
        private bool lastLinesWordWrap;
        private bool hasValidLinesData;

        private float lastLayoutMaxHeight = -1;
        private HorizontalAlignment lastLayoutHAlign;
        private VerticalAlignment lastLayoutVAlign;
        private bool hasValidPositionedGlyphs;

        private TextProcessSettings lastSettings;

        private float cachedRawHeight;
        private float cachedHeightFontSize = -1;
        private float cachedMainAscender;
        private float cachedMainDescender;
        private float cachedMainLineHeight;
        private float cachedEffectiveFirstLineHeight;
        private float cachedEffectiveLastLineHeight;

        private ReadOnlyMemory<char> lastText;

        /// <summary>
        /// Occurs after text parsing completes but before shaping.
        /// </summary>
        /// <remarks>
        /// Use this event to inspect or modify parsed codepoints before shaping occurs.
        /// At this point, <see cref="UniTextBuffers.codepoints"/> contains the parsed text.
        /// </remarks>
        public event Action Parsed;

        /// <summary>
        /// Occurs after text shaping completes.
        /// </summary>
        /// <remarks>
        /// At this point, <see cref="UniTextBuffers.shapedGlyphs"/> and
        /// <see cref="UniTextBuffers.shapedRuns"/> contain the shaping results.
        /// </remarks>
        public event Action Shaped;

        /// <summary>
        /// Occurs after layout calculation completes.
        /// </summary>
        /// <remarks>
        /// At this point, <see cref="PositionedGlyphs"/> contains the final glyph positions
        /// ready for rendering.
        /// </remarks>
        public event Action LayoutComplete;

        /// <summary>
        /// Invoked for each line during layout to allow modifiers to adjust line height.
        /// </summary>
        /// <remarks>
        /// The callback receives the line index, cluster range, and a ref to the line advance.
        /// Modifiers can increase line advance to add spacing between lines.
        /// </remarks>
        public TextLayout.LineHeightDelegate OnCalculateLineHeight;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextProcessor"/> class.
        /// </summary>
        /// <param name="uniTextBuffers">The buffer container for processing data.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="uniTextBuffers"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="UnicodeData"/> has not been initialized.
        /// </exception>
        public TextProcessor(UniTextBuffers uniTextBuffers)
        {
            buf = uniTextBuffers ?? throw new ArgumentNullException(nameof(uniTextBuffers));
            if (UnicodeData.Provider == null)
                throw new InvalidOperationException("UnicodeData not initialized.");
        }

        /// <summary>
        /// Sets the font provider used for font lookup and glyph metrics.
        /// </summary>
        /// <param name="provider">The font provider to use, or <see langword="null"/> to clear.</param>
        public void SetFontProvider(UniTextFontProvider provider)
        {
            if (fontProvider != provider)
                hasValidGlyphsInAtlas = false;
            fontProvider = provider;
        }

        /// <summary>
        /// Gets a value indicating whether valid first pass data (parsing, BiDi, shaping) is available.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if first pass processing has completed successfully;
        /// otherwise, <see langword="false"/>.
        /// </value>
        public bool HasValidFirstPassData => hasValidFirstPassData;

        /// <summary>
        /// Gets a value indicating whether valid positioned glyphs are available.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if layout has completed and <see cref="PositionedGlyphs"/>
        /// contains valid data; otherwise, <see langword="false"/>.
        /// </value>
        public bool HasValidPositionedGlyphs => hasValidPositionedGlyphs;

        /// <summary>
        /// Invalidates all cached processing data, forcing a complete reprocess on next call.
        /// </summary>
        /// <remarks>
        /// Call this method when the text content changes. This invalidates first pass data
        /// (parsing, BiDi, shaping) and all dependent layout data.
        /// </remarks>
        public void InvalidateFirstPassData()
        {
            hasValidFirstPassData = false;
            hasValidGlyphsInAtlas = false;
            InvalidateLayoutData();
        }

        /// <summary>
        /// Invalidates cached layout data while preserving shaping results.
        /// </summary>
        /// <remarks>
        /// Call this method when layout parameters change (width, font size, word wrap)
        /// but the text content remains the same. Shaping data is preserved.
        /// </remarks>
        public void InvalidateLayoutData()
        {
            hasValidLinesData = false;
            hasValidPositionedGlyphs = false;
            lastLinesWidth = -1;
            lastLinesFontSize = -1;
            lastLinesWordWrap = false;
            lastLayoutMaxHeight = -1;
        }

        /// <summary>
        /// Invalidates cached glyph positions while preserving line break data.
        /// </summary>
        /// <remarks>
        /// Call this method when alignment or max height changes but line breaks remain valid.
        /// </remarks>
        public void InvalidatePositionedGlyphs()
        {
            hasValidPositionedGlyphs = false;
            lastLayoutMaxHeight = -1;
        }

        /// <summary>
        /// Ensures the first pass processing (parsing, BiDi, shaping) is complete.
        /// </summary>
        /// <param name="text">The Unicode text to process.</param>
        /// <param name="settings">The processing settings including font size and direction.</param>
        /// <remarks>
        /// <para>
        /// This method performs the first pass of text processing if not already cached:
        /// parsing, script analysis, BiDi analysis, itemization, and shaping.
        /// </para>
        /// <para>
        /// If <see cref="HasValidFirstPassData"/> is <see langword="true"/>, this method
        /// returns immediately without reprocessing.
        /// </para>
        /// </remarks>
        public void EnsureFirstPass(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            UniTextDebug.Increment(ref UniTextDebug.TextProcessor_EnsureShapingCount);

            if (hasValidFirstPassData) return;
            
            UniTextDebug.BeginSample("TextProcessor.EnsureShaping");

            buf.Reset();

            if (text.IsEmpty)
            {
                hasValidFirstPassData = false;
                UniTextDebug.EndSample();
                return;
            }

            fontProvider.SetFontSize(settings.fontSize);
            DoFirstPass(text, settings);

            UniTextDebug.EndSample();
        }

        /// <summary>
        /// Determines whether cached line data can be reused for the specified parameters.
        /// </summary>
        /// <param name="width">The maximum width for line breaking.</param>
        /// <param name="fontSize">The font size in points.</param>
        /// <param name="wordWrap">Whether word wrapping is enabled.</param>
        /// <returns>
        /// <see langword="true"/> if cached line data matches the parameters;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanReuseLines(float width, float fontSize, bool wordWrap)
        {
            return hasValidLinesData &&
                   Math.Abs(lastLinesWidth - width) < 0.001f &&
                   Math.Abs(lastLinesFontSize - fontSize) < 0.001f &&
                   lastLinesWordWrap == wordWrap;
        }

        /// <summary>
        /// Determines whether cached glyph positions can be reused for the specified parameters.
        /// </summary>
        /// <param name="maxHeight">The maximum height for layout.</param>
        /// <param name="hAlign">The horizontal alignment.</param>
        /// <param name="vAlign">The vertical alignment.</param>
        /// <returns>
        /// <see langword="true"/> if cached positions match the parameters;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanReusePositions(float maxHeight, HorizontalAlignment hAlign, VerticalAlignment vAlign)
        {
            if (!hasValidPositionedGlyphs) return false;

            var heightMatches = (float.IsInfinity(lastLayoutMaxHeight) && float.IsInfinity(maxHeight)) ||
                                Math.Abs(lastLayoutMaxHeight - maxHeight) < 0.001f;

            return heightMatches && lastLayoutHAlign == hAlign && lastLayoutVAlign == vAlign;
        }

        /// <summary>
        /// Ensures line breaking is complete for the specified parameters.
        /// </summary>
        /// <param name="width">The maximum width for line breaking in pixels.</param>
        /// <param name="fontSize">The font size in points.</param>
        /// <param name="wordWrap">Whether to wrap text at word boundaries.</param>
        /// <remarks>
        /// <para>
        /// Requires <see cref="EnsureFirstPass"/> to be called first.
        /// If parameters match cached values, returns immediately.
        /// </para>
        /// </remarks>
        public void EnsureLines(float width, float fontSize, bool wordWrap)
        {
            if (!hasValidFirstPassData) return;
            if (CanReuseLines(width, fontSize, wordWrap)) return;

            UniTextDebug.BeginSample("TextProcessor.EnsureLines");
            EnsureLinesInternal(width, fontSize, wordWrap, buf.cpWidths.Span);
            UniTextDebug.EndSample();
        }

        /// <summary>
        /// Ensures final glyph positioning is complete for the specified settings.
        /// </summary>
        /// <param name="settings">The layout settings including alignment and max dimensions.</param>
        /// <remarks>
        /// <para>
        /// Requires <see cref="EnsureLines"/> to be called first.
        /// If parameters match cached values, returns immediately.
        /// </para>
        /// <para>
        /// After this method completes successfully, <see cref="PositionedGlyphs"/>
        /// contains the final glyph data ready for rendering.
        /// </para>
        /// </remarks>
        public void EnsurePositions(TextProcessSettings settings)
        {
            if (!hasValidLinesData) return;
            if (CanReusePositions(settings.MaxHeight, settings.HorizontalAlignment, settings.VerticalAlignment)) return;

            UniTextDebug.BeginSample("TextProcessor.EnsurePositions");

            lastSettings = settings;
            buf.positionedGlyphs.count = 0;
            LayoutText(settings);

            lastLayoutMaxHeight = settings.MaxHeight;
            lastLayoutHAlign = settings.HorizontalAlignment;
            lastLayoutVAlign = settings.VerticalAlignment;
            hasValidPositionedGlyphs = true;

            LayoutComplete?.Invoke();

            UniTextDebug.EndSample();
        }

        private void DoFirstPass(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            UniTextDebug.Increment(ref UniTextDebug.TextProcessor_DoFullShapingCount);

            buf.shapingFontSize = settings.fontSize;

            UniTextDebug.BeginSample("TextProcessor.Parse");
            Parse(text);
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.ComputeBreakOpportunities");
            ComputeBreakOpportunities();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.Parsed?.Invoke()");
            Parsed?.Invoke();
            UniTextDebug.EndSample();

            if (buf.codepoints.count == 0)
            {
                hasValidFirstPassData = false;
                hasValidLinesData = false;
                hasValidPositionedGlyphs = false;
                return;
            }

            UniTextDebug.BeginSample("TextProcessor.AnalyzeBidi");
            AnalyzeBidi(settings.baseDirection);
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.AnalyzeScripts");
            AnalyzeScripts();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.Itemize");
            Itemize();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.Shape");
            Shape();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.Shaped?.Invoke()");
            Shaped?.Invoke();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.ComputeCpWidths");
            ComputeCpWidths();
            UniTextDebug.EndSample();

            hasValidFirstPassData = true;

            Cat.MeowFormat("[TextProcessor] FirstPass: {0} codepoints, {1} runs, {2} glyphs",
                buf.codepoints.count, buf.shapedRuns.count, buf.shapedGlyphs.count);
        }

        /// <summary>
        /// Gets the total width of all text runs without line wrapping.
        /// </summary>
        /// <returns>The unwrapped text width at the shaping font size, or 0 if no valid data.</returns>
        /// <remarks>
        /// This returns the width as if the text were rendered on a single line
        /// at the font size used during shaping.
        /// </remarks>
        public float GetUnwrappedWidth()
        {
            if (!hasValidFirstPassData) return 0;

            float total = 0;
            var count = buf.shapedRuns.count;
            for (var i = 0; i < count; i++)
                total += buf.shapedRuns[i].width;
            return total;
        }

        /// <summary>
        /// Gets the preferred width for the text at the specified font size.
        /// </summary>
        /// <param name="fontSize">The font size in points.</param>
        /// <returns>The preferred width in pixels, or 0 if no valid data.</returns>
        /// <remarks>
        /// Returns the width of the widest line, accounting for explicit line breaks
        /// but not word wrapping. Use this for auto-sizing calculations.
        /// </remarks>
        public float GetPreferredWidth(float fontSize)
        {
            if (!hasValidFirstPassData) return 0;
            var glyphScale = buf.GetGlyphScale(fontSize);
            return Mathf.Ceil(GetMaxLineWidth() * glyphScale);
        }

        /// <summary>
        /// Gets the preferred height for the text at the specified font size.
        /// </summary>
        /// <param name="fontSize">The font size in points.</param>
        /// <param name="lineSpacing">Additional spacing between lines. Default is 0.</param>
        /// <returns>The preferred height in pixels, or 0 if no valid line data.</returns>
        /// <remarks>
        /// Requires <see cref="EnsureLines"/> to be called first.
        /// Returns cached height computed after line breaking, which includes any
        /// per-line adjustments from <see cref="OnCalculateLineHeight"/>.
        /// </remarks>
        public float GetPreferredHeight(float fontSize, float lineSpacing = 0f,
            TextOverEdge overEdge = TextOverEdge.Ascent, TextUnderEdge underEdge = TextUnderEdge.Descent,
            LeadingDistribution leadingDistribution = LeadingDistribution.HalfLeading)
        {
            if (!hasValidLinesData) return 0;

            if (!(Math.Abs(cachedHeightFontSize - fontSize) < 0.001f && lineSpacing == 0f))
                ComputeLineHeights(fontSize, lineSpacing, leadingDistribution);

            var capHeight = fontProvider?.GetCapHeight(fontSize) ?? 0f;
            var trim = TextLayout.ComputeTrimAmount(cachedMainAscender, cachedMainDescender,
                capHeight, overEdge, underEdge, leadingDistribution,
                cachedEffectiveFirstLineHeight, cachedEffectiveLastLineHeight);
            return cachedRawHeight - trim;
        }

        /// <summary>
        /// Gets the maximum width among all lines, considering explicit line breaks.
        /// </summary>
        /// <returns>The maximum line width at the shaping font size, or 0 if no valid data.</returns>
        public float GetMaxLineWidth()
        {
            if (!hasValidFirstPassData) return 0;

            var cpCount = buf.codepoints.count;
            var widths = buf.cpWidths.data;
            var breakOps = buf.breakOpportunities.data;
            var margins = buf.startMargins.data;

            var maxWidth = 0f;
            var currentWidth = 0f;
            var lineStartCp = 0;

            for (var cp = 0; cp < cpCount; cp++)
            {
                currentWidth += widths[cp];

                if (breakOps[cp + 1] == LineBreakType.Mandatory)
                {
                    var lineMargin = lineStartCp < margins.Length ? margins[lineStartCp] : 0;
                    if (currentWidth + lineMargin > maxWidth)
                        maxWidth = currentWidth + lineMargin;

                    currentWidth = 0f;
                    lineStartCp = cp + 1;
                }
            }

            var lastLineMargin = lineStartCp < margins.Length ? margins[lineStartCp] : 0;
            var lastLineWidth = currentWidth + lastLineMargin;
            if (lastLineWidth > maxWidth) maxWidth = lastLineWidth;

            return maxWidth > 0 ? maxWidth : GetUnwrappedWidth();
        }

        /// <summary>
        /// Finds the optimal font size to fit text within the specified dimensions.
        /// </summary>
        /// <param name="minSize">The minimum allowed font size.</param>
        /// <param name="maxSize">The maximum allowed font size.</param>
        /// <param name="targetWidth">The target width in pixels.</param>
        /// <param name="targetHeight">The target height in pixels.</param>
        /// <param name="baseSettings">The base processing settings.</param>
        /// <returns>
        /// The optimal font size that fits the text within the target dimensions,
        /// clamped between <paramref name="minSize"/> and <paramref name="maxSize"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Uses binary search to find the largest font size that fits within the constraints.
        /// Requires <see cref="HasValidFirstPassData"/> to be <see langword="true"/>.
        /// </para>
        /// <para>
        /// <b>Performance:</b> May perform multiple line breaking iterations during search.
        /// </para>
        /// </remarks>
        public float FindOptimalFontSize(
            float minSize,
            float maxSize,
            float targetWidth,
            float targetHeight,
            TextProcessSettings baseSettings)
        {
            if (!hasValidFirstPassData) return minSize;
            if (targetWidth <= 0 || targetHeight <= 0) return minSize;
            if (buf.shapingFontSize <= 0) return minSize;

            var unwrappedWidth = GetUnwrappedWidth();
            var maxGlyphScale = maxSize / buf.shapingFontSize;
            var scaledUnwrappedWidth = unwrappedWidth * maxGlyphScale;

            if (OnCalculateLineHeight == null && (!baseSettings.enableWordWrap || scaledUnwrappedWidth <= targetWidth))
            {
                var lineCount = 1;
                var maxLineWidth = 0f;
                var currentLineWidth = 0f;
                var codepoints = buf.codepoints.Span;
                var glyphs = buf.shapedGlyphs.Span;
                var margins = buf.startMargins.data;
                var glyphIdx = 0;
                var lineStartIdx = 0;

                for (var i = 0; i < codepoints.Length; i++)
                {
                    var cp = codepoints[i];
                    if (UnicodeData.IsLineBreak(cp))
                    {
                        var lineMargin = lineStartIdx < margins.Length ? margins[lineStartIdx] : 0;
                        var totalLineWidth = currentLineWidth + lineMargin;
                        if (totalLineWidth > maxLineWidth) maxLineWidth = totalLineWidth;
                        currentLineWidth = 0f;
                        lineStartIdx = i + 1;
                        lineCount++;
                        if (glyphIdx < glyphs.Length && glyphs[glyphIdx].advanceX == 0f)
                            glyphIdx++;
                    }
                    else if (cp == '\r')
                    {
                        if (glyphIdx < glyphs.Length && glyphs[glyphIdx].advanceX == 0f)
                            glyphIdx++;
                    }
                    else
                    {
                        if (glyphIdx < glyphs.Length)
                        {
                            currentLineWidth += glyphs[glyphIdx].advanceX;
                            glyphIdx++;
                        }
                    }
                }

                var lastLineMargin = lineStartIdx < margins.Length ? margins[lineStartIdx] : 0;
                var lastLineTotal = currentLineWidth + lastLineMargin;
                if (lastLineTotal > maxLineWidth) maxLineWidth = lastLineTotal;

                if (maxLineWidth <= 0f) maxLineWidth = 1f;

                var widthLimitedSize = targetWidth / maxLineWidth * buf.shapingFontSize;

                float lineHeightRatio, ascenderRatio, descenderRatio;
                if (fontProvider != null)
                {
                    fontProvider.GetLineMetrics(1f, out var asc, out var desc, out var lh);
                    lineHeightRatio = lh;
                    ascenderRatio = asc;
                    descenderRatio = desc;
                }
                else
                {
                    lineHeightRatio = 1.2f;
                    ascenderRatio = lineHeightRatio * 0.8f;
                    descenderRatio = -lineHeightRatio * 0.2f;
                }

                var capHeightRatio = fontProvider?.GetCapHeight(1f) ?? 0f;
                var rawHeightRatio = ascenderRatio - descenderRatio + (lineCount - 1) * lineHeightRatio;
                var effectiveLineHeight = lineHeightRatio + baseSettings.LineSpacing;
                var trimRatio = TextLayout.ComputeTrimAmount(ascenderRatio, descenderRatio,
                    capHeightRatio, baseSettings.OverEdge, baseSettings.UnderEdge,
                    baseSettings.LeadingDistribution, effectiveLineHeight, effectiveLineHeight);
                var heightLimitedSize = targetHeight / (rawHeightRatio - trimRatio);

                var optimalSize = Math.Clamp(Math.Min(widthLimitedSize, heightLimitedSize), minSize, maxSize);
                hasValidLinesData = false;
                hasValidPositionedGlyphs = false;
                return optimalSize;
            }

            const float tolerance = 0.5f;
            var lo = minSize;
            var hi = maxSize;

            var minHeight = GetHeightForFontSize(lo, targetWidth, baseSettings);
            if (minHeight > targetHeight)
                return minSize;

            var maxHeight = GetHeightForFontSize(hi, targetWidth, baseSettings);
            if (maxHeight <= targetHeight)
                return maxSize;

            while (hi - lo > tolerance)
            {
                var mid = (lo + hi) * 0.5f;
                var height = GetHeightForFontSize(mid, targetWidth, baseSettings);

                if (height <= targetHeight)
                    lo = mid;
                else
                    hi = mid;
            }

            if (Math.Abs(lastLinesFontSize - lo) > 0.001f)
                GetHeightForFontSize(lo, targetWidth, baseSettings);

            return lo;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetHeightForFontSize(float fontSize, float targetWidth, TextProcessSettings baseSettings)
        {
            var glyphScale = buf.GetGlyphScale(fontSize);
            var effectiveMaxWidth = baseSettings.enableWordWrap ? targetWidth / glyphScale : TextProcessSettings.FloatMax;

            buf.lines.count = 0;
            buf.orderedRuns.count = 0;
            buf.positionedGlyphs.count = 0;

            BreakLines(effectiveMaxWidth, buf.cpWidths.Span);

            lastLinesWidth = targetWidth;
            lastLinesFontSize = fontSize;
            hasValidLinesData = true;
            hasValidPositionedGlyphs = false;

            ComputeLineHeights(fontSize, baseSettings.LineSpacing, baseSettings.LeadingDistribution);
            var capHeight = fontProvider?.GetCapHeight(fontSize) ?? 0f;
            var trim = TextLayout.ComputeTrimAmount(cachedMainAscender, cachedMainDescender,
                capHeight, baseSettings.OverEdge, baseSettings.UnderEdge,
                baseSettings.LeadingDistribution,
                cachedEffectiveFirstLineHeight, cachedEffectiveLastLineHeight);
            return cachedRawHeight - trim;
        }

        /// <summary>
        /// Gets the actual width of the laid out text.
        /// </summary>
        /// <value>The width in pixels after layout, or 0 if layout has not completed.</value>
        public float ResultWidth => resultWidth;

        /// <summary>
        /// Gets the actual height of the laid out text.
        /// </summary>
        /// <value>The height in pixels after layout, or 0 if layout has not completed.</value>
        public float ResultHeight => resultHeight;

        /// <summary>
        /// Gets the positioned glyphs ready for rendering.
        /// </summary>
        /// <value>
        /// A read-only span of <see cref="PositionedGlyph"/> containing final glyph positions,
        /// or an empty span if <see cref="HasValidPositionedGlyphs"/> is <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// Access this property after calling <see cref="EnsurePositions"/> to get the final
        /// glyph data for rendering to a mesh or texture.
        /// </remarks>
        public ReadOnlySpan<PositionedGlyph> PositionedGlyphs
        {
            get
            {
                var b = buf;
                return b.positionedGlyphs.Span;
            }
        }

        private void Parse(ReadOnlySpan<char> text)
        {
            buf.codepoints.count = 0;
            buf.EnsureCodepointCapacity(text.Length);

            var i = 0;
            while (i < text.Length) AddCharacter(text, ref i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddCharacter(ReadOnlySpan<char> text, ref int i)
        {
            var c = text[i];

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var cp = char.ConvertToUtf32(c, text[i + 1]);
                AddCodepoint(cp);
                i += 2;
            }
            else
            {
                AddCodepoint(c);
                i++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddCodepoint(int cp)
        {
            var count = buf.codepoints.count;
            if (count >= buf.codepoints.Capacity)
                buf.EnsureCodepointCapacity(count + 1);
            buf.codepoints[count] = cp;
            buf.codepoints.count = count + 1;
        }

        private void AnalyzeBidi(TextDirection requestedDirection)
        {
            var cpCount = buf.codepoints.count;
            buf.bidiLevels.EnsureCapacity(cpCount);

            var direction = requestedDirection switch
            {
                TextDirection.RightToLeft => BidiParagraphDirection.RightToLeft,
                TextDirection.LeftToRight => BidiParagraphDirection.LeftToRight,
                _ => BidiParagraphDirection.Auto
            };

            var result = BidiEngine.Process(buf.codepoints.Span, direction);

            if (result.levels != null && result.levels.Length > 0)
            {
                var copyLen = Math.Min(result.levels.Length, cpCount);
                result.levels.AsSpan(0, copyLen).CopyTo(buf.bidiLevels.data);
            }
            else
            {
                buf.bidiLevels.data.AsSpan(0, cpCount).Fill(0);
            }

            var paragraphCount = result.paragraphCount;
            if (paragraphCount > 0)
            {
                buf.bidiParagraphs.EnsureCapacity(paragraphCount);
                result.ParagraphsSpan.CopyTo(buf.bidiParagraphs.data);
            }

            buf.bidiParagraphs.count = paragraphCount;

            buf.baseDirection = result.Direction == BidiDirection.RightToLeft
                ? TextDirection.RightToLeft
                : TextDirection.LeftToRight;
        }

        private void AnalyzeScripts()
        {
            var cpCount = buf.codepoints.count;
            buf.scripts.EnsureCapacity(cpCount);
            ScriptAnalyzer.Analyze(buf.codepoints.Span, buf.scripts.data);
        }

        private void Itemize()
        {
            buf.runs.count = 0;

            var cpCount = buf.codepoints.count;
            if (cpCount == 0) return;

            var cpSpan = buf.codepoints.Span;
            var lvlSpan = buf.bidiLevels.data.AsSpan(0, cpCount);
            var scrSpan = buf.scripts.data.AsSpan(0, cpCount);
            var fp = fontProvider;

            if (fp == null)
            {
                ItemizeWithoutFontLookup(cpCount, lvlSpan, scrSpan, 0);
                return;
            }

            buf.graphemeBreaks.EnsureCount(cpCount + 1);
            var graphemeBreaks = buf.graphemeBreaks.Span;
            GraphemeBreaker.GetBreakOpportunities(cpSpan, graphemeBreaks);

            var runStart = 0;
            while (runStart < cpCount && UnicodeData.IsMandatoryBreakChar(cpSpan[runStart]))
                runStart++;

            if (runStart >= cpCount) return;

            var currentLevel = lvlSpan[runStart];
            var currentScript = scrSpan[runStart];
            var currentIsReal = IsRealScript(currentScript);

            var clusterStart = runStart;
            var clusterEnd = FindNextClusterStart(graphemeBreaks, runStart + 1, cpCount);
            var currentFontId = GetFontIdForCluster(cpSpan, clusterStart, clusterEnd, fp);

            for (var i = clusterEnd; i < cpCount; i++)
            {
                if (!graphemeBreaks[i])
                    continue;

                if (UnicodeData.IsMandatoryBreakChar(cpSpan[i]))
                {
                    if (i > runStart)
                        AddRun(runStart, i - runStart, currentLevel, currentScript, currentFontId);

                    runStart = i + 1;
                    while (runStart < cpCount && UnicodeData.IsMandatoryBreakChar(cpSpan[runStart]))
                        runStart++;

                    if (runStart >= cpCount) return;

                    currentLevel = lvlSpan[runStart];
                    currentScript = scrSpan[runStart];
                    currentIsReal = IsRealScript(currentScript);
                    clusterStart = runStart;
                    clusterEnd = FindNextClusterStart(graphemeBreaks, runStart + 1, cpCount);
                    currentFontId = GetFontIdForCluster(cpSpan, clusterStart, clusterEnd, fp);
                    i = runStart;
                    continue;
                }

                var level = lvlSpan[i];
                var script = scrSpan[i];

                clusterStart = i;
                clusterEnd = FindNextClusterStart(graphemeBreaks, i + 1, cpCount);
                var fontId = GetFontIdForCluster(cpSpan, clusterStart, clusterEnd, fp);

                var scriptIsReal = IsRealScript(script);
                var scriptChanged = currentIsReal && scriptIsReal && currentScript != script;

                if (level != currentLevel || scriptChanged || fontId != currentFontId)
                {
                    if (i > runStart)
                        AddRun(runStart, i - runStart, currentLevel, currentScript, currentFontId);

                    runStart = i;
                    currentLevel = level;
                    currentScript = scriptIsReal ? script : currentScript;
                    currentIsReal = scriptIsReal || currentIsReal;
                    currentFontId = fontId;
                }
                else if (scriptIsReal && !currentIsReal)
                {
                    currentScript = script;
                    currentIsReal = true;
                }
            }

            if (runStart < cpCount)
                AddRun(runStart, cpCount - runStart, currentLevel, currentScript, currentFontId);
        }

        /// <summary>
        /// Returns true if script is a "real" script (not Common or Inherited).
        /// Common/Inherited scripts are compatible with any other script per UAX #24.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRealScript(UnicodeScript script)
        {
            return script != UnicodeScript.Common && script != UnicodeScript.Inherited;
        }

        /// <summary>
        /// Returns true if scripts are compatible per ICU/Pango standard.
        /// Common and Inherited are compatible with any script.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SameScript(UnicodeScript s1, UnicodeScript s2)
        {
            return !IsRealScript(s1) || !IsRealScript(s2) || s1 == s2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindNextClusterStart(Span<bool> graphemeBreaks, int from, int cpCount)
        {
            for (int i = from; i < cpCount; i++)
            {
                if (graphemeBreaks[i])
                    return i;
            }
            return cpCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFontIdForCluster(Span<int> cpSpan, int start, int end, UniTextFontProvider fp)
        {
            var clusterLength = end - start;

            if (clusterLength == 1)
            {
                var cp = cpSpan[start];

                if ((uint)cp < UnicodeData.EmojiRangeThreshold)
                    return fp.FindFontForCodepoint(cp);

                if (EmojiFont.IsAvailable && IsSingleCodepointEmoji(cp))
                    return EmojiFont.FontId;

                return fp.FindFontForCodepoint(cp);
            }

            var cluster = cpSpan.Slice(start, clusterLength);
            if (EmojiFont.IsAvailable && EmojiSequenceClassifier.IsEmojiCluster(cluster))
                return EmojiFont.FontId;

            return fp.FindFontForCodepoint(cpSpan[start]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSingleCodepointEmoji(int cp)
        {
            if (UnicodeData.IsRegionalIndicator(cp))
                return false;

            if (UnicodeData.IsInCommonEmojiRange(cp))
                return true;

            var provider = UnicodeData.Provider;
            return provider.IsEmojiPresentation(cp) || provider.IsExtendedPictographic(cp);
        }

        private void ItemizeWithoutFontLookup(int cpCount, Span<byte> lvlSpan, Span<UnicodeScript> scrSpan,
            int fontId)
        {
            var cpSpan = buf.codepoints.Span;

            var runStart = 0;
            while (runStart < cpCount && UnicodeData.IsMandatoryBreakChar(cpSpan[runStart]))
                runStart++;

            if (runStart >= cpCount) return;

            var currentLevel = lvlSpan[runStart];
            var currentScript = scrSpan[runStart];
            var currentIsReal = IsRealScript(currentScript);

            for (var i = runStart + 1; i < cpCount; i++)
            {
                if (UnicodeData.IsMandatoryBreakChar(cpSpan[i]))
                {
                    if (i > runStart)
                        AddRun(runStart, i - runStart, currentLevel, currentScript, fontId);

                    runStart = i + 1;
                    while (runStart < cpCount && UnicodeData.IsMandatoryBreakChar(cpSpan[runStart]))
                        runStart++;

                    if (runStart >= cpCount) return;

                    currentLevel = lvlSpan[runStart];
                    currentScript = scrSpan[runStart];
                    currentIsReal = IsRealScript(currentScript);
                    i = runStart;
                    continue;
                }

                var level = lvlSpan[i];
                var script = scrSpan[i];
                var scriptIsReal = IsRealScript(script);
                var scriptChanged = currentIsReal && scriptIsReal && currentScript != script;

                if (level != currentLevel || scriptChanged)
                {
                    if (i > runStart)
                        AddRun(runStart, i - runStart, currentLevel, currentScript, fontId);

                    runStart = i;
                    currentLevel = level;
                    currentScript = scriptIsReal ? script : currentScript;
                    currentIsReal = scriptIsReal || currentIsReal;
                }
                else if (scriptIsReal && !currentIsReal)
                {
                    currentScript = script;
                    currentIsReal = true;
                }
            }

            if (runStart < cpCount)
                AddRun(runStart, cpCount - runStart, currentLevel, currentScript, fontId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddRun(int start, int length, byte bidiLevel, UnicodeScript script, int fontId)
        {
            var count = buf.runs.count;
            if (count >= buf.runs.Capacity)
                buf.runs.EnsureCapacity(count + 1);

            buf.runs[count] = new TextRun
            {
                range = new TextRange(start, length),
                bidiLevel = bidiLevel,
                script = script,
                fontId = fontId
            };
            buf.runs.count = count + 1;
        }

        private void Shape()
        {
            buf.shapedRuns.count = 0;
            buf.shapedGlyphs.count = 0;

            var cpCount = buf.codepoints.count;
            var runCnt = buf.runs.count;
            var cp = buf.codepoints.Span;
            var runs = buf.runs;

            for (var i = 0; i < runCnt; i++)
            {
                ref readonly var run = ref runs[i];

                var result = Shaper.Shape(
                    cp,
                    run.range.start,
                    run.range.length,
                    fontProvider,
                    run.fontId,
                    run.script,
                    run.Direction);

                var glyphStart = buf.shapedGlyphs.count;
                AddShapedGlyphs(result.Glyphs);

                AddShapedRun(new ShapedRun
                {
                    range = run.range,
                    glyphStart = glyphStart,
                    glyphCount = result.Glyphs.Length,
                    width = result.TotalAdvance,
                    direction = run.Direction,
                    bidiLevel = run.bidiLevel,
                    fontId = run.fontId
                });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddShapedGlyphs(ReadOnlySpan<ShapedGlyph> glyphs)
        {
            var count = buf.shapedGlyphs.count;
            var required = count + glyphs.Length;
            if (buf.shapedGlyphs.Capacity < required)
                buf.shapedGlyphs.EnsureCapacity(required);

            glyphs.CopyTo(buf.shapedGlyphs.data.AsSpan(count));
            buf.shapedGlyphs.count = required;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddShapedRun(ShapedRun run)
        {
            var count = buf.shapedRuns.count;
            if (count >= buf.shapedRuns.Capacity)
                buf.shapedRuns.EnsureCapacity(count + 1);

            buf.shapedRuns[count] = run;
            buf.shapedRuns.count = count + 1;
        }

        private void ComputeBreakOpportunities()
        {
            var cpCount = buf.codepoints.count;
            var requiredLength = cpCount + 1;
            buf.breakOpportunities.EnsureCount(requiredLength);

            SharedPipelineComponents.LineBreakAlgorithm.GetBreakOpportunities(
                buf.codepoints.Span,
                buf.breakOpportunities.data);
        }

        private void ComputeCpWidths()
        {
            var cpCount = buf.codepoints.count;
            buf.cpWidths.EnsureCount(cpCount);

            var widths = buf.cpWidths.data;
            Array.Clear(widths, 0, cpCount);

            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var glyphs = buf.shapedGlyphs.data;

            for (var r = 0; r < runCount; r++)
            {
                ref readonly var run = ref runs[r];
                var end = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < end; g++)
                {
                    var cpIdx = glyphs[g].cluster;
                    if ((uint)cpIdx < (uint)cpCount)
                        widths[cpIdx] += glyphs[g].advanceX;
                }
            }
        }

        /// <summary>
        /// Forces a complete relayout using custom codepoint widths.
        /// </summary>
        /// <param name="cpWidths">Custom widths for each codepoint, used for layout calculations.</param>
        /// <remarks>
        /// Use this method when codepoint widths have been modified externally (e.g., by modifiers)
        /// and the layout needs to be recalculated. Preserves shaping data but recalculates
        /// line breaks and positions.
        /// </remarks>
        public void ForceRelayout(ReadOnlySpan<float> cpWidths)
        {
            if (!hasValidFirstPassData) return;

            InvalidateLayoutData();
            EnsureLinesInternal(lastSettings.MaxWidth, lastSettings.fontSize, lastSettings.enableWordWrap, cpWidths);
            EnsurePositions(lastSettings);
        }

        /// <summary>
        /// Forces recalculation of glyph positions while preserving line breaks.
        /// </summary>
        /// <remarks>
        /// Use this method when run widths have changed but line breaks remain valid.
        /// Updates line widths and recalculates glyph positions without re-breaking lines.
        /// </remarks>
        public void ForceReposition()
        {
            if (!hasValidFirstPassData || !hasValidLinesData) return;

            UpdateLineWidths();
            hasValidPositionedGlyphs = false;
            EnsurePositions(lastSettings);
        }

        private void UpdateLineWidths()
        {
            var lines = buf.lines.data;
            var lineCount = buf.lines.count;
            var runs = buf.orderedRuns.data;

            for (var i = 0; i < lineCount; i++)
            {
                ref var line = ref lines[i];
                var width = 0f;
                var end = line.runStart + line.runCount;
                for (var r = line.runStart; r < end; r++)
                    width += runs[r].width;
                line.width = width;
            }
        }

        private void EnsureLinesInternal(float width, float fontSize, bool wordWrap, ReadOnlySpan<float> cpWidths)
        {
            buf.lines.count = 0;
            buf.orderedRuns.count = 0;
            buf.positionedGlyphs.count = 0;
            hasValidPositionedGlyphs = false;

            var glyphScale = buf.GetGlyphScale(fontSize);
            var effectiveMaxWidth = wordWrap ? width / glyphScale : TextProcessSettings.FloatMax;
            BreakLines(effectiveMaxWidth, cpWidths);

            lastLinesWidth = width;
            lastLinesFontSize = fontSize;
            lastLinesWordWrap = wordWrap;
            hasValidLinesData = true;

            ComputeLineHeights(fontSize, 0f);
        }

        /// <summary>
        /// Computes per-line advances and caches the raw total height and font metrics.
        /// </summary>
        /// <param name="fontSize">Font size for metric calculations.</param>
        /// <param name="lineSpacing">Additional line spacing.</param>
        private void ComputeLineHeights(float fontSize, float lineSpacing,
            LeadingDistribution distribution = LeadingDistribution.HalfLeading)
        {
            var lineCount = buf.lines.count;
            if (lineCount == 0)
            {
                cachedRawHeight = 0;
                cachedHeightFontSize = fontSize;
                return;
            }

            float mainAscender, mainDescender, mainLineHeight;
            if (fontProvider != null)
                fontProvider.GetLineMetrics(fontSize, out mainAscender, out mainDescender, out mainLineHeight);
            else
            {
                mainLineHeight = fontSize * 1.2f;
                mainAscender = mainLineHeight * 0.8f;
                mainDescender = -mainLineHeight * 0.2f;
            }

            buf.perLineAdvances.EnsureCapacity(lineCount);

            var lines = buf.lines.data;
            var orderedRuns = buf.orderedRuns.data;
            var advances = buf.perLineAdvances.data;
            var totalLineAdvances = 0f;

            // Phase 1: Compute per-line effective heights (CSS line box model)
            for (var i = 0; i < lineCount; i++)
            {
                float h = mainLineHeight + lineSpacing;
                if (OnCalculateLineHeight != null)
                {
                    ref readonly var line = ref lines[i];
                    OnCalculateLineHeight.Invoke(i, line.range.start, line.range.End, ref h);
                }
                advances[i] = h;
            }

            cachedEffectiveFirstLineHeight = advances[0];
            cachedEffectiveLastLineHeight = advances[lineCount - 1];

            // Phase 2: Compute inter-line advances based on leading distribution model
            var prevH = advances[0];
            for (var i = 0; i < lineCount - 1; i++)
            {
                var currH = prevH;
                var nextH = advances[i + 1];
                prevH = nextH;

                var advance = distribution switch
                {
                    LeadingDistribution.LeadingAbove => nextH,
                    LeadingDistribution.LeadingBelow => currH,
                    _ => (currH + nextH) * 0.5f
                };

                var maxDescDepth = -mainDescender;
                var maxAscHeight = mainAscender;
                ComputeMaxLineMetrics(lines[i], orderedRuns, fontSize,
                    ref maxDescDepth, ref maxAscHeight);

                var nextAscHeight = mainAscender;
                var nextDescDepth = -mainDescender;
                ComputeMaxLineMetrics(lines[i + 1], orderedRuns, fontSize,
                    ref nextDescDepth, ref nextAscHeight);

                var minAdvance = maxDescDepth + nextAscHeight + lineSpacing;
                advance = Math.Max(advance, minAdvance);

                advances[i] = advance;
                totalLineAdvances += advance;
            }

            if (lineCount > 0)
                advances[lineCount - 1] = 0f;

            buf.perLineAdvances.count = lineCount;
            cachedRawHeight = mainAscender - mainDescender + totalLineAdvances;
            cachedMainAscender = mainAscender;
            cachedMainDescender = mainDescender;
            cachedMainLineHeight = mainLineHeight;
            cachedHeightFontSize = fontSize;
        }

        private void ComputeMaxLineMetrics(in TextLine line, ShapedRun[] orderedRuns, float fontSize,
            ref float maxDescDepth, ref float maxAscHeight)
        {
            if (fontProvider == null) return;

            var runEnd = line.runStart + line.runCount;
            for (var r = line.runStart; r < runEnd; r++)
            {
                var font = fontProvider.GetFontAsset(orderedRuns[r].fontId);
                if (font == null || font.FontScale == 1f) continue;

                var faceInfo = font.FaceInfo;
                var scale = fontSize * font.FontScale / font.UnitsPerEm;

                var asc = faceInfo.ascentLine * scale;
                var desc = faceInfo.descentLine * scale;

                if (asc > maxAscHeight) maxAscHeight = asc;
                if (-desc > maxDescDepth) maxDescDepth = -desc;
            }
        }

        private void BreakLines(float maxWidth, ReadOnlySpan<float> cpWidths)
        {
            UniTextDebug.BeginSample("TextProcessor.BreakLines");
            buf.lines.count = 0;
            buf.orderedRuns.count = 0;

            var linesArr = buf.lines.data;
            var orderedRunsArr = buf.orderedRuns.data;
            var lineCnt = buf.lines.count;
            var orderedRunCnt = buf.orderedRuns.count;

            LineBreaker.BreakLines(
                buf.codepoints.Span,
                buf.shapedRuns.Span,
                buf.shapedGlyphs.Span,
                cpWidths,
                buf.breakOpportunities.Span,
                maxWidth,
                buf.bidiParagraphs.Span,
                ref linesArr, ref lineCnt,
                ref orderedRunsArr, ref orderedRunCnt,
                buf.startMargins.data.AsSpan(0, buf.codepoints.count));

            buf.lines.data = linesArr;
            buf.orderedRuns.data = orderedRunsArr;
            buf.lines.count = lineCnt;
            buf.orderedRuns.count = orderedRunCnt;

            Cat.MeowFormat("[TextProcessor] BreakLines: {0} lines, maxWidth={1:F0}", lineCnt, maxWidth);
            UniTextDebug.EndSample();
        }

        private void LayoutText(TextProcessSettings settings)
        {
            UniTextDebug.BeginSample("TextProcessor.LayoutText");
            buf.positionedGlyphs.count = 0;
            buf.positionedGlyphs.EnsureCapacity(buf.shapedGlyphs.count);

            ComputeLineHeights(settings.fontSize, settings.LineSpacing, settings.LeadingDistribution);

            if (fontProvider != null)
            {
                fontProvider.GetLineMetrics(settings.fontSize, out var ascender, out var descender, out var lineHeight);
                var capHeight = fontProvider.GetCapHeight(settings.fontSize);
                Layout.SetFontMetrics(ascender, descender, lineHeight, buf.GetGlyphScale(settings.fontSize), capHeight);
            }

            Layout.SetLayoutSettings(settings.layout);
            Layout.SetEffectiveLineHeights(cachedEffectiveFirstLineHeight, cachedEffectiveLastLineHeight);

            var glyphCnt = buf.positionedGlyphs.count;
            Layout.Layout(
                buf.lines.Span,
                buf.orderedRuns.Span,
                buf.shapedGlyphs.Span,
                buf.perLineAdvances.Span,
                cachedRawHeight,
                buf.positionedGlyphs.data, ref glyphCnt,
                out resultWidth, out resultHeight);
            buf.positionedGlyphs.count = glyphCnt;

            UniTextDebug.EndSample();
        }
    }
}
