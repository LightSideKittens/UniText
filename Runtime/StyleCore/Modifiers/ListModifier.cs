using System;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Contains information about a single list item for rendering.
    /// </summary>
    public struct ListItemInfo
    {
        public int start;
        public int end;
        public int nestingLevel;
        public int displayNumber;
    }

    /// <summary>
    /// Specifies the numbering style for ordered list markers.
    /// </summary>
    public enum OrderedMarkerStyle
    {
        /// <summary>Decimal numbers (1, 2, 3...)</summary>
        Decimal,
        /// <summary>Lowercase letters (a, b, c...)</summary>
        LowerAlpha,
        /// <summary>Uppercase letters (A, B, C...)</summary>
        UpperAlpha,
        /// <summary>Lowercase Roman numerals (i, ii, iii...)</summary>
        LowerRoman,
        /// <summary>Uppercase Roman numerals (I, II, III...)</summary>
        UpperRoman
    }

    /// <summary>
    /// Renders list markers (bullets or numbers) for text items with automatic indentation.
    /// </summary>
    /// <remarks>
    /// Parameter: optional <c>level:number</c> for numbered lists. Without parameter, renders a bullet.
    ///
    /// Features:
    /// - Supports nested lists with configurable indentation per level
    /// - Bullet markers are customizable per nesting level
    /// - Ordered lists support decimal, alphabetic, and Roman numeral styles
    /// - Automatically handles RTL text direction
    /// </remarks>
    /// <seealso cref="MarkdownListParseRule"/>
    /// <seealso cref="OrderedMarkerStyle"/>
    [Serializable]
    [TypeGroup("Layout", 4)]
    [TypeDescription("Formats text as a bulleted or numbered list.")]
    [ParameterField(0, "Level", "int", "0")]
    [ParameterField(1, "Number", "int", "-1")]
    public class ListModifier : BaseModifier
    {
        private PooledList<ListItemInfo> items;
        private UniTextFontProvider fontProvider;

        [ThreadStatic] private static StringBuilder sharedBuilder;

        /// <summary>Indent per nesting level in em units (e.g., 0.55 = 0.55em).</summary>
        public float indentPerLevel = 0.55f;
        public StyledList<string> bulletMarkers = new("•", "-", "·");
        public StyledList<OrderedMarkerStyle> orderedStyles = new(
            OrderedMarkerStyle.Decimal, OrderedMarkerStyle.LowerAlpha, OrderedMarkerStyle.LowerRoman);

        protected override void OnEnable()
        {
            items ??= new PooledList<ListItemInfo>(32);
            items.FakeClear();
            fontProvider = uniText.FontProvider;
            sharedBuilder ??= new StringBuilder(32);

            uniText.BeforeGenerateMesh += InjectMarkerGlyphs;
        }

        protected override void OnDisable()
        {
            uniText.BeforeGenerateMesh -= InjectMarkerGlyphs;
        }

        protected override void OnDestroy()
        {
            items?.Return();
            items = null;
            fontProvider = null;
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var item = ParseParameter(start, end, parameter);
            items.Add(item);
            ApplyMargins(item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ListItemInfo ParseParameter(int start, int end, string parameter)
        {
            var item = new ListItemInfo { start = start, end = end, displayNumber = -1 };
            if (string.IsNullOrEmpty(parameter)) return item;

            var reader = new ParameterReader(parameter);
            reader.NextInt(out item.nestingLevel);
            reader.NextInt(out item.displayNumber, -1);

            return item;
        }

        private float MeasureMarkerWidthForLayout(ListItemInfo item)
        {
            var buf = buffers;
            var fontSize = buf.shapingFontSize > 0 ? buf.shapingFontSize : fontProvider.FontSize;

            sharedBuilder ??= new StringBuilder(32);
            sharedBuilder.Clear();
            BuildMarkerWithSpace(item, false, sharedBuilder);

            var totalWidth = 0f;
            var len = sharedBuilder.Length;
            for (var i = 0; i < len; i++)
            {
                uint codepoint = sharedBuilder[i];
                var charFontId = fontProvider.FindFontForCodepoint((int)codepoint);
                var charFont = fontProvider.GetFontAsset(charFontId);
                if (Shaper.TryGetGlyphInfo(charFont, codepoint, fontSize, out _, out var advance))
                {
                    totalWidth += advance;
                    buf.virtualCodepoints.Add(codepoint);
                }
            }

            return totalWidth;
        }

        private void ApplyMargins(ListItemInfo item)
        {
            var buf = buffers;
            var fontSize = buf.shapingFontSize > 0 ? buf.shapingFontSize : fontProvider.FontSize;
            var scaledIndent = indentPerLevel * fontSize;
            var contentIndent = item.nestingLevel * scaledIndent + MeasureMarkerWidthForLayout(item);

            if (item.end > buf.startMargins.Capacity) buf.EnsureCodepointCapacity(item.end);
            var margins = buf.startMargins.data;
            var safeEnd = Math.Min(item.end, buf.codepoints.count);
            for (var i = item.start; i < safeEnd; i++)
                if (contentIndent > margins[i])
                    margins[i] = contentIndent;
        }

        private void InjectMarkerGlyphs()
        {
            if (items == null || items.Count == 0) return;

            for (var i = 0; i < items.Count; i++)
                InjectMarkerForItem(items[i]);
        }

        private void InjectMarkerForItem(ListItemInfo item)
        {
            var isRtl = IsItemRtl(item.start);

            float firstGlyphX = 0, baselineY = 0;
            var found = false;
            var buf = buffers;
            for (var i = 0; i < buf.positionedGlyphs.count; i++)
            {
                if (buf.positionedGlyphs[i].cluster >= item.start)
                {
                    firstGlyphX = buf.positionedGlyphs[i].x;
                    baselineY = buf.positionedGlyphs[i].y;
                    found = true;
                    break;
                }
            }
            if (!found) return;

            sharedBuilder ??= new StringBuilder(32);
            sharedBuilder.Clear();
            BuildMarkerWithSpace(item, isRtl, sharedBuilder);

            var fontSize = uniText.CurrentFontSize;
            var len = sharedBuilder.Length;

            var injectedStart = buf.virtualPositionedGlyphs.count;
            var curX = 0f;
            for (var c = 0; c < len; c++)
            {
                uint codepoint = sharedBuilder[c];
                var charFontId = fontProvider.FindFontForCodepoint((int)codepoint);
                var charFont = fontProvider.GetFontAsset(charFontId);
                var charGlyphIndex = charFont.GetGlyphIndexForUnicode(codepoint);
                if (charGlyphIndex == 0) continue;

                var charLookup = charFont.GlyphLookupTable;
                if (charLookup != null && charLookup.TryGetValue(charFont.GlyphKey(charGlyphIndex), out var charGlyph))
                {
                    var upem = (float)charFont.UnitsPerEm;
                    var advance = charGlyph.metrics.horizontalAdvance * fontSize * charFont.FontScale / upem;

                    buf.virtualPositionedGlyphs.Add(new PositionedGlyph
                    {
                        glyphId = (int)charGlyphIndex,
                        cluster = item.start,
                        x = curX,
                        y = baselineY,
                        fontId = charFontId,
                        shapedGlyphIndex = -1,
                        left = curX,
                        right = curX + advance,
                        top = baselineY,
                        bottom = baselineY
                    });
                    curX += advance;
                }
            }

            float offsetX;
            if (isRtl)
            {
                var glyphScale = buf.GetGlyphScale(fontSize);
                offsetX = firstGlyphX + GetLineWidth(item.start) * glyphScale;
            }
            else
            {
                offsetX = firstGlyphX - curX;
            }

            var injectedEnd = buf.virtualPositionedGlyphs.count;
            var data = buf.virtualPositionedGlyphs.data;
            for (var i = injectedStart; i < injectedEnd; i++)
            {
                data[i].x += offsetX;
                data[i].left += offsetX;
                data[i].right += offsetX;
            }
        }

        private void BuildMarkerWithSpace(ListItemInfo item, bool isRtl, StringBuilder sb)
        {
            if (isRtl) sb.Append(' ');

            if (item.displayNumber < 0)
            {
                sb.Append(bulletMarkers[Math.Max(0, Math.Min(item.nestingLevel, bulletMarkers.Length - 1))]);
            }
            else
            {
                var level = Math.Max(0, Math.Min(item.nestingLevel, orderedStyles.Length - 1));
                if (isRtl) sb.Append('.');
                AppendOrderedNumber(sb, item.displayNumber, orderedStyles[level]);
                if (!isRtl) sb.Append('.');
            }

            if (!isRtl) sb.Append(' ');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsItemRtl(int cluster)
        {
            var dir = uniText.BaseDirection;
            if (dir == TextDirection.LeftToRight) return false;
            if (dir == TextDirection.RightToLeft) return true;
            var levels = buffers.bidiLevels.data;
            return (uint)cluster < (uint)levels.Length && (levels[cluster] & 1) == 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetLineWidth(int cluster)
        {
            var buf = buffers;
            for (var i = 0; i < buf.lines.count; i++)
            {
                ref readonly var line = ref buf.lines[i];
                if (cluster >= line.range.start && cluster < line.range.start + line.range.length)
                    return line.width;
            }

            return 0f;
        }

        private static void AppendOrderedNumber(StringBuilder sb, int n, OrderedMarkerStyle style)
        {
            switch (style)
            {
                case OrderedMarkerStyle.Decimal:
                    AppendInt(sb, n);
                    break;
                case OrderedMarkerStyle.LowerAlpha:
                    sb.Append(n > 0 ? (char)('a' + (n - 1) % 26) : '?');
                    break;
                case OrderedMarkerStyle.UpperAlpha:
                    sb.Append(n > 0 ? (char)('A' + (n - 1) % 26) : '?');
                    break;
                case OrderedMarkerStyle.LowerRoman:
                    AppendRoman(sb, n, true);
                    break;
                case OrderedMarkerStyle.UpperRoman:
                    AppendRoman(sb, n, false);
                    break;
                default:
                    AppendInt(sb, n);
                    break;
            }
        }

        private static void AppendInt(StringBuilder sb, int n)
        {
            if (n == 0)
            {
                sb.Append('0');
                return;
            }

            if (n < 0)
            {
                sb.Append('-');
                n = -n;
            }

            var start = sb.Length;
            while (n > 0)
            {
                sb.Append((char)('0' + n % 10));
                n /= 10;
            }

            var end = sb.Length - 1;
            while (start < end)
            {
                (sb[start], sb[end]) = (sb[end], sb[start]);
                start++;
                end--;
            }
        }

        private static readonly int[] RomanValues = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        private static readonly string[] RomanLower = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        private static readonly string[] RomanUpper = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        private static void AppendRoman(StringBuilder sb, int n, bool lower)
        {
            if (n <= 0 || n > 3999)
            {
                AppendInt(sb, n);
                return;
            }

            var symbols = lower ? RomanLower : RomanUpper;
            for (var i = 0; i < RomanValues.Length; i++)
            {
                while (n >= RomanValues[i])
                {
                    sb.Append(symbols[i]);
                    n -= RomanValues[i];
                }
            }
        }
    }
}
