using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// UniText partial class handling pointer interactions and hit testing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements Unity EventSystem interfaces for click handling and hover detection.
    /// Provides hit testing to determine which glyph/cluster was clicked or hovered.
    /// </para>
    /// <para>
    /// Interactive ranges (links, hashtags, mentions, custom) are handled through
    /// <see cref="InteractiveRangeRegistry"/> and <see cref="IInteractiveRangeHandler"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="InteractiveRangeRegistry"/>
    /// <seealso cref="IInteractiveRangeHandler"/>
    public partial class UniText : IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        private const float DefaultMaxClickDistance = 20;

        private TextHitResult lastHoverResult;
        private InteractiveRange lastHoverRange;
        private IInteractiveRangeProvider lastHoverProvider;
        private readonly List<Rect> highlightBoundsCache = new(4);

        /// <summary>Raised when any text is clicked, providing hit test details.</summary>
        public event Action<TextHitResult> TextClicked;

        /// <summary>Raised when an interactive range is clicked.</summary>
        public event Action<InteractiveRangeHit> RangeClicked;

        /// <summary>Raised when the pointer enters an interactive range (desktop only).</summary>
        public event Action<InteractiveRangeHit> RangeEntered;

        /// <summary>Raised when the pointer exits an interactive range (desktop only).</summary>
        public event Action<InteractiveRangeHit> RangeExited;

        /// <summary>Raised when hover position changes, providing hit test details (for InputField).</summary>
        public event Action<TextHitResult> HoverChanged;

        /// <summary>Gets the last hover hit test result.</summary>
        public TextHitResult LastHoverResult => lastHoverResult;

        /// <summary>Returns true if currently hovering over an interactive range.</summary>
        public bool IsHoveringRange => lastHoverRange.IsValid;

        /// <summary>Gets the interactive range currently being hovered, if any.</summary>
        public InteractiveRange CurrentHoverRange => lastHoverRange;

        /// <inheritdoc/>
        public void OnPointerClick(PointerEventData eventData)
        {
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            var result = HitTestScreen(eventData.position, camera);
            if (!result.hit) return;

            Cat.MeowFormat("[UniText] Click: cluster={0}, distance={1:F1}", result.cluster, result.distance);

            TextClicked?.Invoke(result);

            var registry = InteractiveRangeRegistry.Get(buffers);
            if (registry != null && registry.TryGetRangeAt(result.cluster, out var range, out var provider))
            {
                Cat.MeowFormat("[UniText] Range clicked: type={0}, data={1}", range.type, range.data);

                var rangeHit = new InteractiveRangeHit(range, result);
                RangeClicked?.Invoke(rangeHit);

                if (provider is IInteractiveRangeHandler handler)
                    handler.OnRangeClicked(range, result);

                if (highlighter != null)
                {
                    GetRangeBounds(range.start, range.end, highlightBoundsCache);
                    highlighter.OnRangeClicked(range, highlightBoundsCache);
                }
            }
        }

        /// <inheritdoc/>
        public void OnPointerEnter(PointerEventData eventData)
        {
            UpdateHover(eventData);
        }

        /// <inheritdoc/>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (lastHoverRange.IsValid)
            {
                var rangeHit = new InteractiveRangeHit(lastHoverRange, lastHoverResult);
                RangeExited?.Invoke(rangeHit);

                if (lastHoverProvider is IInteractiveRangeHandler handler)
                    handler.OnRangeExited(lastHoverRange);

                highlighter?.OnRangeExited(lastHoverRange);
            }

            lastHoverResult = TextHitResult.None;
            lastHoverRange = default;
            lastHoverProvider = null;
        }

        /// <inheritdoc/>
        public void OnPointerMove(PointerEventData eventData)
        {
            UpdateHover(eventData);
        }

        private void UpdateHover(PointerEventData eventData)
        {
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            var result = HitTestScreen(eventData.position, camera);

            var registry = InteractiveRangeRegistry.Get(buffers);
            InteractiveRange newRange = default;
            IInteractiveRangeProvider newProvider = null;

            if (result.hit && registry != null)
                registry.TryGetRangeAt(result.cluster, out newRange, out newProvider);

            var wasInRange = lastHoverRange.IsValid;
            var isInRange = newRange.IsValid;

            var rangeChanged = wasInRange != isInRange ||
                               (wasInRange && isInRange &&
                                (lastHoverRange.start != newRange.start ||
                                 lastHoverRange.end != newRange.end ||
                                 lastHoverRange.type != newRange.type));

            if (rangeChanged)
            {
                if (wasInRange)
                {
                    var exitHit = new InteractiveRangeHit(lastHoverRange, lastHoverResult);
                    RangeExited?.Invoke(exitHit);

                    if (lastHoverProvider is IInteractiveRangeHandler exitHandler)
                        exitHandler.OnRangeExited(lastHoverRange);

                    highlighter?.OnRangeExited(lastHoverRange);
                }

                if (isInRange)
                {
                    var enterHit = new InteractiveRangeHit(newRange, result);
                    RangeEntered?.Invoke(enterHit);

                    if (newProvider is IInteractiveRangeHandler enterHandler)
                        enterHandler.OnRangeEntered(newRange, result);

                    if (highlighter != null)
                    {
                        GetRangeBounds(newRange.start, newRange.end, highlightBoundsCache);
                        highlighter.OnRangeEntered(newRange, highlightBoundsCache);
                    }
                }
            }

            if (result.cluster != lastHoverResult.cluster || result.hit != lastHoverResult.hit)
                HoverChanged?.Invoke(result);

            lastHoverResult = result;
            lastHoverRange = newRange;
            lastHoverProvider = newProvider;
        }

        /// <summary>Performs hit testing in local coordinates.</summary>
        /// <param name="localPosition">Position in local RectTransform space.</param>
        /// <param name="maxDistance">Maximum distance from glyph center to count as a hit.</param>
        /// <returns>Hit test result with glyph/cluster information.</returns>
        public TextHitResult HitTest(Vector2 localPosition, float maxDistance = DefaultMaxClickDistance)
        {
            if (textProcessor == null)
                return TextHitResult.None;

            var glyphs = textProcessor.PositionedGlyphs;
            var glyphCount = glyphs.Length;
            if (glyphCount == 0)
                return TextHitResult.None;

            var rect = rectTransform.rect;
            var textX = localPosition.x - rect.xMin;
            var textY = rect.yMax - localPosition.y;

            for (var i = 0; i < glyphCount; i++)
            {
                ref readonly var glyph = ref glyphs[i];

                if (textX >= glyph.left && textX <= glyph.right &&
                    textY >= glyph.top && textY <= glyph.bottom)
                    return new TextHitResult(i, glyph.cluster, new Vector2(glyph.x, glyph.y), 0f);
            }

            if (maxDistance <= 0)
                return TextHitResult.None;

            var closestDistSq = float.MaxValue;
            var closestIndex = -1;

            for (var i = 0; i < glyphCount; i++)
            {
                ref readonly var glyph = ref glyphs[i];

                var centerX = (glyph.left + glyph.right) * 0.5f;
                var centerY = (glyph.top + glyph.bottom) * 0.5f;
                var dx = textX - centerX;
                var dy = textY - centerY;
                var distSq = dx * dx + dy * dy;

                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestIndex = i;
                }
            }

            if (closestIndex < 0)
                return TextHitResult.None;

            var distance = Mathf.Sqrt(closestDistSq);
            if (distance > maxDistance)
                return TextHitResult.None;

            ref readonly var closestGlyph = ref glyphs[closestIndex];
            return new TextHitResult(closestIndex, closestGlyph.cluster, new Vector2(closestGlyph.x, closestGlyph.y),
                distance);
        }

        /// <summary>Performs hit testing from screen coordinates.</summary>
        /// <param name="screenPosition">Position in screen space.</param>
        /// <param name="eventCamera">Camera for coordinate conversion (null for overlay canvases).</param>
        /// <param name="maxDistance">Maximum distance from glyph center to count as a hit.</param>
        /// <returns>Hit test result with glyph/cluster information.</returns>
        public TextHitResult HitTestScreen(Vector2 screenPosition, Camera eventCamera, float maxDistance = DefaultMaxClickDistance)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPosition, eventCamera, out var localPos))
                return TextHitResult.None;

            return HitTest(localPos, maxDistance);
        }

        /// <summary>
        /// Gets bounding rectangles for a cluster range.
        /// </summary>
        /// <param name="startCluster">Start cluster index (inclusive).</param>
        /// <param name="endCluster">End cluster index (exclusive).</param>
        /// <param name="results">List to receive bounds (cleared before use).</param>
        /// <remarks>
        /// <para>
        /// Works with cluster indices (codepoint positions in source text).
        /// Returns one Rect per visually contiguous segment.
        /// </para>
        /// <para>
        /// For BiDi text, a single logical range may produce multiple visual segments
        /// when RTL and LTR runs are interleaved.
        /// </para>
        /// </remarks>
        public void GetRangeBounds(int startCluster, int endCluster, IList<Rect> results)
        {
            results.Clear();

            if (textProcessor == null)
                return;

            var lines = buffers.lines;
            var runs = buffers.orderedRuns;
            var glyphs = textProcessor.PositionedGlyphs;

            if (glyphs.Length == 0 || lines.count == 0)
                return;

            var rect = cachedTransformData.rect;

            var glyphIndex = 0;

            for (var lineIdx = 0; lineIdx < lines.count; lineIdx++)
            {
                ref readonly var line = ref lines[lineIdx];

                var lineGlyphCount = 0;
                var runEnd = line.runStart + line.runCount;
                for (var r = line.runStart; r < runEnd; r++)
                    lineGlyphCount += runs[r].glyphCount;

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                var inGroup = false;

                var lineGlyphEnd = glyphIndex + lineGlyphCount;
                for (var g = glyphIndex; g < lineGlyphEnd; g++)
                {
                    ref readonly var glyph = ref glyphs[g];
                    var inRange = glyph.cluster >= startCluster && glyph.cluster < endCluster;

                    if (inRange)
                    {
                        if (glyph.left < minX) minX = glyph.left;
                        if (glyph.right > maxX) maxX = glyph.right;
                        if (glyph.top < minY) minY = glyph.top;
                        if (glyph.bottom > maxY) maxY = glyph.bottom;
                        inGroup = true;
                    }
                    else if (inGroup)
                    {
                        var rectLeft = rect.xMin + minX;
                        var rectBottom = rect.yMax - maxY;
                        var width = maxX - minX;
                        var height = maxY - minY;
                        results.Add(new Rect(rectLeft, rectBottom, width, height));

                        minX = float.MaxValue; maxX = float.MinValue;
                        minY = float.MaxValue; maxY = float.MinValue;
                        inGroup = false;
                    }
                }

                if (inGroup)
                {
                    var rectLeft = rect.xMin + minX;
                    var rectBottom = rect.yMax - maxY;
                    var width = maxX - minX;
                    var height = maxY - minY;
                    results.Add(new Rect(rectLeft, rectBottom, width, height));
                }

                glyphIndex = lineGlyphEnd;
            }
        }

        /// <summary>
        /// Gets the total number of glyphs.
        /// </summary>
        public int GlyphCount => textProcessor?.PositionedGlyphs.Length ?? 0;
    }
}
