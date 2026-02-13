namespace LightSide
{
    /// <summary>
    /// Result of a hit test on an interactive range.
    /// </summary>
    /// <remarks>
    /// Combines <see cref="InteractiveRange"/> with <see cref="TextHitResult"/>
    /// to provide complete information about a click/hover on interactive text.
    /// </remarks>
    public readonly struct InteractiveRangeHit
    {
        /// <summary>True if an interactive range was hit.</summary>
        public readonly bool hit;

        /// <summary>The interactive range that was hit.</summary>
        public readonly InteractiveRange range;

        /// <summary>The underlying text hit result.</summary>
        public readonly TextHitResult textHit;

        /// <summary>Represents no hit.</summary>
        public static readonly InteractiveRangeHit None = new();

        /// <summary>Creates an interactive range hit result.</summary>
        /// <param name="range">The interactive range that was hit.</param>
        /// <param name="textHit">The underlying text hit result.</param>
        public InteractiveRangeHit(InteractiveRange range, TextHitResult textHit)
        {
            hit = true;
            this.range = range;
            this.textHit = textHit;
        }
    }
}
