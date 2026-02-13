using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Modifier that creates clickable hyperlinks in text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applies link styling (color and optional underline) to tagged text ranges
    /// and handles click events. Use with a parse rule that extracts URLs from
    /// tags like &lt;link=url&gt;text&lt;/link&gt;.
    /// </para>
    /// <para>
    /// Subscribe to <see cref="LinkClicked"/> to handle link clicks, or let the
    /// modifier open URLs automatically via <see cref="Application.OpenURL"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var linkMod = uniText.GetModifier&lt;LinkModifier&gt;();
    /// linkMod.LinkClicked += url => Debug.Log($"Link clicked: {url}");
    /// </code>
    /// </example>
    /// <seealso cref="LinkTagParseRule"/>
    /// <seealso cref="MarkdownLinkParseRule"/>
    /// <seealso cref="RawUrlParseRule"/>
    /// <seealso cref="InteractiveModifier"/>
    [Serializable]
    [TypeGroup("Interactive", 3)]
    public class LinkModifier : InteractiveModifier
    {
        [SerializeField]
        [Tooltip("Color applied to link text.")]
        private Color32 linkColor = new(66, 133, 244, 255);

        [SerializeField]
        [Tooltip("Whether to add underline decoration to links.")]
        private bool enableUnderline = true;

        [SerializeField]
        [Tooltip("Whether to automatically open URLs when clicked.")]
        private bool autoOpenUrl = true;

        private ColorModifier colorModifier;
        private UnderlineModifier underlineModifier;
        private string cachedHexColor;

        /// <summary>Raised when a link is clicked, providing the URL.</summary>
        public event Action<string> LinkClicked;

        /// <summary>Raised when the pointer enters a link, providing the URL.</summary>
        public event Action<string> LinkEntered;

        /// <summary>Raised when the pointer exits a link.</summary>
        public event Action LinkExited;

        /// <inheritdoc/>
        public override string RangeType => "link";

        /// <inheritdoc/>
        public override int Priority => 100;

        /// <summary>Gets or sets the color applied to link text.</summary>
        public Color32 LinkColor
        {
            get => linkColor;
            set
            {
                linkColor = value;
                cachedHexColor = ColorToHex(value);
            }
        }

        /// <summary>Gets or sets whether underline decoration is enabled.</summary>
        public bool EnableUnderline
        {
            get => enableUnderline;
            set => enableUnderline = value;
        }

        /// <summary>Gets or sets whether URLs are opened automatically on click.</summary>
        public bool AutoOpenUrl
        {
            get => autoOpenUrl;
            set => autoOpenUrl = value;
        }

        private static string ColorToHex(Color32 c)
        {
            return $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
        }

        /// <inheritdoc/>
        protected override void OnEnable()
        {
            base.OnEnable();

            cachedHexColor = ColorToHex(linkColor);

            colorModifier ??= new ColorModifier();
            colorModifier.SetOwner(uniText);
            colorModifier.Prepare();

            underlineModifier ??= new UnderlineModifier();
            underlineModifier.SetOwner(uniText);
            underlineModifier.Prepare();
        }

        /// <inheritdoc/>
        protected override void OnDisable()
        {
            base.OnDisable();

            colorModifier?.Disable();
            underlineModifier?.Disable();
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            colorModifier?.Destroy();
            colorModifier = null;

            underlineModifier?.Destroy();
            underlineModifier = null;
        }

        /// <inheritdoc/>
        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;

            AddRange(start, end, parameter);

            colorModifier.Apply(start, end, cachedHexColor);

            if (enableUnderline)
                underlineModifier.Apply(start, end, parameter);
        }

        /// <inheritdoc/>
        protected override void HandleRangeClicked(InteractiveRange range, TextHitResult hit)
        {
            Cat.MeowFormat("[LinkModifier] Link clicked: {0}", range.data);

            LinkClicked?.Invoke(range.data);

            if (autoOpenUrl && !string.IsNullOrEmpty(range.data))
                Application.OpenURL(range.data);
        }

        /// <inheritdoc/>
        protected override void HandleRangeEntered(InteractiveRange range, TextHitResult hit)
        {
            LinkEntered?.Invoke(range.data);
        }

        /// <inheritdoc/>
        protected override void HandleRangeExited(InteractiveRange range)
        {
            LinkExited?.Invoke();
        }
    }
}
