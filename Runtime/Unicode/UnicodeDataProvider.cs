using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Internal interface for codepoint range entries used in binary search.
    /// </summary>
    internal interface IRangeEntry
    {
        int StartCodePoint { get; }
        int EndCodePoint { get; }
    }


    /// <summary>
    /// Internal interface for single codepoint entries used in binary search.
    /// </summary>
    internal interface IPointEntry
    {
        int CodePoint { get; }
    }


    /// <summary>
    /// Entry storing BiDi and joining properties for a codepoint range.
    /// </summary>
    internal readonly struct RangeEntry : IRangeEntry
    {
        /// <summary>First codepoint in the range.</summary>
        public readonly int startCodePoint;
        /// <summary>Last codepoint in the range (inclusive).</summary>
        public readonly int endCodePoint;
        /// <summary>BiDi class for this range.</summary>
        public readonly BidiClass bidiClass;
        /// <summary>Arabic joining type for this range.</summary>
        public readonly JoiningType joiningType;
        /// <summary>Arabic joining group for this range.</summary>
        public readonly JoiningGroup joiningGroup;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public RangeEntry(int startCodePoint, int endCodePoint, BidiClass bidiClass, JoiningType joiningType, JoiningGroup joiningGroup)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.bidiClass = bidiClass;
            this.joiningType = joiningType;
            this.joiningGroup = joiningGroup;
        }
    }


    /// <summary>
    /// Entry mapping a codepoint to its BiDi mirror glyph (e.g., '(' to ')').
    /// </summary>
    internal readonly struct MirrorEntry : IPointEntry
    {
        /// <summary>Source codepoint.</summary>
        public readonly int codePoint;
        /// <summary>Mirrored codepoint for RTL rendering.</summary>
        public readonly int mirroredCodePoint;

        int IPointEntry.CodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => codePoint; }

        public MirrorEntry(int codePoint, int mirroredCodePoint)
        {
            this.codePoint = codePoint;
            this.mirroredCodePoint = mirroredCodePoint;
        }
    }


    /// <summary>
    /// Entry storing paired bracket information for BiDi bracket matching (UAX #9 N0).
    /// </summary>
    internal readonly struct BracketEntry : IPointEntry
    {
        /// <summary>Bracket codepoint.</summary>
        public readonly int codePoint;
        /// <summary>Matching bracket codepoint.</summary>
        public readonly int pairedCodePoint;
        /// <summary>Whether this is an opening or closing bracket.</summary>
        public readonly BidiPairedBracketType bracketType;

        int IPointEntry.CodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => codePoint; }

        public BracketEntry(int codePoint, int pairedCodePoint, BidiPairedBracketType bracketType)
        {
            this.codePoint = codePoint;
            this.pairedCodePoint = pairedCodePoint;
            this.bracketType = bracketType;
        }
    }


    /// <summary>Entry storing script property for a codepoint range (UAX #24).</summary>
    internal readonly struct ScriptRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly UnicodeScript script;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public ScriptRangeEntry(int startCodePoint, int endCodePoint, UnicodeScript script)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.script = script;
        }
    }


    /// <summary>Entry storing line break class for a codepoint range (UAX #14).</summary>
    internal readonly struct LineBreakRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly LineBreakClass lineBreakClass;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public LineBreakRangeEntry(int startCodePoint, int endCodePoint, LineBreakClass lineBreakClass)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.lineBreakClass = lineBreakClass;
        }
    }


    /// <summary>Entry marking a codepoint range as extended pictographic (emoji-related).</summary>
    internal readonly struct ExtendedPictographicRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public ExtendedPictographicRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>Entry storing general category for a codepoint range.</summary>
    internal readonly struct GeneralCategoryRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly GeneralCategory generalCategory;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public GeneralCategoryRangeEntry(int startCodePoint, int endCodePoint, GeneralCategory generalCategory)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.generalCategory = generalCategory;
        }
    }


    /// <summary>Entry storing East Asian width for a codepoint range (UAX #11).</summary>
    internal readonly struct EastAsianWidthRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly EastAsianWidth eastAsianWidth;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public EastAsianWidthRangeEntry(int startCodePoint, int endCodePoint, EastAsianWidth eastAsianWidth)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.eastAsianWidth = eastAsianWidth;
        }
    }


    /// <summary>Entry storing grapheme cluster break for a codepoint range (UAX #29).</summary>
    internal readonly struct GraphemeBreakRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly GraphemeClusterBreak graphemeBreak;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public GraphemeBreakRangeEntry(int startCodePoint, int endCodePoint, GraphemeClusterBreak graphemeBreak)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.graphemeBreak = graphemeBreak;
        }
    }


    /// <summary>Entry storing Indic conjunct break for a codepoint range (UAX #29).</summary>
    internal readonly struct IndicConjunctBreakRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly IndicConjunctBreak indicConjunctBreak;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public IndicConjunctBreakRangeEntry(int startCodePoint, int endCodePoint, IndicConjunctBreak indicConjunctBreak)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.indicConjunctBreak = indicConjunctBreak;
        }
    }


    /// <summary>Entry marking a codepoint range as default ignorable (ZWJ, ZWNJ, etc.).</summary>
    internal readonly struct DefaultIgnorableRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public DefaultIgnorableRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>Entry marking a codepoint range as having emoji presentation by default.</summary>
    internal readonly struct EmojiPresentationRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public EmojiPresentationRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>Entry marking a codepoint range as emoji modifier base (can have skin tone).</summary>
    internal readonly struct EmojiModifierBaseRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public EmojiModifierBaseRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>Entry storing script extensions for a codepoint range (multiple scripts).</summary>
    internal readonly struct ScriptExtensionRangeEntry : IRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        /// <summary>Array of scripts this codepoint can appear in.</summary>
        public readonly UnicodeScript[] scripts;

        int IRangeEntry.StartCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => startCodePoint; }
        int IRangeEntry.EndCodePoint { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => endCodePoint; }

        public ScriptExtensionRangeEntry(int startCodePoint, int endCodePoint, UnicodeScript[] scripts)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.scripts = scripts;
        }
    }


    /// <summary>
    /// Provides Unicode character properties from a precompiled binary data file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides Unicode character property lookups from a compiled binary data blob.
    /// Loads compressed Unicode data from a binary blob for efficient lookup.
    /// </para>
    /// <para>
    /// Uses BMP (Basic Multilingual Plane) lookup tables for fast O(1) access to
    /// common codepoints (0-65535), with binary search for supplementary planes.
    /// </para>
    /// </remarks>
    /// <seealso cref="UnicodeData"/>
    internal sealed class UnicodeDataProvider
    {
        private const uint Magic = 0x554C5452;
        private const int BmpSize = 65536;

        private readonly RangeEntry[] ranges;
        private readonly MirrorEntry[] mirrors;
        private readonly BracketEntry[] brackets;
        private readonly ScriptRangeEntry[] scriptRanges;
        private readonly LineBreakRangeEntry[] lineBreakRanges;
        private readonly ExtendedPictographicRangeEntry[] extendedPictographicRanges;
        private readonly GeneralCategoryRangeEntry[] generalCategoryRanges;
        private readonly EastAsianWidthRangeEntry[] eastAsianWidthRanges;
        private readonly GraphemeBreakRangeEntry[] graphemeBreakRanges;
        private readonly IndicConjunctBreakRangeEntry[] indicConjunctBreakRanges;
        private readonly ScriptExtensionRangeEntry[] scriptExtensionRanges;
        private readonly DefaultIgnorableRangeEntry[] defaultIgnorableRanges;
        private readonly EmojiPresentationRangeEntry[] emojiPresentationRanges;
        private readonly EmojiModifierBaseRangeEntry[] emojiModifierBaseRanges;

        private readonly BidiClass[] bmpBidiClass;
        private readonly JoiningType[] bmpJoiningType;
        private readonly UnicodeScript[] bmpScript;
        private readonly LineBreakClass[] bmpLineBreak;
        private readonly GeneralCategory[] bmpGeneralCategory;
        private readonly EastAsianWidth[] bmpEastAsianWidth;
        private readonly GraphemeClusterBreak[] bmpGraphemeBreak;
        private readonly IndicConjunctBreak[] bmpIndicConjunctBreak;
        private readonly bool[] bmpDefaultIgnorable;

        /// <summary>Gets the Unicode version encoded in the data file.</summary>
        public int UnicodeVersionRaw { get; }

        /// <summary>
        /// Initializes the provider from binary Unicode data.
        /// </summary>
        /// <param name="data">Binary blob containing compiled Unicode property data.</param>
        /// <exception cref="InvalidDataException">The data format is invalid.</exception>
        public UnicodeDataProvider(byte[] data)
        {
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream);

            var fileMagic = reader.ReadUInt32();
            if (fileMagic != Magic)
                throw new InvalidDataException("Invalid Unicode data blob: magic mismatch.");

            UnicodeVersionRaw = unchecked((int)reader.ReadUInt32());

            var rangeOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var mirrorOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var bracketOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var scriptOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var lineBreakOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var extPictOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var gcOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var eawOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var gcbOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var incbOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var scxOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var diOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var epOffset = reader.ReadUInt32();
            reader.ReadUInt32();
            var embOffset = reader.ReadUInt32();
            reader.ReadUInt32();

            stream.Position = rangeOffset;
            var rangeCount = reader.ReadUInt32();
            ranges = new RangeEntry[rangeCount];

            for (uint i = 0; i < rangeCount; i++)
            {
                var start = reader.ReadUInt32();
                var end = reader.ReadUInt32();
                var bidi = reader.ReadByte();
                var jt = reader.ReadByte();
                var jg = reader.ReadByte();
                reader.ReadByte();

                ranges[i] = new RangeEntry(
                    unchecked((int)start),
                    unchecked((int)end),
                    (BidiClass)bidi,
                    (JoiningType)jt,
                    (JoiningGroup)jg);
            }

            if (mirrorOffset != 0)
            {
                stream.Position = mirrorOffset;
                var mirrorCount = reader.ReadUInt32();
                mirrors = new MirrorEntry[mirrorCount];

                for (uint i = 0; i < mirrorCount; i++)
                {
                    var cp = reader.ReadUInt32();
                    var mirrored = reader.ReadUInt32();

                    mirrors[i] = new MirrorEntry(
                        unchecked((int)cp),
                        unchecked((int)mirrored));
                }
            }
            else
            {
                mirrors = Array.Empty<MirrorEntry>();
            }

            if (bracketOffset != 0)
            {
                stream.Position = bracketOffset;
                var bracketCount = reader.ReadUInt32();
                brackets = new BracketEntry[bracketCount];

                for (uint i = 0; i < bracketCount; i++)
                {
                    var cp = reader.ReadUInt32();
                    var paired = reader.ReadUInt32();
                    var bpt = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    brackets[i] = new BracketEntry(
                        unchecked((int)cp),
                        unchecked((int)paired),
                        (BidiPairedBracketType)bpt);
                }
            }
            else
            {
                brackets = Array.Empty<BracketEntry>();
            }

            if (scriptOffset != 0)
            {
                stream.Position = scriptOffset;
                var scriptCount = reader.ReadUInt32();
                scriptRanges = new ScriptRangeEntry[scriptCount];

                for (uint i = 0; i < scriptCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var script = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    scriptRanges[i] = new ScriptRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (UnicodeScript)script);
                }
            }
            else
            {
                scriptRanges = Array.Empty<ScriptRangeEntry>();
            }

            if (lineBreakOffset != 0)
            {
                stream.Position = lineBreakOffset;
                var lineBreakCount = reader.ReadUInt32();
                lineBreakRanges = new LineBreakRangeEntry[lineBreakCount];

                for (uint i = 0; i < lineBreakCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var lbc = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    lineBreakRanges[i] = new LineBreakRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (LineBreakClass)lbc);
                }
            }
            else
            {
                lineBreakRanges = Array.Empty<LineBreakRangeEntry>();
            }

            if (extPictOffset != 0)
            {
                stream.Position = extPictOffset;
                var extPictCount = reader.ReadUInt32();
                extendedPictographicRanges = new ExtendedPictographicRangeEntry[extPictCount];

                for (uint i = 0; i < extPictCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();

                    extendedPictographicRanges[i] = new ExtendedPictographicRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end));
                }
            }
            else
            {
                extendedPictographicRanges = Array.Empty<ExtendedPictographicRangeEntry>();
            }

            if (gcOffset != 0)
            {
                stream.Position = gcOffset;
                var gcCount = reader.ReadUInt32();
                generalCategoryRanges = new GeneralCategoryRangeEntry[gcCount];

                for (uint i = 0; i < gcCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var gc = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    generalCategoryRanges[i] = new GeneralCategoryRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (GeneralCategory)gc);
                }
            }
            else
            {
                generalCategoryRanges = Array.Empty<GeneralCategoryRangeEntry>();
            }

            if (eawOffset != 0)
            {
                stream.Position = eawOffset;
                var eawCount = reader.ReadUInt32();
                eastAsianWidthRanges = new EastAsianWidthRangeEntry[eawCount];

                for (uint i = 0; i < eawCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var eaw = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    eastAsianWidthRanges[i] = new EastAsianWidthRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (EastAsianWidth)eaw);
                }
            }
            else
            {
                eastAsianWidthRanges = Array.Empty<EastAsianWidthRangeEntry>();
            }

            if (gcbOffset != 0)
            {
                stream.Position = gcbOffset;
                var gcbCount = reader.ReadUInt32();
                graphemeBreakRanges = new GraphemeBreakRangeEntry[gcbCount];

                for (uint i = 0; i < gcbCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var gcb = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    graphemeBreakRanges[i] = new GraphemeBreakRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (GraphemeClusterBreak)gcb);
                }
            }
            else
            {
                graphemeBreakRanges = Array.Empty<GraphemeBreakRangeEntry>();
            }

            if (incbOffset != 0)
            {
                stream.Position = incbOffset;
                var incbCount = reader.ReadUInt32();
                indicConjunctBreakRanges = new IndicConjunctBreakRangeEntry[incbCount];

                for (uint i = 0; i < incbCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var incb = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    indicConjunctBreakRanges[i] = new IndicConjunctBreakRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (IndicConjunctBreak)incb);
                }
            }
            else
            {
                indicConjunctBreakRanges = Array.Empty<IndicConjunctBreakRangeEntry>();
            }

            if (scxOffset != 0)
            {
                stream.Position = scxOffset;
                var scxCount = reader.ReadUInt32();
                scriptExtensionRanges = new ScriptExtensionRangeEntry[scxCount];

                for (uint i = 0; i < scxCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var scriptCount = reader.ReadByte();

                    var scripts = new UnicodeScript[scriptCount];
                    for (var j = 0; j < scriptCount; j++) scripts[j] = (UnicodeScript)reader.ReadByte();

                    var totalBytes = 8 + 1 + scriptCount;
                    var padding = (4 - totalBytes % 4) % 4;
                    for (var p = 0; p < padding; p++)
                        reader.ReadByte();

                    scriptExtensionRanges[i] = new ScriptExtensionRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        scripts);
                }
            }
            else
            {
                scriptExtensionRanges = Array.Empty<ScriptExtensionRangeEntry>();
            }

            if (diOffset != 0)
            {
                stream.Position = diOffset;
                var diCount = reader.ReadUInt32();
                defaultIgnorableRanges = new DefaultIgnorableRangeEntry[diCount];

                for (uint i = 0; i < diCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();

                    defaultIgnorableRanges[i] = new DefaultIgnorableRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end));
                }
            }
            else
            {
                defaultIgnorableRanges = Array.Empty<DefaultIgnorableRangeEntry>();
            }

            if (epOffset != 0)
            {
                stream.Position = epOffset;
                var epCount = reader.ReadUInt32();
                emojiPresentationRanges = new EmojiPresentationRangeEntry[epCount];

                for (uint i = 0; i < epCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();

                    emojiPresentationRanges[i] = new EmojiPresentationRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end));
                }
            }
            else
            {
                emojiPresentationRanges = Array.Empty<EmojiPresentationRangeEntry>();
            }

            if (embOffset != 0)
            {
                stream.Position = embOffset;
                var embCount = reader.ReadUInt32();
                emojiModifierBaseRanges = new EmojiModifierBaseRangeEntry[embCount];

                for (uint i = 0; i < embCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();

                    emojiModifierBaseRanges[i] = new EmojiModifierBaseRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end));
                }
            }
            else
            {
                emojiModifierBaseRanges = Array.Empty<EmojiModifierBaseRangeEntry>();
            }

            bmpBidiClass = new BidiClass[BmpSize];
            bmpJoiningType = new JoiningType[BmpSize];
            bmpScript = new UnicodeScript[BmpSize];
            bmpLineBreak = new LineBreakClass[BmpSize];
            bmpGeneralCategory = new GeneralCategory[BmpSize];
            bmpEastAsianWidth = new EastAsianWidth[BmpSize];
            bmpGraphemeBreak = new GraphemeClusterBreak[BmpSize];
            bmpIndicConjunctBreak = new IndicConjunctBreak[BmpSize];
            bmpDefaultIgnorable = new bool[BmpSize];

            InitializeBmpTables();
        }

        private void InitializeBmpTables()
        {
            foreach (var range in ranges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++)
                {
                    bmpBidiClass[cp] = range.bidiClass;
                    bmpJoiningType[cp] = range.joiningType;
                }
            }

            foreach (var range in scriptRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpScript[cp] = range.script;
            }

            foreach (var range in lineBreakRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpLineBreak[cp] = range.lineBreakClass;
            }

            foreach (var range in generalCategoryRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpGeneralCategory[cp] = range.generalCategory;
            }

            foreach (var range in eastAsianWidthRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpEastAsianWidth[cp] = range.eastAsianWidth;
            }

            foreach (var range in graphemeBreakRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpGraphemeBreak[cp] = range.graphemeBreak;
            }

            foreach (var range in indicConjunctBreakRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpIndicConjunctBreak[cp] = range.indicConjunctBreak;
            }

            foreach (var range in defaultIgnorableRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpDefaultIgnorable[cp] = true;
            }
        }

        public BidiClass GetBidiClass(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpBidiClass[codePoint];

            return FindInRange(ranges, codePoint, out var entry) ? entry.bidiClass : BidiClass.LeftToRight;
        }

        public bool IsBidiMirrored(int codePoint)
        {
            return FindByPoint(mirrors, codePoint, out _);
        }

        public int GetBidiMirroringGlyph(int codePoint)
        {
            return FindByPoint(mirrors, codePoint, out var mirror) ? mirror.mirroredCodePoint : codePoint;
        }

        public BidiPairedBracketType GetBidiPairedBracketType(int codePoint)
        {
            return FindByPoint(brackets, codePoint, out var bracket) ? bracket.bracketType : BidiPairedBracketType.None;
        }

        public int GetBidiPairedBracket(int codePoint)
        {
            return FindByPoint(brackets, codePoint, out var bracket) ? bracket.pairedCodePoint : codePoint;
        }

        public JoiningType GetJoiningType(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpJoiningType[codePoint];

            return FindInRange(ranges, codePoint, out var entry) ? entry.joiningType : JoiningType.NonJoining;
        }

        public JoiningGroup GetJoiningGroup(int codePoint)
        {
            return FindInRange(ranges, codePoint, out var entry) ? entry.joiningGroup : JoiningGroup.NoJoiningGroup;
        }

        public UnicodeScript GetScript(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpScript[codePoint];

            return FindInRange(scriptRanges, codePoint, out var entry) ? entry.script : UnicodeScript.Unknown;
        }

        public LineBreakClass GetLineBreakClass(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpLineBreak[codePoint];

            return FindInRange(lineBreakRanges, codePoint, out var entry) ? entry.lineBreakClass : LineBreakClass.XX;
        }

        public bool IsExtendedPictographic(int codePoint)
        {
            return FindInRange(extendedPictographicRanges, codePoint, out _);
        }

        public bool IsEmojiPresentation(int codePoint)
        {
            return FindInRange(emojiPresentationRanges, codePoint, out _);
        }

        public bool IsEmojiModifierBase(int codePoint)
        {
            return FindInRange(emojiModifierBaseRanges, codePoint, out _);
        }

        public GeneralCategory GetGeneralCategory(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpGeneralCategory[codePoint];

            return FindInRange(generalCategoryRanges, codePoint, out var entry) ? entry.generalCategory : GeneralCategory.Cn;
        }

        public EastAsianWidth GetEastAsianWidth(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpEastAsianWidth[codePoint];

            return FindInRange(eastAsianWidthRanges, codePoint, out var entry) ? entry.eastAsianWidth : EastAsianWidth.N;
        }


        public bool IsUnambiguousHyphen(int codePoint)
        {
            return GetLineBreakClass(codePoint) == LineBreakClass.HH;
        }


        public bool IsDottedCircle(int codePoint)
        {
            return codePoint == UnicodeData.DottedCircle;
        }


        public bool IsBrahmicForLB28a(int codePoint)
        {
            var script = GetScript(codePoint);
            return script == UnicodeScript.Balinese ||
                   script == UnicodeScript.Batak ||
                   script == UnicodeScript.Buginese ||
                   script == UnicodeScript.Javanese ||
                   script == UnicodeScript.KayahLi ||
                   script == UnicodeScript.Makasar ||
                   script == UnicodeScript.Mandaic ||
                   script == UnicodeScript.Modi ||
                   script == UnicodeScript.Nandinagari ||
                   script == UnicodeScript.Sundanese ||
                   script == UnicodeScript.TaiLe ||
                   script == UnicodeScript.NewTaiLue ||
                   script == UnicodeScript.Takri ||
                   script == UnicodeScript.Tibetan;
        }

        public GraphemeClusterBreak GetGraphemeClusterBreak(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpGraphemeBreak[codePoint];

            return FindInRange(graphemeBreakRanges, codePoint, out var entry) ? entry.graphemeBreak : GraphemeClusterBreak.Other;
        }

        public IndicConjunctBreak GetIndicConjunctBreak(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpIndicConjunctBreak[codePoint];

            return FindInRange(indicConjunctBreakRanges, codePoint, out var entry) ? entry.indicConjunctBreak : IndicConjunctBreak.None;
        }

        public UnicodeScript[] GetScriptExtensions(int codePoint)
        {
            if (FindInRange(scriptExtensionRanges, codePoint, out var entry))
                return entry.scripts;

            var script = GetScript(codePoint);
            return new[] { script };
        }

        public bool HasScriptExtension(int codePoint, UnicodeScript script)
        {
            if (FindInRange(scriptExtensionRanges, codePoint, out var entry))
            {
                foreach (var s in entry.scripts)
                    if (s == script)
                        return true;
                return false;
            }

            return GetScript(codePoint) == script;
        }


        public bool IsDefaultIgnorable(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpDefaultIgnorable[codePoint];

            return FindInRange(defaultIgnorableRanges, codePoint, out _);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool FindInRange<T>(T[] entries, int codePoint, out T result) where T : struct, IRangeEntry
        {
            var lo = 0;
            var hi = entries.Length - 1;

            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                ref readonly var entry = ref entries[mid];

                if (codePoint < entry.StartCodePoint)
                    hi = mid - 1;
                else if (codePoint > entry.EndCodePoint)
                    lo = mid + 1;
                else
                {
                    result = entry;
                    return true;
                }
            }

            result = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool FindByPoint<T>(T[] entries, int codePoint, out T result) where T : struct, IPointEntry
        {
            var lo = 0;
            var hi = entries.Length - 1;

            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                ref readonly var entry = ref entries[mid];
                var entryCodePoint = entry.CodePoint;

                if (codePoint < entryCodePoint)
                    hi = mid - 1;
                else if (codePoint > entryCodePoint)
                    lo = mid + 1;
                else
                {
                    result = entry;
                    return true;
                }
            }

            result = default;
            return false;
        }
    }
}
