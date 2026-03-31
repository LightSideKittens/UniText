using System;

namespace LightSide
{
    /// <summary>
    /// Applies font size changes to text ranges.
    /// </summary>
    /// <remarks>
    /// Parameter: size value with optional unit.
    /// <list type="bullet">
    /// <item><c>24</c> — absolute size in pixels</item>
    /// <item><c>150%</c> — percentage of base font size</item>
    /// <item><c>+10</c> — relative increase in pixels</item>
    /// <item><c>-5</c> — relative decrease in pixels</item>
    /// </list>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Changes the font size of the text.")]
    [ParameterField(0, "Size", "unit:px|%|delta", "24")]
    public class SizeModifier : BaseModifier
    {
        private PooledArrayAttribute<float> attribute;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.Size);
            
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
            buffers?.ReleaseAttributeData(AttributeKeys.Size);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var reader = new ParameterReader(parameter);
            if (!reader.NextUnitFloat(out var value, out var unit))
                return;

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
            var scale = unit switch
            {
                ParameterReader.UnitKind.Percent => value / 100f,
                ParameterReader.UnitKind.Delta => (baseSize + value) / baseSize,
                _ => value / baseSize
            };
            if (scale <= 0f) return;

            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            for (var i = start; i < clampedEnd; i++)
                attribute.buffer[i] = scale;
        }

        private void OnShaped()
        {
            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var bufLen = attribute.buffer.Capacity;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                float width = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var cluster = glyphs[g].cluster;

                    if ((uint)cluster < (uint)bufLen)
                    {
                        var scale = attribute.buffer[cluster];
                        if (scale > 0f)
                            glyphs[g].advanceX *= scale;
                    }

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

            var scale = attribute.buffer[cluster];
            if (scale <= 0f || Math.Abs(scale - 1f) < 0.001f)
                return;

            UniTextMeshGenerator.ScaleGlyphQuad(gen.Vertices, gen.vertexCount - 4, gen.baselineY, scale);
        }
    }
}
