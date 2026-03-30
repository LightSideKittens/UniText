using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that render an additional effect pass behind the face (outline, shadow, glow).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each instance registers an <see cref="EffectPass"/> on the generator.
    /// The system iterates these passes to create per-modifier CanvasRenderers
    /// without any knowledge of what the effect does.
    /// </para>
    /// <para>
    /// Subclasses override <see cref="OnGlyphEffect"/> to record per-glyph effect data
    /// and <see cref="OnApply"/> to collect text ranges.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class EffectModifier : BaseModifier
    {
        /// <summary>Per-glyph data recorded during <see cref="OnGlyphEffect"/>.</summary>
        protected struct EffectGlyph
        {
            public int baseIdx;
            public Vector4 effectUv;
            public float offsetX, offsetY;
        }

        /// <summary>Recorded per-glyph effect data. Populated during OnGlyph, consumed by apply/revert.</summary>
        protected PooledBuffer<EffectGlyph> effectGlyphs;

        private Action applyCallback;
        private Action revertCallback;
        private Action onGlyphCallback;

        /// <summary>
        /// Called for each glyph during mesh generation.
        /// Subclass should check cluster ranges and call <see cref="RecordEffectGlyph"/> for matching glyphs.
        /// </summary>
        protected abstract void OnGlyphEffect();

        public override void PrepareForParallel()
        {
            effectGlyphs.FakeClear();
        }

        protected override void OnEnable()
        {
            effectGlyphs.FakeClear();
            onGlyphCallback ??= OnGlyph;
            applyCallback ??= ApplyToMesh;
            revertCallback ??= RevertFromMesh;
            uniText.MeshGenerator.onGlyph += onGlyphCallback;
            uniText.MeshGenerator.effectPasses.Add(new EffectPass
            {
                apply = applyCallback,
                revert = revertCallback,
                hasVertexShifts = HasVertexShifts()
            });
        }

        protected override void OnDisable()
        {
            uniText.MeshGenerator.onGlyph -= onGlyphCallback;
            var passes = uniText.MeshGenerator.effectPasses;
            for (var i = passes.Count - 1; i >= 0; i--)
            {
                if (passes[i].apply == applyCallback)
                {
                    passes.RemoveAt(i);
                    break;
                }
            }
        }

        protected override void OnDestroy()
        {
            effectGlyphs.Return();
            onGlyphCallback = null;
            applyCallback = null;
            revertCallback = null;
        }

        /// <summary>
        /// Returns true if this effect type can shift vertex positions (shadow with offset).
        /// </summary>
        protected virtual bool HasVertexShifts() => false;

        /// <summary>
        /// Records a glyph for this effect and reports required padding extent to the generator.
        /// </summary>
        protected void RecordEffectGlyph(EffectGlyph glyph, float extent)
        {
            effectGlyphs.Add(glyph);
            var gen = UniTextMeshGenerator.Current;
            if (extent > gen.currentMaxEffectExtent)
                gen.currentMaxEffectExtent = extent;
        }

        private void OnGlyph()
        {
            OnGlyphEffect();
        }

        private void ApplyToMesh()
        {
            var count = effectGlyphs.count;
            if (count == 0) return;

            var gen = uniText.MeshGenerator;
            gen.EnsureUvBuffer(2);
            var uvs2 = gen.Uvs2;
            var verts = gen.Vertices;
            var data = effectGlyphs.data;

            for (var i = 0; i < count; i++)
            {
                ref var eg = ref data[i];
                uvs2[eg.baseIdx] = eg.effectUv;
                uvs2[eg.baseIdx + 1] = eg.effectUv;
                uvs2[eg.baseIdx + 2] = eg.effectUv;
                uvs2[eg.baseIdx + 3] = eg.effectUv;

                if (eg.offsetX != 0f || eg.offsetY != 0f)
                {
                    verts[eg.baseIdx].x += eg.offsetX;
                    verts[eg.baseIdx].y += eg.offsetY;
                    verts[eg.baseIdx + 1].x += eg.offsetX;
                    verts[eg.baseIdx + 1].y += eg.offsetY;
                    verts[eg.baseIdx + 2].x += eg.offsetX;
                    verts[eg.baseIdx + 2].y += eg.offsetY;
                    verts[eg.baseIdx + 3].x += eg.offsetX;
                    verts[eg.baseIdx + 3].y += eg.offsetY;
                }
            }
        }

        private void RevertFromMesh()
        {
            var count = effectGlyphs.count;
            if (count == 0) return;

            var gen = uniText.MeshGenerator;
            var uvs2 = gen.Uvs2;
            var verts = gen.Vertices;
            var data = effectGlyphs.data;

            for (var i = 0; i < count; i++)
            {
                ref var eg = ref data[i];
                uvs2[eg.baseIdx] = default;
                uvs2[eg.baseIdx + 1] = default;
                uvs2[eg.baseIdx + 2] = default;
                uvs2[eg.baseIdx + 3] = default;

                if (eg.offsetX != 0f || eg.offsetY != 0f)
                {
                    verts[eg.baseIdx].x -= eg.offsetX;
                    verts[eg.baseIdx].y -= eg.offsetY;
                    verts[eg.baseIdx + 1].x -= eg.offsetX;
                    verts[eg.baseIdx + 1].y -= eg.offsetY;
                    verts[eg.baseIdx + 2].x -= eg.offsetX;
                    verts[eg.baseIdx + 2].y -= eg.offsetY;
                    verts[eg.baseIdx + 3].x -= eg.offsetX;
                    verts[eg.baseIdx + 3].y -= eg.offsetY;
                }
            }
        }
    }
}
