using System;

namespace LightSide
{
    /// <summary>
    /// Transforms text to uppercase within marked ranges.
    /// </summary>
    /// <remarks>
    /// No parameter. The transformation happens during Apply, after parsing but before shaping,
    /// ensuring correct glyph rendering for uppercase characters.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Converts text to uppercase.")]
    public class UppercaseModifier : BaseModifier
    {
        protected override void OnApply(int start, int end, string parameter)
        {
            var codepoints = buffers.codepoints.data;
            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);

            for (var i = start; i < clampedEnd; i++)
                codepoints[i] = ToUpperCodepoint(codepoints[i]);
        }

        private static int ToUpperCodepoint(int codepoint)
        {
            if (codepoint <= UnicodeData.MaxBmp)
                return char.ToUpperInvariant((char)codepoint);

            return codepoint;
        }
    }
}
