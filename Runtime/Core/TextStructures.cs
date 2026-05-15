using System;

namespace LightSide
{
    /// <summary>
    /// Specifies the base text direction for bidirectional text processing.
    /// </summary>
    /// <seealso cref="BidiEngine"/>
    public enum TextDirection : byte
    {
        /// <summary>Left-to-right direction (e.g., Latin, Cyrillic).</summary>
        LeftToRight = 0,

        /// <summary>Right-to-left direction (e.g., Arabic, Hebrew).</summary>
        RightToLeft = 1,

        /// <summary>Automatically detect direction from text content using UAX #9.</summary>
        Auto = 2
    }


    /// <summary>
    /// Specifies the type of line break opportunity according to UAX #14.
    /// </summary>
    /// <seealso cref="LineBreakAlgorithm"/>
    public enum LineBreakType : byte
    {
        /// <summary>No break allowed at this position.</summary>
        None = 0,

        /// <summary>Break is allowed but not required (soft break).</summary>
        Optional = 1,

        /// <summary>Break is required at this position (hard break after CR, LF, etc.).</summary>
        Mandatory = 2
    }


    /// <summary>
    /// Represents a shaped glyph with positioning information from the shaping engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced by <see cref="Shaper"/> after OpenType shaping. Contains the glyph ID,
    /// cluster mapping back to codepoints, and advance/offset values.
    /// </para>
    /// </remarks>
    public struct ShapedGlyph
    {
        /// <summary>The font-specific glyph identifier.</summary>
        public int glyphId;

        /// <summary>Absolute index into the codepoints array for the cluster that produced this glyph.</summary>
        public int cluster;

        /// <summary>Horizontal advance to the next glyph position.</summary>
        public float advanceX;

        /// <summary>Vertical advance to the next glyph position.</summary>
        public float advanceY;

        /// <summary>Horizontal offset from the pen position for rendering.</summary>
        public float offsetX;

        /// <summary>Vertical offset from the pen position for rendering.</summary>
        public float offsetY;
    }


    /// <summary>
    /// Represents a range of indices in a text buffer.
    /// </summary>
    public readonly struct TextRange : IEquatable<TextRange>
    {
        /// <summary>The starting index of the range.</summary>
        public readonly int start;

        /// <summary>The number of elements in the range.</summary>
        public readonly int length;

        /// <summary>Gets the exclusive end index of the range.</summary>
        public int End => start + length;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextRange"/> struct.
        /// </summary>
        /// <param name="start">The starting index.</param>
        /// <param name="length">The number of elements.</param>
        public TextRange(int start, int length)
        {
            this.start = start;
            this.length = length;
        }

        /// <summary>
        /// Determines whether the specified index is within this range.
        /// </summary>
        /// <param name="index">The index to check.</param>
        /// <returns><see langword="true"/> if the index is within the range; otherwise, <see langword="false"/>.</returns>
        public bool Contains(int index)
        {
            return index >= start && index < End;
        }

        /// <summary>
        /// Determines whether this range overlaps with another range.
        /// </summary>
        /// <param name="other">The other range to check.</param>
        /// <returns><see langword="true"/> if the ranges overlap; otherwise, <see langword="false"/>.</returns>
        public bool Overlaps(TextRange other)
        {
            return start < other.End && End > other.start;
        }

        /// <inheritdoc/>
        public bool Equals(TextRange other)
        {
            return start == other.start && length == other.length;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is TextRange other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(start, length);
        }

        /// <summary>Determines whether two ranges are equal.</summary>
        public static bool operator ==(TextRange left, TextRange right)
        {
            return left.Equals(right);
        }

        /// <summary>Determines whether two ranges are not equal.</summary>
        public static bool operator !=(TextRange left, TextRange right)
        {
            return !left.Equals(right);
        }
    }


    /// <summary>
    /// Represents a segment of text with uniform script, direction, and font before shaping.
    /// </summary>
    /// <remarks>
    /// Created during itemization to split text into homogeneous segments
    /// that can be shaped independently.
    /// </remarks>
    public struct TextRun
    {
        /// <summary>The codepoint range covered by this run.</summary>
        public TextRange range;

