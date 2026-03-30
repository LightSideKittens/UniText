using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// UI Graphic component that renders highlight rectangles for text ranges.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="DefaultTextHighlightHandler"/> to draw click/hover highlights.
    /// Renders multiple rectangles efficiently in a single draw call.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    internal class RangeHighlightGraphic : Graphic
    {
        private readonly List<Rect> rects = new(4);

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <summary>
        /// Sets the rectangles to render.
        /// </summary>
        public void SetRects(List<Rect> bounds)
        {
            rects.Clear();
            if (bounds != null)
                rects.AddRange(bounds);
            SetVerticesDirty();
        }

        /// <summary>
        /// Clears all rectangles.
        /// </summary>
        public void Clear()
        {
            rects.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (rects.Count == 0)
                return;

            var c = color;

            for (var i = 0; i < rects.Count; i++)
            {
                var rect = rects[i];
                var idx = vh.currentVertCount;

                vh.AddVert(new Vector3(rect.xMin, rect.yMin), c, Vector2.zero);
                vh.AddVert(new Vector3(rect.xMin, rect.yMax), c, Vector2.up);
                vh.AddVert(new Vector3(rect.xMax, rect.yMax), c, Vector2.one);
                vh.AddVert(new Vector3(rect.xMax, rect.yMin), c, Vector2.right);

                vh.AddTriangle(idx, idx + 1, idx + 2);
                vh.AddTriangle(idx + 2, idx + 3, idx);
            }
        }
    }
}
