using System;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>
    /// Controls variable font axis values per text range.
    /// </summary>
    /// <remarks>
    /// Parameter: positional axis values in order wght, wdth, ital, slnt, opsz.
    /// Use <c>~</c> to skip an axis. Each value supports absolute, percentage, or delta:
    /// <list type="bullet">
    /// <item><c>700</c> — absolute axis value</item>
    /// <item><c>150%</c> — percentage of font's default</item>
    /// <item><c>+200</c> — delta from font's default</item>
    /// </list>
    /// Examples: <c>700</c>, <c>~,80</c>, <c>700,~,~,-12</c>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 1)]
    [TypeDescription("Controls variable font axis values (weight, width, italic, slant, optical size).")]
    [ParameterField(0, "Weight", "unit:abs[1,1000]|%(25,225)|delta[-300,500]", "400")]
    [ParameterField(1, "Width", "unit:abs[50,200]|%(50,200)|delta[-50,100]", "100")]
    [ParameterField(2, "Italic", "unit:abs[0,1]|delta[0,1]", "0")]
    [ParameterField(3, "Slant", "unit:abs(-90,90)|delta(-90,90)", "0")]
    [ParameterField(4, "Optical Size", "unit:abs(1,144)|%(8,1200)|delta[-11,132]", "12")]
    public class VariationModifier : BaseModifier
    {
        internal const int AxisCount = 5;

        /// <summary>Axis tags in positional order.</summary>
        internal static readonly uint[] axisTags =
        {
            0x77676874, 0x77647468, 0x6974616C, 0x736C6E74, 0x6F70737A,
        };

        /// <summary>Bitmask flags for each axis position.</summary>
        [Flags]
        internal enum AxisMask : byte
        {
            None = 0,
            Wght = 1 << 0,
            Wdth = 1 << 1,
            Ital = 1 << 2,
            Slnt = 1 << 3,
            Opsz = 1 << 4,
        }

        /// <summary>How an axis value is specified.</summary>
        internal enum ValueMode : byte
        {
            Absolute,
            Percent,
            Delta,
        }

        /// <summary>Single axis value with its mode.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct AxisValue
        {
            public float value;
            public ValueMode mode;
        }

        /// <summary>
        /// A unique combination of axis overrides produced by one &lt;var=...&gt; tag.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct VariationConfig
        {
            public AxisValue wght;
            public AxisValue wdth;
            public AxisValue ital;
            public AxisValue slnt;
            public AxisValue opsz;
            public AxisMask mask;

            public AxisValue this[int i]
            {
                get
                {
                    switch (i)
                    {
                        case 0: return wght;
                        case 1: return wdth;
                        case 2: return ital;
                        case 3: return slnt;
                        case 4: return opsz;
                        default: return default;
                    }
                }
            }
        }

        private PooledArrayAttribute<byte> attribute;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.Variation);
            buffers.variationConfigs.FakeClear();
        }

        protected override void OnDisable() { }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.Variation);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;

            if (!TryParse(parameter, out var config)) return;

            if (config.mask == AxisMask.None) return;

            var configIndex = FindOrAddConfig(ref config);
            if (configIndex < 0) return;

            var encoded = (byte)(configIndex + 1);
            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            var buf = attribute.buffer.data;

            for (var i = start; i < clampedEnd; i++)
                buf[i] = encoded;
        }

        private int FindOrAddConfig(ref VariationConfig config)
        {
            ref var configs = ref buffers.variationConfigs;
            for (var i = 0; i < configs.count; i++)
            {
                if (ConfigsEqual(ref configs.data[i], ref config))
                    return i;
            }

            if (configs.count >= 255)
                return -1;

            configs.Add(config);
            return configs.count - 1;
        }

        private static bool ConfigsEqual(ref VariationConfig a, ref VariationConfig b)
        {
            if (a.mask != b.mask) return false;

            for (var i = 0; i < AxisCount; i++)
            {
                var bit = (AxisMask)(1 << i);
                if ((a.mask & bit) == 0) continue;

                var av = a[i];
                var bv = b[i];
                if (av.mode != bv.mode || Math.Abs(av.value - bv.value) > 0.001f)
                    return false;
            }

            return true;
        }

        internal static bool TryParse(ReadOnlySpan<char> param, out VariationConfig config)
        {
            config = default;

            var reader = new ParameterReader(param);
            var axisIndex = 0;

            while (axisIndex < AxisCount)
            {
                if (!reader.NextUnitFloat(out var value, out var unit))
                {
                    if (reader.IsEmpty) break;
                    axisIndex++;
                    continue;
                }

                var mode = unit switch
                {
                    ParameterReader.UnitKind.Percent => ValueMode.Percent,
                    ParameterReader.UnitKind.Delta => ValueMode.Delta,
                    _ => ValueMode.Absolute
                };

                config.mask |= (AxisMask)(1 << axisIndex);
                SetAxis(ref config, axisIndex, new AxisValue { value = value, mode = mode });
                axisIndex++;
            }

            return config.mask != AxisMask.None;
        }

        private static void SetAxis(ref VariationConfig config, int index, AxisValue value)
        {
            switch (index)
            {
                case 0: config.wght = value; break;
                case 1: config.wdth = value; break;
                case 2: config.ital = value; break;
                case 3: config.slnt = value; break;
                case 4: config.opsz = value; break;
            }
        }

        /// <summary>
        /// Resolves a VariationConfig to absolute axis values using the font's axis defaults.
        /// Returns array of resolved values for each axis in the font, plus fills hbVariations and ftCoords.
        /// </summary>
        internal static float[] ResolveAxes(
            in VariationConfig config,
            HB.hb_ot_var_axis_info_t[] fontAxes,
            out HB.hb_variation_t[] hbVariations,
            out int[] ftCoords)
        {
            int axisCount = fontAxes.Length;
            var resolved = new float[axisCount];
            int hbCount = 0;

            for (int i = 0; i < axisCount; i++)
                resolved[i] = fontAxes[i].defaultValue;

            for (int ci = 0; ci < AxisCount; ci++)
            {
                var bit = (AxisMask)(1 << ci);
                if ((config.mask & bit) == 0) continue;

                var tag = axisTags[ci];
                for (int fi = 0; fi < axisCount; fi++)
                {
                    if (fontAxes[fi].tag != tag) continue;

                    var av = config[ci];
                    float value = av.mode switch
                    {
                        ValueMode.Absolute => av.value,
                        ValueMode.Percent => fontAxes[fi].defaultValue * av.value / 100f,
                        ValueMode.Delta => fontAxes[fi].defaultValue + av.value,
                        _ => fontAxes[fi].defaultValue
                    };

                    value = Math.Max(fontAxes[fi].minValue, Math.Min(fontAxes[fi].maxValue, value));
                    resolved[fi] = value;
                    hbCount++;
                    break;
                }
            }

            hbVariations = new HB.hb_variation_t[hbCount];
            int hbIdx = 0;
            for (int fi = 0; fi < axisCount; fi++)
            {
                if (Math.Abs(resolved[fi] - fontAxes[fi].defaultValue) > 0.001f)
                {
                    hbVariations[hbIdx++] = new HB.hb_variation_t
                    {
                        tag = fontAxes[fi].tag,
                        value = resolved[fi]
                    };
                }
            }
            if (hbIdx < hbCount)
                Array.Resize(ref hbVariations, hbIdx);

            ftCoords = new int[axisCount];
            for (int i = 0; i < axisCount; i++)
                ftCoords[i] = (int)(resolved[i] * 65536f);

            return resolved;
        }
    }
}
