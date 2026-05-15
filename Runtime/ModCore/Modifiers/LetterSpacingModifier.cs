using System;

namespace LightSide
{
    /// <summary>
    /// Applies character spacing (tracking) adjustments to text ranges.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <list type="bullet">
    /// <item><c>&lt;cspace=10&gt;text&lt;/cspace&gt;</c> — add 10 pixels between characters</item>
    /// <item><c>&lt;cspace=-5&gt;text&lt;/cspace&gt;</c> — reduce spacing by 5 pixels</item>
    /// <item><c>&lt;cspace=0.5em&gt;text&lt;/cspace&gt;</c> — add 0.5 em (relative to font size)</item>
    /// </list>
    /// </remarks>
    /// <seealso cref="CSpaceParseRule"/>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    public class LetterSpacingModifier : BaseModifier
    {
        private PooledArrayAttribute<float> attribute;

        protected override void OnEnable()
        {
            attribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<float>>(AttributeKeys.LetterSpacing);
            var cpCount = buffers.codepoints.count;
            attribute.EnsureCountAndClear(cpCount);
            
            uniText.TextProcessor.Shaped += OnShaped;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped -= OnShaped;
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.LetterSpacing);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            if (!TryParseSpacing(parameter, out var spacing))
                return;

            var cpCount = buffers.codepoints.count;

            var buffer = attribute.buffer.data;
            var clampedEnd = Math.Min(end, cpCount);
            for (var i = start; i < clampedEnd; i++)
                buffer[i] = spacing;
        }

        private bool TryParseSpacing(string param, out float spacing)
        {
            spacing = 0f;
            if (string.IsNullOrEmpty(param))
                return false;

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;

            if (param.Length > 2 && param.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(param.AsSpan(0, param.Length - 2), out var emValue))
                {
                    spacing = emValue * baseSize;
                    return true;
                }
                return false;
            }

            if (float.TryParse(param, out var pxValue))
            {
                spacing = pxValue;
                return true;
            }

            return false;
        }

        private void OnShaped()
        {
            if (attribute == null)
                return;

            var buffer = attribute.buffer.data;
            if (buffer == null)
                return;

            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var bufLen = buffer.Length;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                float width = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var cluster = glyphs[g].cluster;

                    if ((uint)cluster < (uint)bufLen)
                    {
                        var spacing = buffer[cluster];
                        if (spacing != 0f)
                            glyphs[g].advanceX += spacing;
                    }

                    width += glyphs[g].advanceX;
                }

                run.width = width;
            }
        }
    }

}
