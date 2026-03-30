using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies a sine-wave wobble animation to text by oscillating glyph vertices vertically.
    /// </summary>
    /// <remarks>
    /// Each glyph moves up and down based on <c>sin(time * speed + cluster * spread) * amplitude</c>.
    /// The modifier continuously triggers mesh rebuilds while active.
    /// </remarks>
    [Serializable]
    [TypeGroup("Animation", 6)]
    [TypeDescription("Animates text with a sine-wave wobble effect.")]
    [ParameterField(0, "Amplitude", "float", "3")]
    [ParameterField(1, "Speed", "float", "3")]
    [ParameterField(2, "Spread", "float", "0.5")]
    public class WobbleAnimationModifier : BaseModifier
    {
        private struct WobbleParams
        {
            public float amplitude;
            public float speed;
            public float spread;
        }

        private PooledArrayAttribute<byte> attribute;
        private readonly PooledList<WobbleParams> paramSets = new();
        private float cachedTime;
        private bool hasAnyWobble;

        public override void PrepareForParallel()
        {
            cachedTime = Time.time;
        }

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, "wobble");
            paramSets.Clear();
            hasAnyWobble = false;

            uniText.MeshGenerator.onGlyph += OnGlyph;
            UniTextBase.AfterProcess += OnAfterProcess;
        }

        protected override void OnDisable()
        {
            uniText.MeshGenerator.onGlyph -= OnGlyph;
            UniTextBase.AfterProcess -= OnAfterProcess;
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData("wobble");
            attribute = null;
            paramSets.Return();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            ParseParameters(parameter, out var amp, out var spd, out var spr);

            var index = paramSets.Count;
            paramSets.Add(new WobbleParams { amplitude = amp, speed = spd, spread = spr });

            var paramIndex = (byte)(index + 1);
            var cpCount = buffers.codepoints.count;
            var actualEnd = Math.Min(end, cpCount);
            var buffer = attribute.buffer.data;
            for (var i = start; i < actualEnd; i++)
                buffer[i] = paramIndex;

            hasAnyWobble = true;
        }

        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;
            var cluster = gen.currentCluster;
            var paramIndex = attribute.buffer.data[cluster];
            if (paramIndex == 0) return;

            ref readonly var p = ref paramSets[paramIndex - 1];
            var offset = Mathf.Sin(cachedTime * p.speed + cluster * p.spread) * p.amplitude;

            var baseIdx = gen.vertexCount - 4;
            var verts = gen.Vertices;
            verts[baseIdx].y += offset;
            verts[baseIdx + 1].y += offset;
            verts[baseIdx + 2].y += offset;
            verts[baseIdx + 3].y += offset;
        }

        private void OnAfterProcess()
        {
            if (hasAnyWobble)
                uniText.SetDirty(UniTextBase.DirtyFlags.Color);
        }

        private static void ParseParameters(string parameter, out float amp, out float spd, out float spr)
        {
            amp = 3f;
            spd = 3f;
            spr = 0.5f;

            if (string.IsNullOrEmpty(parameter)) return;

            var reader = new ParameterReader(parameter);

            if (reader.NextFloat(out var a)) amp = a;
            if (reader.NextFloat(out var s)) spd = s;
            if (reader.NextFloat(out var p)) spr = p;
        }
    }
}
