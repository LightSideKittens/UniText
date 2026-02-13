using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies color to text ranges using hex codes or named colors.
    /// </summary>
    /// <remarks>
    /// Usage: <c>&lt;color=#FF0000&gt;red text&lt;/color&gt;</c> or <c>&lt;color=red&gt;red text&lt;/color&gt;</c>
    ///
    /// Supported formats:
    /// - Hex: #RGB, #RRGGBB, #RRGGBBAA
    /// - Named colors: white, black, red, green, blue, yellow, cyan, magenta, orange, purple, gray, lime, brown, pink, navy, teal, olive, maroon, silver, gold
    ///
    /// The alpha channel from the color parameter is preserved. The base alpha is inherited from the component's color.
    /// </remarks>
    /// <seealso cref="ColorParseRule"/>
    [Serializable]
    [TypeGroup("Appearance", 2)]
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

            if (!TryParseColor(parameter, out var color))
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

        private static bool TryParseColor(string value, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            if (string.IsNullOrEmpty(value))
                return false;
            if (value[0] == '#')
                return TryParseHexColor(value, out color);
            return TryParseNamedColor(value, out color);
        }

        private static bool TryParseHexColor(string hex, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            var len = hex.Length - 1;

            if (len == 3)
            {
                var r = ParseHexDigit(hex[1]);
                var g = ParseHexDigit(hex[2]);
                var b = ParseHexDigit(hex[3]);
                color = new Color32((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), 255);
                return true;
            }

            if (len == 6)
            {
                color = new Color32(ParseHexByte(hex[1], hex[2]), ParseHexByte(hex[3], hex[4]),
                    ParseHexByte(hex[5], hex[6]), 255);
                return true;
            }

            if (len == 8)
            {
                color = new Color32(ParseHexByte(hex[1], hex[2]), ParseHexByte(hex[3], hex[4]),
                    ParseHexByte(hex[5], hex[6]), ParseHexByte(hex[7], hex[8]));
                return true;
            }

            return false;
        }

        private static byte ParseHexDigit(char c)
        {
            if (c >= '0' && c <= '9') return (byte)(c - '0');
            if (c >= 'a' && c <= 'f') return (byte)(c - 'a' + 10);
            if (c >= 'A' && c <= 'F') return (byte)(c - 'A' + 10);
            return 0;
        }

        private static byte ParseHexByte(char high, char low)
        {
            return (byte)(ParseHexDigit(high) * 16 + ParseHexDigit(low));
        }

        private static readonly Dictionary<string, Color32> namedColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = new Color32(255, 255, 255, 255),
            ["black"] = new Color32(0, 0, 0, 255),
            ["red"] = new Color32(255, 0, 0, 255),
            ["green"] = new Color32(0, 128, 0, 255),
            ["blue"] = new Color32(0, 0, 255, 255),
            ["yellow"] = new Color32(255, 255, 0, 255),
            ["cyan"] = new Color32(0, 255, 255, 255),
            ["magenta"] = new Color32(255, 0, 255, 255),
            ["orange"] = new Color32(255, 165, 0, 255),
            ["purple"] = new Color32(128, 0, 128, 255),
            ["gray"] = new Color32(128, 128, 128, 255),
            ["grey"] = new Color32(128, 128, 128, 255),
            ["lime"] = new Color32(0, 255, 0, 255),
            ["brown"] = new Color32(165, 42, 42, 255),
            ["pink"] = new Color32(255, 192, 203, 255),
            ["navy"] = new Color32(0, 0, 128, 255),
            ["teal"] = new Color32(0, 128, 128, 255),
            ["olive"] = new Color32(128, 128, 0, 255),
            ["maroon"] = new Color32(128, 0, 0, 255),
            ["silver"] = new Color32(192, 192, 192, 255),
            ["gold"] = new Color32(255, 215, 0, 255)
        };

        private static bool TryParseNamedColor(string name, out Color32 color)
        {
            return namedColors.TryGetValue(name, out color);
        }
    }

}
