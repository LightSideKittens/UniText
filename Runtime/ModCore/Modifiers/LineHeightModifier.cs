using System;

namespace LightSide
{
    /// <summary>
    /// Adjusts line height/spacing for text ranges.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <list type="bullet">
    /// <item><c>&lt;line-height=1.5&gt;text&lt;/line-height&gt;</c> — 150% of default line height</item>
    /// <item><c>&lt;line-height=40&gt;text&lt;/line-height&gt;</c> — absolute 40 pixels</item>
    /// <item><c>&lt;line-spacing=10&gt;text&lt;/line-spacing&gt;</c> — add 10 pixels to default</item>
    /// <item><c>&lt;line-spacing=-5&gt;text&lt;/line-spacing&gt;</c> — reduce by 5 pixels</item>
    /// </list>
    /// </remarks>
    /// <seealso cref="LineHeightParseRule"/>
    /// <seealso cref="LineSpacingParseRule"/>
    [Serializable]
    [TypeGroup("Layout", 3)]
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

            var isSpacing = false;
            var paramSpan = parameter.AsSpan();

            if (paramSpan.Length > 2 && paramSpan[0] == 's' && paramSpan[1] == ':')
            {
                isSpacing = true;
                paramSpan = paramSpan.Slice(2);
            }

            if (!TryParseValue(paramSpan, out var value, out var isAbsolute))
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

            if (param[param.Length - 1] == '%')
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
