using System;

namespace LightSide
{
    /// <summary>
    /// Applies font size changes to text ranges.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <list type="bullet">
    /// <item><c>&lt;size=24&gt;text&lt;/size&gt;</c> — absolute size in pixels</item>
    /// <item><c>&lt;size=150%&gt;text&lt;/size&gt;</c> — percentage of base font size</item>
    /// <item><c>&lt;size=+10&gt;text&lt;/size&gt;</c> — relative increase</item>
    /// <item><c>&lt;size=-5&gt;text&lt;/size&gt;</c> — relative decrease</item>
    /// </list>
    /// </remarks>
    /// <seealso cref="SizeParseRule"/>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    public class SizeModifier : BaseModifier
    {
        private PooledArrayAttribute<float> attribute;

        protected override void OnEnable()
        {
            attribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<float>>(AttributeKeys.Size);
            var cpCount = buffers.codepoints.count;
            attribute.EnsureCountAndClear(cpCount);
            
            uniText.TextProcessor.Shaped += OnShaped;
            uniText.MeshGenerator.OnGlyph += OnGlyph;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped -= OnShaped;
            uniText.MeshGenerator.OnGlyph -= OnGlyph;
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.Size);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            if (!TryParseSize(parameter, out var scale))
                return;

            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            for (var i = start; i < clampedEnd; i++)
                attribute.buffer[i] = scale;
        }

        private bool TryParseSize(string param, out float scale)
        {
            scale = 0f;
            if (string.IsNullOrEmpty(param))
                return false;

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;

            if (param[param.Length - 1] == '%')
            {
                if (float.TryParse(param.AsSpan(0, param.Length - 1), out var percent))
                {
                    scale = percent / 100f;
                    return scale > 0f;
                }
                return false;
            }

            if (param[0] == '+' || param[0] == '-')
            {
                if (float.TryParse(param, out var delta))
                {
                    var targetSize = baseSize + delta;
                    scale = targetSize / baseSize;
                    return scale > 0f;
                }
                return false;
            }

            if (float.TryParse(param, out var absoluteSize))
            {
                scale = absoluteSize / baseSize;
                return scale > 0f;
            }

            return false;
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

            var baseIdx = gen.vertexCount - 4;
            var verts = gen.Vertices;
            var baselineY = gen.baselineY;

            var leftX = verts[baseIdx].x;

            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                v.x = leftX + (v.x - leftX) * scale;
                v.y = baselineY + (v.y - baselineY) * scale;
            }
        }
    }
}
