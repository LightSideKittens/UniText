using System;

namespace LightSide
{
    /// <summary>
    /// Renders lowercase letters as small capitals.
    /// </summary>
    /// <remarks>
    /// Two-tier approach:
    /// <list type="bullet">
    /// <item>Native: activates OpenType 'smcp' feature via HarfBuzz (proper small cap glyphs).</item>
    /// <item>Synthesis: converts lowercase to uppercase and scales down (fallback for fonts without smcp).</item>
    /// </list>
    /// Attribute byte encoding: 0 = unchanged, 1 = native smcp, 2 = synthesis.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Renders lowercase letters as small uppercase letters.")]
    public class SmallCapsModifier : BaseModifier
    {
        private const float SynthesisScale = 0.8f;

        private const byte Native = 1;
        private const byte Synthesis = 2;

        private PooledArrayAttribute<byte> attribute;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.SmallCaps);

            uniText.TextProcessor.Shaped += OnShaped;
            uniText.MeshGenerator.onGlyph += OnGlyph;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped -= OnShaped;
            uniText.MeshGenerator.onGlyph -= OnGlyph;
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.SmallCaps);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var codepoints = buffers.codepoints.data;
            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            var buf = attribute.buffer.data;

            var mainFont = uniText.PrimaryFont;
            bool hasSmcp = Shaper.Instance.HasSmcpFeature(mainFont);

            if (hasSmcp)
            {
                for (var i = start; i < clampedEnd; i++)
                {
                    var cp = codepoints[i];
                    if (IsLowercase(cp))
                        buf[i] = Native;
                }
            }
            else
            {
                for (var i = start; i < clampedEnd; i++)
                {
                    var cp = codepoints[i];
                    var upper = ToUpperCodepoint(cp);
                    if (upper != cp)
                    {
                        buf[i] = Synthesis;
                        codepoints[i] = upper;
                    }
                }
            }
        }

        private void OnShaped()
        {
            var glyphs = buffers.shapedGlyphs.data;
            var runs = buffers.shapedRuns.data;
            var runCount = buffers.shapedRuns.count;
            var attrBuf = attribute.buffer.data;
            var bufLen = attribute.buffer.Capacity;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                float width = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster < (uint)bufLen && attrBuf[cluster] == Synthesis)
                        glyphs[g].advanceX *= SynthesisScale;

                    width += glyphs[g].advanceX;
                }

                run.width = width;
            }
        }

        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;
            var cluster = gen.currentCluster;

            if ((uint)cluster >= (uint)attribute.buffer.Capacity)
                return;

            if (attribute.buffer[cluster] != Synthesis)
                return;

            UniTextMeshGenerator.ScaleGlyphQuad(gen.Vertices, gen.vertexCount - 4, gen.baselineY, SynthesisScale);
        }

        private static bool IsLowercase(int codepoint)
        {
            if (codepoint > UnicodeData.MaxBmp) return false;
            var ch = (char)codepoint;
            return char.IsLower(ch);
        }

        private static int ToUpperCodepoint(int codepoint)
        {
            if (codepoint <= UnicodeData.MaxBmp)
                return char.ToUpperInvariant((char)codepoint);

            return codepoint;
        }
    }
}
