using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies color to text ranges using hex codes or named colors.
    /// </summary>
    /// <remarks>
    /// Parameter: a color value in hex or by name.
    ///
    /// Supported formats:
    /// - Hex: #RGB, #RRGGBB, #RRGGBBAA
    /// - Named colors: white, black, red, green, blue, yellow, cyan, magenta, orange, purple, gray, lime, brown, pink, navy, teal, olive, maroon, silver, gold
    ///
    /// The alpha channel from the color parameter is preserved. The base alpha is inherited from the component's color.
    /// </remarks>
    /// <seealso cref="IParseRule"/>
    [Serializable]
    [TypeGroup("Appearance", 2)]
    [TypeDescription("Changes the color of the text.")]
    [ParameterField(0, "Color", "color")]
    public class ColorModifier : GlyphModifier<uint>
    {
        protected override string AttributeKey => AttributeKeys.Color;

        protected override Action GetOnGlyphCallback()
        {
            return OnGlyph;
        }

        protected override void DoApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            if (!ColorParsing.TryParse(parameter, out var color))
                return;

            var cpCount = buffers.codepoints.count;
            var packed = PackColor(color);
            var buffer = attribute.buffer.data;
            buffer.SetValueRange(start, Math.Min(end, cpCount), packed);
        }


        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;

            if (gen.font.IsColor) return;

            var buffer = attribute.buffer.data;
            var cluster = gen.currentCluster;
            var packed = buffer.GetValueOrDefault(cluster);
            if (packed == 0)
                return;

            var color = UnpackColor(packed);
            color.a = gen.defaultColor.a;
            var baseIdx = gen.vertexCount - 4;
            var colors = gen.Colors;

            colors[baseIdx] = color;
            colors[baseIdx + 1] = color;
            colors[baseIdx + 2] = color;
            colors[baseIdx + 3] = color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PackColor(Color32 c)
        {
            var a = c.a == 0 ? (byte)1 : c.a;
            return ((uint)a << 24) | ((uint)c.r << 16) | ((uint)c.g << 8) | c.b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Color32 UnpackColor(uint packed)
        {
            return new Color32(
                (byte)((packed >> 16) & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)(packed & 0xFF),
                (byte)((packed >> 24) & 0xFF)
            );
        }

        /// <summary>
        /// Tries to get the custom color for a cluster from the specified buffers.
        /// </summary>
        /// <param name="buffers">The UniText buffers containing color attribute data.</param>
        /// <param name="cluster">The cluster index to look up.</param>
        /// <param name="color">The color if found.</param>
        /// <returns>True if a custom color exists for the cluster; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetColor(UniTextBuffers buffers, int cluster, out Color32 color)
        {
            var attr = buffers?.GetAttributeData<PooledArrayAttribute<uint>>(AttributeKeys.Color);
            if (attr == null)
            {
                color = default;
                return false;
            }

            var buffer = attr.buffer.data;
            if (buffer == null || (uint)cluster >= (uint)buffer.Length)
            {
                color = default;
                return false;
            }

            var packed = buffer[cluster];
            if (packed == 0)
            {
                color = default;
                return false;
            }

            color = UnpackColor(packed);
            return true;
        }

    }

}
