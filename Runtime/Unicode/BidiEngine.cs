#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Specifies the paragraph direction for bidirectional text processing.
    /// </summary>
    /// <seealso cref="BidiEngine"/>
    internal enum BidiParagraphDirection
    {
        /// <summary>Force left-to-right paragraph direction.</summary>
        LeftToRight = 0,
        /// <summary>Force right-to-left paragraph direction.</summary>
        RightToLeft = 1,
        /// <summary>Detect paragraph direction automatically from first strong character.</summary>
        Auto = 2
    }

    /// <summary>
    /// Represents a paragraph within bidirectional text with its resolved base direction.
    /// </summary>
    /// <remarks>
    /// A paragraph is a unit of text separated by paragraph separators (U+2029 or hard line breaks).
    /// The base level determines the default direction for neutral characters.
    /// </remarks>
    internal readonly struct BidiParagraph
    {
        /// <summary>Start index of the paragraph in the codepoint array.</summary>
        public readonly int startIndex;
        /// <summary>End index (inclusive) of the paragraph in the codepoint array.</summary>
        public readonly int endIndex;
        /// <summary>Resolved embedding level (0 = LTR, 1 = RTL).</summary>
        public readonly byte baseLevel;

        /// <summary>
        /// Initializes a new paragraph with the specified range and base level.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiParagraph(int startIndex, int endIndex, byte baseLevel)
        {
            this.startIndex = startIndex;
            this.endIndex = endIndex;
            this.baseLevel = baseLevel;
        }

        /// <summary>Gets the resolved direction of this paragraph.</summary>
        public BidiDirection Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (baseLevel & 1) == 0 ? BidiDirection.LeftToRight : BidiDirection.RightToLeft;
        }

        /// <summary>Gets the length of the paragraph in codepoints.</summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => endIndex - startIndex + 1;
        }
    }

    /// <summary>
    /// Contains the results of bidirectional text processing.
    /// </summary>
    /// <remarks>
    /// The <see cref="levels"/> array contains per-character embedding levels (0-125).
    /// Odd levels indicate RTL runs, even levels indicate LTR runs.
    /// Use <see cref="BidiEngine.ReorderLine"/> to convert levels to visual order.
    /// </remarks>
    internal readonly struct BidiResult
    {
        /// <summary>Per-codepoint embedding levels. Odd = RTL, even = LTR.</summary>
        public readonly byte[] levels;
        /// <summary>Number of valid levels in the array (may be less than array length for pooled buffers).</summary>
        public readonly int levelsLength;
        /// <summary>Array of paragraphs found in the text.</summary>
        public readonly BidiParagraph[] paragraphs;
        /// <summary>Number of valid paragraphs in the array.</summary>
        public readonly int paragraphCount;

        /// <summary>
        /// Initializes a new result with the given levels and paragraphs.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiResult(byte[] levels, BidiParagraph[] paragraphs)
        {
            this.levels = levels;
            this.levelsLength = levels?.Length ?? 0;
            this.paragraphs = paragraphs;
            paragraphCount = paragraphs?.Length ?? 0;
        }

        /// <summary>
        /// Initializes a new result with pooled arrays (used internally for zero-allocation processing).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiResult(byte[] levels, int levelsLength, BidiParagraph[] paragraphs, int paragraphCount)
        {
            this.levels = levels;
            this.levelsLength = levelsLength;
            this.paragraphs = paragraphs;
            this.paragraphCount = paragraphCount;
        }

        /// <summary>Gets the direction of the first paragraph, or LTR if empty.</summary>
        public BidiDirection Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => paragraphCount > 0 ? paragraphs[0].Direction : BidiDirection.LeftToRight;
        }

        /// <summary>Gets the valid paragraphs as a span.</summary>
        public ReadOnlySpan<BidiParagraph> ParagraphsSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => paragraphs != null ? paragraphs.AsSpan(0, paragraphCount) : ReadOnlySpan<BidiParagraph>.Empty;
        }

        /// <summary>Returns true if any character has an odd (RTL) embedding level.</summary>
        public bool HasRtlContent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var lvls = levels;
                for (var i = 0; i < lvls.Length; i++)
                    if ((lvls[i] & 1) != 0)
                        return true;

                return false;
            }
        }
    }

    /// <summary>
    /// Implements the Unicode Bidirectional Algorithm (UAX #9) for mixed-direction text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The BiDi algorithm determines the correct visual ordering of text containing
    /// both left-to-right (Latin, Cyrillic) and right-to-left (Arabic, Hebrew) scripts.
    /// </para>
    /// <para>
    /// This implementation passes 100% of the Unicode BiDi conformance tests and supports:
    /// <list type="bullet">
    /// <item>Explicit embedding levels (LRE, RLE, LRO, RLO, PDF)</item>
    /// <item>Isolate controls (LRI, RLI, FSI, PDI)</item>
    /// <item>Paired bracket resolution (rule N0)</item>
    /// <item>Multiple paragraphs with independent base directions</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <seealso cref="BidiResult"/>
    /// <seealso cref="BidiParagraph"/>
    internal sealed class BidiEngine
    {
        private readonly struct LevelRun
        {
            public readonly int startIndex;
            public readonly int endIndex;
            public readonly byte level;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public LevelRun(int startIndex, int endIndex, byte level)
            {
                this.startIndex = startIndex;
                this.endIndex = endIndex;
                this.level = level;
            }
        }

        private readonly struct IsolatingRunSequence
        {
            public readonly int indexStart;
            public readonly int indexLength;
            public readonly byte level;
            public readonly BidiClass sos;
            public readonly BidiClass eos;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IsolatingRunSequence(int indexStart, int indexLength, byte level, BidiClass sos, BidiClass eos)
            {
                this.indexStart = indexStart;
                this.indexLength = indexLength;
                this.level = level;
                this.sos = sos;
                this.eos = eos;
            }

            public int Length
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => indexLength;
            }

            public int this[int sequenceIndex]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => SequenceIndicesBuffer[indexStart + sequenceIndex];
            }
        }

        private struct EmbeddingState
        {
            public byte level;
            public sbyte overrideStatus;
            public bool isIsolate;
        }

        private readonly struct BracketPair
        {
            public readonly int openIndex;
            public readonly int closeIndex;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public BracketPair(int openIndex, int closeIndex)
            {
                this.openIndex = openIndex;
                this.closeIndex = closeIndex;
            }
        }

        private sealed class BracketPairComparer : IComparer<BracketPair>
        {
            public static readonly BracketPairComparer Instance = new();
            public int Compare(BracketPair a, BracketPair b) => a.openIndex.CompareTo(b.openIndex);
        }

        private const int MaxExplicitLevel = 125;

        private readonly UnicodeDataProvider unicodeData;

        [ThreadStatic] private static EmbeddingState[]? embeddingStack;
        [ThreadStatic] private static BidiClass[]? bidiClasses;
        [ThreadStatic] private static BidiClass[]? originalBidiClasses;
        [ThreadStatic] private static PooledList<BidiParagraph>? paragraphs;
        [ThreadStatic] private static PooledList<LevelRun>? levelRuns;
        [ThreadStatic] private static PooledList<IsolatingRunSequence>? sequences;
        [ThreadStatic] private static PooledList<int>? seqIndices;
        [ThreadStatic] private static PooledList<BracketPair>? bracketPairs;
        [ThreadStatic] private static PooledList<int>? openStack;
        [ThreadStatic] private static PooledList<int>? isolatePairStack;
        [ThreadStatic] private static Stack<int>? isolateStack;

        [ThreadStatic] private static byte[]? levelsBuffer;
        [ThreadStatic] private static BidiParagraph[]? paragraphsResultBuffer;
        [ThreadStatic] private static int[]? matchingIsolateBuffer;
        [ThreadStatic] private static int[]? runIndexByPositionBuffer;
        [ThreadStatic] private static PooledList<LevelRun>? bracketLevelRuns;
        [ThreadStatic] private static PooledList<IsolatingRunSequence>? bracketSequences;

        [ThreadStatic] private static int[]? sequenceIndicesBuffer;
        [ThreadStatic] private static int sequenceIndicesCount;

        [ThreadStatic] private static int[]? nextRunBuffer;
        [ThreadStatic] private static bool[]? hasPredecessorBuffer;
        [ThreadStatic] private static bool[]? visitedBuffer;

        private static EmbeddingState[] EmbeddingStack => embeddingStack ??= new EmbeddingState[MaxExplicitLevel + 2];
        private static PooledList<BidiParagraph> ParagraphsBuffer => paragraphs ??= new PooledList<BidiParagraph>(4);
        private static PooledList<IsolatingRunSequence> SequencesBuffer => sequences ??= new PooledList<IsolatingRunSequence>(16);
        private static PooledList<int> SeqIndicesBuffer => seqIndices ??= new PooledList<int>(128);
        private static PooledList<BracketPair> BracketPairsBuffer => bracketPairs ??= new PooledList<BracketPair>(32);
        private static PooledList<int> OpenStackBuffer => openStack ??= new PooledList<int>(32);
        private static PooledList<int> IsolatePairStackBuffer => isolatePairStack ??= new PooledList<int>(32);
        private static Stack<int> IsolateStackBuffer => isolateStack ??= new Stack<int>(16);

        private static void EnsureBidiClassesCapacity(int length)
        {
            if (bidiClasses == null || bidiClasses.Length < length)
            {
                var newSize = Math.Max(length, 64);
                bidiClasses = new BidiClass[newSize];
                originalBidiClasses = new BidiClass[newSize];
            }
        }

        private static void EnsureLevelsCapacity(int length)
        {
            if (levelsBuffer == null || levelsBuffer.Length < length)
                levelsBuffer = new byte[Math.Max(length, 256)];
        }

        private static void EnsureParagraphsResultCapacity(int count)
        {
            if (paragraphsResultBuffer == null || paragraphsResultBuffer.Length < count)
                paragraphsResultBuffer = new BidiParagraph[Math.Max(count, 8)];
        }

        private static void EnsureMatchingIsolateCapacity(int length)
        {
            if (matchingIsolateBuffer == null || matchingIsolateBuffer.Length < length)
                matchingIsolateBuffer = new int[Math.Max(length, 256)];
            if (runIndexByPositionBuffer == null || runIndexByPositionBuffer.Length < length)
                runIndexByPositionBuffer = new int[Math.Max(length, 256)];
        }

        private static void EnsureRunBuffersCapacity(int runCount)
        {
            if (nextRunBuffer == null || nextRunBuffer.Length < runCount)
            {
                var newSize = Math.Max(runCount, 32);
                nextRunBuffer = new int[newSize];
                hasPredecessorBuffer = new bool[newSize];
                visitedBuffer = new bool[newSize];
            }
            else if (hasPredecessorBuffer == null || hasPredecessorBuffer.Length < runCount)
            {
                hasPredecessorBuffer = new bool[nextRunBuffer.Length];
                visitedBuffer = new bool[nextRunBuffer.Length];
            }
        }

        private static PooledList<LevelRun> BracketLevelRunsBuffer => bracketLevelRuns ??= new PooledList<LevelRun>(32);

        private static PooledList<IsolatingRunSequence> BracketSequencesBuffer =>
            bracketSequences ??= new PooledList<IsolatingRunSequence>(16);

        [ThreadStatic] private static PooledList<(int runStart, int runEnd, byte level)>? tempLevelRuns;

        private static PooledList<(int runStart, int runEnd, byte level)> TempLevelRunsBuffer
            => tempLevelRuns ??= new PooledList<(int, int, byte)>(32);

        private static int[] SequenceIndicesBuffer
        {
            get
            {
                if (sequenceIndicesBuffer == null)
                    sequenceIndicesBuffer = new int[1024];
                return sequenceIndicesBuffer;
            }
        }

        private static void EnsureSequenceIndicesCapacity(int additionalRequired)
        {
            var required = sequenceIndicesCount + additionalRequired;
            if (sequenceIndicesBuffer == null || sequenceIndicesBuffer.Length < required)
            {
                var newSize = Math.Max(required, (sequenceIndicesBuffer?.Length ?? 256) * 2);
                Array.Resize(ref sequenceIndicesBuffer, newSize);
            }
        }

        private static void ResetSequenceIndices()
        {
            sequenceIndicesCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReadOnlySpan<int> GetSequenceIndices(in IsolatingRunSequence seq)
        {
            return SequenceIndicesBuffer.AsSpan(seq.indexStart, seq.indexLength);
        }

        private static BidiClass[] BidiClassesBuffer => bidiClasses!;
        private static BidiClass[] OriginalBidiClassesBuffer => originalBidiClasses!;

        /// <summary>
        /// Initializes a new BidiEngine with a specific Unicode data provider.
        /// </summary>
        public BidiEngine(UnicodeDataProvider unicodeData)
        {
            this.unicodeData = unicodeData ?? throw new ArgumentNullException(nameof(unicodeData));
        }

        /// <summary>
        /// Initializes a new BidiEngine using the global <see cref="UnicodeData.Provider"/>.
        /// </summary>
        public BidiEngine()
        {
            unicodeData = UnicodeData.Provider ?? throw new InvalidOperationException(
                "UnicodeData not initialized. Call UnicodeData.EnsureInitialized() first.");
        }

        /// <summary>
        /// Processes codepoints through the BiDi algorithm and returns embedding levels.
        /// Uses pooled arrays internally - zero allocation per call.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <param name="direction">Paragraph direction hint, or Auto to detect.</param>
        /// <returns>BiDi result containing per-character levels and paragraph info.
        /// The returned arrays are pooled and will be reused on the next call.</returns>
        public BidiResult Process(ReadOnlySpan<int> codePoints,
            BidiParagraphDirection direction = BidiParagraphDirection.Auto)
        {
            byte? forcedLevel = direction switch
            {
                BidiParagraphDirection.LeftToRight => 0,
                BidiParagraphDirection.RightToLeft => 1,
                _ => null
            };
            return ProcessInternal(codePoints, forcedLevel);
        }

        /// <summary>
        /// Processes codepoints with an integer direction hint.
        /// Uses pooled arrays internally - zero allocation per call.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <param name="paragraphDirection">0 = LTR, 1 = RTL, 2 = Auto.</param>
        /// <returns>BiDi result containing per-character levels and paragraph info.
        /// The returned arrays are pooled and will be reused on the next call.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiResult Process(ReadOnlySpan<int> codePoints, int paragraphDirection)
        {
            byte? forcedLevel = paragraphDirection switch
            {
                0 => 0,
                1 => 1,
                2 => null,
                _ => throw new ArgumentOutOfRangeException(nameof(paragraphDirection))
            };
            return ProcessInternal(codePoints, forcedLevel);
        }

        /// <summary>
        /// Quickly detects the dominant direction without full BiDi processing.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <returns>Direction based on the first strong character found.</returns>
        public BidiDirection DetectDirection(ReadOnlySpan<int> codePoints)
        {
            for (var i = 0; i < codePoints.Length; i++)
            {
                var bc = unicodeData.GetBidiClass(codePoints[i]);
                if (bc == BidiClass.LeftToRight)
                    return BidiDirection.LeftToRight;
                if (bc == BidiClass.RightToLeft || bc == BidiClass.ArabicLetter)
                    return BidiDirection.RightToLeft;
            }

            return BidiDirection.LeftToRight;
        }

        /// <summary>
        /// Computes the visual display order for a span of codepoints.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <param name="direction">Paragraph direction hint.</param>
        /// <returns>
        /// Array where indexMap[visual] = logical. Use to reorder characters for display.
        /// </returns>
        public int[] GetVisualOrder(ReadOnlySpan<int> codePoints,
            BidiParagraphDirection direction = BidiParagraphDirection.Auto)
        {
            if (codePoints.Length == 0)
                return Array.Empty<int>();

            var result = Process(codePoints, direction);
            var order = new int[codePoints.Length];
            ReorderLine(result.levels, 0, codePoints.Length - 1, order);
            return order;
        }

        /// <summary>
        /// Converts embedding levels to visual order indices using the L2 algorithm.
        /// </summary>
        /// <param name="levels">Per-character embedding levels.</param>
        /// <param name="start">Start index in the levels array.</param>
        /// <param name="end">End index (inclusive) in the levels array.</param>
        /// <param name="indexMap">Output array: indexMap[visual] = logical position.</param>
        /// <remarks>
        /// Implements UAX #9 rule L2: from the highest level down to the lowest odd level,
        /// reverse any contiguous sequence of characters at that level or higher.
        /// </remarks>
        public static void ReorderLine(byte[] levels, int start, int end, int[] indexMap)
        {
            var length = end - start + 1;
            if (length <= 0)
                return;

            for (var i = 0; i < length; i++)
                indexMap[i] = start + i;

            byte maxLevel = 0;
            var minOddLevel = byte.MaxValue;

            for (var i = start; i <= end; i++)
            {
                var level = levels[i];
                if (level > maxLevel)
                    maxLevel = level;
                if ((level & 1) != 0 && level < minOddLevel)
                    minOddLevel = level;
            }

            if (minOddLevel == byte.MaxValue)
                return;

            for (var level = maxLevel; level >= minOddLevel; level--)
            {
                var i = 0;
                while (i < length)
                    if (levels[indexMap[i]] >= level)
                    {
                        var runStart = i;
                        var runEnd = i + 1;
                        while (runEnd < length && levels[indexMap[runEnd]] >= level)
                            runEnd++;

                        var left = runStart;
                        var right = runEnd - 1;
                        while (left < right)
                        {
                            (indexMap[left], indexMap[right]) = (indexMap[right], indexMap[left]);
                            left++;
                            right--;
                        }

                        i = runEnd;
                    }
                    else
                    {
                        i++;
                    }

                if (level == 0)
                    break;
            }
        }

        private BidiResult ProcessInternal(ReadOnlySpan<int> codePoints, byte? forcedParagraphLevel)
        {
            UniTextDebug.Increment(ref UniTextDebug.Bidi_ProcessCount);

            var length = codePoints.Length;
            if (length == 0)
                return new BidiResult(Array.Empty<byte>(), Array.Empty<BidiParagraph>());

            EnsureBidiClassesCapacity(length);
            EnsureLevelsCapacity(length);
            var bidiClasses = BidiClassesBuffer;
            var originalClasses = OriginalBidiClassesBuffer;

            for (var i = 0; i < length; i++)
            {
                var cp = codePoints[i];
                if ((uint)cp > 0x10FFFFU)
                    cp = UnicodeData.ReplacementCharacter;

                var bc = unicodeData.GetBidiClass(cp);
                bidiClasses[i] = bc;
                originalClasses[i] = bc;
            }

            var paragraphList = ParagraphsBuffer;
            paragraphList.FakeClear();

            if (forcedParagraphLevel.HasValue)
                BuildParagraphsWithExplicitBaseLevel(bidiClasses, length, forcedParagraphLevel.Value, paragraphList);
            else
                BuildParagraphs(bidiClasses, length, paragraphList);

            var levels = levelsBuffer!;
            Array.Clear(levels, 0, length);
            var paragraphCount = paragraphList.Count;

            for (var pIndex = 0; pIndex < paragraphCount; pIndex++)
            {
                var paragraph = paragraphList[pIndex];
                ResolveExplicitLevelsForParagraph(paragraph.startIndex, paragraph.endIndex, paragraph.baseLevel,
                    bidiClasses, levels);
            }

            for (var pIndex = 0; pIndex < paragraphCount; pIndex++)
            {
                var paragraph = paragraphList[pIndex];

                var sequences = BuildIsolatingRunSequences(
                    paragraph.startIndex, paragraph.endIndex, paragraph.baseLevel, bidiClasses, levels);

                ResolveWeakTypesWithSequences(sequences, bidiClasses);
                ResolvePairedBracketsForParagraph(codePoints, paragraph.startIndex, paragraph.endIndex,
                    paragraph.baseLevel, bidiClasses, levels);
                ResolveNeutralTypesWithSequences(sequences, bidiClasses);
            }

            for (var pIndex = 0; pIndex < paragraphCount; pIndex++)
            {
                var paragraph = paragraphList[pIndex];
                ResolveImplicitLevelsForParagraph(paragraph.startIndex, paragraph.endIndex, bidiClasses, levels);
            }

            for (var pIndex = 0; pIndex < paragraphCount; pIndex++)
            {
                var paragraph = paragraphList[pIndex];
                ApplyLineBreakRuleL1ForParagraph(codePoints, paragraph.startIndex, paragraph.endIndex, paragraph.baseLevel,
                    levels);
            }

            EnsureParagraphsResultCapacity(paragraphCount);
            for (var i = 0; i < paragraphCount; i++)
                paragraphsResultBuffer![i] = paragraphList[i];

            return new BidiResult(levels, length, paragraphsResultBuffer!, paragraphCount);
        }


        private PooledList<IsolatingRunSequence> BuildIsolatingRunSequences(
            int start,
            int end,
            byte paragraphBaseLevel,
            BidiClass[] bidiClasses,
            byte[] levels)
        {
            UniTextDebug.Increment(ref UniTextDebug.Bidi_BuildIsoRunSeqCount);
            SequencesBuffer.FakeClear();
            ResetSequenceIndices();

            if (start > end)
                return SequencesBuffer;

            var length = end - start + 1;

            var isolateToPdi = UniTextArrayPool<int>.Rent(length);
            var pdiToIsolate = UniTextArrayPool<int>.Rent(length);

            try
            {
                for (var i = 0; i < length; i++)
                {
                    isolateToPdi[i] = -1;
                    pdiToIsolate[i] = -1;
                }

                var seqBuffer = SeqIndicesBuffer;
                var isolateStack = IsolateStackBuffer;

                isolateStack.Clear();

                for (var index = start; index <= end; index++)
                {
                    var bc = bidiClasses[index];

                    if (bc == BidiClass.LeftToRightIsolate ||
                        bc == BidiClass.RightToLeftIsolate ||
                        bc == BidiClass.FirstStrongIsolate)
                        isolateStack.Push(index);
                    else if (bc == BidiClass.PopDirectionalIsolate)
                        if (isolateStack.Count > 0)
                        {
                            var open = isolateStack.Pop();
                            isolateToPdi[open - start] = index;
                            pdiToIsolate[index - start] = open;
                        }
                }

                var levelRuns = TempLevelRunsBuffer;
                levelRuns.FakeClear();

                {
                    var runStart = -1;
                    byte runLevel = 0;

                    for (var i = start; i <= end; i++)
                    {
                        if (bidiClasses[i] == BidiClass.BoundaryNeutral)
                            continue;

                        var l = levels[i];

                        if (runStart == -1)
                        {
                            runStart = i;
                            runLevel = l;
                        }
                        else if (l != runLevel)
                        {
                            levelRuns.Add((runStart, FindLastNonBnBefore(i, start, bidiClasses), runLevel));
                            runStart = i;
                            runLevel = l;
                        }
                    }

                    if (runStart != -1)
                        levelRuns.Add((runStart, FindLastNonBnBefore(end + 1, start, bidiClasses), runLevel));
                }

                var levelRunsCount = levelRuns.Count;

                for (var r = 0; r < levelRunsCount; r++)
                {
                    var run = levelRuns[r];
                    var firstIndex = run.runStart;
                    var firstBc = bidiClasses[firstIndex];

                    if (firstBc == BidiClass.PopDirectionalIsolate &&
                        pdiToIsolate[firstIndex - start] != -1)
                        continue;

                    seqBuffer.FakeClear();
                    var currentRunIndex = r;

                    while (true)
                    {
                        var currentRun = levelRuns[currentRunIndex];

                        var runLen = currentRun.runEnd - currentRun.runStart + 1;
                        seqBuffer.EnsureCapacity(seqBuffer.buffer.count + runLen);
                        var seqArr = seqBuffer.buffer.data;
                        var seqIdx = seqBuffer.buffer.count;
                        for (var i = currentRun.runStart; i <= currentRun.runEnd; i++)
                            seqArr[seqIdx++] = i;
                        seqBuffer.buffer.count = seqIdx;

                        var lastIndex = currentRun.runEnd;
                        var lastBc = bidiClasses[lastIndex];

                        var isIsolateInitiator =
                            lastBc == BidiClass.LeftToRightIsolate ||
                            lastBc == BidiClass.RightToLeftIsolate ||
                            lastBc == BidiClass.FirstStrongIsolate;

                        if (!isIsolateInitiator) break;

                        var pdiIndex = isolateToPdi[lastIndex - start];
                        if (pdiIndex < 0) break;

                        var nextRunIndex = -1;
                        for (var i = 0; i < levelRunsCount; i++)
                        {
                            var r2 = levelRuns[i];
                            if (r2.runStart <= pdiIndex && pdiIndex <= r2.runEnd)
                            {
                                nextRunIndex = i;
                                break;
                            }
                        }

                        if (nextRunIndex < 0 || nextRunIndex == currentRunIndex) break;

                        currentRunIndex = nextRunIndex;
                    }

                    var seqCount = seqBuffer.Count;
                    if (seqCount == 0)
                        continue;

                    EnsureSequenceIndicesCapacity(seqCount);
                    var seqStart = sequenceIndicesCount;
                    var sharedBuffer = SequenceIndicesBuffer;
                    for (var i = 0; i < seqCount; i++)
                        sharedBuffer[sequenceIndicesCount++] = seqBuffer[i];

                    var firstIdx = sharedBuffer[seqStart];
                    var lastIdx = sharedBuffer[seqStart + seqCount - 1];
                    var seqLevel = levels[firstIdx];

                    var sos = ComputeSequenceBoundaryType(
                        start,
                        end,
                        paragraphBaseLevel,
                        firstIdx,
                        true,
                        bidiClasses,
                        levels,
                        isolateToPdi);

                    var eos = ComputeSequenceBoundaryType(
                        start,
                        end,
                        paragraphBaseLevel,
                        lastIdx,
                        false,
                        bidiClasses,
                        levels,
                        isolateToPdi);

                    SequencesBuffer.Add(new IsolatingRunSequence(seqStart, seqCount, seqLevel, sos, eos));
                }

                return SequencesBuffer;
            }
            finally
            {
                UniTextArrayPool<int>.Return(isolateToPdi);
                UniTextArrayPool<int>.Return(pdiToIsolate);
            }
        }

        private void ResolveNeutralTypesWithSequences(PooledList<IsolatingRunSequence> sequences, BidiClass[] bidiClasses)
        {
            for (var i = 0; i < sequences.Count; i++)
            {
                ref var seq = ref sequences[i];
                var indices = GetSequenceIndices(seq);
                var seqLen = indices.Length;
                if (seqLen == 0)
                    continue;

                var k = 0;
                while (k < seqLen)
                {
                    var idx = indices[k];
                    var bc = bidiClasses[idx];

                    if (!IsNeutralType(bc))
                    {
                        k++;
                        continue;
                    }

                    var neutralStartPos = k;
                    var neutralEndPos = k;

                    while (neutralEndPos + 1 < seqLen &&
                           IsNeutralType(bidiClasses[indices[neutralEndPos + 1]]))
                        neutralEndPos++;

                    var sGroup = BidiClass.OtherNeutral;
                    var sGroupFromActualChar = false;

                    for (var pos = neutralStartPos - 1; pos >= 0; pos--)
                    {
                        var j = indices[pos];
                        var t = bidiClasses[j];
                        if (t == BidiClass.BoundaryNeutral) continue;
                        var strong = MapToStrongTypeForNeutrals(t);
                        if (strong != BidiClass.OtherNeutral)
                        {
                            sGroup = strong;
                            sGroupFromActualChar = true;
                            break;
                        }
                    }

                    if (sGroup == BidiClass.OtherNeutral)
                        sGroup = MapToStrongTypeForNeutrals(seq.sos);

                    var eGroup = BidiClass.OtherNeutral;
                    var eGroupFromActualChar = false;

                    for (var pos = neutralEndPos + 1; pos < seqLen; pos++)
                    {
                        var j = indices[pos];
                        var t = bidiClasses[j];
                        if (t == BidiClass.BoundaryNeutral) continue;
                        var strong = MapToStrongTypeForNeutrals(t);
                        if (strong != BidiClass.OtherNeutral)
                        {
                            eGroup = strong;
                            eGroupFromActualChar = true;
                            break;
                        }
                    }

                    if (eGroup == BidiClass.OtherNeutral)
                        eGroup = MapToStrongTypeForNeutrals(seq.eos);

                    BidiClass resolvedType;
                    if (sGroup != BidiClass.OtherNeutral && sGroup == eGroup)
                        resolvedType = sGroup;
                    else
                        resolvedType = (seq.level & 1) == 0 ? BidiClass.LeftToRight : BidiClass.RightToLeft;

                    var skipIsolateControls = !sGroupFromActualChar && !eGroupFromActualChar;

                    for (var pos = neutralStartPos; pos <= neutralEndPos; pos++)
                    {
                        var j = indices[pos];
                        var t = bidiClasses[j];
                        if (t == BidiClass.BoundaryNeutral) continue;
                        if (skipIsolateControls && IsIsolateControl(t)) continue;
                        if (IsNeutralType(t))
                            bidiClasses[j] = resolvedType;
                    }

                    k = neutralEndPos + 1;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIsolateControl(BidiClass bc)
        {
            return bc == BidiClass.LeftToRightIsolate ||
                   bc == BidiClass.RightToLeftIsolate ||
                   bc == BidiClass.FirstStrongIsolate ||
                   bc == BidiClass.PopDirectionalIsolate;
        }

        private void ApplyLineBreakRuleL1ForParagraph(
            ReadOnlySpan<int> codePoints,
            int start,
            int end,
            byte paragraphBaseLevel,
            byte[] levels)
        {
            if (start > end)
                return;

            var lineStart = start;
            var lineEnd = end;

            for (var i = lineStart; i <= lineEnd; i++)
            {
                var cls = unicodeData.GetBidiClass(codePoints[i]);

                if (cls == BidiClass.SegmentSeparator || cls == BidiClass.ParagraphSeparator)
                {
                    levels[i] = paragraphBaseLevel;

                    var j = i - 1;
                    while (j >= lineStart)
                    {
                        var prev = unicodeData.GetBidiClass(codePoints[j]);

                        if (prev == BidiClass.WhiteSpace ||
                            prev == BidiClass.LeftToRightIsolate ||
                            prev == BidiClass.RightToLeftIsolate ||
                            prev == BidiClass.FirstStrongIsolate ||
                            prev == BidiClass.PopDirectionalIsolate ||
                            prev == BidiClass.LeftToRightEmbedding ||
                            prev == BidiClass.RightToLeftEmbedding ||
                            prev == BidiClass.LeftToRightOverride ||
                            prev == BidiClass.RightToLeftOverride ||
                            prev == BidiClass.PopDirectionalFormat ||
                            prev == BidiClass.BoundaryNeutral)
                        {
                            levels[j] = paragraphBaseLevel;
                            j--;
                            continue;
                        }

                        break;
                    }
                }
            }

            var k = lineEnd;
            while (k >= lineStart)
            {
                var cls = unicodeData.GetBidiClass(codePoints[k]);

                if (cls == BidiClass.WhiteSpace ||
                    cls == BidiClass.LeftToRightIsolate ||
                    cls == BidiClass.RightToLeftIsolate ||
                    cls == BidiClass.FirstStrongIsolate ||
                    cls == BidiClass.PopDirectionalIsolate ||
                    cls == BidiClass.LeftToRightEmbedding ||
                    cls == BidiClass.RightToLeftEmbedding ||
                    cls == BidiClass.LeftToRightOverride ||
                    cls == BidiClass.RightToLeftOverride ||
                    cls == BidiClass.PopDirectionalFormat ||
                    cls == BidiClass.BoundaryNeutral)
                {
                    levels[k] = paragraphBaseLevel;
                    k--;
                    continue;
                }

                break;
            }
        }


        private void ResolvePairedBracketsForParagraph(
            ReadOnlySpan<int> codePoints,
            int start,
            int end,
            byte paragraphBaseLevel,
            BidiClass[] bidiClasses,
            byte[] levels)
        {
            if (start > end)
                return;

            var levelRuns = BracketLevelRunsBuffer;
            levelRuns.FakeClear();
            var sequences = BracketSequencesBuffer;
            sequences.FakeClear();

            EnsureMatchingIsolateCapacity(codePoints.Length);
            var matchingIsolate = matchingIsolateBuffer!;
            var runIndexByPosition = runIndexByPositionBuffer!;

            BuildIsolatingRunSequencesForParagraph(
                start,
                end,
                paragraphBaseLevel,
                bidiClasses,
                levels,
                levelRuns,
                sequences,
                matchingIsolate,
                runIndexByPosition);

            for (var s = 0; s < sequences.Count; s++)
                ResolvePairedBracketsForSequence(
                    codePoints,
                    sequences[s],
                    bidiClasses);
        }

        private void ResolvePairedBracketsForSequence(
            ReadOnlySpan<int> codePoints,
            IsolatingRunSequence sequence,
            BidiClass[] bidiClasses)
        {
            const int MaxPairingDepth = 63;

            var openStack = OpenStackBuffer;
            var bracketPairs = BracketPairsBuffer;

            openStack.FakeClear();
            bracketPairs.FakeClear();

            var indices = GetSequenceIndices(sequence);
            var seqLen = indices.Length;

            for (var k = 0; k < seqLen; k++)
            {
                var index = indices[k];

                if (bidiClasses[index] != BidiClass.OtherNeutral)
                    continue;

                var cp = codePoints[index];
                var bt = unicodeData.GetBidiPairedBracketType(cp);

                if (bt == BidiPairedBracketType.Open)
                {
                    if (openStack.Count >= MaxPairingDepth)
                    {
                        bracketPairs.FakeClear();
                        openStack.FakeClear();
                        break;
                    }

                    openStack.Add(index);
                }
                else if (bt == BidiPairedBracketType.Close)
                {
                    for (var s = openStack.Count - 1; s >= 0; s--)
                    {
                        var openIndex = openStack[s];
                        var openCp = codePoints[openIndex];

                        if (BracketsMatch(openCp, cp))
                        {
                            bracketPairs.Add(new BracketPair(openIndex, index));

                            openStack.RemoveRange(s, openStack.Count - s);
                            break;
                        }
                    }
                }
            }

            if (bracketPairs.Count == 0)
                return;

            bracketPairs.Sort(0, bracketPairs.Count, BracketPairComparer.Instance);

            var embeddingDir = GetStrongTypeFromLevel(sequence.level);

            var pairsCount = bracketPairs.Count;
            for (var p = 0; p < pairsCount; p++)
            {
                var openIndex = bracketPairs[p].openIndex;
                var closeIndex = bracketPairs[p].closeIndex;

                var innerMatch = BidiClass.OtherNeutral;
                var innerOpposite = BidiClass.OtherNeutral;

                for (var k = 0; k < seqLen; k++)
                {
                    var idx = indices[k];

                    if (idx <= openIndex || idx >= closeIndex)
                        continue;

                    var strong = MapToStrongTypeForN0(bidiClasses[idx]);

                    if (strong != BidiClass.LeftToRight && strong != BidiClass.RightToLeft)
                        continue;

                    if (strong == embeddingDir)
                    {
                        innerMatch = embeddingDir;
                        break;
                    }

                    innerOpposite = strong;
                }

                if (innerMatch == embeddingDir)
                {
                    bidiClasses[openIndex] = embeddingDir;
                    bidiClasses[closeIndex] = embeddingDir;
                    continue;
                }

                if (innerOpposite == BidiClass.LeftToRight || innerOpposite == BidiClass.RightToLeft)
                {
                    var preceding = BidiClass.OtherNeutral;

                    var openSeqPos = 0;
                    for (; openSeqPos < seqLen; openSeqPos++)
                        if (indices[openSeqPos] == openIndex)
                            break;

                    for (var k = openSeqPos - 1; k >= 0; k--)
                    {
                        var idx = indices[k];

                        if (bidiClasses[idx] == BidiClass.BoundaryNeutral)
                            continue;

                        var strong = MapToStrongTypeForN0(bidiClasses[idx]);
                        if (strong == BidiClass.LeftToRight || strong == BidiClass.RightToLeft)
                        {
                            preceding = strong;
                            break;
                        }
                    }

                    if (preceding != BidiClass.LeftToRight && preceding != BidiClass.RightToLeft) preceding = sequence.sos;

                    bidiClasses[openIndex] = preceding;
                    bidiClasses[closeIndex] = preceding;
                    continue;
                }
            }

            for (var p = 0; p < pairsCount; p++)
            {
                var openIndex = bracketPairs[p].openIndex;
                var closeIndex = bracketPairs[p].closeIndex;

                var pairType = bidiClasses[openIndex];
                if (pairType != BidiClass.LeftToRight && pairType != BidiClass.RightToLeft)
                    continue;

                var openSeqPos = 0;
                for (; openSeqPos < seqLen; openSeqPos++)
                    if (indices[openSeqPos] == openIndex)
                        break;

                var closeSeqPos = 0;
                for (; closeSeqPos < seqLen; closeSeqPos++)
                    if (indices[closeSeqPos] == closeIndex)
                        break;

                var kPos = openSeqPos + 1;
                while (kPos < seqLen)
                {
                    var idx = indices[kPos];
                    var original = unicodeData.GetBidiClass(codePoints[idx]);

                    if (original == BidiClass.BoundaryNeutral)
                    {
                        kPos++;
                        continue;
                    }

                    if (original != BidiClass.NonspacingMark)
                        break;

                    bidiClasses[idx] = pairType;
                    kPos++;
                }

                kPos = closeSeqPos + 1;
                while (kPos < seqLen)
                {
                    var idx = indices[kPos];
                    var original = unicodeData.GetBidiClass(codePoints[idx]);

                    if (original == BidiClass.BoundaryNeutral)
                    {
                        kPos++;
                        continue;
                    }

                    if (original != BidiClass.NonspacingMark)
                        break;

                    bidiClasses[idx] = pairType;
                    kPos++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool BracketsMatch(int openCp, int closeCp)
        {
            var openType = unicodeData.GetBidiPairedBracketType(openCp);
            if (openType != BidiPairedBracketType.None)
            {
                var paired = unicodeData.GetBidiPairedBracket(openCp);
                if (paired == closeCp)
                    return true;

                if (openCp == UnicodeData.LeftPointingAngleBracket && (closeCp == UnicodeData.RightPointingAngleBracket ||
                                                                       closeCp == UnicodeData.RightAngleBracket))
                    return true;
                if (openCp == UnicodeData.LeftAngleBracket && (closeCp == UnicodeData.RightPointingAngleBracket ||
                                                               closeCp == UnicodeData.RightAngleBracket))
                    return true;

                return false;
            }

            return openCp == UnicodeData.LeftParenthesis && closeCp == UnicodeData.RightParenthesis;
        }

        private void ResolveWeakTypesWithSequences(PooledList<IsolatingRunSequence> sequences, BidiClass[] bidiClasses)
        {
            for (var i = 0; i < sequences.Count; i++)
            {
                ref var seq = ref sequences[i];
                ResolveWeakTypesForSequence(seq, bidiClasses);
            }
        }

        private void ResolveWeakTypesForSequence(
            in IsolatingRunSequence seq,
            BidiClass[] bidiClasses)
        {
            var indices = GetSequenceIndices(seq);
            var length = indices.Length;

            if (length == 0)
                return;

            {
                var prevType = seq.sos;

                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    var t = bidiClasses[idx];

                    if (t == BidiClass.BoundaryNeutral)
                        continue;

                    if (t == BidiClass.NonspacingMark)
                    {
                        if (IsIsolateInitiator(prevType) ||
                            prevType == BidiClass.PopDirectionalIsolate)
                            bidiClasses[idx] = BidiClass.OtherNeutral;
                        else
                            bidiClasses[idx] = prevType;

                        t = bidiClasses[idx];
                    }

                    prevType = t;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    if (bidiClasses[idx] != BidiClass.EuropeanNumber)
                        continue;

                    var strong = FindPrevStrongTypeForW2(seq, bidiClasses, indices, i);

                    if (strong == BidiClass.ArabicLetter) bidiClasses[idx] = BidiClass.ArabicNumber;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    if (bidiClasses[idx] == BidiClass.ArabicLetter) bidiClasses[idx] = BidiClass.RightToLeft;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    var t = bidiClasses[idx];

                    if (t != BidiClass.EuropeanSeparator &&
                        t != BidiClass.CommonSeparator)
                        continue;

                    var before = GetTypeBeforeInSequence(seq, bidiClasses, indices, i);
                    var after = GetTypeAfterInSequence(seq, bidiClasses, indices, i);

                    if (t == BidiClass.EuropeanSeparator)
                    {
                        if (before == BidiClass.EuropeanNumber &&
                            after == BidiClass.EuropeanNumber)
                            bidiClasses[idx] = BidiClass.EuropeanNumber;
                    }
                    else
                    {
                        if (before == BidiClass.EuropeanNumber &&
                            after == BidiClass.EuropeanNumber)
                            bidiClasses[idx] = BidiClass.EuropeanNumber;
                        else if (before == BidiClass.ArabicNumber &&
                                 after == BidiClass.ArabicNumber)
                            bidiClasses[idx] = BidiClass.ArabicNumber;
                    }
                }
            }

            {
                var i = 0;
                while (i < length)
                {
                    var idx = indices[i];
                    var bc = bidiClasses[idx];

                    if (bc != BidiClass.EuropeanTerminator)
                    {
                        i++;
                        continue;
                    }

                    var runStart = i;
                    var runEnd = i;

                    while (runEnd + 1 < length)
                    {
                        var nextBc = bidiClasses[indices[runEnd + 1]];
                        if (nextBc == BidiClass.EuropeanTerminator || nextBc == BidiClass.BoundaryNeutral)
                            runEnd++;
                        else
                            break;
                    }

                    var before = GetTypeBeforeInSequence(seq, bidiClasses, indices, runStart);
                    var after = GetTypeAfterInSequence(seq, bidiClasses, indices, runEnd);

                    var beforeIsEn = before == BidiClass.EuropeanNumber;
                    var afterIsEn = after == BidiClass.EuropeanNumber;

                    if (beforeIsEn || afterIsEn)
                    {
                        for (var p = runStart; p <= runEnd; p++)
                        {
                            if (bidiClasses[indices[p]] == BidiClass.EuropeanTerminator)
                                bidiClasses[indices[p]] = BidiClass.EuropeanNumber;
                        }
                    }

                    i = runEnd + 1;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    var t = bidiClasses[idx];

                    if (t == BidiClass.EuropeanSeparator ||
                        t == BidiClass.CommonSeparator ||
                        t == BidiClass.EuropeanTerminator)
                        bidiClasses[idx] = BidiClass.OtherNeutral;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    if (bidiClasses[idx] != BidiClass.EuropeanNumber)
                        continue;

                    var strong = FindPrevStrongTypeForW7(seq, bidiClasses, indices, i);

                    if (strong == BidiClass.LeftToRight) bidiClasses[idx] = BidiClass.LeftToRight;
                }
            }
        }


        private static BidiClass GetTypeBeforeInSequence(
            IsolatingRunSequence seq,
            BidiClass[] classes,
            ReadOnlySpan<int> indices,
            int position)
        {
            for (var i = position - 1; i >= 0; i--)
            {
                var t = classes[indices[i]];
                if (t == BidiClass.BoundaryNeutral)
                    continue;

                return t;
            }

            return seq.sos;
        }

        private static BidiClass GetTypeAfterInSequence(
            IsolatingRunSequence seq,
            BidiClass[] classes,
            ReadOnlySpan<int> indices,
            int position)
        {
            for (var i = position + 1; i < indices.Length; i++)
            {
                var t = classes[indices[i]];
                if (t == BidiClass.BoundaryNeutral)
                    continue;

                return t;
            }

            return seq.eos;
        }

        private static BidiClass FindPrevStrongTypeForW2(
            IsolatingRunSequence seq,
            BidiClass[] classes,
            ReadOnlySpan<int> indices,
            int position)
        {
            for (var i = position - 1; i >= 0; i--)
            {
                var t = classes[indices[i]];

                if (t == BidiClass.BoundaryNeutral)
                    continue;

                if (t == BidiClass.LeftToRight ||
                    t == BidiClass.RightToLeft ||
                    t == BidiClass.ArabicLetter)
                    return t;
            }

            return seq.sos;
        }

        private static BidiClass FindPrevStrongTypeForW7(
            IsolatingRunSequence seq,
            BidiClass[] classes,
            ReadOnlySpan<int> indices,
            int position)
        {
            for (var i = position - 1; i >= 0; i--)
            {
                var t = classes[indices[i]];

                if (IsNeutralType(t))
                    continue;

                if (t == BidiClass.LeftToRight ||
                    t == BidiClass.RightToLeft)
                    return t;
            }

            return seq.sos;
        }


        private void ResolveImplicitLevelsForParagraph(
            int start,
            int end,
            BidiClass[] bidiClasses,
            byte[] levels)
        {
            var i = start;

            while (i <= end)
            {
                var runLevel = levels[i];
                var runStart = i;
                var runEnd = i;

                while (runEnd + 1 <= end && levels[runEnd + 1] == runLevel) runEnd++;

                var isEvenLevel = (runLevel & 1) == 0;

                if (isEvenLevel)
                    for (var j = runStart; j <= runEnd; j++)
                        switch (bidiClasses[j])
                        {
                            case BidiClass.RightToLeft:
                            case BidiClass.ArabicLetter:
                                levels[j] = (byte)(runLevel + 1);
                                break;

                            case BidiClass.EuropeanNumber:
                            case BidiClass.ArabicNumber:
                                levels[j] = (byte)(runLevel + 2);
                                break;
                        }
                else
                    for (var j = runStart; j <= runEnd; j++)
                        switch (bidiClasses[j])
                        {
                            case BidiClass.LeftToRight:
                            case BidiClass.EuropeanNumber:
                            case BidiClass.ArabicNumber:
                                levels[j] = (byte)(runLevel + 1);
                                break;
                        }

                i = runEnd + 1;
            }
        }


        private void ResolveExplicitLevelsForParagraph(
            int start,
            int end,
            byte baseLevel,
            BidiClass[] bidiClasses,
            byte[] levels)
        {
            var stackDepth = 1;
            EmbeddingStack[0].level = baseLevel;
            EmbeddingStack[0].overrideStatus = 0;
            EmbeddingStack[0].isIsolate = false;

            var currentLevel = baseLevel;
            sbyte overrideStatus = 0;
            var overflowIsolateCount = 0;
            var overflowEmbeddingCount = 0;

            for (var i = start; i <= end; i++)
            {
                var bc = bidiClasses[i];

                levels[i] = currentLevel;

                switch (bc)
                {
                    case BidiClass.LeftToRightEmbedding:
                        PushEmbedding(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, false, 0);
                        bidiClasses[i] = BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.RightToLeftEmbedding:
                        PushEmbedding(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, true, 0);
                        bidiClasses[i] = BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.LeftToRightOverride:
                        PushEmbedding(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, false, 1);
                        bidiClasses[i] = BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.RightToLeftOverride:
                        PushEmbedding(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, true, 2);
                        bidiClasses[i] = BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.PopDirectionalFormat:
                        PopEmbedding(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount);
                        levels[i] = currentLevel;
                        bidiClasses[i] = BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.LeftToRightIsolate:
                        PushIsolate(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, false);
                        break;

                    case BidiClass.RightToLeftIsolate:
                        PushIsolate(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, true);
                        break;

                    case BidiClass.FirstStrongIsolate:
                    {
                        var isRtl = ResolveFirstStrongIsolateDirection(i, end, bidiClasses, baseLevel);
                        PushIsolate(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, isRtl);
                        break;
                    }

                    case BidiClass.PopDirectionalIsolate:
                        PopIsolate(ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount);
                        levels[i] = currentLevel;
                        break;

                    default:
                        if (overrideStatus == 1)
                        {
                            if (bc != BidiClass.BoundaryNeutral &&
                                bc != BidiClass.ParagraphSeparator &&
                                bc != BidiClass.SegmentSeparator)
                                bidiClasses[i] = BidiClass.LeftToRight;
                        }
                        else if (overrideStatus == 2)
                        {
                            if (bc != BidiClass.BoundaryNeutral &&
                                bc != BidiClass.ParagraphSeparator &&
                                bc != BidiClass.SegmentSeparator)
                                bidiClasses[i] = BidiClass.RightToLeft;
                        }

                        break;
                }
            }
        }

        private void PushEmbedding(
            ref int stackDepth,
            ref byte currentLevel,
            ref sbyte overrideStatus,
            ref int overflowIsolateCount,
            ref int overflowEmbeddingCount,
            bool isRtl,
            sbyte overrideClass)
        {
            if (overflowIsolateCount > 0 || overflowEmbeddingCount > 0)
            {
                if (overflowIsolateCount == 0)
                    overflowEmbeddingCount++;
                return;
            }

            byte newLevel;
            if (isRtl)
                newLevel = (byte)((currentLevel + 1) | 1);
            else
                newLevel = (byte)((currentLevel + 2) & ~1);

            if (newLevel > MaxExplicitLevel || stackDepth >= EmbeddingStack.Length)
            {
                overflowEmbeddingCount++;
                return;
            }

            EmbeddingStack[stackDepth].level = newLevel;
            EmbeddingStack[stackDepth].overrideStatus = overrideClass;
            EmbeddingStack[stackDepth].isIsolate = false;
            stackDepth++;

            currentLevel = newLevel;
            overrideStatus = overrideClass;
        }

        private void PopEmbedding(
            ref int stackDepth,
            ref byte currentLevel,
            ref sbyte overrideStatus,
            ref int overflowIsolateCount,
            ref int overflowEmbeddingCount)
        {
            if (overflowIsolateCount > 0)
                return;

            if (overflowEmbeddingCount > 0)
            {
                overflowEmbeddingCount--;
                return;
            }

            if (stackDepth <= 1)
                return;

            var topIndex = stackDepth - 1;
            if (EmbeddingStack[topIndex].isIsolate)
                return;

            stackDepth--;
            var state = EmbeddingStack[stackDepth - 1];
            currentLevel = state.level;
            overrideStatus = state.overrideStatus;
        }

        private void PushIsolate(
            ref int stackDepth,
            ref byte currentLevel,
            ref sbyte overrideStatus,
            ref int overflowIsolateCount,
            ref int overflowEmbeddingCount,
            bool isRtl)
        {
            if (overflowIsolateCount > 0)
            {
                overflowIsolateCount++;
                return;
            }

            byte newLevel;
            if (isRtl)
                newLevel = (byte)((currentLevel + 1) | 1);
            else
                newLevel = (byte)((currentLevel + 2) & ~1);

            if (newLevel > MaxExplicitLevel || stackDepth >= EmbeddingStack.Length)
            {
                overflowIsolateCount++;
                return;
            }

            EmbeddingStack[stackDepth].level = newLevel;
            EmbeddingStack[stackDepth].overrideStatus = 0;
            EmbeddingStack[stackDepth].isIsolate = true;
            stackDepth++;

            currentLevel = newLevel;
            overrideStatus = 0;
        }

        private void PopIsolate(
            ref int stackDepth,
            ref byte currentLevel,
            ref sbyte overrideStatus,
            ref int overflowIsolateCount,
            ref int overflowEmbeddingCount)
        {
            if (overflowIsolateCount > 0)
            {
                overflowIsolateCount--;
                return;
            }

            if (stackDepth <= 1) return;

            var isolateIndex = -1;
            for (var i = stackDepth - 1; i >= 1; i--)
                if (EmbeddingStack[i].isIsolate)
                {
                    isolateIndex = i;
                    break;
                }

            if (isolateIndex == -1) return;

            stackDepth = isolateIndex;
            overflowEmbeddingCount = 0;
            var state = EmbeddingStack[stackDepth - 1];
            currentLevel = state.level;
            overrideStatus = state.overrideStatus;
        }

        private static BidiClass ComputeSequenceBoundaryType(
            int start,
            int end,
            byte paragraphBaseLevel,
            int boundaryIndex,
            bool isStart,
            BidiClass[] bidiClasses,
            byte[] levels,
            int[] isolateToPdiRelative)
        {
            var levelHere = levels[boundaryIndex];
            byte otherLevel;

            if (isStart)
            {
                var i = boundaryIndex - 1;
                while (i >= start)
                {
                    if (bidiClasses[i] != BidiClass.BoundaryNeutral)
                    {
                        otherLevel = levels[i];
                        goto Compute;
                    }

                    i--;
                }

                otherLevel = paragraphBaseLevel;
            }
            else
            {
                var unmatchedIsolate =
                    IsIsolateInitiator(bidiClasses[boundaryIndex]) &&
                    isolateToPdiRelative[boundaryIndex - start] < 0;

                if (unmatchedIsolate)
                {
                    otherLevel = paragraphBaseLevel;
                }
                else
                {
                    var i = boundaryIndex + 1;
                    while (i <= end)
                    {
                        var bc = bidiClasses[i];
                        if (bc != BidiClass.BoundaryNeutral)
                        {
                            if (bc == BidiClass.ParagraphSeparator)
                                otherLevel = paragraphBaseLevel;
                            else
                                otherLevel = levels[i];
                            goto Compute;
                        }

                        i++;
                    }

                    otherLevel = paragraphBaseLevel;
                }
            }

            Compute:
            var higher = levelHere >= otherLevel ? levelHere : otherLevel;
            return (higher & 1) == 0
                ? BidiClass.LeftToRight
                : BidiClass.RightToLeft;
        }

        private PooledList<IsolatingRunSequence> BuildIsolatingRunSequencesForParagraph(
            int start,
            int end,
            byte paragraphBaseLevel,
            BidiClass[] bidiClasses,
            byte[] levels,
            PooledList<LevelRun> levelRuns,
            PooledList<IsolatingRunSequence> sequences,
            int[] matchingIsolate,
            int[] runIndexByPosition)
        {
            UniTextDebug.Increment(ref UniTextDebug.Bidi_BuildIsoRunSeqForParagraphCount);
            sequences.FakeClear();
            ResetSequenceIndices();

            if (start > end)
                return sequences;

            ComputeIsolatePairs(start, end, bidiClasses, matchingIsolate);

            BuildLevelRunsForParagraph(start, end, levels, bidiClasses, levelRuns);
            var runCount = levelRuns.Count;

            if (runCount == 0)
                return sequences;

            for (var i = start; i <= end; i++)
                runIndexByPosition[i] = -1;

            for (var r = 0; r < runCount; r++)
            {
                var run = levelRuns[r];
                for (var i = run.startIndex; i <= run.endIndex; i++)
                    runIndexByPosition[i] = r;
            }

            EnsureRunBuffersCapacity(runCount);
            var nextRun = nextRunBuffer!;
            var hasPredecessor = hasPredecessorBuffer!;
            var visited = visitedBuffer!;

            for (var r = 0; r < runCount; r++)
            {
                nextRun[r] = -1;
                hasPredecessor[r] = false;
                visited[r] = false;
            }

            for (var r = 0; r < runCount; r++)
            {
                var run = levelRuns[r];
                var lastIndex = run.endIndex;
                var lastType = bidiClasses[lastIndex];

                if (lastType == BidiClass.LeftToRightIsolate ||
                    lastType == BidiClass.RightToLeftIsolate ||
                    lastType == BidiClass.FirstStrongIsolate)
                {
                    var mate = matchingIsolate[lastIndex];
                    if (mate >= 0)
                    {
                        var mateRun = runIndexByPosition[mate];
                        if (mateRun >= 0)
                            nextRun[r] = mateRun;
                    }
                }
            }

            for (var r = 0; r < runCount; r++)
            {
                var succ = nextRun[r];
                if (succ >= 0 && succ < runCount)
                    hasPredecessor[succ] = true;
            }

            var seqBuffer = SeqIndicesBuffer;

            void AddSequenceFromRun(int startRunIndex)
            {
                var currentRun = startRunIndex;
                seqBuffer.FakeClear();
                var firstRun = levelRuns[currentRun];
                var sequenceLevel = firstRun.level;

                while (true)
                {
                    visited[currentRun] = true;
                    var run = levelRuns[currentRun];
                    var runLen = run.endIndex - run.startIndex + 1;
                    seqBuffer.EnsureCapacity(seqBuffer.buffer.count + runLen);
                    var seqArr = seqBuffer.buffer.data;
                    var seqIdx = seqBuffer.buffer.count;
                    for (var i = run.startIndex; i <= run.endIndex; i++)
                        seqArr[seqIdx++] = i;
                    seqBuffer.buffer.count = seqIdx;

                    var succ = nextRun[currentRun];
                    if (succ < 0 || visited[succ])
                        break;
                    currentRun = succ;
                }

                var indicesCount = seqBuffer.Count;
                if (indicesCount == 0)
                    return;

                var sequenceFirstIndex = seqBuffer[0];
                var sequenceLastIndex = seqBuffer[indicesCount - 1];

                ComputeSosEosForSequence(
                    start,
                    end,
                    sequenceFirstIndex,
                    sequenceLastIndex,
                    paragraphBaseLevel,
                    sequenceLevel,
                    bidiClasses,
                    levels,
                    matchingIsolate,
                    out var sos,
                    out var eos);

                EnsureSequenceIndicesCapacity(indicesCount);
                var seqStart = sequenceIndicesCount;
                var sharedBuffer = SequenceIndicesBuffer;
                for (var i = 0; i < indicesCount; i++)
                    sharedBuffer[sequenceIndicesCount++] = seqBuffer[i];

                sequences.Add(new IsolatingRunSequence(seqStart, indicesCount, sequenceLevel, sos, eos));
            }

            for (var r = 0; r < runCount; r++)
                if (!hasPredecessor[r] && !visited[r])
                    AddSequenceFromRun(r);

            for (var r = 0; r < runCount; r++)
                if (!visited[r])
                    AddSequenceFromRun(r);

            return sequences;
        }

        private static void BuildParagraphsWithExplicitBaseLevel(
            BidiClass[] bidiClasses,
            int length,
            byte givenBaseLevel,
            PooledList<BidiParagraph> paragraphs)
        {
            var paraStart = 0;
            for (var i = 0; i < length; i++)
                if (bidiClasses[i] == BidiClass.ParagraphSeparator)
                {
                    paragraphs.Add(new BidiParagraph(paraStart, i, givenBaseLevel));
                    paraStart = i + 1;
                }

            if (paraStart < length)
                paragraphs.Add(new BidiParagraph(paraStart, length - 1, givenBaseLevel));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIsolateInitiator(BidiClass bc)
        {
            return bc == BidiClass.LeftToRightIsolate ||
                   bc == BidiClass.RightToLeftIsolate ||
                   bc == BidiClass.FirstStrongIsolate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindLastNonBnBefore(int beforeIndex, int start, BidiClass[] bidiClasses)
        {
            for (var i = beforeIndex - 1; i >= start; i--)
                if (bidiClasses[i] != BidiClass.BoundaryNeutral)
                    return i;

            return start;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNeutralType(BidiClass bc)
        {
            switch (bc)
            {
                case BidiClass.WhiteSpace:
                case BidiClass.SegmentSeparator:
                case BidiClass.OtherNeutral:
                case BidiClass.BoundaryNeutral:
                case BidiClass.LeftToRightIsolate:
                case BidiClass.RightToLeftIsolate:
                case BidiClass.FirstStrongIsolate:
                case BidiClass.PopDirectionalIsolate:
                    return true;

                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiClass MapToStrongTypeForNeutrals(BidiClass bc)
        {
            switch (bc)
            {
                case BidiClass.LeftToRight:
                    return BidiClass.LeftToRight;

                case BidiClass.RightToLeft:
                    return BidiClass.RightToLeft;

                case BidiClass.EuropeanNumber:
                case BidiClass.ArabicNumber:
                    return BidiClass.RightToLeft;

                default:
                    return BidiClass.OtherNeutral;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiClass MapToStrongTypeForN0(BidiClass t)
        {
            switch (t)
            {
                case BidiClass.LeftToRight:
                    return BidiClass.LeftToRight;

                case BidiClass.RightToLeft:
                case BidiClass.ArabicLetter:
                case BidiClass.EuropeanNumber:
                case BidiClass.ArabicNumber:
                    return BidiClass.RightToLeft;

                default:
                    return BidiClass.OtherNeutral;
            }
        }

        private static bool ResolveFirstStrongIsolateDirection(
            int fsiIndex,
            int paragraphEnd,
            BidiClass[] bidiClasses,
            byte paragraphBaseLevel)
        {
            var depth = 1;

            for (var i = fsiIndex + 1; i <= paragraphEnd; i++)
            {
                var bc = bidiClasses[i];

                switch (bc)
                {
                    case BidiClass.LeftToRightIsolate:
                    case BidiClass.RightToLeftIsolate:
                    case BidiClass.FirstStrongIsolate:
                        depth++;
                        break;

                    case BidiClass.PopDirectionalIsolate:
                        depth--;
                        if (depth == 0) goto EndScan;

                        break;
                }

                if (depth < 1)
                    break;

                if (depth == 1)
                {
                    if (bc == BidiClass.LeftToRight)
                        return false;

                    if (bc == BidiClass.RightToLeft || bc == BidiClass.ArabicLetter)
                        return true;
                }
            }

            EndScan:
            return false;
        }

        private static void BuildParagraphs(BidiClass[] bidiClasses, int length, PooledList<BidiParagraph> paragraphs)
        {
            var paraStart = 0;
            for (var i = 0; i < length; i++)
                if (bidiClasses[i] == BidiClass.ParagraphSeparator)
                {
                    var baseLevel = ComputeParagraphBaseLevel(bidiClasses, paraStart, i - 1);
                    paragraphs.Add(new BidiParagraph(paraStart, i, baseLevel));
                    paraStart = i + 1;
                }

            if (paraStart < length)
            {
                var baseLevel = ComputeParagraphBaseLevel(bidiClasses, paraStart, length - 1);
                paragraphs.Add(new BidiParagraph(paraStart, length - 1, baseLevel));
            }
        }

        /// <summary>
        /// Computes paragraph base level per UAX#9 rules P2 and P3.
        /// P2: Find the first character of type L, R, or AL that is NOT inside an isolate.
        /// P3: If no such character found, return 0 (LTR).
        /// </summary>
        private static byte ComputeParagraphBaseLevel(BidiClass[] bidiClasses, int start, int end)
        {
            var isolateDepth = 0;

            for (var i = start; i <= end; i++)
            {
                var bc = bidiClasses[i];

                if (bc == BidiClass.LeftToRightIsolate ||
                    bc == BidiClass.RightToLeftIsolate ||
                    bc == BidiClass.FirstStrongIsolate)
                {
                    isolateDepth++;
                    continue;
                }

                if (bc == BidiClass.PopDirectionalIsolate)
                {
                    if (isolateDepth > 0)
                        isolateDepth--;
                    continue;
                }

                if (isolateDepth > 0)
                    continue;

                switch (bc)
                {
                    case BidiClass.LeftToRight:
                        return 0;

                    case BidiClass.RightToLeft:
                    case BidiClass.ArabicLetter:
                        return 1;
                }
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiClass GetStrongTypeFromLevel(byte level)
        {
            return (level & 1) == 0 ? BidiClass.LeftToRight : BidiClass.RightToLeft;
        }

        private void ComputeIsolatePairs(
            int start,
            int end,
            BidiClass[] bidiClasses,
            int[] matchingIsolate)
        {
            for (var i = start; i <= end; i++)
                matchingIsolate[i] = -1;

            var isolatePairStack = IsolatePairStackBuffer;
            isolatePairStack.FakeClear();

            for (var i = start; i <= end; i++)
            {
                var bc = bidiClasses[i];

                if (bc == BidiClass.LeftToRightIsolate ||
                    bc == BidiClass.RightToLeftIsolate ||
                    bc == BidiClass.FirstStrongIsolate)
                {
                    isolatePairStack.Add(i);
                }
                else if (bc == BidiClass.PopDirectionalIsolate)
                {
                    var count = isolatePairStack.Count;
                    if (count == 0)
                        continue;

                    var openIndex = isolatePairStack[count - 1];
                    isolatePairStack.RemoveAt(count - 1);

                    matchingIsolate[openIndex] = i;
                    matchingIsolate[i] = openIndex;
                }
            }
        }

        private static void BuildLevelRunsForParagraph(
            int start,
            int end,
            byte[] levels,
            BidiClass[] bidiClasses,
            PooledList<LevelRun> levelRuns)
        {
            levelRuns.FakeClear();

            if (start > end)
                return;

            var runStart = -1;
            byte currentLevel = 0;

            for (var i = start; i <= end; i++)
            {
                if (bidiClasses[i] == BidiClass.BoundaryNeutral)
                    continue;

                var level = levels[i];

                if (runStart == -1)
                {
                    runStart = i;
                    currentLevel = level;
                }
                else if (level != currentLevel)
                {
                    levelRuns.Add(new LevelRun(runStart, FindLastNonBnBefore(i, start, bidiClasses), currentLevel));
                    runStart = i;
                    currentLevel = level;
                }
            }

            if (runStart != -1)
                levelRuns.Add(new LevelRun(runStart, FindLastNonBnBefore(end + 1, start, bidiClasses), currentLevel));
        }

        private static void ComputeSosEosForSequence(
            int paragraphStart,
            int paragraphEnd,
            int sequenceFirstIndex,
            int sequenceLastIndex,
            byte paragraphBaseLevel,
            byte sequenceLevel,
            BidiClass[] bidiClasses,
            byte[] levels,
            int[] matchingIsolate,
            out BidiClass sos,
            out BidiClass eos)
        {
            byte leftLevel;

            var prevIndex = sequenceFirstIndex - 1;
            while (prevIndex >= paragraphStart && bidiClasses[prevIndex] == BidiClass.BoundaryNeutral)
                prevIndex--;

            if (prevIndex >= paragraphStart)
                leftLevel = levels[prevIndex];
            else
                leftLevel = paragraphBaseLevel;

            var maxLeft = leftLevel >= sequenceLevel ? leftLevel : sequenceLevel;
            sos = GetStrongTypeFromLevel(maxLeft);

            var lastNonBn = sequenceLastIndex;
            while (lastNonBn >= sequenceFirstIndex && bidiClasses[lastNonBn] == BidiClass.BoundaryNeutral)
                lastNonBn--;

            var lastIsIsolateInitiatorWithoutMatch = false;
            if (lastNonBn >= sequenceFirstIndex)
            {
                var lastType = bidiClasses[lastNonBn];
                if (lastType == BidiClass.LeftToRightIsolate ||
                    lastType == BidiClass.RightToLeftIsolate ||
                    lastType == BidiClass.FirstStrongIsolate)
                {
                    var mate = matchingIsolate[lastNonBn];
                    if (mate < 0)
                        lastIsIsolateInitiatorWithoutMatch = true;
                }
            }

            byte rightLevel;
            var nextIndex = sequenceLastIndex + 1;

            while (nextIndex <= paragraphEnd && bidiClasses[nextIndex] == BidiClass.BoundaryNeutral)
                nextIndex++;

            if (nextIndex <= paragraphEnd && !lastIsIsolateInitiatorWithoutMatch)
            {
                var nextClass = bidiClasses[nextIndex];
                if (nextClass == BidiClass.ParagraphSeparator)
                    rightLevel = paragraphBaseLevel;
                else
                    rightLevel = levels[nextIndex];
            }
            else
                rightLevel = paragraphBaseLevel;

            var maxRight = rightLevel >= sequenceLevel ? rightLevel : sequenceLevel;
            eos = GetStrongTypeFromLevel(maxRight);
        }
    }
}
