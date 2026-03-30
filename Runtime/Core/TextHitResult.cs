using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Result of a text hit test operation.
    /// </summary>
    /// <remarks>
    /// Returned by <see cref="UniText.HitTest"/> and <see cref="UniText.HitTestScreen"/>
    /// to identify which glyph/cluster was hit by a pointer.
    /// </remarks>
    public readonly struct TextHitResult
    {
        /// <summary>True if a glyph was hit.</summary>
        public readonly bool hit;

        /// <summary>Index of the hit glyph in the positioned glyphs array.</summary>
        public readonly int glyphIndex;

        /// <summary>Cluster index (codepoint offset in source text).</summary>
        public readonly int cluster;

        /// <summary>Position of the hit glyph.</summary>
        public readonly Vector2 glyphPosition;

        /// <summary>Distance from the hit point to the glyph center.</summary>
        public readonly float distance;

        /// <summary>Represents no hit.</summary>
        public static readonly TextHitResult None = new();

        public TextHitResult(int glyphIndex, int cluster, Vector2 glyphPosition, float distance)
        {
            hit = true;
            this.glyphIndex = glyphIndex;
            this.cluster = cluster;
            this.glyphPosition = glyphPosition;
            this.distance = distance;
        }
    }
}
