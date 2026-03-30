using System;

namespace LightSide
{
    /// <summary>
    /// Adjusts line height/spacing for text ranges.
    /// </summary>
    /// <remarks>
    /// Parameter: line height or spacing value.
    /// <list type="bullet">
    /// <item><c>1.5</c> — 150% of default line height (multiplier)</item>
    /// <item><c>40</c> — absolute 40 pixels</item>
    /// <item><c>+10</c> — add 10 pixels to default (delta)</item>
    /// <item><c>-5</c> — reduce by 5 pixels (delta)</item>
    /// </list>
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 3)]
    [TypeDescription("Adjusts the vertical spacing between lines.")]
    [ParameterField(0, "Mode", "enum:h|s", "h")]
    [ParameterField(1, "Value", "unit:px|%|delta", "24")]
    public class LineHeightModifier : BaseModifier
    {
        private struct Range
        {
            public int start;
            public int end;
            public float value;
            public bool isAbsolute;
            public bool isSpacing;
        }

        private PooledList<Range> ranges;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<Range>(4);
            ranges.FakeClear();
            uniText.TextProcessor.OnCalculateLineHeight += OnCalculateLineHeight;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.OnCalculateLineHeight -= OnCalculateLineHeight;
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            var reader = new ParameterReader(parameter);
            if (!reader.Next(out var first))
                return;

            var isSpacing = false;
            ReadOnlySpan<char> valueSpan;

            if (reader.Next(out var second))
            {
                if (first.Length == 1 && (first[0] == 's' || first[0] == 'S'))
                    isSpacing = true;
                valueSpan = second;
            }
            else
            {
                valueSpan = first;
            }

            if (!TryParseValue(valueSpan, out var value, out var isAbsolute))
                return;

            ranges.Add(new Range
            {
                start = start,
                end = end,
                value = value,
                isAbsolute = isAbsolute,
                isSpacing = isSpacing
            });
        }

        private static bool TryParseValue(ReadOnlySpan<char> param, out float value, out bool isAbsolute)
        {
            value = 0f;
            isAbsolute = false;

            if (param.IsEmpty)
                return false;

            if (param[^1] == '%')
            {
                if (float.TryParse(param.Slice(0, param.Length - 1), out var percent))
                {
                    value = percent / 100f;
                    isAbsolute = false;
                    return true;
                }
                return false;
            }

            if (param.Length > 2 && param.EndsWith("em".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(param.Slice(0, param.Length - 2), out var emValue))
                {
                    value = emValue;
                    isAbsolute = false;
                    return true;
                }
                return false;
            }

            if (float.TryParse(param, out var numValue))
            {
                value = numValue;
                isAbsolute = true;
                return true;
            }

            return false;
        }

        private void OnCalculateLineHeight(int lineIndex, int lineStartCluster, int lineEndCluster, ref float lineAdvance)
        {
            if (ranges == null || ranges.Count == 0)
                return;

            var defaultAdvance = lineAdvance;
            var maxMultiplier = 1f;
            var hasSpacing = false;
            var spacingValue = 0f;
            var hasAbsoluteHeight = false;
            var absoluteHeight = 0f;

            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];

                if (range.end <= lineStartCluster || range.start >= lineEndCluster)
                    continue;

                if (range.isSpacing)
                {
                    float spacing;
                    if (range.isAbsolute)
                        spacing = range.value;
                    else
                        spacing = defaultAdvance * (range.value - 1f);

                    if (!hasSpacing)
                    {
                        hasSpacing = true;
                        spacingValue = spacing;
                    }
                    else
                    {
                        if (Math.Abs(spacing) > Math.Abs(spacingValue))
                            spacingValue = spacing;
                    }
                }
                else
                {
                    if (range.isAbsolute)
                    {
                        hasAbsoluteHeight = true;
                        absoluteHeight = Math.Max(absoluteHeight, range.value);
                    }
                    else
                    {
                        maxMultiplier = Math.Max(maxMultiplier, range.value);
                    }
                }
            }

            if (hasAbsoluteHeight)
                lineAdvance = absoluteHeight + spacingValue;
            else
                lineAdvance = defaultAdvance * maxMultiplier + spacingValue;
        }
    }

}
