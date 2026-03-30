using System;

namespace LightSide
{
    /// <summary>
    /// Applies superscript or subscript formatting to text ranges.
    /// </summary>
    /// <remarks>
    /// Two-tier approach:
    /// <list type="bullet">
    /// <item>Native: activates OpenType 'sups'/'subs' feature via HarfBuzz (proper glyphs).</item>
    /// <item>Synthesis: scales down and shifts vertically using OS/2 metrics (fallback).</item>
    /// </list>
    ///
    /// Attribute sbyte encoding: 0 = unchanged, +1 = native super, -1 = native sub, +2 = synth super, -2 = synth sub.
    ///
    /// Create two styles to support both tags:
    /// <list type="bullet">
    /// <item>Style 1: ScriptPositionModifier + TagRule("sup") with defaultParameter = "super"</item>
    /// <item>Style 2: ScriptPositionModifier + TagRule("sub") with defaultParameter = "sub"</item>
    /// </list>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Applies superscript or subscript formatting.")]
    [ParameterField(0, "Mode", "enum:super|sub", "super")]
    public class ScriptPositionModifier : BaseModifier
    {
        private const sbyte NativeSuper = 1;
        private const sbyte NativeSub = -1;
        private const sbyte SynthSuper = 2;
        private const sbyte SynthSub = -2;

        private PooledArrayAttribute<sbyte> attribute;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.ScriptPosition);

            uniText.TextProcessor.Shaped += OnShaped;
            uniText.MeshGenerator.onGlyph += OnGlyph;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped -= OnShaped;
            uniText.MeshGenerator.onGlyph -= OnGlyph;
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.ScriptPosition);
            attribute = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            bool isSuper;
            if (string.IsNullOrEmpty(parameter) ||
                parameter.Equals("super", StringComparison.OrdinalIgnoreCase) ||
                parameter.Equals("sup", StringComparison.OrdinalIgnoreCase))
                isSuper = true;
            else if (parameter.Equals("sub", StringComparison.OrdinalIgnoreCase))
                isSuper = false;
            else
                return;

            var mainFont = uniText.PrimaryFont;
            var shaper = Shaper.Instance;

            bool fontHasFeature = isSuper
                ? shaper.HasSupsFeature(mainFont)
                : shaper.HasSubsFeature(mainFont);

            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            var buf = attribute.buffer.data;

            if (!fontHasFeature)
            {
                var synthMode = isSuper ? SynthSuper : SynthSub;
                for (var i = start; i < clampedEnd; i++)
                    buf[i] = synthMode;
            }
            else
            {
                var codepoints = buffers.codepoints.data;
                for (var i = start; i < clampedEnd; i++)
                {
                    var cp = codepoints[i];
                    bool hasNative = isSuper
                        ? shaper.HasSupsForCodepoint(mainFont, cp)
                        : shaper.HasSubsForCodepoint(mainFont, cp);
                    buf[i] = isSuper
                        ? (hasNative ? NativeSuper : SynthSuper)
                        : (hasNative ? NativeSub : SynthSub);
                }
            }
        }

        private void OnShaped()
        {
            var glyphs = buffers.shapedGlyphs.data;
            var runs = buffers.shapedRuns.data;
            var runCount = buffers.shapedRuns.count;
            var bufLen = attribute.buffer.Capacity;
            var fontProvider = uniText.FontProvider;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                var widthDirty = false;
                var superScale = 0f;
                var subScale = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster >= (uint)bufLen)
                        continue;

                    var mode = attribute.buffer[cluster];
                    if (mode == 0 || mode == NativeSuper || mode == NativeSub)
                        continue;

                    float scale;
                    if (mode == SynthSuper)
                    {
                        if (superScale == 0f)
                        {
                            var font = fontProvider.GetFontAsset(run.fontId);
                            superScale = font != null ? GetScale(font, true) : 0.7f;
                        }
                        scale = superScale;
                    }
                    else
                    {
                        if (subScale == 0f)
                        {
                            var font = fontProvider.GetFontAsset(run.fontId);
                            subScale = font != null ? GetScale(font, false) : 0.7f;
                        }
                        scale = subScale;
                    }

                    glyphs[g].advanceX *= scale;
                    widthDirty = true;
                }

                if (widthDirty)
                {
                    float width = 0f;
                    for (var g = run.glyphStart; g < glyphEnd; g++)
                        width += glyphs[g].advanceX;
                    run.width = width;
                }
            }
        }

        private void OnGlyph()
        {
            var gen = UniTextMeshGenerator.Current;
            var cluster = gen.currentCluster;

            if ((uint)cluster >= (uint)attribute.buffer.Capacity)
                return;

            var mode = attribute.buffer[cluster];
            if (mode == 0 || mode == NativeSuper || mode == NativeSub)
                return;

            var font = gen.font;
            var fi = font.FaceInfo;
            var upem = (float)fi.unitsPerEm;
            var fontSize = gen.FontSize * font.FontScale;
            var isSuper = mode > 0;

            var scale = GetScale(font, isSuper);
            var rawOffset = isSuper ? fi.superscriptOffset : fi.subscriptOffset;
            if (rawOffset <= 0)
                rawOffset = isSuper ? (int)(upem * 0.35f) : (int)(upem * 0.12f);
            var offset = (isSuper ? 1f : -1f) * (rawOffset / upem * fontSize);

            UniTextMeshGenerator.ScaleGlyphQuad(gen.Vertices, gen.vertexCount - 4, gen.baselineY, scale, offset);
        }

        private static float GetScale(UniTextFont font, bool isSuper)
        {
            var fi = font.FaceInfo;
            var size = isSuper ? fi.superscriptSize : fi.subscriptSize;
            if (size <= 0)
                size = isSuper ? fi.subscriptSize : fi.superscriptSize;
            if (size <= 0 || size >= fi.unitsPerEm)
                return 0.7f;

            return size / (float)fi.unitsPerEm;
        }
    }
}
