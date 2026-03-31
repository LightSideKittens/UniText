using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies an outline effect via a dedicated Base-pass CanvasRenderer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All parameters come from the tag/rule parameter string.
    /// Format: <c>&lt;outline=dilate&gt;</c>, <c>&lt;outline=#color&gt;</c>,
    /// or <c>&lt;outline=dilate,#color&gt;</c>.
    /// Defaults: dilate = 0.2, color = black (#000000FF).
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 3)]
    [TypeDescription("Adds an outline effect around the text.")]
    [ParameterField(0, "Dilate", "float", "0.2")]
    [ParameterField(1, "Color", "color", "#000000FF")]
    public class OutlineModifier : EffectModifier
    {
        private const float defaultDilate = 0.2f;
        private static readonly Color32 defaultColor = new(0, 0, 0, 255);

        /// <summary>When true, dilate is in fixed pixels and compensated by gradientScale per glyph.</summary>
        [SerializeField] public bool fixedPixelSize;

        private struct EffectRange
        {
            public int start;
            public int end;
            public float dilate;
            public float packedColor;
        }

        private PooledBuffer<EffectRange> ranges;

        protected override void OnEnable()
        {
            ranges.FakeClear();
            base.OnEnable();
        }

        protected override void OnDestroy()
        {
            ranges.Return();
            base.OnDestroy();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var dilate = defaultDilate;
            var color = defaultColor;

            if (!string.IsNullOrEmpty(parameter))
                ParseParameter(parameter, ref dilate, ref color);

            ranges.Add(new EffectRange
            {
                start = start,
                end = end,
                dilate = dilate,
                packedColor = EffectPacking.PackColor(color)
            });
        }

        protected override void OnGlyphEffect()
        {
            var gen = UniTextMeshGenerator.Current;
            var cluster = gen.currentCluster;
            var count = ranges.count;
            var data = ranges.data;

            for (var i = 0; i < count; i++)
            {
                ref var range = ref data[i];
                if (cluster < range.start || cluster >= range.end) continue;

                var baseIdx = gen.vertexCount - 4;
                var glyphH = gen.Uvs0[baseIdx].w;
                var faceDilate = gen.Uvs1[baseIdx].y;

                var dilate = fixedPixelSize
                    ? range.dilate / (GlyphAtlas.Pad * gen.fontMetricFactor)
                    : range.dilate;

                var extent = (faceDilate + dilate) * GlyphAtlas.Pad / glyphH;

                RecordEffectGlyph(new EffectGlyph
                {
                    baseIdx = baseIdx,
                    effectUv = new Vector4(dilate, range.packedColor, 0f, 0f)
                }, extent);
                return;
            }
        }

        private static void ParseParameter(ReadOnlySpan<char> param, ref float dilate, ref Color32 color)
        {
            var reader = new ParameterReader(param);
            while (reader.Next(out var token))
            {
                if (token.IsEmpty) continue;
                if (ColorParsing.TryParse(token, out var c))
                    color = c;
                else
                    ParameterReader.ParseFloat(token, out dilate);
            }
        }
    }
}
