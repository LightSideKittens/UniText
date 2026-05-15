using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies bold styling to text by modifying glyph UV coordinates for shader-based rendering
    /// and adjusting glyph advances to compensate for SDF dilation.
    /// </summary>
    /// <remarks>
    /// Usage: <c>&lt;b&gt;bold text&lt;/b&gt;</c>
    ///
    /// The bold effect is achieved through the shader by manipulating the signed distance field.
    /// Advance correction is proportional to the shader's _WeightBold and _WeightNormal properties,
    /// scaled by FreeType's emboldening ratio (em/24). Works with SDF fonts only.
    /// For bitmap fonts, _WeightBold/_WeightNormal are absent (GetFloat returns 0),
    /// resulting in weightDelta=0 and no advance correction — which is correct since
    /// SDF dilation doesn't apply to bitmap rendering.
    /// </remarks>
    /// <seealso cref="BoldParseRule"/>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    public class BoldModifier : BaseModifier
    {
        /// <summary>
        /// Base advance ratio per unit of bold weight.
        /// FreeType's FT_GlyphSlot_Embolden uses em/24 at weight delta 1.0.
        /// </summary>
        private const float BaseAdvanceRatio = 1f / 24f;

        private static readonly int WeightBoldId = Shader.PropertyToID("_WeightBold");
        private static readonly int WeightNormalId = Shader.PropertyToID("_WeightNormal");

        private PooledArrayAttribute<byte> attribute;
        private Action onGlyphCallback;

        public override void PrepareForParallel()
        {
            uniText.FontProvider?.Appearance?.CachePropertyDelta(WeightBoldId, WeightNormalId);
        }

        protected override void OnEnable()
        {
            attribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<byte>>(AttributeKeys.Bold);
            attribute.EnsureCountAndClear(buffers.codepoints.count);

            onGlyphCallback ??= OnGlyph;
            uniText.MeshGenerator.OnGlyph += onGlyphCallback;
            uniText.TextProcessor.Shaped += OnShaped;
        }

        protected override void OnDisable()
        {
            uniText.MeshGenerator.OnGlyph -= onGlyphCallback;
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
            var cpCount = buffers.codepoints.count;
            attribute.buffer.data.SetFlagRange(start, Math.Min(end, cpCount));
        }

        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;
            var cluster = gen.currentCluster;
            if (!attribute.buffer.data.HasFlag(cluster))
                return;

            var negXScale = -gen.xScale;
            var baseIdx = gen.vertexCount - 4;
            var uvs = gen.Uvs0;

            uvs[baseIdx].w = negXScale;
            uvs[baseIdx + 1].w = negXScale;
            uvs[baseIdx + 2].w = negXScale;
            uvs[baseIdx + 3].w = negXScale;
        }

        private void OnShaped()
        {
            if (attribute == null)
                return;

            var buffer = attribute.buffer.data;
            if (buffer == null || !buffer.HasAnyFlags())
                return;

            var fontProvider = uniText.FontProvider;
            if (fontProvider == null)
                return;

            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var bufLen = buffer.Length;
            var fontSize = buf.shapingFontSize > 0 ? buf.shapingFontSize : uniText.FontSize;
            var baseAdvance = fontSize * BaseAdvanceRatio;

            var cachedFontId = -1;
            var boldAdvance = 0f;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];

                if (run.fontId != cachedFontId)
                {
                    cachedFontId = run.fontId;
                    var weightDelta = GetWeightDelta(fontProvider, cachedFontId);
                    boldAdvance = weightDelta > 0f ? baseAdvance * weightDelta : 0f;
                }

                if (boldAdvance <= 0f)
                    continue;

                var glyphEnd = run.glyphStart + run.glyphCount;
                float width = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var cluster = glyphs[g].cluster;

                    if ((uint)cluster < (uint)bufLen && buffer[cluster] != 0)
                        glyphs[g].advanceX += boldAdvance;

                    width += glyphs[g].advanceX;
                }
                
                run.width = width;
            }
        }

        private static float GetWeightDelta(UniTextFontProvider fontProvider, int fontId)
        {
            var appearance = fontProvider.Appearance;
            if (appearance is null)
                return 0f;

            var materials = fontProvider.GetMaterials(fontId);
            if (materials == null || materials.Length == 0 || materials[0] is null)
                return 0f;

            return appearance.GetCachedPropertyDelta(RuntimeHelpers.GetHashCode(materials[0]));
        }
    }
}
