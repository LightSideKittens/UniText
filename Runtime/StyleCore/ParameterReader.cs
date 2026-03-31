using System;
using System.Globalization;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Zero-allocation tokenizer for comma-separated modifier parameters.
    /// Eliminates manual IndexOf/Slice/Trim boilerplate in modifier parsers.
    /// </summary>
    /// <example>
    /// <code>
    /// var reader = new ParameterReader(parameter);
    /// reader.NextString(out var name);
    /// reader.NextFloat(out var angle);
    /// reader.NextColor(out var color);
    /// </code>
    /// </example>
    ref struct ParameterReader
    {
        private ReadOnlySpan<char> remaining;

        public ParameterReader(ReadOnlySpan<char> parameter) => remaining = parameter;

        public ParameterReader(string parameter) => remaining = parameter.AsSpan();

        /// <summary>True if there are no more tokens to read.</summary>
        public bool IsEmpty => remaining.IsEmpty;

        /// <summary>Reads the next comma-delimited token (trimmed). Returns false if no tokens remain.</summary>
        public bool Next(out ReadOnlySpan<char> token)
        {
            if (remaining.IsEmpty)
            {
                token = default;
                return false;
            }

            var comma = remaining.IndexOf(',');
            if (comma < 0)
            {
                token = remaining.Trim();
                remaining = default;
            }
            else
            {
                token = remaining.Slice(0, comma).Trim();
                remaining = remaining.Slice(comma + 1);
            }

            return true;
        }

        /// <summary>Parses a span as float using InvariantCulture. For pre-extracted tokens where Next() was already called.</summary>
        public static bool ParseFloat(ReadOnlySpan<char> s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>Reads the next token as a float. Returns false if missing or unparseable.</summary>
        public bool NextFloat(out float value, float defaultValue = 0f)
        {
            value = defaultValue;
            return Next(out var token) && !token.IsEmpty && ParseFloat(token, out value);
        }

        /// <summary>Reads the next token as an int. Returns false if missing or unparseable.</summary>
        public bool NextInt(out int value, int defaultValue = 0)
        {
            value = defaultValue;
            return Next(out var token) && !token.IsEmpty &&
                   int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Reads the next token as a Color32 via <see cref="ColorParsing"/>. Returns false if missing or unparseable.</summary>
        public bool NextColor(out Color32 value)
        {
            value = default;
            return Next(out var token) && !token.IsEmpty && ColorParsing.TryParse(token, out value);
        }
        
        /// <summary>Unit type for values parsed by <see cref="NextUnitFloat"/>.</summary>
        public enum UnitKind : byte { Absolute, Percent, Em, Delta }

        /// <summary>
        /// Reads the next token as a float with optional unit suffix.
        /// Recognizes: <c>24</c> (absolute), <c>150%</c>, <c>0.5em</c>, <c>+10</c>/<c>-5</c> (delta).
        /// Always uses InvariantCulture.
        /// </summary>
        public bool NextUnitFloat(out float value, out UnitKind unit, float defaultValue = 0f)
        {
            value = defaultValue;
            unit = UnitKind.Absolute;

            if (!Next(out var token) || token.IsEmpty)
                return false;

            if (token[^1] == '%')
            {
                unit = UnitKind.Percent;
                return float.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            if (token.Length > 2 && (token[^1] == 'm' || token[^1] == 'M') && (token[^2] == 'e' || token[^2] == 'E'))
            {
                unit = UnitKind.Em;
                return float.TryParse(token[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            if (token[0] == '+' || (token[0] == '-' && token.Length > 1))
            {
                unit = UnitKind.Delta;
                return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Reads the next token as a string (allocates). Returns false if missing or empty.</summary>
        public bool NextString(out string value)
        {
            if (Next(out var token) && !token.IsEmpty)
            {
                value = token.ToString();
                return true;
            }

            value = null;
            return false;
        }
    }
}
