using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies gradient coloring to text ranges using named gradients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter format: <c>name[,shape][,angle]</c>
    /// <list type="bullet">
    /// <item><c>rainbow</c> — linear gradient, angle 0</item>
    /// <item><c>rainbow,linear,45</c> — linear at 45°</item>
    /// <item><c>rainbow,radial</c> — radial gradient from center</item>
    /// <item><c>rainbow,angular</c> — angular (conic) sweep from top</item>
    /// <item><c>rainbow,angular,90</c> — angular sweep rotated 90°</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Shape</b> controls the gradient form: <c>linear</c> (projection onto an axis), <c>radial</c> (distance from center), or <c>angular</c> (conic sweep).
    /// </para>
    /// <para>
    /// Gradients are defined in <see cref="UniTextGradients"/> ScriptableObject referenced by <see cref="UniTextSettings"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextGradients"/>
    /// <seealso cref="IParseRule"/>
    [Serializable]
    [TypeGroup("Appearance", 2)]
    [TypeDescription("Applies a gradient color effect to the text.")]
    [ParameterField(0, "Name", "enum:@gradients")]
    [ParameterField(1, "Shape", "enum:linear|radial|angular", "linear")]
    [ParameterField(2, "Angle", "float(0,360)", "0")]
    public sealed class GradientModifier : BaseModifier
    {
        private enum GradientShape : byte
        {
            Linear,
            Radial,
            Angular
        }

        private struct GradientDef
        {
            public int startCluster;
            public int endCluster;
            public Gradient gradient;
            public float angleDeg;
            public float minProj;
            public float maxProj;
            public float cosAngle;
            public float sinAngle;
            public float centerX;
            public float centerY;
            public float radius;
            public GradientShape shape;
        }
        
        private PooledArrayAttribute<byte> attribute;
        private readonly PooledList<GradientDef> gradientDefs = new();
        private readonly PooledList<Rect> boundsCache = new();

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.Gradient);

            gradientDefs.Clear();
            
            uniText.TextProcessor.LayoutComplete += OnLayoutComplete;
            uniText.MeshGenerator.onGlyph += OnGlyph;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.LayoutComplete -= OnLayoutComplete;
            uniText.MeshGenerator.onGlyph -= OnGlyph;
        }

        protected override void OnDestroy()
        {
            buffers.ReleaseAttributeData(AttributeKeys.Gradient);
            attribute = null;
            gradientDefs.Return();
            boundsCache.Return();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            if (!TryParse(parameter, out var gradientName, out var angle, out var shape))
                return;

            var gradientsAsset = UniTextSettings.Gradients;
            if (gradientsAsset == null)
            {
                Debug.LogWarning("[GradientModifier] UniTextSettings.Gradients is not assigned");
                return;
            }

            if (!gradientsAsset.TryGetGradient(gradientName, out var gradient))
            {
                Debug.LogWarning($"[GradientModifier] Gradient '{gradientName}' not found");
                return;
            }

            var buffer = attribute.buffer.data;

            var index = gradientDefs.Count;
            gradientDefs.Add(new GradientDef
            {
                startCluster = start,
                endCluster = end,
                gradient = gradient,
                angleDeg = angle,
                shape = shape
            });

            var gradientIndex = (byte)(index + 1);
            var cpCount = buffers.codepoints.count;
            var actualEnd = Math.Min(end, cpCount);

            for (var i = start; i < actualEnd; i++)
                buffer[i] = gradientIndex;
        }

        private void OnLayoutComplete()
        {
            if (gradientDefs.Count == 0) return;

            for (var i = 0; i < gradientDefs.Count; i++)
            {
                ref var g = ref gradientDefs[i];

                uniText.GetRangeBounds(g.startCluster, g.endCluster, boundsCache);
                if (boundsCache.Count == 0) continue;

                if (g.shape == GradientShape.Linear)
                {
                    var rad = g.angleDeg * Mathf.Deg2Rad;
                    g.cosAngle = Mathf.Cos(rad);
                    g.sinAngle = Mathf.Sin(rad);

                    g.minProj = float.MaxValue;
                    g.maxProj = float.MinValue;

                    for (var j = 0; j < boundsCache.Count; j++)
                    {
                        ref readonly var rect = ref boundsCache[j];
                        UpdateProj(rect.xMin, rect.yMin, ref g);
                        UpdateProj(rect.xMax, rect.yMin, ref g);
                        UpdateProj(rect.xMin, rect.yMax, ref g);
                        UpdateProj(rect.xMax, rect.yMax, ref g);
                    }
                }
                else
                {
                    var minX = float.MaxValue;
                    var maxX = float.MinValue;
                    var minY = float.MaxValue;
                    var maxY = float.MinValue;

                    for (var j = 0; j < boundsCache.Count; j++)
                    {
                        ref readonly var rect = ref boundsCache[j];
                        if (rect.xMin < minX) minX = rect.xMin;
                        if (rect.xMax > maxX) maxX = rect.xMax;
                        if (rect.yMin < minY) minY = rect.yMin;
                        if (rect.yMax > maxY) maxY = rect.yMax;
                    }

                    g.centerX = (minX + maxX) * 0.5f;
                    g.centerY = (minY + maxY) * 0.5f;

                    var dx = (maxX - minX) * 0.5f;
                    var dy = (maxY - minY) * 0.5f;
                    g.radius = Mathf.Sqrt(dx * dx + dy * dy);
                }
            }
        }

        private static void UpdateProj(float x, float y, ref GradientDef g)
        {
            var proj = x * g.cosAngle + y * g.sinAngle;
            if (proj < g.minProj) g.minProj = proj;
            if (proj > g.maxProj) g.maxProj = proj;
        }

        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen.font.IsColor) return;

            var buffer = attribute.buffer.data;
            var cluster = gen.currentCluster;
            
            var gradientIndex = buffer[cluster];
            if (gradientIndex == 0) return;

            ref readonly var g = ref gradientDefs[gradientIndex - 1];

            var baseIdx = gen.vertexCount - 4;
            var colors = gen.Colors;
            var alpha = gen.defaultColor.a;

            if (g.shape == GradientShape.Radial)
            {
                if (g.radius <= 0) return;

                var verts = gen.Vertices;
                var invRadius = 1f / g.radius;

                for (var i = 0; i < 4; i++)
                {
                    ref readonly var v = ref verts[baseIdx + i];
                    var dx = v.x - g.centerX;
                    var dy = v.y - g.centerY;
                    var t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * invRadius);

                    var color = g.gradient.Evaluate(t);
                    colors[baseIdx + i] = new Color32(
                        (byte)(color.r * 255),
                        (byte)(color.g * 255),
                        (byte)(color.b * 255),
                        alpha
                    );
                }
            }
            else if (g.shape == GradientShape.Angular)
            {
                var verts = gen.Vertices;
                var angleOffset = g.angleDeg * Mathf.Deg2Rad;
                var invTwoPi = 1f / (Mathf.PI * 2f);

                for (var i = 0; i < 4; i++)
                {
                    ref readonly var v = ref verts[baseIdx + i];
                    var dx = v.x - g.centerX;
                    var dy = v.y - g.centerY;
                    var a = Mathf.Atan2(dx, dy) + angleOffset;
                    var t = a * invTwoPi + 0.5f;
                    t -= Mathf.Floor(t);

                    var color = g.gradient.Evaluate(t);
                    colors[baseIdx + i] = new Color32(
                        (byte)(color.r * 255),
                        (byte)(color.g * 255),
                        (byte)(color.b * 255),
                        alpha
                    );
                }
            }
            else
            {
                var range = g.maxProj - g.minProj;
                if (range <= 0) return;

                var verts = gen.Vertices;

                for (var i = 0; i < 4; i++)
                {
                    ref readonly var v = ref verts[baseIdx + i];
                    var proj = v.x * g.cosAngle + v.y * g.sinAngle;
                    var t = Mathf.Clamp01((proj - g.minProj) / range);

                    var color = g.gradient.Evaluate(t);
                    colors[baseIdx + i] = new Color32(
                        (byte)(color.r * 255),
                        (byte)(color.g * 255),
                        (byte)(color.b * 255),
                        alpha
                    );
                }
            }
        }

        private static bool TryParse(ReadOnlySpan<char> param, out string name, out float angle,
            out GradientShape shape)
        {
            name = null;
            angle = 0f;
            shape = GradientShape.Linear;

            var reader = new ParameterReader(param);
            if (!reader.NextString(out name))
                return false;

            if (reader.Next(out var shapeToken) && !shapeToken.IsEmpty)
            {
                if (shapeToken.Equals("radial".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    shape = GradientShape.Radial;
                else if (shapeToken.Equals("angular".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    shape = GradientShape.Angular;
            }

            reader.NextFloat(out angle);

            return true;
        }
    }
}
