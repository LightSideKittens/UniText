using System;

namespace LightSide
{
    /// <summary>
    /// Applies bold styling to text using CSS font-weight scale (100-900).
    /// </summary>
    /// <remarks>
    /// Parameter: optional CSS font-weight (100-900). Without parameter, computes max(700, baseWeight + 300).
    ///
    /// When used with font variants (static or variable), ResolveVariants selects the
    /// appropriate real font and clears the bold buffer. When no variant is available,
    /// BoldModifier applies fake bold via SDF shader dilate (UV1.y) and advance correction.
    ///
    /// Encoding: byte buffer per codepoint.
    /// 0 = not bold, 1-255 = CSS weight mapped to byte range.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Makes text thicker by expanding the distance field via shader dilate and adjusting glyph advances.")]
    [ParameterField(0, "Weight", "int(100,900)", "700")]
    public class BoldModifier : BaseModifier
    {
        /// <summary>
        /// FreeType's FT_GlyphSlot_Embolden ratio: em/24 total stroke width per unit weight.
        /// Used for both advance correction and dilate (shader applies × DILATE_SCALE = × 0.5).
        /// </summary>
        private const float EmboldenRatio = 1f / 24f;

        private PooledArrayAttribute<byte> attribute;
        private Action onGlyphCallback;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.Bold);

            onGlyphCallback ??= OnGlyph;
            uniText.MeshGenerator.onGlyph += onGlyphCallback;
            uniText.TextProcessor.Shaped += OnShaped;
        }

        protected override void OnDisable()
        {
            uniText.MeshGenerator.onGlyph -= onGlyphCallback;
            uniText.TextProcessor.Shaped -= OnShaped;
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.Bold);
            attribute = null;
            onGlyphCallback = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            int cssWeight;
            var reader = new ParameterReader(parameter);
            if (reader.NextInt(out var parsed))
            {
                cssWeight = Math.Clamp(parsed, 100, 900);
            }
            else
            {
                var baseWeight = uniText.PrimaryFont.FaceInfo.weightClass;
                cssWeight = Math.Min(Math.Max(700, baseWeight + 300), 900);
            }

            var encoded = EncodeCssWeight(cssWeight);
            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            var buf = attribute.buffer.data;

            for (var i = start; i < clampedEnd; i++)
                buf[i] = encoded;
        }

        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen.font.IsColor) return;

            var cluster = gen.currentCluster;
            var buf = attribute.buffer.data;
            if (!buf.HasFlag(cluster))
                return;

            var cssWeight = DecodeCssWeight(buf[cluster]);
            var baseWeight = gen.font.FaceInfo.weightClass;
            var fakeBoldWeight = Math.Max(0f, (cssWeight - baseWeight) / 300f);
            var dilate = fakeBoldWeight * EmboldenRatio;
            var baseIdx = gen.vertexCount - 4;
            var uvs1 = gen.Uvs1;

            uvs1[baseIdx].y = dilate;
            uvs1[baseIdx + 1].y = dilate;
            uvs1[baseIdx + 2].y = dilate;
            uvs1[baseIdx + 3].y = dilate;
        }

        private void OnShaped()
        {
            if (attribute == null)
                return;

            var buffer = attribute.buffer.data;
            if (buffer == null || !buffer.HasAnyFlags())
                return;

            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var bufLen = buffer.Length;
            var fontSize = buf.shapingFontSize > 0 ? buf.shapingFontSize : uniText.FontSize;
            var fp = uniText.FontProvider;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;

                var runFont = fp.GetFontAsset(run.fontId);
                var baseWeight = runFont.FaceInfo.weightClass;

                float width = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var cluster = glyphs[g].cluster;

                    if ((uint)cluster < (uint)bufLen && buffer[cluster] != 0)
                    {
                        var cssWeight = DecodeCssWeight(buffer[cluster]);
                        var fakeBoldWeight = Math.Max(0f, (cssWeight - baseWeight) / 300f);
                        glyphs[g].advanceX += fontSize * EmboldenRatio * fakeBoldWeight;
                    }

                    width += glyphs[g].advanceX;
                }

                run.width = width;
            }
        }

        /// <summary>Encodes CSS weight (100-900) to byte (1-255).</summary>
        internal static byte EncodeCssWeight(int cssWeight)
        {
            return (byte)Math.Clamp((cssWeight - 100) * 254 / 800 + 1, 1, 255);
        }

        /// <summary>Decodes byte (1-255) back to CSS weight (100-900).</summary>
        internal static int DecodeCssWeight(byte encoded)
        {
            return (encoded - 1) * 800 / 254 + 100;
        }
    }
}
