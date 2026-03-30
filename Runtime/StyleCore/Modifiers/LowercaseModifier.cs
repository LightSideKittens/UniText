using System;

namespace LightSide
{
    /// <summary>
    /// Transforms text to lowercase within marked ranges.
    /// </summary>
    /// <remarks>
    /// No parameter. The transformation happens during Apply, after parsing but before shaping,
    /// ensuring correct glyph rendering for lowercase characters.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Converts text to lowercase.")]
    public class LowercaseModifier : BaseModifier
    {
        protected override void OnApply(int start, int end, string parameter)
        {
            var codepoints = buffers.codepoints.data;
            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);

            for (var i = start; i < clampedEnd; i++)
                codepoints[i] = ToLowerCodepoint(codepoints[i]);
        }

        private static int ToLowerCodepoint(int codepoint)
        {
            if (codepoint <= UnicodeData.MaxBmp)
                return char.ToLowerInvariant((char)codepoint);

            return codepoint;
        }
    }
}
