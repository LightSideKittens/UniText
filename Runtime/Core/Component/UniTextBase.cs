using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Abstract base class for UniText text rendering components.
    /// Contains all shared text processing logic (Unicode, BiDi, shaping, line breaking,
    /// modifiers, emoji, font fallback, variable fonts).
    /// </summary>
    /// <remarks>
    /// Concrete subclasses provide the rendering backend:
    /// <see cref="UniText"/> for Canvas (CanvasRenderer), <see cref="UniTextWorld"/> for world-space (MeshRenderer).
    /// </remarks>
    [ExecuteAlways]
    public abstract partial class UniTextBase : MaskableGraphic
#if UNITY_EDITOR
        , ISerializationCallbackReceiver
#endif
    {
        #region Enums

        /// <summary>Flags indicating which parts of the text need rebuilding.</summary>
        [Flags]
        public enum DirtyFlags
        {
            /// <summary>No rebuild needed.</summary>
            None = 0,
            /// <summary>Color changed, vertex colors need update.</summary>
            Color = 1 << 0,
            /// <summary>Alignment changed, positions need recalculation.</summary>
            Alignment = 1 << 1,
            /// <summary>Layout changed, line breaking needs recalculation.</summary>
            Layout = 1 << 2,
            /// <summary>Font size changed.</summary>
            FontSize = 1 << 3,
            /// <summary>Font asset changed, full rebuild required.</summary>
            Font = 1 << 4,
            /// <summary>Text direction changed.</summary>
            Direction = 1 << 5,
            /// <summary>Text content changed, full rebuild required.</summary>
            Text = 1 << 6,
            /// <summary>Material changed (atlas texture, render mode).</summary>
            Material = 1 << 7,
            /// <summary>Sorting order or layer changed (world-space only).</summary>
            Sorting = 1 << 8,
            /// <summary>Layout or font size changed.</summary>
            LayoutRebuild = Layout | FontSize,
            /// <summary>Text, font, or direction changed.</summary>
            FullRebuild = Text | Font | Direction,
            /// <summary>Everything needs rebuilding.</summary>
            All = Color | Alignment | Layout | FontSize | FullRebuild | Sorting
        }

        /// <summary>
        /// Text rendering mode: SDF (rounded corners on effects) or MSDF (sharp corners).
        /// </summary>
        public enum RenderModee : byte
        {
            /// <summary>Single-channel SDF. Naturally rounds corners on outline/underlay effects.</summary>
            SDF = 0,
            /// <summary>Multi-channel SDF. Preserves sharp corners on outline/underlay effects.</summary>
            MSDF = 1,
        }

        #endregion

        #region Serialized Fields

        [TextArea(3, 10)]
        [SerializeField]
        [Tooltip("The text content to display. Supports Unicode, emoji, and custom markup.")]
        private string text = "";

        [NonSerialized] protected ReadOnlyMemory<char> sourceText;
        [NonSerialized] private bool isTextFromBuffer;

        [SerializeField]
        [Tooltip("Font collection with primary font and fallback chain.")]
        private UniTextFontStack fontStack;

        [SerializeField]
        [Tooltip("Base font size in points.")]
        protected float fontSize = 36f;

        [SerializeField]
        [Tooltip("Base text direction. Auto detects from first strong directional character.")]
        protected TextDirection baseDirection = TextDirection.Auto;

        [SerializeField]
        [Tooltip("Enable word wrapping at container boundaries.")]
        protected bool wordWrap = true;

        [SerializeField]
        [Tooltip("Horizontal text alignment within the container.")]
        private HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;

        [SerializeField]
        [Tooltip("Vertical text alignment within the container.")]
        private VerticalAlignment verticalAlignment = VerticalAlignment.Top;

        [SerializeField]
        [Tooltip("Top edge metric for text box trimming. CapHeight removes space above capital letters.")]
        protected TextOverEdge overEdge = TextOverEdge.Ascent;

        [SerializeField]
        [Tooltip("Bottom edge metric for text box trimming. Baseline removes space below the last line.")]
        protected TextUnderEdge underEdge = TextUnderEdge.Descent;

        [SerializeField]
        [Tooltip("How extra leading from line-height is distributed: HalfLeading (CSS), LeadingAbove (Figma), LeadingBelow (Android).")]
        protected LeadingDistribution leadingDistribution = LeadingDistribution.HalfLeading;

        [SerializeField]
        [Tooltip("Automatically adjust font size to fit container.")]
        protected bool autoSize;

        [SerializeField]
        [Tooltip("Minimum font size when auto-sizing.")]
        protected float minFontSize = 10f;

        [SerializeField]
        [Tooltip("Maximum font size when auto-sizing.")]
        protected float maxFontSize = 72f;

        [SerializeField]
        [Tooltip("Modifier/rule pairs that define how markup is parsed and applied (e.g., color, bold, links).")]
        private StyledList<Style> styles = new();

        [SerializeField]
        [Tooltip("Shared modifier configurations (ScriptableObjects) to apply in addition to local styles.")]
        private StyledList<StylePreset> stylePresets = new();

        /// <summary>Runtime copies of stylePresets to avoid ownership conflicts.</summary>
        private readonly List<StylePreset> runtimeStylePresetCopies = new();

        [SerializeField]
        [Tooltip("SDF: rounded corners on outline/underlay effects. MSDF: sharp corners.")]
        private RenderModee renderMode = RenderModee.SDF;

        #endregion

        #region Runtime State

        protected TextProcessor textProcessor;
        private UniTextFontProvider fontProvider;
        protected UniTextMeshGenerator meshGenerator;
        private AttributeParser attributeParser;
        protected UniTextBuffers buffers;

        private DirtyFlags dirtyFlags = DirtyFlags.All;

        /// <summary>Gets the current dirty flags indicating what needs rebuilding.</summary>
        public DirtyFlags CurrentDirtyFlags => dirtyFlags;
        private bool textIsParsed;
        private bool isRegisteredDirty;

        private float resultWidth;
        private float resultHeight;

        /// <summary>Cached effective font size (set by layout/auto-size).</summary>
        protected float cachedEffectiveFontSize;

        private struct RefCountTracker
        {
            private PooledBuffer<long> current;
            private PooledBuffer<long> previous;

            public int Count => current.count;

            public void Update(GlyphAtlas atlas, ref PooledBuffer<long> newKeys)
            {
                (previous, current) = (current, previous);
                current.FakeClear();
                current.EnsureCapacity(newKeys.count);
                newKeys.Span.CopyTo(current.data);
                current.count = newKeys.count;
                for (int i = 0; i < current.count; i++)
                    atlas.AddRef(current.data[i]);
                for (int i = 0; i < previous.count; i++)
                    atlas.Release(previous.data[i]);
            }

            public void ReleaseAll(GlyphAtlas atlas)
            {
                for (int i = 0; i < current.count; i++)
                    atlas.Release(current.data[i]);
                current.FakeClear();
            }

            public void Return()
            {
                current.Return();
                previous.Return();
            }
        }

        private RefCountTracker glyphRefs;
        private RefCountTracker emojiRefs;

        protected List<UniTextRenderData> renderData;

        private float lastKnownWidth = -1;
        private float lastKnownHeight = -1;

        /// <summary>Raised before text is rebuilt.</summary>
        public event Action Rebuilding;

        /// <summary>Raised after glyph positioning but before mesh generation. Modifiers inject virtual PositionedGlyphs here.</summary>
        public event Action BeforeGenerateMesh;

        /// <summary>Raised when the RectTransform height changes.</summary>
        public event Action RectHeightChanged;

        /// <summary>Raised when dirty flags change, indicating what needs rebuilding.</summary>
        public event Action<DirtyFlags> DirtyFlagsChanged;

        #endregion

        #region Public API

        /// <summary>Gets the text processor instance handling shaping and layout.</summary>
        public TextProcessor TextProcessor => textProcessor;

        /// <summary>Gets the mesh generator instance.</summary>
        public UniTextMeshGenerator MeshGenerator => meshGenerator;

        /// <summary>Gets the font provider managing font assets and fallbacks.</summary>
        public UniTextFontProvider FontProvider => fontProvider;

        /// <summary>Gets the buffer container for text processing.</summary>
        public UniTextBuffers Buffers => buffers;

        /// <summary>Gets the text with markup stripped.</summary>
        public string CleanText => attributeParser?.CleanText ?? Text;

        /// <summary>Gets the computed size of the rendered text.</summary>
        public Vector2 ResultSize => new(resultWidth, resultHeight);

        /// <summary>Gets the positioned glyphs after processing.</summary>
        public ReadOnlySpan<PositionedGlyph> ResultGlyphs => textProcessor != null ? textProcessor.PositionedGlyphs : ReadOnlySpan<PositionedGlyph>.Empty;

        /// <summary>Gets the primary font from the font collection.</summary>
        public UniTextFont PrimaryFont => fontStack?.PrimaryFont;

        /// <summary>Gets the current effective font size (accounts for auto-sizing).</summary>
        public float CurrentFontSize => autoSize
            ? (cachedEffectiveFontSize > 0 ? cachedEffectiveFontSize : maxFontSize)
            : fontSize;

        /// <summary>Gets the list of registered modifiers.</summary>
        public IReadOnlyList<Style> Styles => styles;

        /// <summary>Gets the list of modifier configuration assets.</summary>
        public IReadOnlyList<StylePreset> StylePresets => stylePresets;

        /// <summary>Gets or sets the source text, which may contain markup parsed by registered <see cref="IParseRule"/> implementations.</summary>
        public string Text
        {
            get
            {
                if (isTextFromBuffer)
                {
                    text = new string(sourceText.Span);
                    isTextFromBuffer = false;
                }
                return text;
            }
            set
            {
                if (value != null && value.IndexOf('\r') >= 0)
                    value = NormalizeLineEndings(value);

                if (!isTextFromBuffer && text == value) return;
                text = value;
                sourceText = (value ?? "").AsMemory();
                isTextFromBuffer = false;
                if (sourceText.IsEmpty)
                {
                    DeInit();
                }
                else
                {
                    SetDirty(DirtyFlags.Text);
                }
            }
        }

        /// <summary>
        /// Sets text content from a char array without allocating a string.
        /// Ideal for frequently updated text (timers, scores, etc.).
        /// </summary>
        public void SetText(char[] source, int start, int length)
        {
            sourceText = new ReadOnlyMemory<char>(source, start, length);
            isTextFromBuffer = true;
            if (length == 0)
            {
                DeInit();
            }
            else
            {
                SetDirty(DirtyFlags.Text);
            }
        }

        private static string NormalizeLineEndings(string input)
        {
            var crlfCount = 0;
            for (var i = 0; i < input.Length - 1; i++)
            {
                if (input[i] == '\r' && input[i + 1] == '\n')
                    crlfCount++;
            }

            return string.Create(input.Length - crlfCount, input, static (span, src) =>
            {
                var writePos = 0;
                for (var i = 0; i < src.Length; i++)
                {
                    var c = src[i];
                    if (c == '\r')
                    {
                        if (i + 1 < src.Length && src[i + 1] == '\n')
                            continue;
                        span[writePos++] = '\n';
                    }
                    else
                    {
                        span[writePos++] = c;
                    }
                }
            });
        }

        /// <summary>Gets or sets the font collection.</summary>
        public UniTextFontStack FontStack
        {
            get => fontStack;
            set
            {
                if (fontStack == value) return;

#if UNITY_EDITOR
                UnlistenConfigChanged();
#endif
                if (fontStack != null) fontStack.Changed -= OnConfigChanged;
                fontStack = value;
                if (fontStack != null) fontStack.Changed += OnConfigChanged;

#if UNITY_EDITOR
                ListenConfigChanged();
#endif
                SetDirty(DirtyFlags.Font);
            }
        }

        /// <summary>Gets or sets the base font size in points.</summary>
        public float FontSize
        {
            get => fontSize;
            set
            {
                if (Mathf.Approximately(fontSize, value)) return;
                fontSize = Mathf.Max(0.01f, value);
                SetDirty(DirtyFlags.FontSize);
            }
        }

        /// <summary>Gets or sets the base text direction (LTR, RTL, or Auto-detect).</summary>
        public TextDirection BaseDirection
        {
            get => baseDirection;
            set
            {
                if (baseDirection == value) return;
                baseDirection = value;
                SetDirty(DirtyFlags.Direction);
            }
        }

        /// <summary>Gets or sets whether word wrapping is enabled.</summary>
        public bool WordWrap
        {
            get => wordWrap;
            set
            {
                if (wordWrap == value) return;
                wordWrap = value;
                SetDirty(DirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the horizontal text alignment.</summary>
        public HorizontalAlignment HorizontalAlignment
        {
            get => horizontalAlignment;
            set
            {
                if (horizontalAlignment == value) return;
                horizontalAlignment = value;
                SetDirty(DirtyFlags.Alignment);
            }
        }

        /// <summary>Gets or sets the vertical text alignment.</summary>
        public VerticalAlignment VerticalAlignment
        {
            get => verticalAlignment;
            set
            {
                if (verticalAlignment == value) return;
                verticalAlignment = value;
                SetDirty(DirtyFlags.Alignment);
            }
        }

        /// <summary>Gets or sets the top edge metric for text box trimming.</summary>
        public TextOverEdge OverEdge
        {
            get => overEdge;
            set
            {
                if (overEdge == value) return;
                overEdge = value;
                SetDirty(DirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the bottom edge metric for text box trimming.</summary>
        public TextUnderEdge UnderEdge
        {
            get => underEdge;
            set
            {
                if (underEdge == value) return;
                underEdge = value;
                SetDirty(DirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets how extra leading from line-height is distributed.</summary>
        public LeadingDistribution LeadingDistribution
        {
            get => leadingDistribution;
            set
            {
                if (leadingDistribution == value) return;
                leadingDistribution = value;
                SetDirty(DirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the text rendering mode (SDF for rounded, MSDF for sharp corner effects).</summary>
        public RenderModee RenderMode
        {
            get => renderMode;
            set
            {
                if (renderMode == value) return;
                Cat.MeowFormat("[UniText] RenderMode switch '{0}': {1}→{2}", name, renderMode, value);
                ReleaseAllGlyphAtlasRefs();
                renderMode = value;
                if (textProcessor != null)
                    textProcessor.HasValidGlyphsInAtlas = false;
                SetDirty(DirtyFlags.Material);
            }
        }

        /// <summary>Gets or sets whether automatic font sizing is enabled.</summary>
        public bool AutoSize
        {
            get => autoSize;
            set
            {
                if (autoSize == value) return;
                autoSize = value;
                SetDirty(DirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the minimum font size for auto-sizing.</summary>
        public float MinFontSize
        {
            get => minFontSize;
            set
            {
                value = Mathf.Max(0.01f, value);
                if (Mathf.Approximately(minFontSize, value)) return;
                minFontSize = value;
                if (autoSize) SetDirty(DirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the maximum font size for auto-sizing.</summary>
        public float MaxFontSize
        {
            get => maxFontSize;
            set
            {
                value = Mathf.Max(0.01f, value);
                if (Mathf.Approximately(maxFontSize, value)) return;
                maxFontSize = value;
                if (autoSize) SetDirty(DirtyFlags.Layout);
            }
        }

        /// <inheritdoc/>
        public override Color color
        {
            get => base.color;
            set
            {
                if (base.color == value) return;
                base.color = value;
                SetDirty(DirtyFlags.Color);
            }
        }

        /// <summary>Marks the specified aspects of the text as needing rebuild.</summary>
        public void SetDirty(DirtyFlags flags)
        {
            if (flags == DirtyFlags.None) return;
            Cat.MeowFormat("[UniText] SetDirty: {0}, {1}", flags, name);
            dirtyFlags |= flags;

            if ((flags & DirtyFlags.Font) != 0)
            {
                DeinitializeAllStyles();
                fontProvider = null;
                meshGenerator?.Dispose();
                meshGenerator = null;
            }

            if ((flags & DirtyFlags.FullRebuild) != 0)
            {
                textIsParsed = false;
                textProcessor?.InvalidateFirstPassData();
                InvalidateLayoutCache();
            }
            else if ((flags & DirtyFlags.LayoutRebuild) != 0)
            {
                textProcessor?.InvalidateLayoutData();
                InvalidateLayoutCache();
            }
            else if ((flags & DirtyFlags.Alignment) != 0)
            {
                textProcessor?.InvalidatePositionedGlyphs();
            }

            RegisterDirty(this);

            DirtyFlagsChanged?.Invoke(flags);
            OnSetDirty(flags);
        }

        /// <inheritdoc/>
        public override void SetVerticesDirty() { }

        /// <inheritdoc/>
        public override void SetMaterialDirty() { }

        #endregion

        #region Modifiers

        /// <summary>Adds a style to this component at runtime.</summary>
        public void AddStyle(Style style)
        {
            if (!style.IsValid) return;

            if (style.IsRegistered && style.Owner == this) return;

            if (style.Owner != null && style.Owner != this)
            {
                Debug.LogError($"[UniText] Style already owned by {style.Owner.name}. Cannot add to {name}.");
                return;
            }

            styles.Add(style);

            if (textProcessor != null)
            {
                EnsureAttributeParserCreated();
                style.Register(this, attributeParser);
                SetDirty(DirtyFlags.Text);
            }
        }
        
#if UNITY_EDITOR
        /// <summary>Editor-only: adds a style without requiring IsValid. Allows adding empty styles for configuration.</summary>
        internal void AddStyle_Editor(Style style)
        {
            styles.Add(style);
            if (style.IsValid && textProcessor != null)
            {
                EnsureAttributeParserCreated();
                style.Register(this, attributeParser);
            }
            SetDirty(DirtyFlags.Text);
        }
#endif
        
        /// <summary>Removes a style from this component at runtime.</summary>
        public bool RemoveStyle(Style style)
        {
            var removed = styles.Remove(style);
            if (!removed) return false;

            if (style.IsRegistered && style.Owner == this)
            {
                style.Unregister(attributeParser);
                SetDirty(DirtyFlags.Text);
            }

            if (styles.Count == 0 && !HasAnyStylePresets())
            {
                DestroyAttributeParser();
            }

            return true;
        }

        /// <summary>Removes all styles from this component.</summary>
        public void ClearStyles()
        {
            for (var i = 0; i < styles.Count; i++)
            {
                styles[i].Unregister(attributeParser);
            }
            styles.Clear();
            DestroyAttributeParser();
        }

        /// <summary>
        /// Registers a Style with the parser. Called by Style during hot-swap.
        /// </summary>
        internal void RegisterStyleWithParser(Style style)
        {
            if (attributeParser == null) return;
            style.Register(this, attributeParser);
        }

        /// <summary>
        /// Unregisters a Style from the parser. Called by Style during hot-swap.
        /// </summary>
        internal void UnregisterStyleFromParser(Style style)
        {
            style.Unregister(attributeParser);
        }

        /// <summary>Reinitializes all registered modifiers (used by Editor/OnValidate).</summary>
        private void ReInitStyles()
        {
            DestroyAttributeParser();
            EnsureAttributeParserCreated();
        }

        /// <summary>Deinitializes all modifiers but keeps them registered (for font changes).</summary>
        private void DeinitializeAllStyles()
        {
            for (var i = 0; i < styles.Count; i++)
            {
                styles[i].DeinitializeModifier();
            }
            for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
            {
                var config = runtimeStylePresetCopies[i];
                for (var j = 0; j < config.styles.Count; j++)
                {
                    config.styles[j].DeinitializeModifier();
                }
            }
        }

        /// <summary>Resets all Style states (for deserialization/Editor reload).</summary>
        private void ResetAllStyleStates()
        {
            for (var i = 0; i < styles.Count; i++)
            {
                styles[i].ResetState();
            }
            for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
            {
                var config = runtimeStylePresetCopies[i];
                for (var j = 0; j < config.styles.Count; j++)
                {
                    config.styles[j].ResetState();
                }
            }
        }

        private void EnsureAttributeParserCreated()
        {
            if (attributeParser != null) return;
            if (textProcessor == null) return;

            if (styles is { Count: > 0 } || HasAnyStylePresets())
            {
                EnsureRuntimeConfigCopiesCreated();

                attributeParser = new AttributeParser();
                RegisterStylesWithParser(styles);
                for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
                {
                    RegisterStylesWithParser(runtimeStylePresetCopies[i].styles);
                }
                textProcessor.Parsed += attributeParser.Apply;
                SetDirty(DirtyFlags.Text);
            }
        }

        private void EnsureRuntimeConfigCopiesCreated()
        {
            if (runtimeStylePresetCopies.Count > 0) return;

            for (var i = 0; i < stylePresets.Count; i++)
            {
                var config = stylePresets[i];
                if (config != null)
                {
                    runtimeStylePresetCopies.Add(Instantiate(config));
                }
            }
        }

        private bool HasAnyStylePresets()
        {
            for (var i = 0; i < stylePresets.Count; i++)
            {
                var config = stylePresets[i];
                if (config != null && config.styles is { Count: > 0 })
                    return true;
            }
            return false;
        }

        /// <summary>Registers all valid Styles with the parser.</summary>
        private void RegisterStylesWithParser(StyledList<Style> mods)
        {
            for (var i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod is { IsValid: true })
                {
                    mod.Register(this, attributeParser);
                }
            }
        }

        private void DestroyAttributeParser()
        {
            if (attributeParser == null) return;

            attributeParser.DeinitializeModifiers();
            ResetAllStyleStates();
            DestroyRuntimeConfigCopies();

            attributeParser.Release();
            if (textProcessor != null)
            {
                textProcessor.Parsed -= attributeParser.Apply;
            }

            attributeParser = null;
            SetDirty(DirtyFlags.Text);
        }

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            Cat.Meow($"[UniText] OnEnable, {name}", this);
            sourceText = (text ?? "").AsMemory();
            Sub();
            SetDirty(DirtyFlags.All);
        }

        protected override void OnDisable()
        {
            UnSub();
            base.OnDisable();
            DeInit();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            DeInit(true);
            DestroyRuntimeConfigCopies();
        }

        protected virtual void Sub()
        {
            if (fontStack != null) fontStack.Changed += OnConfigChanged;
#if UNITY_EDITOR
            ListenConfigChanged();
#endif
            EmojiFont.DisableChanged += OnEmojiFontDisableChanged;
            GlyphAtlas.AnyAtlasCompacted += OnAtlasCompacted;
        }

        protected virtual void UnSub()
        {
            if (fontStack != null) fontStack.Changed -= OnConfigChanged;
#if UNITY_EDITOR
            UnlistenConfigChanged();
#endif
            EmojiFont.DisableChanged -= OnEmojiFontDisableChanged;
            GlyphAtlas.AnyAtlasCompacted -= OnAtlasCompacted;
        }

        private void OnAtlasCompacted(GlyphAtlas compactedAtlas)
        {
            bool isMyAtlas = compactedAtlas == GlyphAtlas.GetInstance(RenderMode);
            bool isEmojiAtlas = compactedAtlas == GlyphAtlas.Emoji;
            if (!isMyAtlas && !isEmojiAtlas) return;
            if (isMyAtlas && glyphRefs.Count == 0) return;
            if (isEmojiAtlas && emojiRefs.Count == 0) return;

            Cat.MeowFormat("[UniText] OnAtlasCompacted '{0}': regen mesh, glyphRefs={1}, emojiRefs={2}",
                name, glyphRefs.Count, emojiRefs.Count);

            if (textProcessor != null)
                textProcessor.buf.hasValidGlyphCache = false;
            SetDirty(DirtyFlags.Material);
        }

        protected void DeInit(bool isDestroying = false)
        {
            Cat.MeowFormat("[UniText] DeInit '{0}': isDestroying={1}, heldKeys={2}+{3}e",
                name, isDestroying, glyphRefs.Count, emojiRefs.Count);
            ReleaseAllGlyphAtlasRefs();
            glyphRefs.Return();
            emojiRefs.Return();
            if (!isDestroying)
            {
                ClearAllRenderers();
            }
            DestroyAttributeParser();
            MeshApplied?.Invoke();

            textProcessor = null;
            fontProvider = null;
            meshGenerator?.Dispose();
            meshGenerator = null;

            OnDeInit();
            buffers?.EnsureReturnBuffers();
            UnregisterDirty(this);
        }

        /// <summary>
        /// Updates glyph atlas reference counts. AddRef all new keys first, then Release
        /// all old keys.
        /// </summary>
        private void UpdateGlyphAtlasRefCounts()
        {
            if (meshGenerator == null) return;

            glyphRefs.Update(GlyphAtlas.GetInstance(RenderMode), ref meshGenerator.usedGlyphKeys);

            var emojiAtlas = GlyphAtlas.Emoji;
            if (emojiAtlas != null)
                emojiRefs.Update(emojiAtlas, ref meshGenerator.usedEmojiKeys);

            Cat.MeowFormat("[UniText] UpdateRefCounts '{0}': glyph={1}, emoji={2}",
                name, glyphRefs.Count, emojiRefs.Count);
        }

        private void ReleaseAllGlyphAtlasRefs()
        {
            if (glyphRefs.Count > 0)
                glyphRefs.ReleaseAll(GlyphAtlas.GetInstance(RenderMode));

            var emojiAtlas = GlyphAtlas.Emoji;
            if (emojiAtlas != null && emojiRefs.Count > 0)
                emojiRefs.ReleaseAll(emojiAtlas);
        }

        private void OnEmojiFontDisableChanged()
        {
            SetDirty(DirtyFlags.All);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            var rect = rectTransform.rect;
            var width = rect.width;
            var height = rect.height;

            var widthChanged = !Mathf.Approximately(width, lastKnownWidth);
            var heightChanged = !Mathf.Approximately(height, lastKnownHeight);

            if (heightChanged)
            {
                lastKnownHeight = height;
                RectHeightChanged?.Invoke();
            }

            if (widthChanged)
            {
                lastKnownWidth = width;

                var effectiveFontSize = autoSize ? maxFontSize : fontSize;
                var canReuse = textProcessor != null && textProcessor.CanReuseLines(width, effectiveFontSize, wordWrap);

                if (canReuse)
                {
                    SetDirty(DirtyFlags.Alignment);
                }
                else
                {
                    SetDirty(DirtyFlags.Layout);
                }
            }
            else
            {
                SetDirty(DirtyFlags.Alignment);
            }
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            SetDirty(DirtyFlags.Layout);
        }

        private void OnConfigChanged()
        {
            if (UniTextFont.IsAtlasClearing)
                ReleaseAllGlyphAtlasRefs();
            SetDirty(DirtyFlags.All);
        }

        private void DestroyRuntimeConfigCopies()
        {
            for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
            {
                ObjectUtils.SafeDestroy(runtimeStylePresetCopies[i]);
            }
            runtimeStylePresetCopies.Clear();
        }

        #endregion

        #region Rebuild

        /// <inheritdoc/>
        public override void Rebuild(CanvasUpdate update) { }

        /// <inheritdoc/>
        protected override void UpdateMaterial() { }

        protected virtual bool ValidateAndInitialize()
        {
            UniTextDebug.BeginSample("UniText.ValidateAndInitialize");

#if UNITY_EDITOR
            if (!TryInitFonts())
            {
                UniTextDebug.EndSample();
                return false;
            }
#endif

            buffers ??= new UniTextBuffers();
            buffers.EnsureRentBuffers(sourceText.Length);

            if (textProcessor == null)
            {
                textProcessor = new TextProcessor(buffers);
                Cat.Meow("[UniText] TextProcessor created", this);
            }

            EnsureAttributeParserCreated();

            if (fontProvider == null)
            {
                fontProvider = new UniTextFontProvider(fontStack);
                meshGenerator = new UniTextMeshGenerator(fontProvider, buffers);
                textProcessor.SetFontProvider(fontProvider);
                Cat.Meow("[UniText] FontProvider created", this);
            }

            UniTextDebug.EndSample();
            return true;
        }

        private ReadOnlySpan<char> ParseOrGetParsedAttributes()
        {
            if (!textIsParsed)
            {
                UniTextDebug.BeginSample("UniText.ParseAttributes");
                attributeParser?.ResetModifiers();
                attributeParser?.Parse(sourceText.Span);
                textIsParsed = true;
                UniTextDebug.EndSample();
            }

            return attributeParser != null ? attributeParser.CleanTextSpan : sourceText.Span;
        }

        private TextProcessSettings CreateProcessSettings(Rect rect, float effectiveFontSize) => new()
        {
            MaxWidth = rect.width,
            MaxHeight = rect.height,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            OverEdge = overEdge,
            UnderEdge = underEdge,
            LeadingDistribution = leadingDistribution,
            fontSize = effectiveFontSize,
            baseDirection = baseDirection,
            enableWordWrap = wordWrap
        };

        #endregion

        #region Abstract / Virtual Contract

        /// <summary>Applies generated mesh data to the rendering backend (CanvasRenderer or MeshRenderer sub-meshes).</summary>
        protected abstract void UpdateRendering();

        /// <summary>Clears all sub-mesh renderers (without destroying GameObjects).</summary>
        protected abstract void ClearAllRenderers();

        /// <summary>Returns true if the component renders in world space with a camera (affects xScale calculation).</summary>
        protected abstract bool GetHasWorldCamera();

        /// <summary>Called after SetDirty. Override to trigger Canvas layout rebuild.</summary>
        protected virtual void OnSetDirty(DirtyFlags flags) { }

        /// <summary>Called during DeInit for subclass-specific cleanup (e.g., stencil materials).</summary>
        protected virtual void OnDeInit() { }

        /// <summary>Called when layout cache should be invalidated. Override for ILayoutElement support.</summary>
        protected virtual void InvalidateLayoutCache() { }

#if UNITEXT_TESTS
        /// <summary>Called after meshes are applied, before ReturnInstanceBuffers. Override for test mesh copying.</summary>
        protected virtual void CopyMeshesForTests() { }
#endif

        #endregion

        #region Glyph Query

        /// <summary>
        /// Gets bounding rectangles for a cluster range.
        /// </summary>
        /// <param name="startCluster">Start cluster index (inclusive).</param>
        /// <param name="endCluster">End cluster index (exclusive).</param>
        /// <param name="results">List to receive bounds (cleared before use).</param>
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

        /// <summary>Gets the total number of glyphs.</summary>
        public int GlyphCount => textProcessor?.PositionedGlyphs.Length ?? 0;

        #endregion

#if UNITY_EDITOR

        /// <summary>Style presets we subscribed to Changed event (for correct unsubscription).</summary>
        private readonly List<StylePreset> subscribedConfigs = new();

        private void ListenConfigChanged()
        {
            UniTextSettings.Changed += OnConfigChanged;
            ListenStylePresetChanged();
        }

        private void UnlistenConfigChanged()
        {
            UniTextSettings.Changed -= OnConfigChanged;
            UnlistenStylePresetChanged();
        }

        internal void ListenStylePresetChanged()
        {
            for (var i = 0; i < stylePresets.Count; i++)
            {
                var config = stylePresets[i];
                if (config != null)
                {
                    config.Changed += OnStylePresetChanged;
                    subscribedConfigs.Add(config);
                }
            }
        }

        internal void UnlistenStylePresetChanged()
        {
            for (var i = 0; i < subscribedConfigs.Count; i++)
            {
                var config = subscribedConfigs[i];
                if (config != null)
                    config.Changed -= OnStylePresetChanged;
            }
            subscribedConfigs.Clear();
        }

        private void OnStylePresetChanged()
        {
            ReInitStyles();
        }

        private bool TryInitFonts()
        {
            var changed = false;

            if (fontStack == null)
            {
                fontStack = UniTextSettings.DefaultFontStack;
                changed = true;
            }

            if (changed) UnityEditor.EditorUtility.SetDirty(this);

            return fontStack != null;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            for (var i = componentsBuffer.count - 1; i >= 0; i--)
            {
                var comp = componentsBuffer[i];
                if (comp == null || comp == this)
                {
                    if (comp != null)
                        comp.isRegisteredDirty = false;
                    componentsBuffer.SwapRemoveAt(i);
                }
            }

            UnregisterDirty(this);

            UnityEditor.EditorApplication.update += OnUpdate;

            void OnUpdate()
            {
                UnityEditor.EditorApplication.update -= OnUpdate;
                if (this == null) return;
                UnlistenStylePresetChanged();
                ListenStylePresetChanged();
                ReInitStyles();
            }
        }

#endif
    }
}
