using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies a shadow effect via a dedicated Base-pass CanvasRenderer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shadow offset is applied via mesh vertex displacement. Each instance gets its own
    /// CanvasRenderer, enabling unlimited stacking of shadow/outline effects.
    /// </para>
    /// <para>
    /// All parameters come from the tag/rule parameter string.
    /// Format: <c>&lt;shadow=#color&gt;</c>, <c>&lt;shadow=dilate,#color&gt;</c>,
    /// or <c>&lt;shadow=dilate,#color,offsetX,offsetY,softness&gt;</c>.
    /// Defaults: dilate = 0, color = black 50% (#00000080), offset = (0.1,-0.1), softness = 0.1.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 4)]
    [TypeDescription("Adds a shadow effect behind the text.")]
    [ParameterField(0, "Dilate", "float", "0.1")]
    [ParameterField(1, "Color", "color", "#00000080")]
    [ParameterField(2, "Offset X", "float", "0.1")]
    [ParameterField(3, "Offset Y", "float", "-0.1")]
    [ParameterField(4, "Softness", "float", "0.1")]
    public class ShadowModifier : EffectModifier
    {
        private static readonly Color32 defaultColor = new(0, 0, 0, 128);

        /// <summary>When true, dilate/offset/softness are in fixed pixels and compensated by gradientScale per glyph.</summary>
        [SerializeField] public bool fixedPixelSize;

        private struct EffectRange
        {
            public int start;
            public int end;
            public float dilate;
            public float packedColor;
            public float offsetX;
            public float offsetY;
            public float softness;
        }

        private PooledBuffer<EffectRange> ranges;
        private bool isWorldText;
        private Quaternion inverseRotation;

        protected override bool HasVertexShifts() => true;

        protected override void OnEnable()
        {
            isWorldText = uniText is UniTextWorld;
            ranges.FakeClear();
            base.OnEnable();
        }

        public override void PrepareForParallel()
        {
            base.PrepareForParallel();
            if (isWorldText)
                inverseRotation = Quaternion.Inverse(uniText.transform.rotation);
        }

        protected override void OnDestroy()
        {
            ranges.Return();
            base.OnDestroy();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var dilate = 0.1f;
            var color = defaultColor;
            var ox = 0.1f;
            var oy = -0.1f;
            var soft = 0.1f;

            if (!string.IsNullOrEmpty(parameter))
                ParseParameter(parameter, ref dilate, ref color, ref ox, ref oy, ref soft);

            ranges.Add(new EffectRange
            {
                start = start,
                end = end,
                dilate = dilate,
                packedColor = EffectPacking.PackColor(color),
                offsetX = ox,
                offsetY = oy,
                softness = soft
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
                var uvToObj = glyphH * gen.fontMetricFactor;

                float dilate, softness, meshOffX, meshOffY;

                if (fixedPixelSize)
                {
                    var mf = gen.fontMetricFactor;
                    dilate = range.dilate / (GlyphAtlas.Pad * mf);
                    softness = range.softness / mf;
                    var of = mf / gen.height;
                    meshOffX = range.offsetX * of;
                    meshOffY = range.offsetY * of;
                }
                else
                {
                    dilate = range.dilate;
                    softness = range.softness;
                    meshOffX = range.offsetX * gen.fontMetricFactor;
                    meshOffY = range.offsetY * gen.fontMetricFactor;
                }

                if (isWorldText)
                {
                    var localDir = inverseRotation * new Vector3(meshOffX, meshOffY, 0f);
                    meshOffX = localDir.x;
                    meshOffY = localDir.y;
                }

                var extent = (faceDilate + dilate) * GlyphAtlas.Pad / glyphH + softness / glyphH;
                if (meshOffX != 0f || meshOffY != 0f)
                {
                    var invUvToObj = 1f / uvToObj;
                    extent += Mathf.Max(Mathf.Abs(meshOffX), Mathf.Abs(meshOffY)) * invUvToObj;
                }

                RecordEffectGlyph(new EffectGlyph
                {
                    baseIdx = baseIdx,
                    effectUv = new Vector4(dilate, range.packedColor, 0f, softness),
                    offsetX = meshOffX,
                    offsetY = meshOffY
                }, extent);
                return;
            }
        }

        private static void ParseParameter(ReadOnlySpan<char> param, ref float dilate, ref Color32 color,
            ref float ox, ref float oy, ref float soft)
        {
            var reader = new ParameterReader(param);
            var numIdx = 0;

            while (reader.Next(out var token))
            {
                if (token.IsEmpty) continue;

                if (ColorParsing.TryParse(token, out var c))
                {
                    color = c;
                }
                else if (ParameterReader.ParseFloat(token, out var f))
                {
                    switch (numIdx)
                    {
                        case 0: dilate = f; break;
                        case 1: ox = f; break;
                        case 2: oy = f; break;
                        case 3: soft = f; break;
                    }
                    numIdx++;
                }
            }
        }
    }
}
