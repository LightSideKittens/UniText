using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Default implementation of text highlighting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides visual feedback for interactive range clicks with a fade-out animation.
    /// Creates a child <see cref="RangeHighlightGraphic"/> component for rendering.
    /// </para>
    /// </remarks>
    [Serializable]
    public class DefaultTextHighlighter : TextHighlighter
    {
        [SerializeField]
        [Tooltip("Color of the click highlight.")]
        private Color clickColor = new(0.2f, 0.5f, 1f, 0.6f);

        [SerializeField]
        [Tooltip("Duration of the fade-out animation in seconds.")]
        private float fadeDuration = 0.25f;

        [SerializeField]
        [Tooltip("Color of the hover highlight.")]
        private Color hoverColor = new(0.2f, 0.5f, 1f, 0.1f);

        private RangeHighlightGraphic clickGraphic;
        private RangeHighlightGraphic hoverGraphic;
        private float clickAlpha;
        private Color currentClickColor;
        private readonly List<Rect> boundsCache = new(4);

        /// <summary>Gets or sets the click highlight color.</summary>
        public Color ClickColor
        {
            get => clickColor;
            set => clickColor = value;
        }

        /// <summary>Gets or sets the fade duration in seconds.</summary>
        public float FadeDuration
        {
            get => fadeDuration;
            set => fadeDuration = Mathf.Max(0.01f, value);
        }

        /// <summary>Gets or sets the hover highlight color.</summary>
        public Color HoverColor
        {
            get => hoverColor;
            set => hoverColor = value;
        }

        public override void Initialize(UniText owner)
        {
            base.Initialize(owner);
            EnsureGraphics();
        }

        private void EnsureGraphics()
        {
            if (owner == null) return;

            var ownerRT = owner.rectTransform;

            if (clickGraphic == null)
            {
                var clickGo = new GameObject("ClickHighlight");
                clickGo.transform.SetParent(owner.transform, false);
                clickGo.transform.SetAsLastSibling();
                clickGo.hideFlags = HideFlags.HideAndDontSave;

                var rt = clickGo.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = ownerRT.pivot;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                clickGraphic = clickGo.AddComponent<RangeHighlightGraphic>();
                clickGraphic.color = Color.clear;
            }

            if (hoverGraphic == null)
            {
                var hoverGo = new GameObject("HoverHighlight");
                hoverGo.transform.SetParent(owner.transform, false);
                hoverGo.transform.SetAsFirstSibling();
                hoverGo.hideFlags = HideFlags.HideAndDontSave;

                var rt = hoverGo.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = ownerRT.pivot;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                hoverGraphic = hoverGo.AddComponent<RangeHighlightGraphic>();
                hoverGraphic.color = Color.clear;
            }
        }

        public override void OnRangeClicked(InteractiveRange range, List<Rect> bounds)
        {
            if (bounds == null || bounds.Count == 0) return;

            EnsureGraphics();
            if (clickGraphic == null) return;

            clickGraphic.transform.SetAsLastSibling();
            clickGraphic.SetRects(bounds);
            clickAlpha = 1f;
            currentClickColor = clickColor;
            clickGraphic.color = currentClickColor;
        }

        public override void OnRangeEntered(InteractiveRange range, List<Rect> bounds)
        {
            if (bounds == null || bounds.Count == 0) return;

            EnsureGraphics();
            if (hoverGraphic == null) return;

            hoverGraphic.SetRects(bounds);
            hoverGraphic.color = hoverColor;
        }

        public override void OnRangeExited(InteractiveRange range)
        {
            if (hoverGraphic != null)
            {
                hoverGraphic.Clear();
                hoverGraphic.color = Color.clear;
            }
        }

        public override void OnSelectionChanged(int startCluster, int endCluster, List<Rect> bounds)
        {
        }

        public override void Update()
        {
            if (clickAlpha <= 0) return;

            clickAlpha -= Time.deltaTime / fadeDuration;

            if (clickAlpha <= 0)
            {
                clickAlpha = 0;
                if (clickGraphic != null)
                {
                    clickGraphic.Clear();
                    clickGraphic.color = Color.clear;
                }
            }
            else if (clickGraphic != null)
            {
                currentClickColor.a = clickColor.a * clickAlpha;
                clickGraphic.color = currentClickColor;
            }
        }

        public override void Destroy()
        {
            if (clickGraphic != null)
            {
                ObjectUtils.SafeDestroy(clickGraphic.gameObject);
                clickGraphic = null;
            }

            if (hoverGraphic != null)
            {
                ObjectUtils.SafeDestroy(hoverGraphic.gameObject);
                hoverGraphic = null;
            }

            base.Destroy();
        }
    }
}
