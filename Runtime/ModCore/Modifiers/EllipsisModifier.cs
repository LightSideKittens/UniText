using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Truncates text that overflows its container and appends an ellipsis (...).
    /// </summary>
    /// <remarks>
    /// Usage: <c>&lt;ellipsis&gt;long text&lt;/ellipsis&gt;</c> or <c>&lt;ellipsis=0.5&gt;...&lt;/ellipsis&gt;</c>
    ///
    /// The position parameter (0-1) controls where the ellipsis appears:
    /// - <c>0</c>: Truncates from the start, ellipsis at the beginning
    /// - <c>0.5</c>: Truncates from the middle, ellipsis in the center
    /// - <c>1</c> (default): Truncates from the end, ellipsis at the end
    ///
    /// The modifier automatically detects overflow based on container height (with word wrap)
    /// or width (without word wrap) and uses binary search to find the optimal truncation point.
    /// </remarks>
    /// <seealso cref="EllipsisTagRule"/>
    [Serializable]
    [TypeGroup("Layout", 4)]
    public class EllipsisModifier : BaseModifier
    {
        public struct Range
        {
            public int start;
            public int end;
            public float position;
            public int truncateMinCluster;
            public int truncateMaxCluster;
            public int ellipsisCluster;
            public bool needsEllipsis;
        }

        private struct LineTruncation
        {
            public int truncateMinCluster;
            public int truncateMaxCluster;
            public int ellipsisCluster;
        }

        private const string EllipsisText = "...";
        private const int MaxIterations = 10;
        private const float OverflowEpsilon = 0.5f;

        private PooledList<Range> ranges;
        private PooledBuffer<float> originalAdvances;
        private bool needsRestore;
        private bool isProcessingRelayout;
        private int iterationCount;

        private PooledBuffer<int> glyphToGlobalCluster;
        private PooledBuffer<(int firstGlyph, int lastGlyph, int minCluster, int maxCluster)> rangeGlyphBoundsCache;

        private PooledBuffer<float> currentCpWidths;
        private PooledBuffer<float> clusterWidthsBuffer;
        private PooledList<LineTruncation> lineTruncations;
        private PooledBuffer<byte> truncationFlags;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<Range>(8);
            ranges.FakeClear();
            originalAdvances.Rent(256);
            glyphToGlobalCluster.Rent(256);
            rangeGlyphBoundsCache.Rent(8);
            currentCpWidths.Rent(256);
            clusterWidthsBuffer.Rent(256);
            lineTruncations ??= new PooledList<LineTruncation>(8);
            lineTruncations.FakeClear();
            truncationFlags.Rent(256);
            needsRestore = false;
            isProcessingRelayout = false;
            
            uniText.RectHeightChanged += OnRectHeightChanged;
            uniText.DirtyFlagsChanged += OnDirtyFlagsChanged;
            uniText.TextProcessor.Shaped += OnShaped;
            uniText.TextProcessor.LayoutComplete += OnLayoutComplete;
            uniText.MeshGenerator.OnGlyph += OnGlyph;
            uniText.MeshGenerator.OnAfterGlyphsPerFont += OnAfterGlyphsPerFont;
            uniText.MeshGenerator.OnRebuildEnd += OnRebuildEnd;
        }

        private void OnRectHeightChanged()
        {
            if ((uniText.CurrentDirtyFlags & UniText.DirtyFlags.Layout) == 0)
                uniText.SetDirty(UniText.DirtyFlags.Layout);
        }

        private void OnDirtyFlagsChanged(UniText.DirtyFlags flags)
        {
            if ((flags & UniText.DirtyFlags.Alignment) != 0 &&
                (uniText.CurrentDirtyFlags & UniText.DirtyFlags.Layout) == 0)
            {
                uniText.SetDirty(UniText.DirtyFlags.Layout);
            }
        }

        protected override void OnDisable()
        {
            uniText.RectHeightChanged -= OnRectHeightChanged;
            uniText.DirtyFlagsChanged -= OnDirtyFlagsChanged;
            uniText.TextProcessor.Shaped -= OnShaped;
            uniText.TextProcessor.LayoutComplete -= OnLayoutComplete;
            uniText.MeshGenerator.OnGlyph -= OnGlyph;
            uniText.MeshGenerator.OnAfterGlyphsPerFont -= OnAfterGlyphsPerFont;
            uniText.MeshGenerator.OnRebuildEnd -= OnRebuildEnd;
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
            originalAdvances.Return();
            glyphToGlobalCluster.Return();
            rangeGlyphBoundsCache.Return();
            currentCpWidths.Return();
            clusterWidthsBuffer.Return();
            lineTruncations?.Return();
            lineTruncations = null;
            truncationFlags.Return();
            needsRestore = false;
            isProcessingRelayout = false;
        }
        
        protected override void OnApply(int start, int end, string parameter)
        {
            ranges.Add(new Range
            {
                start = start,
                end = end,
                position = ParsePosition(parameter),
                truncateMinCluster = -1,
                truncateMaxCluster = -1,
                ellipsisCluster = -1,
                needsEllipsis = false
            });

            for (var i = 0; i < EllipsisText.Length; i++)
                buffers.virtualCodepoints.Add(EllipsisText[i]);
        }

        private void ClearEllipsisState()
        {
            lineTruncations?.FakeClear();
            truncationFlags.FakeClear();

            if (ranges == null) return;

            for (var r = 0; r < ranges.Count; r++)
            {
                var range = ranges[r];
                range.needsEllipsis = false;
                range.truncateMinCluster = -1;
                range.truncateMaxCluster = -1;
                range.ellipsisCluster = -1;
                ranges[r] = range;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ParsePosition(string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return 1f;

            if (float.TryParse(parameter, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                return Math.Clamp(value, 0f, 1f);

            return 1f;
        }

        private void OnShaped()
        {
            BuildGlyphToClusterMap();
            BuildRangeGlyphBounds();
        }

        private void BuildGlyphToClusterMap()
        {
            var buf = buffers;
            var glyphCount = buf.shapedGlyphs.count;

            glyphToGlobalCluster.FakeClear();
            if (glyphCount == 0)
                return;

            glyphToGlobalCluster.EnsureCapacity(glyphCount);

            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var glyphs = buf.shapedGlyphs.data;
            var clusterData = glyphToGlobalCluster.data;

            for (var r = 0; r < runCount; r++)
            {
                ref readonly var run = ref runs[r];
                var end = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < end; g++)
                    clusterData[g] = glyphs[g].cluster;
            }

            glyphToGlobalCluster.count = glyphCount;
        }

        private void BuildRangeGlyphBounds()
        {
            rangeGlyphBoundsCache.FakeClear();
            if (ranges == null || ranges.Count == 0)
                return;

            var rangeCount = ranges.Count;
            rangeGlyphBoundsCache.EnsureCapacity(rangeCount);

            var glyphCount = glyphToGlobalCluster.count;
            var clusterData = glyphToGlobalCluster.data;
            var boundsData = rangeGlyphBoundsCache.data;

            for (var r = 0; r < rangeCount; r++)
                boundsData[r] = (-1, -1, int.MaxValue, int.MinValue);

            for (var g = 0; g < glyphCount; g++)
            {
                var cluster = clusterData[g];
                for (var r = 0; r < rangeCount; r++)
                {
                    var range = ranges[r];
                    if (cluster >= range.start && cluster < range.end)
                    {
                        ref var bounds = ref boundsData[r];
                        if (bounds.Item1 < 0) bounds.Item1 = g;
                        bounds.Item2 = g;
                        if (cluster < bounds.Item3) bounds.Item3 = cluster;
                        if (cluster > bounds.Item4) bounds.Item4 = cluster;
                    }
                }
            }

            for (var r = 0; r < rangeCount; r++)
            {
                ref var bounds = ref boundsData[r];
                if (bounds.Item3 == int.MaxValue) bounds.Item3 = -1;
                if (bounds.Item4 == int.MinValue) bounds.Item4 = -1;
            }

            rangeGlyphBoundsCache.count = rangeCount;
        }

        private void OnLayoutComplete()
        {
            if (ranges == null || ranges.Count == 0)
                return;

            if (isProcessingRelayout)
                return;

            ClearEllipsisState();
            iterationCount = 0;

            var rect = uniText.cachedTransformData.rect;
            var maxWidth = rect.width;
            var maxHeight = rect.height;
            var resultWidth = uniText.TextProcessor.ResultWidth;
            var resultHeight = uniText.TextProcessor.ResultHeight;

            var hasHeightOverflow = maxHeight > 0 && !float.IsInfinity(maxHeight) && resultHeight > maxHeight + OverflowEpsilon;
            var hasWidthOverflow = !uniText.WordWrap && maxWidth > 0 && resultWidth > maxWidth + OverflowEpsilon;

            if (!hasHeightOverflow && !hasWidthOverflow)
                return;

            if (hasWidthOverflow && !uniText.WordWrap)
                ProcessNonWordWrapOverflow(maxWidth);
            else
                ProcessOverflowIterative(maxHeight);

            BuildTruncationFlags();
        }

        private void BuildTruncationFlags()
        {
            var maxCluster = buffers.codepoints.count;
            if (maxCluster == 0)
            {
                truncationFlags.count = 0;
                return;
            }

            truncationFlags.EnsureCount(maxCluster);
            truncationFlags.data.AsSpan(0, maxCluster).Clear();

            var flagsData = truncationFlags.data;

            if (lineTruncations != null)
            {
                for (var i = 0; i < lineTruncations.Count; i++)
                {
                    var lt = lineTruncations[i];
                    var min = Math.Max(0, lt.truncateMinCluster);
                    var max = Math.Min(maxCluster - 1, lt.truncateMaxCluster);
                    for (var c = min; c <= max; c++)
                        flagsData[c] = 1;
                }
            }

            if (ranges != null)
            {
                for (var r = 0; r < ranges.Count; r++)
                {
                    var range = ranges[r];
                    if (!range.needsEllipsis)
                        continue;

                    var min = Math.Max(0, range.truncateMinCluster);
                    var max = Math.Min(maxCluster - 1, range.truncateMaxCluster);
                    for (var c = min; c <= max; c++)
                        flagsData[c] = 1;
                }
            }
        }

        private void ProcessNonWordWrapOverflow(float maxWidth)
        {
            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var glyphCount = buf.shapedGlyphs.count;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;

            if (glyphCount == 0)
                return;

            var glyphScale = buf.GetGlyphScale(uniText.CurrentFontSize);
            var epsilonInShapingUnits = glyphScale > 0 ? OverflowEpsilon / glyphScale : OverflowEpsilon;
            var maxWidthInShapingUnits = glyphScale > 0 ? maxWidth / glyphScale : maxWidth;

            var ellipsisWidthDisplay = MeasureEllipsisWidth();
            var ellipsisWidth = glyphScale > 0 ? ellipsisWidthDisplay / glyphScale : ellipsisWidthDisplay;

            originalAdvances.EnsureCapacity(glyphCount);
            SaveOriginalAdvances(glyphs, glyphCount);

            isProcessingRelayout = true;
            needsRestore = true;

            var lines = buf.lines.data;
            var lineCount = buf.lines.count;
            var orderedRuns = buf.orderedRuns.data;

            for (var lineIdx = 0; lineIdx < lineCount; lineIdx++)
            {
                ref readonly var line = ref lines[lineIdx];
                if (line.width <= maxWidthInShapingUnits + epsilonInShapingUnits)
                    continue;

                var lineExcess = line.width - maxWidthInShapingUnits;

                var lineFirstGlyph = int.MaxValue;
                var lineLastGlyph = int.MinValue;
                for (var r = line.runStart; r < line.runStart + line.runCount; r++)
                {
                    ref readonly var run = ref orderedRuns[r];
                    if (run.glyphStart < lineFirstGlyph) lineFirstGlyph = run.glyphStart;
                    var runEnd = run.glyphStart + run.glyphCount - 1;
                    if (runEnd > lineLastGlyph) lineLastGlyph = runEnd;
                }

                var lineRangeWidth = 0f;
                var rangesOnLine = 0;
                var rangeCount = ranges.Count;
                var boundsData = rangeGlyphBoundsCache.data;
                var origAdvances = originalAdvances.data;
                var clusterData = glyphToGlobalCluster.data;

                for (var r = 0; r < rangeCount; r++)
                {
                    var (firstGlyph, lastGlyph, _, _) = boundsData[r];
                    if (firstGlyph < 0 || lastGlyph < lineFirstGlyph || firstGlyph > lineLastGlyph)
                        continue;

                    rangesOnLine++;

                    var lineRangeFirst = Math.Max(firstGlyph, lineFirstGlyph);
                    var lineRangeLast = Math.Min(lastGlyph, lineLastGlyph);

                    for (var g = lineRangeFirst; g <= lineRangeLast; g++)
                        lineRangeWidth += origAdvances[g];
                }

                if (lineRangeWidth <= 0)
                    continue;

                var lineWidthToRemove = lineExcess + ellipsisWidth * rangesOnLine;

                for (var r = 0; r < rangeCount; r++)
                {
                    var (firstGlyph, lastGlyph, _, _) = boundsData[r];
                    if (firstGlyph < 0)
                        continue;
                    if (lastGlyph < lineFirstGlyph || firstGlyph > lineLastGlyph)
                        continue;

                    var lineRangeFirst = Math.Max(firstGlyph, lineFirstGlyph);
                    var lineRangeLast = Math.Min(lastGlyph, lineLastGlyph);

                    var lineMinCluster = int.MaxValue;
                    var lineMaxCluster = int.MinValue;
                    for (var g = lineRangeFirst; g <= lineRangeLast; g++)
                    {
                        var cluster = clusterData[g];
                        if (cluster < lineMinCluster) lineMinCluster = cluster;
                        if (cluster > lineMaxCluster) lineMaxCluster = cluster;
                    }

                    if (lineMinCluster > lineMaxCluster)
                        continue;

                    var rangeWidth = 0f;
                    for (var g = lineRangeFirst; g <= lineRangeLast; g++)
                        rangeWidth += origAdvances[g];

                    var rangeWidthToRemove = lineWidthToRemove * (rangeWidth / lineRangeWidth);

                    var clusterCount = lineMaxCluster - lineMinCluster + 1;
                    clusterWidthsBuffer.EnsureCapacity(clusterCount);
                    var clusterWidths = clusterWidthsBuffer.data.AsSpan(0, clusterCount);
                    clusterWidths.Clear();
                    BuildClusterWidths(lineRangeFirst, lineRangeLast, lineMinCluster, clusterWidths);

                    var range = ranges[r];
                    var (truncMin, truncMax, ellipsisClusterTarget) = FindWidthBasedTruncation(
                        range.position, clusterWidths, lineMinCluster, lineMaxCluster, rangeWidthToRemove);

                    if (truncMin > truncMax)
                        continue;

                    var ellipsisGlyph = ApplyTruncationToGlyphs(
                        glyphs, lineRangeFirst, lineRangeLast, truncMin, truncMax, ellipsisClusterTarget,
                        ellipsisWidth, origAdvances, null, 0, clusterData);

                    if (ellipsisGlyph >= 0)
                    {
                        lineTruncations.Add(new LineTruncation
                        {
                            truncateMinCluster = truncMin,
                            truncateMaxCluster = truncMax,
                            ellipsisCluster = clusterData[ellipsisGlyph]
                        });
                    }
                }
            }

            RecalculateRunWidths(glyphs, runs, runCount);
            RecalculateRunWidths(glyphs, buf.orderedRuns.data, buf.orderedRuns.count);

            uniText.TextProcessor.ForceReposition();

            isProcessingRelayout = false;
        }

        private void BuildClusterWidths(int firstGlyph, int lastGlyph, int minCluster, Span<float> clusterWidths)
        {
            var clusterData = glyphToGlobalCluster.data;
            var origAdvances = originalAdvances.data;

            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                var cluster = clusterData[g];
                clusterWidths[cluster - minCluster] += origAdvances[g];
            }
        }

        private static (int truncMin, int truncMax, int ellipsisCluster) FindWidthBasedTruncation(
            float position, Span<float> clusterWidths, int minCluster, int maxCluster, float widthToRemove)
        {
            var anchor = minCluster + (int)(position * (maxCluster - minCluster));
            var truncMin = anchor;
            var truncMax = anchor;
            var accumulated = clusterWidths[anchor - minCluster];

            while (accumulated < widthToRemove)
            {
                var canExpandLeft = truncMin > minCluster;
                var canExpandRight = truncMax < maxCluster;

                if (!canExpandLeft && !canExpandRight)
                    break;

                if (canExpandLeft && canExpandRight)
                {
                    var leftWidth = clusterWidths[truncMin - 1 - minCluster];
                    var rightWidth = clusterWidths[truncMax + 1 - minCluster];

                    if (leftWidth <= rightWidth)
                    {
                        truncMin--;
                        accumulated += leftWidth;
                    }
                    else
                    {
                        truncMax++;
                        accumulated += rightWidth;
                    }
                }
                else if (canExpandLeft)
                {
                    truncMin--;
                    accumulated += clusterWidths[truncMin - minCluster];
                }
                else
                {
                    truncMax++;
                    accumulated += clusterWidths[truncMax - minCluster];
                }
            }

            return (truncMin, truncMax, anchor);
        }

        private void ProcessOverflowIterative(float maxHeight)
        {
            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var glyphCount = buf.shapedGlyphs.count;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var cpCount = buf.codepoints.count;

            if (glyphCount == 0)
                return;

            originalAdvances.EnsureCapacity(glyphCount);
            SaveOriginalAdvances(glyphs, glyphCount);

            currentCpWidths.EnsureCapacity(cpCount);

            var ellipsisWidth = MeasureEllipsisWidth();

            isProcessingRelayout = true;
            needsRestore = true;

            var low = 0f;
            var high = 1f;
            const float epsilon = 0.01f;

            while (iterationCount < MaxIterations && (high - low) > epsilon)
            {
                var mid = (low + high) / 2f;

                RestoreAdvancesForBinarySearch(glyphs, glyphCount);

                CopyBaseCpWidths(cpCount);
                ApplyTruncationRatio(glyphs, mid, ellipsisWidth);
                RecalculateRunWidths(glyphs, runs, runCount);

                uniText.TextProcessor.ForceRelayout(currentCpWidths.Span);
                var resultHeight = uniText.TextProcessor.ResultHeight;

                if (resultHeight > maxHeight)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }

                iterationCount++;
            }

            RestoreAdvancesForBinarySearch(glyphs, glyphCount);
            CopyBaseCpWidths(cpCount);
            ApplyTruncationRatio(glyphs, high, ellipsisWidth);
            RecalculateRunWidths(glyphs, runs, runCount);
            uniText.TextProcessor.ForceRelayout(currentCpWidths.Span);

            var rect = uniText.cachedTransformData.rect;
            var maxWidth = rect.width;
            var glyphScale = buf.GetGlyphScale(uniText.CurrentFontSize);

            if (OptimizeLineFill(glyphs, maxWidth, glyphScale))
            {
                RecalculateRunWidths(glyphs, runs, runCount);
                RecalculateRunWidths(glyphs, buf.orderedRuns.data, buf.orderedRuns.count);
                uniText.TextProcessor.ForceReposition();
            }

            isProcessingRelayout = false;
        }

        private bool OptimizeLineFill(ShapedGlyph[] glyphs, float maxWidth, float glyphScale)
        {
            if (ranges == null || ranges.Count == 0)
                return false;

            var buf = buffers;
            var lines = buf.lines.data;
            var lineCount = buf.lines.count;
            var clusterData = glyphToGlobalCluster.data;
            var origAdvances = originalAdvances.data;
            var boundsData = rangeGlyphBoundsCache.data;
            var cpWidths = currentCpWidths.data;
            var cpCount = currentCpWidths.count;

            var ellipsisWidthDisplay = MeasureEllipsisWidth();
            var ellipsisWidth = glyphScale > 0 ? ellipsisWidthDisplay / glyphScale : ellipsisWidthDisplay;

            var anyChanges = false;

            for (var r = 0; r < ranges.Count; r++)
            {
                var range = ranges[r];
                if (!range.needsEllipsis || range.truncateMinCluster < 0)
                    continue;

                var ellipsisLine = -1;
                for (var li = 0; li < lineCount; li++)
                {
                    ref readonly var line = ref lines[li];
                    if (range.ellipsisCluster >= line.range.start && range.ellipsisCluster < line.range.End)
                    {
                        ellipsisLine = li;
                        break;
                    }
                }

                if (ellipsisLine < 0)
                    continue;

                ref readonly var targetLine = ref lines[ellipsisLine];
                var availableWidth = (maxWidth - targetLine.width * glyphScale - targetLine.startMargin) / glyphScale;

                if (availableWidth <= 0)
                    continue;

                var (firstGlyph, lastGlyph, _, _) = boundsData[r];
                if (firstGlyph < 0)
                    continue;

                var clustersRestored = RestoreClustersForPosition(
                    range.position, glyphs, firstGlyph, lastGlyph,
                    range.truncateMinCluster, range.truncateMaxCluster,
                    availableWidth, clusterData, origAdvances, ellipsisWidth, cpWidths, cpCount,
                    out var newTruncMin, out var newTruncMax, out var newEllipsisCluster);

                if (clustersRestored > 0)
                {
                    range.truncateMinCluster = newTruncMin;
                    range.truncateMaxCluster = newTruncMax;
                    range.ellipsisCluster = newEllipsisCluster;
                    range.needsEllipsis = newTruncMin <= newTruncMax;
                    ranges[r] = range;
                    anyChanges = true;
                }
            }

            if (anyChanges)
                BuildTruncationFlags();

            return anyChanges;
        }

        private int RestoreClustersForPosition(
            float position, ShapedGlyph[] glyphs, int firstGlyph, int lastGlyph,
            int truncMin, int truncMax, float availableWidth,
            int[] clusterData, float[] origAdvances, float ellipsisWidth, float[] cpWidths, int cpCount,
            out int newTruncMin, out int newTruncMax, out int newEllipsisCluster)
        {
            newTruncMin = truncMin;
            newTruncMax = truncMax;
            newEllipsisCluster = -1;
            var restored = 0;

            var truncRange = truncMax - truncMin;
            var oldEllipsisCluster = truncRange > 0
                ? truncMin + (int)(position * truncRange)
                : truncMin;
            RemoveEllipsisFromCluster(glyphs, firstGlyph, lastGlyph, oldEllipsisCluster, clusterData, ellipsisWidth, cpWidths, cpCount);

            var remaining = availableWidth;
            var left = truncMin;
            var right = truncMax;
            var leftAcc = 0f;
            var rightAcc = 0f;

            while (left <= right && remaining > 0)
            {
                leftAcc += position;
                rightAcc += 1f - position;

                bool fromLeft;
                if (leftAcc >= rightAcc) { fromLeft = true; leftAcc -= 1f; }
                else { fromLeft = false; rightAcc -= 1f; }

                var cluster = fromLeft ? left : right;
                var clusterWidth = GetClusterWidth(firstGlyph, lastGlyph, cluster, clusterData, origAdvances);

                if (clusterWidth > remaining)
                    break;

                RestoreClusterGlyphs(glyphs, firstGlyph, lastGlyph, cluster, clusterData, origAdvances, cpWidths, cpCount);
                remaining -= clusterWidth;
                restored++;

                if (fromLeft) { newTruncMin = left + 1; left++; }
                else { newTruncMax = right - 1; right--; }
            }

            if (newTruncMin <= newTruncMax)
            {
                var newRange = newTruncMax - newTruncMin;
                newEllipsisCluster = newRange > 0
                    ? newTruncMin + (int)(position * newRange)
                    : newTruncMin;
                SetEllipsisAtCluster(glyphs, firstGlyph, lastGlyph, newEllipsisCluster, clusterData, ellipsisWidth, cpWidths, cpCount);
            }

            return restored;
        }

        private static void RemoveEllipsisFromCluster(ShapedGlyph[] glyphs, int firstGlyph, int lastGlyph,
            int cluster, int[] clusterData, float ellipsisWidth, float[] cpWidths, int cpCount)
        {
            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                if (clusterData[g] == cluster && glyphs[g].advanceX > 0)
                {
                    if ((uint)cluster < (uint)cpCount)
                        cpWidths[cluster] -= ellipsisWidth;
                    glyphs[g].advanceX = 0;
                    return;
                }
            }
        }

        private static void SetEllipsisAtCluster(ShapedGlyph[] glyphs, int firstGlyph, int lastGlyph,
            int cluster, int[] clusterData, float ellipsisWidth, float[] cpWidths, int cpCount)
        {
            var found = false;
            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                if (clusterData[g] == cluster)
                {
                    if (!found)
                    {
                        glyphs[g].advanceX = ellipsisWidth;
                        if ((uint)cluster < (uint)cpCount)
                            cpWidths[cluster] += ellipsisWidth;
                        found = true;
                    }
                    else
                    {
                        glyphs[g].advanceX = 0;
                    }
                }
            }
        }

        private static float GetClusterWidth(int firstGlyph, int lastGlyph, int cluster,
            int[] clusterData, float[] origAdvances)
        {
            var width = 0f;
            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                if (clusterData[g] == cluster)
                    width += origAdvances[g];
            }
            return width;
        }

        private static void RestoreClusterGlyphs(ShapedGlyph[] glyphs, int firstGlyph, int lastGlyph,
            int cluster, int[] clusterData, float[] origAdvances, float[] cpWidths, int cpCount)
        {
            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                if (clusterData[g] == cluster)
                {
                    var advance = origAdvances[g];
                    glyphs[g].advanceX = advance;

                    if ((uint)cluster < (uint)cpCount)
                        cpWidths[cluster] += advance;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RestoreAdvancesForBinarySearch(ShapedGlyph[] glyphs, int glyphCount)
        {
            var advances = originalAdvances.data;
            var count = Math.Min(originalAdvances.count, glyphCount);
            for (var i = 0; i < count; i++)
                glyphs[i].advanceX = advances[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyBaseCpWidths(int cpCount)
        {
            var src = buffers.cpWidths.data;
            var dst = currentCpWidths.data;
            var count = Math.Min(buffers.cpWidths.count, cpCount);
            Array.Copy(src, dst, count);
            currentCpWidths.count = count;
        }

        private void ApplyTruncationRatio(ShapedGlyph[] glyphs, float ratio, float ellipsisWidth)
        {
            if (ratio <= 0)
                return;

            var rangeCount = ranges.Count;
            var boundsData = rangeGlyphBoundsCache.data;
            var origAdvances = originalAdvances.data;
            var cpWidths = currentCpWidths.data;
            var clusterData = glyphToGlobalCluster.data;
            var cpCount = currentCpWidths.count;

            for (var r = 0; r < rangeCount; r++)
            {
                var (firstGlyph, lastGlyph, minCluster, maxCluster) = boundsData[r];
                if (firstGlyph < 0 || minCluster > maxCluster)
                    continue;

                var clusterRange = maxCluster - minCluster + 1;
                var clustersToTruncate = (int)Math.Ceiling(clusterRange * ratio);
                if (clustersToTruncate <= 0)
                    continue;

                var range = ranges[r];
                var (truncMin, truncMax, ellipsisCluster) = FindRatioBasedTruncation(
                    range.position, minCluster, maxCluster, clustersToTruncate);

                var ellipsisGlyph = ApplyTruncationToGlyphs(
                    glyphs, firstGlyph, lastGlyph, truncMin, truncMax, ellipsisCluster,
                    ellipsisWidth, origAdvances, cpWidths, cpCount, clusterData);

                UpdateRangeState(r, truncMin, truncMax, ellipsisGlyph, clusterData);
            }
        }

        private int ApplyTruncationToGlyphs(
            ShapedGlyph[] glyphs, int firstGlyph, int lastGlyph,
            int truncMin, int truncMax, int ellipsisClusterTarget, float ellipsisWidth,
            float[] origAdvances, float[] cpWidths, int cpCount, int[] clusterData)
        {
            var ellipsisGlyph = -1;

            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                var cluster = clusterData[g];
                if (cluster < truncMin || cluster > truncMax)
                    continue;

                var oldAdvance = origAdvances[g];
                if (ellipsisGlyph < 0 || cluster == ellipsisClusterTarget)
                    ellipsisGlyph = g;

                glyphs[g].advanceX = 0f;

                if (cpWidths != null && (uint)cluster < (uint)cpCount)
                    cpWidths[cluster] -= oldAdvance;
            }

            if (ellipsisGlyph >= 0)
            {
                glyphs[ellipsisGlyph].advanceX = ellipsisWidth;
                if (cpWidths != null)
                {
                    var cluster = clusterData[ellipsisGlyph];
                    if ((uint)cluster < (uint)cpCount)
                        cpWidths[cluster] += ellipsisWidth;
                }
            }

            return ellipsisGlyph;
        }

        private void UpdateRangeState(int rangeIndex, int truncMin, int truncMax, int ellipsisGlyph, int[] clusterData)
        {
            var range = ranges[rangeIndex];
            range.truncateMinCluster = truncMin;
            range.truncateMaxCluster = truncMax;
            range.ellipsisCluster = ellipsisGlyph >= 0 ? clusterData[ellipsisGlyph] : -1;
            range.needsEllipsis = ellipsisGlyph >= 0;
            ranges[rangeIndex] = range;
        }

        private static (int truncMin, int truncMax, int ellipsisCluster) FindRatioBasedTruncation(
            float position, int minCluster, int maxCluster, int clustersToTruncate)
        {
            var clusterRange = maxCluster - minCluster + 1;
            var keptTotal = clusterRange - clustersToTruncate;
            var keptFromStart = (int)(position * Math.Max(keptTotal, 0));

            var truncMin = Math.Max(minCluster + keptFromStart, minCluster);
            var truncMax = Math.Min(truncMin + clustersToTruncate - 1, maxCluster);

            if (truncMax == maxCluster)
                truncMin = Math.Max(maxCluster - clustersToTruncate + 1, minCluster);

            var anchor = minCluster + (int)(position * (maxCluster - minCluster));
            var ellipsisCluster = Math.Clamp(anchor, truncMin, truncMax);

            return (truncMin, truncMax, ellipsisCluster);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SaveOriginalAdvances(ShapedGlyph[] glyphs, int count)
        {
            var advances = originalAdvances.data;
            for (var i = 0; i < count; i++)
                advances[i] = glyphs[i].advanceX;

            originalAdvances.count = count;
        }

        private float MeasureEllipsisWidth()
        {
            var fontProvider = uniText.FontProvider;
            if (fontProvider == null)
                return 0f;

            var fontAsset = fontProvider.GetFontAsset(0);
            if (fontAsset == null)
                return 0f;

            var fontSize = uniText.CurrentFontSize;
            var scale = fontSize * fontAsset.FontScale / fontAsset.UnitsPerEm;

            var charTable = fontAsset.CharacterLookupTable;
            if (charTable != null && charTable.TryGetValue('.', out var dotCh) && dotCh != null)
                return dotCh.glyph.metrics.horizontalAdvance * scale * 3;

            return 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RecalculateRunWidths(ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount)
        {
            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var width = 0f;
                var end = run.glyphStart + run.glyphCount;

                for (var g = run.glyphStart; g < end; g++)
                    width += glyphs[g].advanceX;

                run.width = width;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnGlyph()
        {
            var cluster = UniTextMeshGenerator.Current.currentCluster;

            if ((uint)cluster < (uint)truncationFlags.count && truncationFlags.data[cluster] != 0)
            {
                var gen = UniTextMeshGenerator.Current;
                gen.vertexCount -= 4;
                gen.triangleCount -= 6;
            }
        }

        private void OnAfterGlyphsPerFont()
        {
            if (lineTruncations != null && lineTruncations.Count > 0)
            {
                for (var i = 0; i < lineTruncations.Count; i++)
                    DrawEllipsisAtCluster(lineTruncations[i].ellipsisCluster);
            }

            if (ranges == null || ranges.Count == 0)
                return;

            for (var r = 0; r < ranges.Count; r++)
            {
                var range = ranges[r];
                if (!range.needsEllipsis)
                    continue;

                DrawEllipsisAtCluster(range.ellipsisCluster);
            }
        }

        private void DrawEllipsisAtCluster(int ellipsisCluster)
        {
            var positionedGlyphs = buffers.positionedGlyphs.data;
            var positionedCount = buffers.positionedGlyphs.count;

            if (positionedCount == 0)
                return;

            var fontProvider = uniText.FontProvider;
            if (fontProvider == null)
                return;

            var gen = UniTextMeshGenerator.Current;
            var shapedGlyphs = buffers.shapedGlyphs.data;
            var glyphScale = buffers.GetGlyphScale(uniText.CurrentFontSize);

            for (var i = 0; i < positionedCount; i++)
            {
                if (positionedGlyphs[i].cluster == ellipsisCluster)
                {
                    ref readonly var pg = ref positionedGlyphs[i];
                    ref readonly var shapedGlyph = ref shapedGlyphs[pg.shapedGlyphIndex];

                    var baselineX = pg.x - shapedGlyph.offsetX * glyphScale;
                    var baselineY = pg.y + shapedGlyph.offsetY * glyphScale;

                    var x = gen.offsetX + baselineX;
                    var y = gen.offsetY - baselineY;
                    GlyphRenderHelper.DrawString(fontProvider, EllipsisText, x, y, gen.defaultColor);
                    return;
                }
            }
        }

        private void OnRebuildEnd()
        {
            if (!needsRestore)
                return;

            RestoreOriginalAdvances();
            needsRestore = false;
        }

        private void RestoreOriginalAdvances()
        {
            if (originalAdvances.count == 0)
                return;

            var glyphs = buffers.shapedGlyphs.data;
            var advances = originalAdvances.data;
            var count = Math.Min(originalAdvances.count, buffers.shapedGlyphs.count);

            for (var i = 0; i < count; i++)
                glyphs[i].advanceX = advances[i];

            RecalculateRunWidths(glyphs, buffers.shapedRuns.data, buffers.shapedRuns.count);
            RecalculateRunWidths(glyphs, buffers.orderedRuns.data, buffers.orderedRuns.count);

            originalAdvances.FakeClear();
        }
    }
}
