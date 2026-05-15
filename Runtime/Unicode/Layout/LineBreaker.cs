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
            ReorderRunsPerLine(paragraphs);

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
                    if (lastBreakCp >= 0 && lastBreakCp >= lineStartCp)
                    {
                        CreateLineFromCodepoints(runs, glyphs, lineStartCp, lastBreakCp, rawMargin);
                        lineStartCp = lastBreakCp + 1;
                        lineWidth -= widthAtLastBreak;
                        lastBreakCp = -1;
                        widthAtLastBreak = 0;
                        rawMargin = (uint)lineStartCp < (uint)startMargins.Length ? startMargins[lineStartCp] : 0f;
                        effectiveMaxWidth = maxWidth - rawMargin;
                    }
                    else if (cpIdx > lineStartCp)
                    {
                        CreateLineFromCodepoints(runs, glyphs, lineStartCp, cpIdx - 1, rawMargin);
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

                if (breakType == LineBreakType.Mandatory)
                {
                    CreateLineFromCodepoints(runs, glyphs, lineStartCp, cpIdx, rawMargin);
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
                CreateLineFromCodepoints(runs, glyphs, lineStartCp, cpCount - 1, rawMargin);
        }

        private void CreateLineFromCodepoints(
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

            EnsureLineCapacity(tempLineCount + 1);
            tempLines[tempLineCount++] = new TextLine
            {
                range = new TextRange(startCp, endCp - startCp + 1),
                runStart = lineRunStart,
                runCount = lineRunCount,
                width = actualLineWidth,
                startMargin = startMargin
            };
        }

        private void ReorderRunsPerLine(ReadOnlySpan<BidiParagraph> paragraphs)
        {
            for (var i = 0; i < tempLineCount; i++)
            {
                var line = tempLines[i];

                var paragraphBaseLevel = FindParagraphBaseLevel(paragraphs, line.range.start);

                ReorderRunsInLine(line.runStart, line.runCount, paragraphBaseLevel);

                line.paragraphBaseLevel = paragraphBaseLevel;
                tempLines[i] = line;
            }
        }


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
