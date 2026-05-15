using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Atlas rendering mode for font glyphs.
    /// </summary>
    public enum UniTextRenderMode
    {
        /// <summary>Signed Distance Field — resolution-independent, supports outlines/shadows/glow.</summary>
        SDF,

        /// <summary>Anti-aliased grayscale bitmap — pixel-perfect at sampling size.</summary>
        Smooth,

        /// <summary>1-bit monochrome bitmap — no anti-aliasing.</summary>
        Mono
    }

    /// <summary>
    /// Error codes for font loading operations.
    /// </summary>
    public enum UniTextFontError
    {
        Success = 0,
        InvalidFile = 1,
        InvalidFace = 2,
        AtlasFull = 3
    }

    /// <summary>
    /// Font face metrics from OpenType/TrueType tables.
    /// All metric values are in font design units unless noted otherwise.
    /// </summary>
    [Serializable]
    public struct FaceInfo
    {
        /// <summary>Index of this face within a font collection (TTC).</summary>
        public int faceIndex;
        /// <summary>Font family name from the name table.</summary>
        public string familyName;
        /// <summary>Font style name (Regular, Bold, Italic, etc.).</summary>
        public string styleName;

        /// <summary>Point size used for atlas sampling.</summary>
        public int pointSize;

        /// <summary>Font design units per em (typically 1000 for CFF, 2048 for TrueType).</summary>
        public int unitsPerEm;

        /// <summary>Distance between consecutive baselines (hhea table).</summary>
        public int lineHeight;

        /// <summary>Top of the tallest glyph, positive above baseline (hhea ascender).</summary>
        public int ascentLine;

        /// <summary>Top of capital letters (OS/2 sCapHeight).</summary>
        public int capLine;

        /// <summary>Top of lowercase letters, x-height (OS/2 sxHeight).</summary>
        public int meanLine;

        /// <summary>Bottom of the deepest descender, negative below baseline (hhea descender).</summary>
        public int descentLine;

        /// <summary>Vertical offset for superscript glyphs (OS/2 ySuperscriptYOffset).</summary>
        public int superscriptOffset;
        /// <summary>Suggested point size for superscript glyphs (OS/2 ySuperscriptYSize).</summary>
        public int superscriptSize;
        /// <summary>Vertical offset for subscript glyphs (OS/2 ySubscriptYOffset).</summary>
        public int subscriptOffset;
        /// <summary>Suggested point size for subscript glyphs (OS/2 ySubscriptYSize).</summary>
        public int subscriptSize;

        /// <summary>Vertical position of the underline relative to baseline (post table).</summary>
        public int underlineOffset;
        /// <summary>Thickness of the underline stroke (post table).</summary>
        public int underlineThickness;
        /// <summary>Vertical position of the strikethrough relative to baseline (OS/2 yStrikeoutPosition).</summary>
        public int strikethroughOffset;
        /// <summary>Thickness of the strikethrough stroke (OS/2 yStrikeoutSize).</summary>
        public int strikethroughThickness;
        /// <summary>Width of a tab character, derived from space advance.</summary>
        public int tabWidth;
    }

    /// <summary>
    /// Position and size of a glyph within an atlas texture.
    /// </summary>
    [Serializable]
    public struct GlyphRect : IEquatable<GlyphRect>
    {
        /// <summary>Horizontal position in the atlas (pixels).</summary>
        public int x;
        /// <summary>Vertical position in the atlas (pixels).</summary>
        public int y;
        /// <summary>Width of the glyph region (pixels).</summary>
        public int width;
        /// <summary>Height of the glyph region (pixels).</summary>
        public int height;

        public static readonly GlyphRect zero = default;

        public GlyphRect(int x, int y, int width, int height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public bool Equals(GlyphRect other) =>
            x == other.x && y == other.y && width == other.width && height == other.height;

        public override bool Equals(object obj) => obj is GlyphRect other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, width, height);

        public static bool operator ==(GlyphRect lhs, GlyphRect rhs) => lhs.Equals(rhs);
        public static bool operator !=(GlyphRect lhs, GlyphRect rhs) => !lhs.Equals(rhs);
    }

    /// <summary>
    /// Metrics that define size, position and spacing of a glyph for text layout.
    /// Values are in font design units.
    /// </summary>
    [Serializable]
    public struct GlyphMetrics
    {
        /// <summary>Width of the glyph bounding box in design units.</summary>
        public float width;
        /// <summary>Height of the glyph bounding box in design units.</summary>
        public float height;
        /// <summary>Horizontal distance from the origin to the left edge of the bounding box.</summary>
        public float horizontalBearingX;
        /// <summary>Vertical distance from the baseline to the top edge of the bounding box.</summary>
        public float horizontalBearingY;
        /// <summary>Horizontal distance to advance to the next glyph origin.</summary>
        public float horizontalAdvance;

        public GlyphMetrics(float width, float height, float bearingX, float bearingY, float advance)
        {
            this.width = width;
            this.height = height;
            horizontalBearingX = bearingX;
            horizontalBearingY = bearingY;
            horizontalAdvance = advance;
        }
    }

    /// <summary>
    /// A glyph: visual representation of a character in a font atlas.
    /// </summary>
    [Serializable]
    public struct Glyph
    {
        /// <summary>Glyph index in the font (0 = .notdef / missing glyph).</summary>
        public uint index;
        /// <summary>Glyph metrics for layout (size, bearings, advance).</summary>
        public GlyphMetrics metrics;
        /// <summary>Position and size of this glyph in the atlas texture.</summary>
        public GlyphRect glyphRect;
        /// <summary>Index of the atlas texture containing this glyph.</summary>
        public int atlasIndex;

        public Glyph(uint index, GlyphMetrics metrics, GlyphRect glyphRect, int atlasIndex)
        {
            this.index = index;
            this.metrics = metrics;
            this.glyphRect = glyphRect;
            this.atlasIndex = atlasIndex;
        }
    }
}
