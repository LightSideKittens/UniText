using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared color parsing utilities for modifiers that accept color parameters.
    /// Supports hex (#RGB, #RRGGBB, #RRGGBBAA) and named colors.
    /// </summary>
    public static class ColorParsing
    {
        public static bool TryParse(string value, out Color32 color)
        {
            return TryParse(value.AsSpan(), out color);
        }

        public static bool TryParse(ReadOnlySpan<char> value, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            if (value.IsEmpty)
                return false;
            if (value[0] == '#')
                return TryParseHex(value, out color);
            return TryParseNamed(value, out color);
        }

        public static bool TryParseHex(ReadOnlySpan<char> hex, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            var len = hex.Length - 1;

            if (len == 3)
            {
                color = new Color32(
                    (byte)(ParseHexDigit(hex[1]) * 17),
                    (byte)(ParseHexDigit(hex[2]) * 17),
                    (byte)(ParseHexDigit(hex[3]) * 17), 255);
                return true;
            }

            if (len == 6)
            {
                color = new Color32(
                    ParseHexByte(hex[1], hex[2]),
                    ParseHexByte(hex[3], hex[4]),
                    ParseHexByte(hex[5], hex[6]), 255);
                return true;
            }

            if (len == 8)
            {
                color = new Color32(
                    ParseHexByte(hex[1], hex[2]),
                    ParseHexByte(hex[3], hex[4]),
                    ParseHexByte(hex[5], hex[6]),
                    ParseHexByte(hex[7], hex[8]));
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ParseHexDigit(char c)
        {
            if (c >= '0' && c <= '9') return (byte)(c - '0');
            if (c >= 'a' && c <= 'f') return (byte)(c - 'a' + 10);
            if (c >= 'A' && c <= 'F') return (byte)(c - 'A' + 10);
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        private static bool TryParseNamed(ReadOnlySpan<char> name, out Color32 color)
        {
            if (name.Length <= 16)
            {
                Span<char> lower = stackalloc char[name.Length];
                for (var i = 0; i < name.Length; i++)
                    lower[i] = char.ToLowerInvariant(name[i]);
                var key = new string(lower);
                return namedColors.TryGetValue(key, out color);
            }

            color = default;
            return false;
        }
    }
}
