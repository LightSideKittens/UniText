using System;

namespace LightSide
{
    /// <summary>
    /// Transforms text to uppercase within marked ranges.
    /// </summary>
    /// <remarks>
    /// Usage: <c>&lt;upper&gt;text&lt;/upper&gt;</c>
    ///
    /// The transformation happens during Apply, after parsing but before shaping,
    /// ensuring correct glyph rendering for uppercase characters.
    /// </remarks>
    /// <seealso cref="UppercaseParseRule"/>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    public class UppercaseModifier : BaseModifier
    {
        protected override void OnEnable() { }

        protected override void OnDisable() { }

        protected override void OnDestroy() { }

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
