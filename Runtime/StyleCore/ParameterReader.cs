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

        /// <summary>Reads the next token as a float. Returns false if missing or unparseable.</summary>
        public bool NextFloat(out float value, float defaultValue = 0f)
        {
            value = defaultValue;
            return Next(out var token) && !token.IsEmpty &&
                   float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