        /// <summary>The BiDi embedding level (odd = RTL, even = LTR).</summary>
        public byte bidiLevel;

        /// <summary>The Unicode script of this run.</summary>
        public UnicodeScript script;

        /// <summary>The font ID to use for shaping this run.</summary>
        public int fontId;

        /// <summary>Gets the text direction derived from the BiDi level.</summary>
        public TextDirection Direction => (bidiLevel & 1) == 0
            ? TextDirection.LeftToRight
            : TextDirection.RightToLeft;
    }


    /// <summary>
    /// Represents a text run after shaping, with glyph information and metrics.
    /// </summary>
    public struct ShapedRun
    {
        /// <summary>The codepoint range covered by this run.</summary>
        public TextRange range;

        /// <summary>Index of the first glyph in the shaped glyphs buffer.</summary>
        public int glyphStart;

        /// <summary>Number of glyphs in this run.</summary>
        public int glyphCount;

        /// <summary>Total width of all glyphs in this run.</summary>
        public float width;

        /// <summary>The text direction of this run.</summary>
        public TextDirection direction;

        /// <summary>The BiDi embedding level.</summary>
        public byte bidiLevel;

        /// <summary>The font ID used for this run.</summary>
        public int fontId;
    }


    /// <summary>
    /// Represents a line of text after line breaking.
    /// </summary>
    public struct TextLine
    {
        /// <summary>The codepoint range covered by this line.</summary>
        public TextRange range;

        /// <summary>Index of the first run in the ordered runs buffer for this line.</summary>
        public int runStart;

        /// <summary>Number of runs in this line.</summary>
        public int runCount;

        /// <summary>Total width of all runs in this line.</summary>
        public float width;

        /// <summary>The base BiDi level of the paragraph containing this line.</summary>
        public byte paragraphBaseLevel;

        /// <summary>Left margin for this line (e.g., for list indentation).</summary>
        public float startMargin;
    }


    /// <summary>
    /// Represents a glyph with final position coordinates ready for rendering.
    /// </summary>
    /// <remarks>
    /// This is the final output of the text processing pipeline, containing
    /// all information needed to render a glyph to a mesh or texture.
    /// </remarks>
    public struct PositionedGlyph
    {
        /// <summary>The font-specific glyph identifier.</summary>
        public int glyphId;

        /// <summary>Index of the source codepoint cluster.</summary>
        public int cluster;

        /// <summary>X position of the glyph origin.</summary>
        public float x;

        /// <summary>Y position of the glyph origin.</summary>
        public float y;

        /// <summary>The font ID used for this glyph.</summary>
        public int fontId;

        /// <summary>Index into the shaped glyphs buffer.</summary>
        public int shapedGlyphIndex;

        /// <summary>Left edge of the glyph bounding box.</summary>
        public float left;

        /// <summary>Top edge of the glyph bounding box.</summary>
        public float top;

        /// <summary>Right edge of the glyph bounding box.</summary>
        public float right;

        /// <summary>Bottom edge of the glyph bounding box.</summary>
        public float bottom;
    }


    /// <summary>
    /// Cached glyph rendering data for efficient mesh generation.
    /// </summary>
    internal struct CachedGlyphData
    {
        /// <summary>X position in the font atlas texture.</summary>
        public int rectX;

        /// <summary>Y position in the font atlas texture.</summary>
        public int rectY;

        /// <summary>Width in the font atlas texture.</summary>
        public int rectWidth;

        /// <summary>Height in the font atlas texture.</summary>
        public int rectHeight;

        /// <summary>Horizontal bearing (offset from origin to left edge).</summary>
        public float bearingX;

        /// <summary>Vertical bearing (offset from baseline to top edge).</summary>
        public float bearingY;

        /// <summary>Glyph width in pixels.</summary>
        public float width;

        /// <summary>Glyph height in pixels.</summary>
        public float height;

