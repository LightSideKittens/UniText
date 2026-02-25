using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Main text rendering component for Unity UI with full Unicode support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UniText is a drop-in replacement for Unity's Text/TextMeshPro with proper support for:
    /// <list type="bullet">
    /// <item>Bidirectional text (Arabic, Hebrew) via UAX #9</item>
    /// <item>Complex script shaping (Devanagari, Thai, etc.) via HarfBuzz</item>
    /// <item>Proper line breaking via UAX #14</item>
    /// <item>Color emoji rendering</item>
    /// <item>Extensible markup system via <see cref="IParseRule"/> (HTML tags, Markdown, custom markers)</item>
    /// </list>
    /// </para>
    /// <para>
    /// The component uses a batched rendering pipeline with optional parallel processing
    /// for multiple UniText instances. Text is processed in two passes: shaping (can be parallel)
    /// and mesh generation (main thread for atlas updates).
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="UniTextMeshGenerator"/>
    /// <seealso cref="BaseModifier"/>
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public partial class UniText : MaskableGraphic
    #if UNITY_EDITOR
        , ISerializationCallbackReceiver
    #endif
    {
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
            /// <summary>Material changed.</summary>
            Material = 1 << 7,
            /// <summary>Layout or font size changed.</summary>
            LayoutRebuild = Layout | FontSize,
            /// <summary>Text, font, or direction changed.</summary>
            FullRebuild = Text | Font | Direction,
            /// <summary>Everything needs rebuilding.</summary>
            All = Color | Alignment | Layout | FontSize | FullRebuild
        }

        #region Serialized Fields

        [TextArea(3, 10)]
        [SerializeField]
        [Tooltip("The text content to display. Supports Unicode, emoji, and custom markup.")]
        private string text = "";

        [NonSerialized] private ReadOnlyMemory<char> sourceText;
        [NonSerialized] private bool isTextFromBuffer;
        
        [SerializeField]
        [Tooltip("Font collection with main font and fallback chain.")]
        private UniTextFontStack fontStack;

        [SerializeField]
        [Tooltip("Material and rendering appearance settings.")]
        private UniTextAppearance appearance;

        [SerializeField]
        [Tooltip("Base font size in points.")]
        private float fontSize = 36f;
        
        [SerializeField]
        [Tooltip("Base text direction. Auto detects from first strong directional character.")]
        private TextDirection baseDirection = TextDirection.Auto;

        [SerializeField]
        [Tooltip("Enable word wrapping at container boundaries.")]
        private bool wordWrap = true;
        
        [SerializeField]
        [Tooltip("Horizontal text alignment within the container.")]
        private HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;

        [SerializeField]
        [Tooltip("Vertical text alignment within the container.")]
        private VerticalAlignment verticalAlignment = VerticalAlignment.Top;

        [SerializeField]
        [Tooltip("Top edge metric for text box trimming. CapHeight removes space above capital letters.")]
        private TextOverEdge overEdge = TextOverEdge.Ascent;

        [SerializeField]
        [Tooltip("Bottom edge metric for text box trimming. Baseline removes space below the last line.")]
        private TextUnderEdge underEdge = TextUnderEdge.Descent;

        [SerializeField]
        [Tooltip("How extra leading from line-height is distributed: HalfLeading (CSS), LeadingAbove (Figma), LeadingBelow (Android).")]
        private LeadingDistribution leadingDistribution = LeadingDistribution.HalfLeading;

        [SerializeField]
        [Tooltip("Automatically adjust font size to fit container.")]
        private bool autoSize;

        [SerializeField]
        [Tooltip("Minimum font size when auto-sizing.")]
        private float minFontSize = 10f;

        [SerializeField]
        [Tooltip("Maximum font size when auto-sizing.")]
        private float maxFontSize = 72f;

        [SerializeField]
        [Tooltip("Modifier/rule pairs that define how markup is parsed and applied (e.g., color, bold, links).")]
        private StyledList<ModRegister> modRegisters = new();

        [SerializeField]
        [Tooltip("Shared modifier configurations (ScriptableObjects) to apply in addition to local modRegisters.")]
        private StyledList<ModRegisterConfig> modRegisterConfigs = new();

        /// <summary>Runtime copies of modRegisterConfigs to avoid ownership conflicts.</summary>
        private readonly List<ModRegisterConfig> runtimeConfigCopies = new();
        
        [SerializeReference]
        [TypeSelector]
        [Tooltip("Text highlighter for visual feedback (click, hover, selection). Set to null to disable.")]
        private TextHighlighter highlighter = new DefaultTextHighlighter();

        #endregion

        #region Runtime State

        private TextProcessor textProcessor;
        private UniTextFontProvider fontProvider;
        private UniTextMeshGenerator meshGenerator;
        private AttributeParser attributeParser;
        private UniTextBuffers buffers;

        private DirtyFlags dirtyFlags = DirtyFlags.All;

        /// <summary>Gets the current dirty flags indicating what needs rebuilding.</summary>
        public DirtyFlags CurrentDirtyFlags => dirtyFlags;
        private bool textIsParsed;
        private bool isRegisteredDirty;

        private float resultWidth;
        private float resultHeight;

        /// <summary>Cached sub-mesh renderer data to avoid GetComponent calls.</summary>
        private struct SubMeshRenderer
        {
            public CanvasRenderer renderer;
            public RectTransform rectTransform;
        }

        private readonly List<SubMeshRenderer> subMeshRenderers = new();
        private readonly List<Material> stencilMaterials = new();
        private List<UniTextRenderData> renderData;

        private Rect cachedClipRect;
        private bool cachedValidClip;
        private Vector4 cachedClipSoftness;
        private int cachedStencilDepth;
        private bool stencilDepthDirty = true;
        private Vector2 lastSyncedPivot;

        private float lastKnownWidth = -1;
        private float lastKnownHeight = -1;
        private RenderMode cachedCanvasRenderMode;

        /// <summary>Raised before text is rebuilt.</summary>
        public event Action Rebuilding;

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

        /// <summary>Gets or sets the text highlighter for visual feedback on interactions.</summary>
        public TextHighlighter Highlighter
        {
            get => highlighter;
            set
            {
                if (highlighter == value) return;
                highlighter?.Destroy();
                highlighter = value;
                highlighter?.Initialize(this);
            }
        }

        /// <summary>Gets the computed size of the rendered text.</summary>
        public Vector2 ResultSize => new(resultWidth, resultHeight);

        /// <summary>Gets the positioned glyphs after processing.</summary>
        public ReadOnlySpan<PositionedGlyph> ResultGlyphs => textProcessor != null ? textProcessor.PositionedGlyphs : ReadOnlySpan<PositionedGlyph>.Empty;

        /// <summary>Gets the main font from the font collection.</summary>
        public UniTextFont MainFont => fontStack?.MainFont;

        /// <summary>Gets the current effective font size (accounts for auto-sizing).</summary>
        public float CurrentFontSize => autoSize
            ? (cachedEffectiveFontSize > 0 ? cachedEffectiveFontSize : maxFontSize)
            : fontSize;

        /// <summary>Gets the list of registered modifiers.</summary>
        public IReadOnlyList<ModRegister> ModRegisters => modRegisters;

        /// <summary>Gets the list of modifier configuration assets.</summary>
        public IReadOnlyList<ModRegisterConfig> ModRegisterConfigs => modRegisterConfigs;

        /// <summary>Gets all canvas renderers used for sub-meshes.</summary>
        public IEnumerable<CanvasRenderer> CanvasRenderers
        {
            get
            {
                for (var i = 0; i < subMeshRenderers.Count; i++)
                    yield return subMeshRenderers[i].renderer;
            }
        }

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
        /// <param name="source">Source character array.</param>
        /// <param name="start">Starting index in the array.</param>
        /// <param name="length">Number of characters to use.</param>
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

        /// <summary>Gets or sets the appearance configuration.</summary>
        public UniTextAppearance Appearance
        {
            get => appearance;
            set
            {
                if (appearance == value) return;
    #if UNITY_EDITOR
                UnlistenConfigChanged();
    #endif

                appearance = value;
                fontProvider.Appearance = value; 
    #if UNITY_EDITOR
                ListenConfigChanged();
    #endif
                SetDirty(DirtyFlags.Material);
            }
        }

        /// <summary>Gets or sets the base font size in points.</summary>
        public float FontSize
        {
            get => fontSize;
            set
            {
                if (Mathf.Approximately(fontSize, value)) return;
                fontSize = Mathf.Max(1f, value);
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
                value = Mathf.Max(1f, value);
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
                value = Mathf.Max(1f, value);
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
        /// <param name="flags">Flags indicating what needs rebuilding.</param>
        public void SetDirty(DirtyFlags flags)
        {
            if (flags == DirtyFlags.None) return;
            Cat.MeowFormat("[UniText] SetDirty: {0}, {1}", flags, name);
            dirtyFlags |= flags;

            if ((flags & DirtyFlags.Font) != 0)
            {
                DeinitializeAllModifiers();
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
            if ((flags & (DirtyFlags.FullRebuild | DirtyFlags.LayoutRebuild)) != 0)
            {
                LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
            }
        }

        #endregion

        #region Modifiers

        /// <summary>Registers a modifier/rule pair for text processing at runtime.</summary>
        /// <param name="register">The modifier registration containing the rule and modifier.</param>
        public void RegisterModifier(ModRegister register)
        {
            if (!register.IsValid) return;

            if (register.IsRegistered && register.Owner == this) return;

            if (register.Owner != null && register.Owner != this)
            {
                Debug.LogError($"[UniText] ModRegister already owned by {register.Owner.name}. Cannot register to {name}.");
                return;
            }

            modRegisters.Add(register);

            if (textProcessor != null)
            {
                EnsureAttributeParserCreated();
                register.Register(this, attributeParser);
                SetDirty(DirtyFlags.Text);
            }
        }

        /// <summary>Unregisters a modifier/rule pair at runtime.</summary>
        /// <param name="register">The modifier registration to remove.</param>
        public bool UnregisterModifier(ModRegister register)
        {
            var removed = modRegisters.Remove(register);
            if (!removed) return false;

            if (register.IsRegistered && register.Owner == this)
            {
                register.Unregister(attributeParser);
                SetDirty(DirtyFlags.Text);
            }

            if (modRegisters.Count == 0 && !HasAnyModRegisterConfigs())
            {
                DestroyAttributeParser();
            }

            return true;
        }

        /// <summary>Removes all registered modifiers.</summary>
        public void ClearModifiers()
        {
            for (var i = 0; i < modRegisters.Count; i++)
            {
                modRegisters[i].Unregister(attributeParser);
            }
            modRegisters.Clear();
            DestroyAttributeParser();
        }

        /// <summary>
        /// Registers a ModRegister with the parser. Called by ModRegister during hot-swap.
        /// </summary>
        internal void RegisterModifierWithParser(ModRegister register)
        {
            if (attributeParser == null) return;
            register.Register(this, attributeParser);
        }

        /// <summary>
        /// Unregisters a ModRegister from the parser. Called by ModRegister during hot-swap.
        /// </summary>
        internal void UnregisterModifierFromParser(ModRegister register)
        {
            register.Unregister(attributeParser);
        }

        /// <summary>Reinitializes all registered modifiers (used by Editor/OnValidate).</summary>
        private void ReInitModifiers()
        {
            DestroyAttributeParser();
            EnsureAttributeParserCreated();
        }

        /// <summary>Deinitializes all modifiers but keeps them registered (for font changes).</summary>
        private void DeinitializeAllModifiers()
        {
            for (var i = 0; i < modRegisters.Count; i++)
            {
                modRegisters[i].DeinitializeModifier();
            }
            for (var i = 0; i < runtimeConfigCopies.Count; i++)
            {
                var config = runtimeConfigCopies[i];
                for (var j = 0; j < config.modRegisters.Count; j++)
                {
                    config.modRegisters[j].DeinitializeModifier();
                }
            }
        }

        /// <summary>Resets all ModRegister states (for deserialization/Editor reload).</summary>
        private void ResetAllModRegisterStates()
        {
            for (var i = 0; i < modRegisters.Count; i++)
            {
                modRegisters[i].ResetState();
            }
            for (var i = 0; i < runtimeConfigCopies.Count; i++)
            {
                var config = runtimeConfigCopies[i];
                for (var j = 0; j < config.modRegisters.Count; j++)
                {
                    config.modRegisters[j].ResetState();
                }
            }
        }

        private void EnsureAttributeParserCreated()
        {
            if (attributeParser != null) return;
            if (textProcessor == null) return;

            if (modRegisters is { Count: > 0 } || HasAnyModRegisterConfigs())
            {
                EnsureRuntimeConfigCopiesCreated();

                attributeParser = new AttributeParser();
                RegisterModsWithParser(modRegisters);
                for (var i = 0; i < runtimeConfigCopies.Count; i++)
                {
                    RegisterModsWithParser(runtimeConfigCopies[i].modRegisters);
                }
                textProcessor.Parsed += attributeParser.Apply;
                SetDirty(DirtyFlags.Text);
            }
        }

        private void EnsureRuntimeConfigCopiesCreated()
        {
            if (runtimeConfigCopies.Count > 0) return;

            for (var i = 0; i < modRegisterConfigs.Count; i++)
            {
                var config = modRegisterConfigs[i];
                if (config != null)
                {
                    runtimeConfigCopies.Add(Instantiate(config));
                }
            }
        }

        private bool HasAnyModRegisterConfigs()
        {
            for (var i = 0; i < modRegisterConfigs.Count; i++)
            {
                var config = modRegisterConfigs[i];
                if (config != null && config.modRegisters is { Count: > 0 })
                    return true;
            }
            return false;
        }

        /// <summary>Registers all valid ModRegisters with the parser.</summary>
        private void RegisterModsWithParser(StyledList<ModRegister> mods)
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
            ResetAllModRegisterStates();
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
            CollectExistingSubMeshRenderers();
            cachedCanvasRenderMode = canvas != null ? canvas.renderMode : RenderMode.ScreenSpaceOverlay;
            SetDirty(DirtyFlags.All);
            highlighter?.Initialize(this);
        }

        /// <summary>
        /// Ensures the parent Canvas provides vertex data channels required by UniText shaders.
        /// Without TexCoord1, spreadRatio is zero → normFactor becomes 100x too large →
        /// all SDF effects (outline, underlay) are massively distorted.
        /// </summary>
        private static void EnsureCanvasShaderChannels(Canvas c)
        {
            const AdditionalCanvasShaderChannels required =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.Normal;

            var current = c.additionalShaderChannels;
            var missing = required & ~current;
            if (missing != 0)
                c.additionalShaderChannels = current | missing;
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
            highlighter?.Destroy();
            DeInit();
            DestroyRuntimeConfigCopies();
        }

        private void Update()
        {
            highlighter?.Update();
            var c = canvas;
            
            if (c != null)
            {
                EnsureCanvasShaderChannels(c);

                var mode = c.renderMode;
                if (mode != cachedCanvasRenderMode)
                {
                    cachedCanvasRenderMode = mode;
                    SetDirty(DirtyFlags.Alignment);
                }
            }
        }

        private void DestroyRuntimeConfigCopies()
        {
            for (var i = 0; i < runtimeConfigCopies.Count; i++)
            {
                ObjectUtils.SafeDestroy(runtimeConfigCopies[i]);
            }
            runtimeConfigCopies.Clear();
        }

        private void Sub()
        {
            if (fontStack != null) fontStack.Changed += OnConfigChanged;
#if UNITY_EDITOR
            ListenConfigChanged();
#endif
            EmojiFont.DisableChanged += OnEmojiFontDisableChanged;
        }

        private void UnSub()
        {
            if (fontStack != null) fontStack.Changed -= OnConfigChanged;
#if UNITY_EDITOR
            UnlistenConfigChanged();
#endif
            EmojiFont.DisableChanged -= OnEmojiFontDisableChanged;
        }

        private void DeInit()
        {
            ClearAllRenderers();
            DestroyAttributeParser();
            MeshApplied?.Invoke();

            textProcessor = null;
            fontProvider = null;
            meshGenerator?.Dispose();
            meshGenerator = null;

            ReleaseSubMeshStencilMaterials();
            buffers?.EnsureReturnBuffers();
            UnregisterDirty(this);
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


    #if UNITY_EDITOR

        /// <summary>Configs we subscribed to Changed event (for correct unsubscription).</summary>
        private readonly List<ModRegisterConfig> subscribedConfigs = new();

        private void ListenConfigChanged()
        {
            UniTextSettings.Changed += OnConfigChanged;
            if (appearance != null) appearance.Changed += OnConfigChanged;
            ListenModRegisterConfig();
        }

        private void UnlistenConfigChanged()
        {
            UniTextSettings.Changed -= OnConfigChanged;
            if (appearance != null) appearance.Changed -= OnConfigChanged;
            UnlistenModRegisterConfig();
        }

        internal void ListenModRegisterConfig()
        {
            for (var i = 0; i < modRegisterConfigs.Count; i++)
            {
                var config = modRegisterConfigs[i];
                if (config != null)
                {
                    config.Changed += OnModRegisterConfigChanged;
                    subscribedConfigs.Add(config);
                }
            }
        }

        internal void UnlistenModRegisterConfig()
        {
            for (var i = 0; i < subscribedConfigs.Count; i++)
            {
                var config = subscribedConfigs[i];
                if (config != null)
                    config.Changed -= OnModRegisterConfigChanged;
            }
            subscribedConfigs.Clear();
        }

        private void OnModRegisterConfigChanged()
        {
            ReInitModifiers();
        }

        private bool TryInitFontsAndAppearance()
        {
            var changed = false;

            if (fontStack == null)
            {
                fontStack = UniTextSettings.DefaultFontStack;
                changed = true;
            }

            if (appearance == null)
            {
                appearance = UniTextSettings.DefaultAppearance;
                changed = true;
            }

            if (changed) UnityEditor.EditorUtility.SetDirty(this);

            return fontStack != null && appearance != null;
        }
    #endif

        private void OnConfigChanged()
        {
            SetDirty(DirtyFlags.All);
        }
        
        #endregion

        #region Rebuild

        /// <inheritdoc/>
        public override void Rebuild(CanvasUpdate update) { }

        private bool ValidateAndInitialize()
        {
            UniTextDebug.BeginSample("UniText.ValidateAndInitialize");

    #if UNITY_EDITOR
            if (!TryInitFontsAndAppearance())
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
                fontProvider = new UniTextFontProvider(fontStack, appearance);
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

        #region Rendering

        private void UpdateRendering()
        {
            UniTextDebug.BeginSample("UniText.UpdateRendering");

            if (renderData == null || renderData.Count == 0)
            {
                ClearAllRenderers();
                UniTextDebug.EndSample();
                return;
            }

            UpdateSubMeshes();

            UniTextDebug.EndSample();
        }

        protected override void UpdateMaterial() { }

        /// <summary>Sets the clipping rectangle for masking, applying to all sub-mesh renderers.</summary>
        /// <inheritdoc/>
        public override void SetClipRect(Rect clipRect, bool validRect)
        {
            base.SetClipRect(clipRect, validRect);
            cachedClipRect = clipRect;
            cachedValidClip = validRect;

            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r == null) continue;
                if (validRect) r.EnableRectClipping(clipRect);
                else
                {
                    r.DisableRectClipping();
                    r.cull = false;
                }
            }
        }

        /// <summary>Sets soft clipping edges for smooth mask transitions on all sub-mesh renderers.</summary>
        /// <inheritdoc/>
        public override void SetClipSoftness(Vector2 clipSoftness)
        {
            base.SetClipSoftness(clipSoftness);
            cachedClipSoftness = new Vector4(clipSoftness.x, clipSoftness.y, 0, 0);

            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r != null) r.clippingSoftness = cachedClipSoftness;
            }
        }

        /// <summary>Applies visibility culling to all sub-mesh renderers based on clip rect.</summary>
        /// <inheritdoc/>
        public override void Cull(Rect clipRect, bool validRect)
        {
            base.Cull(clipRect, validRect);
            var cull = canvasRenderer != null && canvasRenderer.cull;

            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r != null) r.cull = cull;
            }
        }

        /// <summary>Recalculates stencil masking, releasing cached stencil materials.</summary>
        /// <inheritdoc/>
        public override void RecalculateMasking()
        {
            base.RecalculateMasking();
            stencilDepthDirty = true;
            ReleaseSubMeshStencilMaterials();
            SetDirty(DirtyFlags.Material);
        }

        #endregion

        #region Sub-mesh Management

        private void CollectExistingSubMeshRenderers()
        {
            subMeshRenderers.Clear();
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("-_UTSM_-"))
                {
                    var r = child.GetComponent<CanvasRenderer>();
                    var rt = child.GetComponent<RectTransform>();
                    if (r != null) subMeshRenderers.Add(new SubMeshRenderer { renderer = r, rectTransform = rt });
                }
            }
        }

        private void UpdateSubMeshes()
        {
            UniTextDebug.BeginSample("UniText.UpdateSubMeshes");

            var requiredCount = renderData.Count;
            var existingCount = subMeshRenderers.Count;

            var currentPivot = rectTransform.pivot;
            if (currentPivot != lastSyncedPivot)
            {
                lastSyncedPivot = currentPivot;
                for (var i = 0; i < existingCount; i++)
                    subMeshRenderers[i].rectTransform.pivot = currentPivot;
            }

            if (stencilDepthDirty)
            {
                cachedStencilDepth = 0;
                if (maskable)
                {
                    var rootCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
                    cachedStencilDepth = MaskUtilities.GetStencilDepth(transform, rootCanvas);
                }
                stencilDepthDirty = false;
            }
            var stencilDepth = cachedStencilDepth;

            for (var i = requiredCount; i < existingCount; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r != null) { r.Clear(); r.gameObject.SetActive(false); }
            }

            for (var i = 0; i < requiredCount; i++)
            {
                var pair = renderData[i];

                if (i < existingCount)
                {
                    var r = subMeshRenderers[i].renderer;
                    if (r != null)
                    {
                        if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                        SetSubMeshRendererData(r, pair.mesh, pair.materials, pair.texture, i, stencilDepth);
                        continue;
                    }
                }

                var newR = CreateSubMeshRenderer(i, pair.mesh, pair.materials, pair.texture, stencilDepth);
                if (i < existingCount) subMeshRenderers[i] = newR;
                else subMeshRenderers.Add(newR);
            }

            UniTextDebug.EndSample();
        }

        private void SetSubMeshRendererData(CanvasRenderer r, Mesh mesh, Material[] mats, Texture tex, int subMeshIndex, int stencilDepth)
        {
            if (mesh == null || mesh.vertexCount == 0) { r.Clear(); return; }

            r.SetMesh(mesh);

            var matCount = mats?.Length ?? 0;
            if (matCount == 0)
            {
                r.materialCount = 0;
                return;
            }

            r.materialCount = matCount;

            for (var i = 0; i < matCount; i++)
            {
                var mat = mats[i];
                var matToUse = mat;

                if (stencilDepth > 0 && mat != null)
                {
                    var stencilId = (1 << stencilDepth) - 1;
                    var stencilMat = StencilMaterial.Add(mat, stencilId, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, stencilId, 0);

                    var stencilIndex = subMeshIndex * 2 + i;
                    while (stencilMaterials.Count <= stencilIndex) stencilMaterials.Add(null);
                    if (stencilMaterials[stencilIndex] != null) StencilMaterial.Remove(stencilMaterials[stencilIndex]);

                    stencilMaterials[stencilIndex] = stencilMat;
                    matToUse = stencilMat;
                }
                
                r.SetMaterial(matToUse, i);
            }

            r.SetTexture(tex);
        }


        private SubMeshRenderer CreateSubMeshRenderer(int index, Mesh mesh, Material[] mats, Texture tex, int stencilDepth)
        {
            var go = new GameObject("-_UTSM_-") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.pivot = rectTransform.pivot;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var r = go.AddComponent<CanvasRenderer>();
            SetSubMeshRendererData(r, mesh, mats, tex, index, stencilDepth);

            if (cachedValidClip) r.EnableRectClipping(cachedClipRect);
            r.clippingSoftness = cachedClipSoftness;
            r.cull = subMeshRenderers.Count > 0 && subMeshRenderers[0].renderer != null && subMeshRenderers[0].renderer.cull;

            return new SubMeshRenderer { renderer = r, rectTransform = rt };
        }

        #endregion

        #region Cleanup

        private void ClearAllRenderers()
        {
            for (var i = 0; i < subMeshRenderers.Count; i++) subMeshRenderers[i].renderer?.Clear();
        }

        private void ReleaseSubMeshStencilMaterials()
        {
            for (var i = 0; i < stencilMaterials.Count; i++)
            {
                if (stencilMaterials[i] != null)
                {
                    StencilMaterial.Remove(stencilMaterials[i]);
                    stencilMaterials[i] = null;
                }
            }
            stencilMaterials.Clear();
        }

        #endregion

    #if UNITY_EDITOR
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
                if(this ==  null) return;
                UnlistenModRegisterConfig();
                ListenModRegisterConfig();
                ReInitModifiers();
            }
        }
        
    #endif
    }

}
