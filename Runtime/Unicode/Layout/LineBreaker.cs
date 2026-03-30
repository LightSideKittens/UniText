using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Performs word wrapping by breaking shaped text into lines based on available width.
    /// </summary>
    /// <remarks>
    /// Uses break opportunities from <see cref="LineBreakAlgorithm"/> to determine where
    /// lines can be split. Handles BiDi reordering of runs within each line according to
    /// the Unicode Bidirectional Algorithm (UAX #9).
    /// </remarks>
    /// <seealso cref="LineBreakAlgorithm"/>
    /// <seealso cref="TextLine"/>
    internal sealed class LineBreaker
    {
        private TextLine[] tempLines;
        private int tempLineCount;
        private ShapedRun[] tempOrderedRuns;
        private int tempOrderedRunCount;
        private int searchStartRunIdx;

        public void BreakLines(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<LineBreakType> breakTypes,
            float maxWidth,
            ReadOnlySpan<BidiParagraph> paragraphs,
            ref TextLine[] linesOut,
            ref int lineCount,
            ref ShapedRun[] orderedRunsOut,
            ref int orderedRunCount,
            ReadOnlySpan<float> startMargins)
        {
            tempLines = linesOut;
            tempLineCount = 0;
            tempOrderedRuns = orderedRunsOut;
            tempOrderedRunCount = 0;

            if (runs.IsEmpty)
            {
                lineCount = 0;
                orderedRunCount = 0;
                return;
            }

            WrapLines(codepoints, runs, glyphs, cpWidths, breakTypes, maxWidth, startMargins);
            ReorderRunsPerLine(codepoints, glyphs, paragraphs);

            linesOut = tempLines;
            orderedRunsOut = tempOrderedRuns;
            lineCount = tempLineCount;
            orderedRunCount = tempOrderedRunCount;
        }

        /// <summary>
        /// Gets the break type after the specified codepoint index.
        /// </summary>
        /// <remarks>
        /// breakTypes[i+1] represents the break type between codepoint[i] and codepoint[i+1].
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsHangingWhitespace(int codepoint) => codepoint == UnicodeData.Space || codepoint == UnicodeData.Tab;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LineBreakType GetBreakTypeAfter(ReadOnlySpan<LineBreakType> breakTypes, int index)
        {
            var breakIndex = index + 1;
            return (uint)breakIndex < (uint)breakTypes.Length ? breakTypes[breakIndex] : LineBreakType.None;
        }

        private void WrapLines(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<LineBreakType> breakTypes,
            float maxWidth,
            ReadOnlySpan<float> startMargins)
        {
            searchStartRunIdx = 0;

            var cpCount = codepoints.Length;

            var lineStartCp = 0;
            float lineWidth = 0;
            var lastBreakCp = -1;
            float widthAtLastBreak = 0;

            var rawMargin = (uint)lineStartCp < (uint)startMargins.Length ? startMargins[lineStartCp] : 0f;
            var effectiveMaxWidth = maxWidth - rawMargin;

            for (var cpIdx = 0; cpIdx < cpCount; cpIdx++)
            {
                lineWidth += cpWidths[cpIdx];

                var breakType = GetBreakTypeAfter(breakTypes, cpIdx);

                while (lineWidth > effectiveMaxWidth)
                {
                    float trailingSpaceWidth = 0;
                    for (var j = cpIdx; j >= lineStartCp; j--)
                    {
                        if (!IsHangingWhitespace(codepoints[j])) break;
                        trailingSpaceWidth += cpWidths[j];
                    }

                    if (lineWidth - trailingSpaceWidth <= effectiveMaxWidth)
                        break;

                    if (lastBreakCp >= 0 && lastBreakCp >= lineStartCp)
                    {
                        CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs, lineStartCp, lastBreakCp, rawMargin);
                        lineStartCp = lastBreakCp + 1;
                        lineWidth -= widthAtLastBreak;
                        lastBreakCp = -1;
                        widthAtLastBreak = 0;
                        rawMargin = (uint)lineStartCp < (uint)startMargins.Length ? startMargins[lineStartCp] : 0f;
                        effectiveMaxWidth = maxWidth - rawMargin;
                    }
                    else if (cpIdx > lineStartCp)
                    {
                        CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs, lineStartCp, cpIdx - 1, rawMargin);
                        lineStartCp = cpIdx;
                        lineWidth = cpWidths[cpIdx];
                        lastBreakCp = -1;
                        widthAtLastBreak = 0;
                        rawMargin = (uint)lineStartCp < (uint)startMargins.Length ? startMargins[lineStartCp] : 0f;
                        effectiveMaxWidth = maxWidth - rawMargin;
                    }
                    else
                    {
                        break;
                    }
                }

                if (breakType == LineBreakType.Mandatory)
                {
                    CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs, lineStartCp, cpIdx, rawMargin);
                    lineStartCp = cpIdx + 1;
                    lineWidth = 0;
                    lastBreakCp = -1;
                    widthAtLastBreak = 0;
                    rawMargin = (uint)lineStartCp < (uint)startMargins.Length ? startMargins[lineStartCp] : 0f;
                    effectiveMaxWidth = maxWidth - rawMargin;
                    continue;
                }

                if (breakType == LineBreakType.Optional)
                {
                    lastBreakCp = cpIdx;
                    widthAtLastBreak = lineWidth;
                }
            }

            if (lineStartCp < cpCount)
                CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs, lineStartCp, cpCount - 1, rawMargin);
        }

        private void CreateLineFromCodepoints(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            int startCp, int endCp, float startMargin = 0f)
        {
            if (startCp > endCp) return;

            var lineRunStart = tempOrderedRunCount;
            var lineRunCount = 0;

            for (var runIdx = searchStartRunIdx; runIdx < runs.Length; runIdx++)
            {
                var run = runs[runIdx];
                var runStart = run.range.start;
                var runEnd = run.range.End - 1;

                if (runEnd < startCp)
                {
                    searchStartRunIdx = runIdx + 1;
                    continue;
                }

                if (runStart > endCp)
                    break;

                int glyphFirst = -1, glyphLast = -1;

                for (var g = 0; g < run.glyphCount; g++)
                {
                    var glyph = glyphs[run.glyphStart + g];
                    var cpIdx = glyph.cluster;
                    var inRange = cpIdx >= startCp && cpIdx <= endCp;

                    if (inRange)
                    {
                        if (glyphFirst < 0) glyphFirst = g;
                        glyphLast = g;
                    }
                }

                if (glyphFirst < 0) continue;

                var glyphCount = glyphLast - glyphFirst + 1;

                float partialWidth = 0;
                for (var g = glyphFirst; g <= glyphLast; g++) partialWidth += glyphs[run.glyphStart + g].advanceX;

                EnsureOrderedRunCapacity(tempOrderedRunCount + 1);
                tempOrderedRuns[tempOrderedRunCount++] = new ShapedRun
                {
                    range = run.range,
                    glyphStart = run.glyphStart + glyphFirst,
                    glyphCount = glyphCount,
                    width = partialWidth,
                    direction = run.direction,
                    bidiLevel = run.bidiLevel,
                    fontId = run.fontId
                };
                lineRunCount++;
            }

            float actualLineWidth = 0;
            for (var i = lineRunStart; i < tempOrderedRunCount; i++) actualLineWidth += tempOrderedRuns[i].width;

            float trailingWsWidth = 0;
            for (var j = endCp; j >= startCp; j--)
            {
                if (!IsHangingWhitespace(codepoints[j])) break;
                trailingWsWidth += cpWidths[j];
            }

            EnsureLineCapacity(tempLineCount + 1);
            tempLines[tempLineCount++] = new TextLine
            {
                range = new TextRange(startCp, endCp - startCp + 1),
                runStart = lineRunStart,
                runCount = lineRunCount,
                width = actualLineWidth - trailingWsWidth,
                trailingWhitespace = trailingWsWidth,
                startMargin = startMargin
            };
        }

        private void ReorderRunsPerLine(ReadOnlySpan<int> codepoints, ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<BidiParagraph> paragraphs)
        {
            for (var i = 0; i < tempLineCount; i++)
            {
                ref var line = ref tempLines[i];
                var paragraphBaseLevel = FindParagraphBaseLevel(paragraphs, line.range.start);

                ApplyL1ForLine(codepoints, glyphs, ref line, paragraphBaseLevel);
                ReorderRunsInLine(line.runStart, line.runCount, paragraphBaseLevel);

                line.paragraphBaseLevel = paragraphBaseLevel;
            }
        }

        /// <summary>
        /// UAX #9 Rule L1 sub-rule 4: at the end of each line, any sequence of whitespace,
        /// isolate formatting, embedding formatting, or boundary neutral characters is reset
        /// to the paragraph embedding level. Applied per-line after wrapping, before L2 reorder.
        /// </summary>
        private void ApplyL1ForLine(ReadOnlySpan<int> codepoints, ReadOnlySpan<ShapedGlyph> glyphs,
            ref TextLine line, byte paragraphBaseLevel)
        {
            var lineEnd = line.range.start + line.range.length - 1;

            var firstWsCp = lineEnd + 1;
            var unicodeData = UnicodeData.Provider;
            for (var cp = lineEnd; cp >= line.range.start; cp--)
            {
                if (!IsL1Trailing(unicodeData.GetBidiClass(codepoints[cp]))) break;
                firstWsCp = cp;
            }

            if (firstWsCp > lineEnd) return;

            var runEnd = line.runStart + line.runCount;
            for (var r = line.runStart; r < runEnd; r++)
            {
                ref var run = ref tempOrderedRuns[r];
                if (run.bidiLevel == paragraphBaseLevel) continue;

                var gStart = run.glyphStart;
                var gEnd = gStart + run.glyphCount;
                int wsCount = 0, contentCount = 0;

                for (var g = gStart; g < gEnd; g++)
                {
                    if (glyphs[g].cluster >= firstWsCp) wsCount++;
                    else contentCount++;
                }

                if (wsCount == 0) continue;

                if (contentCount == 0)
                {
                    run.bidiLevel = paragraphBaseLevel;
                    continue;
                }

                int contentStart, wsStart;
                if (run.direction == TextDirection.RightToLeft)
                {
                    wsStart = gStart;
                    contentStart = gStart + wsCount;
                }
                else
                {
                    contentStart = gStart;
                    wsStart = gStart + contentCount;
                }

                float wsWidth = 0;
                for (var g = wsStart; g < wsStart + wsCount; g++)
                    wsWidth += glyphs[g].advanceX;

                var wsRun = new ShapedRun
                {
                    range = run.range,
                    glyphStart = wsStart,
                    glyphCount = wsCount,
                    width = wsWidth,
                    direction = run.direction,
                    bidiLevel = paragraphBaseLevel,
                    fontId = run.fontId
                };

                run.glyphStart = contentStart;
                run.glyphCount = contentCount;
                run.width -= wsWidth;

                EnsureOrderedRunCapacity(tempOrderedRunCount + 1);
                for (var j = tempOrderedRunCount; j > r + 1; j--)
                    tempOrderedRuns[j] = tempOrderedRuns[j - 1];
                tempOrderedRuns[r + 1] = wsRun;
                tempOrderedRunCount++;
                line.runCount++;

                for (var li = 0; li < tempLineCount; li++)
                    if (tempLines[li].runStart > r)
                        tempLines[li].runStart++;

                runEnd++;
                r++;
            }
        }

        /// <summary>
        /// Returns true if the given original bidi class is eligible for L1 trailing reset.
        /// Per UAX #9 L1: WS, isolate formatting (FSI/LRI/RLI/PDI), embedding formatting
        /// (LRE/RLE/LRO/RLO/PDF), and boundary neutral (BN).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsL1Trailing(BidiClass cls) => cls switch
        {
            BidiClass.WhiteSpace => true,
            BidiClass.BoundaryNeutral => true,
            BidiClass.LeftToRightIsolate => true,
            BidiClass.RightToLeftIsolate => true,
            BidiClass.FirstStrongIsolate => true,
            BidiClass.PopDirectionalIsolate => true,
            BidiClass.LeftToRightEmbedding => true,
            BidiClass.RightToLeftEmbedding => true,
            BidiClass.LeftToRightOverride => true,
            BidiClass.RightToLeftOverride => true,
            BidiClass.PopDirectionalFormat => true,
            _ => false
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte FindParagraphBaseLevel(ReadOnlySpan<BidiParagraph> paragraphs, int codepointIndex)
        {
            if (paragraphs.IsEmpty)
                return 0;

            if (paragraphs.Length == 1)
                return paragraphs[0].baseLevel;

            for (var i = 0; i < paragraphs.Length; i++)
            {
                var para = paragraphs[i];
                if (codepointIndex >= para.startIndex && codepointIndex <= para.endIndex)
                    return para.baseLevel;
            }

            return paragraphs[0].baseLevel;
        }

        private void ReorderRunsInLine(int start, int count, byte paragraphBaseLevel)
        {
            if (count <= 1) return;

            var maxLevel = paragraphBaseLevel;
            var minLevel = paragraphBaseLevel;

            for (var i = 0; i < count; i++)
            {
                var level = tempOrderedRuns[start + i].bidiLevel;
                if (level > maxLevel) maxLevel = level;
                if (level < minLevel) minLevel = level;
            }

            var lowestOddLevel = (minLevel & 1) == 1 ? minLevel : (byte)(minLevel + 1);
            if (lowestOddLevel > maxLevel) return;

            for (var level = maxLevel; level >= lowestOddLevel; level--)
            {
                var runStart = -1;

                for (var i = 0; i <= count; i++)
                {
                    var inSequence = i < count && tempOrderedRuns[start + i].bidiLevel >= level;

                    if (inSequence && runStart < 0)
                    {
                        runStart = i;
                    }
                    else if (!inSequence && runStart >= 0)
                    {
                        ReverseRuns(start + runStart, i - runStart);
                        runStart = -1;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReverseRuns(int start, int count)
        {
            var arr = tempOrderedRuns;
            var end = start + count - 1;
            while (start < end)
            {
                (arr[start], arr[end]) = (arr[end], arr[start]);
                start++;
                end--;
            }
        }

        private void EnsureLineCapacity(int required)
        {
            if (tempLines != null && tempLines.Length >= required) return;

            var newSize = Math.Max(required, tempLines?.Length * 2 ?? 128);
            var newBuffer = UniTextArrayPool<TextLine>.Rent(newSize);

            if (tempLines != null)
            {
                tempLines.AsSpan(0, tempLineCount).CopyTo(newBuffer);
                UniTextArrayPool<TextLine>.Return(tempLines);
            }

            tempLines = newBuffer;
        }

        private void EnsureOrderedRunCapacity(int required)
        {
            if (tempOrderedRuns != null && tempOrderedRuns.Length >= required) return;

            var newSize = Math.Max(required, tempOrderedRuns?.Length * 2 ?? 512);
            var newBuffer = UniTextArrayPool<ShapedRun>.Rent(newSize);

            if (tempOrderedRuns != null)
            {
                tempOrderedRuns.AsSpan(0, tempOrderedRunCount).CopyTo(newBuffer);
                UniTextArrayPool<ShapedRun>.Return(tempOrderedRuns);
            }

            tempOrderedRuns = newBuffer;
        }
    }

}