        /// <summary>Indicates whether this cache entry contains valid data.</summary>
        public bool isValid;
    }


    /// <summary>
    /// Specifies horizontal text alignment within the layout bounds.
    /// </summary>
    public enum HorizontalAlignment : byte
    {
        /// <summary>Align text to the left edge.</summary>
        Left = 0,

        /// <summary>Center text horizontally.</summary>
        Center = 1,

        /// <summary>Align text to the right edge.</summary>
        Right = 2
    }


    /// <summary>
    /// Specifies vertical text alignment within the layout bounds.
    /// </summary>
    public enum VerticalAlignment : byte
    {
        /// <summary>Align text to the top edge.</summary>
        Top = 0,

        /// <summary>Center text vertically.</summary>
        Middle = 1,

        /// <summary>Align text to the bottom edge.</summary>
        Bottom = 2
    }

    /// <summary>
    /// Specifies which metric defines the top edge of the text box.
    /// </summary>
    /// <remarks>
    /// Controls how the first line is positioned relative to the container top.
    /// Matches CSS <c>text-box-edge</c> over-edge values and Figma Vertical Trim.
    /// </remarks>
    public enum TextOverEdge : byte
    {
        /// <summary>Top edge at ascent line — default, fits all ascenders and diacritics.</summary>
        Ascent = 0,

        /// <summary>Top edge at cap height — tighter fit, matches Figma Vertical Trim.</summary>
        CapHeight = 1,

        /// <summary>Top edge includes half-leading — matches CSS and Figma Standard mode.</summary>
        HalfLeading = 2,
    }


    /// <summary>
    /// Specifies which metric defines the bottom edge of the text box.
    /// </summary>
    /// <remarks>
    /// Controls how the last line contributes to the total text height.
    /// Matches CSS <c>text-box-edge</c> under-edge values and Figma Vertical Trim.
    /// </remarks>
    public enum TextUnderEdge : byte
    {
        /// <summary>Bottom edge at descent line — default, fits all descenders.</summary>
        Descent = 0,

        /// <summary>Bottom edge at baseline — tighter fit, matches Figma Vertical Trim.</summary>
        Baseline = 1,

        /// <summary>Bottom edge includes half-leading — matches CSS and Figma Standard mode.</summary>
        HalfLeading = 2,
    }


    /// <summary>
    /// Controls how extra leading from line-height is distributed relative to the content area.
    /// </summary>
    /// <remarks>
    /// Different platforms use different models:
    /// <list type="bullet">
    /// <item><see cref="HalfLeading"/> — CSS standard: split equally above and below.</item>
    /// <item><see cref="LeadingAbove"/> — Figma / iOS: all extra space above the line.</item>
    /// <item><see cref="LeadingBelow"/> — Android View / legacy: all extra space below the line.</item>
    /// </list>
    /// </remarks>
    public enum LeadingDistribution : byte
    {
        /// <summary>Extra leading split equally above and below (CSS half-leading model).</summary>
        HalfLeading = 0,

        /// <summary>All extra leading placed above the line (Figma model).</summary>
        LeadingAbove = 1,

        /// <summary>All extra leading placed below the line (Android View model).</summary>
        LeadingBelow = 2,
    }


    /// <summary>
    /// Result of a shaping operation containing glyphs and metrics.
    /// </summary>
    /// <remarks>
    /// This is a ref struct to allow returning a span without allocation.
    /// </remarks>
    public readonly ref struct ShapingResult
    {
        /// <summary>The shaped glyphs with positioning information.</summary>
        public readonly ReadOnlySpan<ShapedGlyph> Glyphs;

        /// <summary>The total horizontal advance of all glyphs.</summary>
        public readonly float TotalAdvance;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapingResult"/> struct.
        /// </summary>
        /// <param name="glyphs">The shaped glyphs.</param>
        /// <param name="totalAdvance">The total advance width.</param>
        public ShapingResult(ReadOnlySpan<ShapedGlyph> glyphs, float totalAdvance)
        {
            Glyphs = glyphs;
            TotalAdvance = totalAdvance;
        }
    }
}
