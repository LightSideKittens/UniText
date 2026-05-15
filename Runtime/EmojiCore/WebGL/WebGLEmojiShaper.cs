#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Text shaper for emoji sequences in WebGL builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Converts emoji codepoint sequences into shaped glyphs for rendering.
    /// Handles grapheme cluster segmentation and ZWJ sequence fallback.
    /// </para>
    /// <para>
    /// When a ZWJ sequence is not supported by the browser, falls back to
    /// rendering individual emoji components separately.
    /// </para>
    /// </remarks>
    /// <seealso cref="WebGLEmoji"/>
    /// <seealso cref="EmojiFont"/>
    internal static class WebGLEmojiShaper
    {
        [ThreadStatic] private static ShapedGlyph[] outputBuffer;
        [ThreadStatic] private static bool[] graphemeBreaks;

        private static readonly FastIntDictionary<bool> zwjSupportCache = new();

        private static ShapedGlyph[] EnsureOutputBuffer(int capacity)
        {
            if (outputBuffer == null || outputBuffer.Length < capacity)
                outputBuffer = new ShapedGlyph[Math.Max(capacity, 64)];
            return outputBuffer;
        }

        /// <summary>Shapes emoji codepoints into positioned glyphs.</summary>
        /// <param name="codepoints">Input emoji codepoint sequence.</param>
        /// <param name="fontSize">Desired font size.</param>
        /// <param name="upem">Units per em of the font.</param>
        /// <returns>Shaping result with positioned glyphs and total advance.</returns>
        /// <remarks>
        /// Segments input by grapheme clusters. For unsupported ZWJ sequences,
        /// splits into individual components for separate rendering.
        /// </remarks>
        public static ShapingResult Shape(
            ReadOnlySpan<int> codepoints,
            float fontSize,
            int upem)
        {
            if (codepoints.IsEmpty || !WebGLEmoji.IsSupported)
                return new ShapingResult(ReadOnlySpan<ShapedGlyph>.Empty, 0);

            int cpCount = codepoints.Length;

            if (graphemeBreaks == null || graphemeBreaks.Length < cpCount + 1)
                graphemeBreaks = new bool[Math.Max(cpCount + 1, 256)];

            Array.Clear(graphemeBreaks, 0, cpCount + 1);
            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(codepoints, graphemeBreaks.AsSpan(0, cpCount + 1));

            int clusterCount = 0;
            int maxPossibleGlyphs = 0;
            int tempStart = 0;
            for (int i = 1; i <= cpCount; i++)
            {
                if (graphemeBreaks[i])
                {
                    clusterCount++;
                    int clusterLen = i - tempStart;
                    maxPossibleGlyphs += Math.Max(1, CountZWJ(codepoints.Slice(tempStart, clusterLen)) + 1);
                    tempStart = i;
                }
            }

            if (clusterCount == 0)
            {
                clusterCount = 1;
                maxPossibleGlyphs = 1;
            }

            var outBuf = EnsureOutputBuffer(maxPossibleGlyphs);
            float totalAdvance = 0;
            int glyphIndex = 0;
            int clusterStart = 0;
            float fixedAdvance = fontSize;

            for (int i = 1; i <= cpCount; i++)
            {
                if (!graphemeBreaks[i])
                    continue;

                var cluster = codepoints.Slice(clusterStart, i - clusterStart);

                if (ContainsZWJ(cluster) && !IsZwjSequenceSupported(cluster))
                {
                    int subStart = 0;
                    for (int j = 0; j < cluster.Length; j++)
                    {
                        bool isZwj = cluster[j] == UnicodeData.ZeroWidthJoiner;
                        bool isLast = j == cluster.Length - 1;

                        if (isZwj || isLast)
                        {
                            int subEnd = isZwj ? j : j + 1;
                            if (subEnd > subStart)
                            {
                                var subcluster = cluster.Slice(subStart, subEnd - subStart);
                                uint subHash = WebGLEmoji.RegisterSequence(subcluster);

                                outBuf[glyphIndex++] = new ShapedGlyph
                                {
                                    glyphId = (int)subHash,
                                    cluster = clusterStart + subStart,
                                    advanceX = fixedAdvance,
                                    advanceY = 0,
                                    offsetX = 0,
                                    offsetY = 0
                                };
                                totalAdvance += fixedAdvance;
                            }
                            subStart = j + 1; 
                        }
                    }
                }
                else
                {
                    uint hash = WebGLEmoji.RegisterSequence(cluster);

                    outBuf[glyphIndex++] = new ShapedGlyph
                    {
                        glyphId = (int)hash,
                        cluster = clusterStart,
                        advanceX = fixedAdvance,
                        advanceY = 0,
                        offsetX = 0,
                        offsetY = 0
                    };
                    totalAdvance += fixedAdvance;
                }

                clusterStart = i;
            }

            return new ShapingResult(outBuf.AsSpan(0, glyphIndex), totalAdvance);
        }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsZwjSequenceSupported(ReadOnlySpan<int> cluster)
        {
            uint hash = WebGLEmoji.ComputeSequenceHash(cluster);
            int hashInt = (int)hash;

            if (zwjSupportCache.TryGetValue(hashInt, out bool cached))
                return cached;

            int pixelSize = EmojiFont.Instance?.EmojiPixelSize ?? EmojiFont.DefaultSize;
            bool isSupported = WebGLEmoji.IsZwjSupported(cluster, pixelSize);

            zwjSupportCache[hashInt] = isSupported;
            return isSupported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsZWJ(ReadOnlySpan<int> cluster)
        {
            for (int i = 1; i < cluster.Length; i++)
            {
                if (cluster[i] == UnicodeData.ZeroWidthJoiner)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountZWJ(ReadOnlySpan<int> cluster)
        {
            int count = 0;
            for (int i = 1; i < cluster.Length; i++)
            {
                if (cluster[i] == UnicodeData.ZeroWidthJoiner)
                    count++;
            }
            return count;
        }

        /// <summary>Gets a glyph index (hash) for a single codepoint.</summary>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <returns>Hash value for use as glyph ID, or 0 if unsupported.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetGlyphIndex(uint codepoint)
        {
            if (!WebGLEmoji.IsSupported)
                return 0;

            Span<int> single = stackalloc int[1] { (int)codepoint };
            return WebGLEmoji.ComputeSequenceHash(single);
        }

        /// <summary>Gets glyph information for a single codepoint.</summary>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <param name="fontSize">Desired font size.</param>
        /// <param name="upem">Units per em of the font.</param>
        /// <param name="glyphIndex">Output glyph hash/ID.</param>
        /// <param name="advance">Output horizontal advance in font units.</param>
        /// <returns>True if glyph info was successfully retrieved.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetGlyphInfo(uint codepoint, float fontSize, int upem, out uint glyphIndex, out float advance)
        {
            glyphIndex = 0;
            advance = 0;

            if (!WebGLEmoji.IsSupported)
                return false;

            Span<int> single = stackalloc int[1] { (int)codepoint };

            glyphIndex = WebGLEmoji.RegisterSequence(single);

            int pixelSize = EmojiFont.Instance?.EmojiPixelSize ?? EmojiFont.DefaultSize;
            float browserWidth = WebGLEmoji.MeasureEmoji(single, pixelSize);
            if (browserWidth <= 0)
                return false;

            float advanceInDesignUnits = browserWidth * upem / pixelSize;
            advance = advanceInDesignUnits * fontSize / upem;

            return true;
        }
    }
}
#endif
