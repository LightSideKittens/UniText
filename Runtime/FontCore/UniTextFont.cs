using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Font asset containing glyph data, metrics, and texture atlases for text rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UniTextFont is a ScriptableObject that stores:
    /// <list type="bullet">
    /// <item>Font file data (TTF/OTF bytes) for FreeType rendering</item>
    /// <item>Face information (metrics, ascender, descender)</item>
    /// <item>Glyph table with UV coordinates and metrics</item>
    /// <item>SDF texture atlas(es) for rendering</item>
    /// </list>
    /// </para>
    /// <para>
    /// Glyphs are rendered to the atlas at runtime when first needed.
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextFontProvider"/>
    /// <seealso cref="UniTextFontStack"/>
    [Serializable]
    public class UniTextFont : ScriptableObject
    {

        #region Serialized Fields

        [SerializeField]
        [Tooltip("Raw font file data (TTF/OTF bytes).")]
        protected byte[] fontData;

        [SerializeField]
        [Tooltip("Hash of font data for identification.")]
        protected int fontDataHash;

        [SerializeField]
        [Tooltip("Path to source font file (Editor only).")]
        private string sourceFontFilePath;

        [SerializeField]
        [Tooltip("Italic slant angle in degrees.")]
        private float italicStyle = 30;

        [SerializeField]
        [Tooltip("Font face metrics (ascender, descender, line height, etc.).")]
        internal FaceInfo faceInfo;

        [SerializeField]
        [Tooltip("Font design units per em (typically 1000 or 2048).")]
        internal int unitsPerEm = 1000;

        [SerializeField]
        [Tooltip("Visual scale multiplier for this font. Use to normalize fonts that appear too small or too large by design (e.g. Dongle). Applied after all metric conversions.")]
        [Range(0.1f, 5f)]
        internal float fontScale = 1f;

        [NonSerialized]
        internal List<Glyph> glyphTable = new();

        [NonSerialized]
        internal List<UniTextCharacter> characterTable = new();

        [NonSerialized]
        internal List<Texture2D> atlasTextures = new();

        [SerializeField]
        [Tooltip("Atlas texture size in pixels (square).")]
        internal int atlasSize = 1024;

        [SerializeField]
        [Tooltip("SDF spread as a fraction of point size (0-1). Padding = PointSize * SpreadStrength.")]
        [Range(0.1f, 1f)]
        internal float spreadStrength = 0.25f;

        [SerializeField]
        [Tooltip("Glyph rendering mode (SDF, bitmap, etc.).")]
        internal UniTextRenderMode atlasRenderMode = UniTextRenderMode.SDF;

        [NonSerialized]
        protected List<GlyphRect> usedGlyphRects;

        [NonSerialized]
        protected List<GlyphRect> freeGlyphRects;

        #endregion

        #region Runtime Fields

        private static readonly HashSet<UniTextFont> loadedFonts = new();

        internal Dictionary<uint, Glyph> glyphLookupDictionary;
        internal Dictionary<uint, UniTextCharacter> characterLookupDictionary;

        protected List<uint> glyphIndexList = new();

        private int cachedFaceIndex = -1;
        private int cachedInstanceId;
        public string CachedName { get; private set; }

        [ThreadStatic] private static HashSet<uint> toAddSet;

        protected const int PackingSpacing = 1;
        [NonSerialized] protected int shelfX;
        [NonSerialized] protected int shelfY;
        [NonSerialized] protected int shelfHeight;

        [NonSerialized] private IntPtr ftFace;

#if !UNITY_WEBGL || UNITY_EDITOR
        [NonSerialized] private FreeTypeFacePool sdfFacePool;
#endif

        [ThreadStatic] private static List<uint> toAddList;

        internal event Action Changed;
        
        #endregion

        /// <summary>Gets the cached Unity instance ID, initializing on first access.</summary>
        /// <returns>The font asset's instance ID for use as a dictionary key.</returns>
        public virtual int GetCachedInstanceId()
        {
            if (cachedInstanceId == 0)
            {
                cachedInstanceId = GetInstanceID();
                CachedName = name;
            }

            return cachedInstanceId;
        }

        #region Properties

        /// <summary>Gets the raw font file data (TTF/OTF bytes).</summary>
        public virtual byte[] FontData => fontData;

        /// <summary>Gets the italic slant angle in degrees.</summary>
        public float ItalicStyle => italicStyle;

        /// <summary>Gets the hash of the font data for identification.</summary>
        public virtual int FontDataHash => fontDataHash;

        /// <summary>Returns true if font file data is available.</summary>
        public virtual bool HasFontData => fontData != null && fontData.Length > 0;

        /// <summary>
        /// Computes a hash of font file data for identification.
        /// </summary>
        /// <param name="data">Font file bytes.</param>
        /// <returns>Hash value, or 0 if data is null/empty.</returns>
        public static int ComputeFontDataHash(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            unchecked
            {
                var hash = -2128831035;
                var len = data.Length;
                var step = len > 4096 ? len / 1024 : 1;
                for (var i = 0; i < len; i += step)
                    hash = (hash ^ data[i]) * 16777619;
                return (hash ^ len) * 16777619;
            }
        }


        /// <summary>Gets or sets the font face information (metrics, ascender, descender, etc.).</summary>
        public FaceInfo FaceInfo
        {
            get => faceInfo;
            internal set => faceInfo = value;
        }

        /// <summary>Gets or sets the font design units per em (typically 1000 or 2048).</summary>
        /// <remarks>
        /// This is the fundamental scaling unit for font metrics. All glyph measurements
        /// are expressed relative to this value. Industry standard values are 1000 (CFF/OTF)
        /// or 2048 (TrueType). Used for correct scaling: scale = fontSize / unitsPerEm.
        /// </remarks>
        public int UnitsPerEm
        {
            get => unitsPerEm > 0 ? unitsPerEm : 1000;
            internal set => unitsPerEm = value > 0 ? value : 1000;
        }

        /// <summary>Visual scale multiplier for this font asset.</summary>
        /// <remarks>
        /// Use to normalize fonts that appear too small or too large by design.
        /// For example, Dongle font renders visually smaller than other fonts at the same size —
        /// setting FontScale to 1.5 compensates for this. Applied as a post-conversion multiplier
        /// in all fontSize/UnitsPerEm scaling calculations.
        /// </remarks>
        public float FontScale
        {
            get => fontScale > 0f ? fontScale : 1f;
            set => fontScale = value > 0f ? value : 1f;
        }

        /// <summary>Gets the primary atlas texture.</summary>
        public Texture2D AtlasTexture
        {
            get
            {
                if (atlasTextures != null && atlasTextures.Count > 0)
                    return atlasTextures[0];
                return null;
            }
        }

        /// <summary>Gets or sets all atlas textures (multiple atlases for large character sets).</summary>
        public List<Texture2D> AtlasTextures => atlasTextures;

        /// <summary>Gets the atlas texture size in pixels (square).</summary>
        public int AtlasSize => atlasSize;

        /// <summary>Gets the SDF spread strength (0-1). Padding = PointSize * SpreadStrength.</summary>
        public float SpreadStrength => spreadStrength;

        /// <summary>Gets the padding between glyphs in the atlas. For SDF: computed from SpreadStrength. For COLOR/bitmap: minimal.</summary>
        public int AtlasPadding
        {
            get
            {
                if (atlasRenderMode != UniTextRenderMode.SDF)
                    return 1;
                return Mathf.Max(1, Mathf.RoundToInt(faceInfo.pointSize * spreadStrength));
            }
        }

        /// <summary>Gets the glyph render mode (SDF, bitmap, etc.).</summary>
        public UniTextRenderMode AtlasRenderMode => atlasRenderMode;

        /// <summary>Gets the glyph lookup table (glyph index → Glyph).</summary>
        public Dictionary<uint, Glyph> GlyphLookupTable
        {
            get
            {
                if (glyphLookupDictionary == null)
                {
                    Cat.MeowFormat("[GlyphLookupTable] {0}: dict is NULL, calling ReadFontAssetDefinition", name);
                    ReadFontAssetDefinition();
                }
                return glyphLookupDictionary;
            }
        }

        /// <summary>Gets the character lookup table (unicode → UniTextCharacter).</summary>
        internal Dictionary<uint, UniTextCharacter> CharacterLookupTable
        {
            get
            {
                if (characterLookupDictionary == null)
                {
                    Cat.MeowFormat("[CharacterLookupTable] {0}: dict is NULL, calling ReadFontAssetDefinition. glyphLookup={1}, glyphTable={2}, atlas={3}",
                        name, glyphLookupDictionary?.Count ?? -1, glyphTable?.Count ?? -1, atlasTextures?.Count ?? -1);
                    ReadFontAssetDefinition();
                }
                return characterLookupDictionary;
            }
        }
        
        public bool IsColor => this is EmojiFont;

        internal int GlyphLookupDiagCount => glyphLookupDictionary?.Count ?? -1;
        internal int AtlasTexturesDiagCount => atlasTextures?.Count ?? -1;
        internal int GlyphTableDiagCount => glyphTable?.Count ?? -1;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes lookup dictionaries from serialized glyph and character tables.
        /// </summary>
        public void ReadFontAssetDefinition()
        {
            Cat.MeowFormat("[ReadFontAssetDefinition] {0}: CALLED. glyphTable={1}, glyphLookup={2}, charLookup={3}, atlas={4}",
                name,
                glyphTable?.Count ?? -1,
                glyphLookupDictionary?.Count ?? -1,
                characterLookupDictionary?.Count ?? -1,
                atlasTextures?.Count ?? -1);
            Cat.MeowFormat("[ReadFontAssetDefinition] {0}: stacktrace:\n{1}", name, UnityEngine.StackTraceUtility.ExtractStackTrace());
            InitializeGlyphLookupDictionary();
            InitializeCharacterLookupDictionary();
            AddSynthesizedCharacters();
            Cat.MeowFormat("[ReadFontAssetDefinition] {0}: DONE. glyphLookup={1}, charLookup={2}",
                name, glyphLookupDictionary?.Count ?? -1, characterLookupDictionary?.Count ?? -1);
        }

        private void InitializeGlyphLookupDictionary()
        {
            glyphLookupDictionary ??= new Dictionary<uint, Glyph>();
            glyphLookupDictionary.Clear();

            glyphIndexList ??= new List<uint>();
            glyphIndexList.Clear();

            if (glyphTable == null) return;

            int zeroRectCount = 0;
            for (var i = 0; i < glyphTable.Count; i++)
            {
                var glyph = glyphTable[i];
                var index = glyph.index;

                if (glyphLookupDictionary.TryAdd(index, glyph))
                {
                    glyphIndexList.Add(index);
                    var r = glyph.glyphRect;
                    if (r.width == 0 || r.height == 0)
                        zeroRectCount++;
                }
            }

            if (glyphTable.Count > 0)
                Cat.MeowFormat("[InitGlyphLookup] {0}: read {1} from glyphTable, {2} zero-rect, atlas={3}",
                    name, glyphLookupDictionary.Count, zeroRectCount, atlasTextures?.Count ?? -1);
        }

        private void InitializeCharacterLookupDictionary()
        {
            characterLookupDictionary ??= new Dictionary<uint, UniTextCharacter>();
            characterLookupDictionary.Clear();

            if (characterTable == null) return;

            for (var i = 0; i < characterTable.Count; i++)
            {
                var character = characterTable[i];
                var unicode = character.unicode;

                if (characterLookupDictionary.TryAdd(unicode, character))
                {
                    if (glyphLookupDictionary.TryGetValue(character.glyphIndex, out var glyph))
                        character.glyph = glyph;
                }
            }
        }

        private void AddSynthesizedCharacters()
        {
            var fontLoaded = LoadFontFace() == UniTextFontError.Success;

            AddSynthesizedCharacter(UnicodeData.Tab, fontLoaded, true);
            AddSynthesizedCharacter(UnicodeData.LineFeed, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.CarriageReturn, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.ZeroWidthSpace, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.LeftToRightMark, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.RightToLeftMark, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.LineSeparator, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.ParagraphSeparator, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.WordJoiner, fontLoaded);
            AddSynthesizedCharacter(UnicodeData.ArabicLetterMark, fontLoaded);
        }

        private void AddSynthesizedCharacter(int unicode, bool fontLoaded, bool addImmediately = false)
        {
            var cp = (uint)unicode;

            if (characterLookupDictionary.ContainsKey(cp))
                return;

            Glyph glyph;

            if (fontLoaded)
            {
                var glyphIdx = Shaper.GetGlyphIndex(this, cp);
                if (glyphIdx != 0)
                {
                    if (!addImmediately) return;

                    var face = EnsureFTFace();
                    if (face != IntPtr.Zero)
                    {
                        var pointSize = faceInfo.pointSize > 0 ? faceInfo.pointSize : 90;
                        FT.SetPixelSize(face, pointSize);
                        if (FT.LoadGlyph(face, glyphIdx, FT.LOAD_DEFAULT | FT.LOAD_NO_BITMAP))
                        {
                            var ftMetrics = FT.GetGlyphMetrics(face);
                            var metricsConversion = pointSize > 0 && pointSize != unitsPerEm
                                ? (float)unitsPerEm / pointSize : 1f;
                            var advance = (ftMetrics.advanceX / 64f) * metricsConversion;
                            glyph = new Glyph(glyphIdx,
                                new GlyphMetrics(
                                    ftMetrics.width * metricsConversion,
                                    ftMetrics.height * metricsConversion,
                                    ftMetrics.bearingX * metricsConversion,
                                    ftMetrics.bearingY * metricsConversion,
                                    advance),
                                GlyphRect.zero, 0);
                            characterLookupDictionary.Add(cp, new UniTextCharacter(cp, glyph));
                        }
                    }

                    return;
                }
            }

            glyph = new Glyph(0, new GlyphMetrics(0, 0, 0, 0, 0), GlyphRect.zero, 0);
            characterLookupDictionary.Add(cp, new UniTextCharacter(cp, glyph));
        }

        #endregion

        #region Font Loading

        /// <summary>
        /// Ensures a FreeType face handle is loaded for this font asset.
        /// </summary>
        /// <returns>FT_Face handle, or IntPtr.Zero if loading failed.</returns>
        protected IntPtr EnsureFTFace()
        {
            if (ftFace != IntPtr.Zero)
                return ftFace;

            if (fontData == null || fontData.Length == 0)
                return IntPtr.Zero;

            if (!FT.IsInitialized)
                FT.Initialize();

            if (cachedFaceIndex < 0)
                cachedFaceIndex = faceInfo.faceIndex;

            ftFace = FT.LoadFace(fontData, cachedFaceIndex < 0 ? 0 : cachedFaceIndex);
            Cat.MeowFormat("[EnsureFTFace] {0}: loaded face={1}", name, ftFace != IntPtr.Zero);
            return ftFace;
        }

        /// <summary>
        /// Releases the FreeType face handle if loaded.
        /// </summary>
        protected void ReleaseFTFace()
        {
            if (ftFace != IntPtr.Zero)
            {
                FT.UnloadFace(ftFace);
                ftFace = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Loads the font face for glyph operations.
        /// </summary>
        /// <returns>Success if the font was loaded, error code otherwise.</returns>
        public virtual UniTextFontError LoadFontFace()
        {
            return EnsureFTFace() != IntPtr.Zero
                ? UniTextFontError.Success
                : UniTextFontError.InvalidFile;
        }

        #endregion

        #region Dynamic Character Loading

        /// <summary>
        /// Gets the glyph index for a Unicode codepoint.
        /// </summary>
        /// <param name="unicode">Unicode codepoint.</param>
        /// <returns>Glyph index, or 0 if the glyph is not available.</returns>
        public uint GetGlyphIndexForUnicode(uint unicode)
        {
            uint glyphIndex = 0;

            if (HasFontData)
                glyphIndex = Shaper.GetGlyphIndex(this, unicode);

            if (glyphIndex == 0)
            {
                uint specialCodepoint = unicode switch
                {
                    UnicodeData.NoBreakSpace => UnicodeData.Space,
                    UnicodeData.SoftHyphen => UnicodeData.Hyphen,
                    UnicodeData.NonBreakingHyphen => UnicodeData.Hyphen,
                    _ => 0
                };

                if (specialCodepoint != 0 && HasFontData)
                    glyphIndex = Shaper.GetGlyphIndex(this, specialCodepoint);
            }

            return glyphIndex;
        }

        /// <summary>
        /// Registers character-to-glyph mappings for later lookup.
        /// </summary>
        /// <param name="entries">List of (unicode, glyphIndex) pairs.</param>
        public void RegisterCharacterEntries(List<(uint unicode, uint glyphIndex)> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            if (characterLookupDictionary == null)
                ReadFontAssetDefinition();

            characterTable ??= new List<UniTextCharacter>();

            for (int i = 0; i < entries.Count; i++)
            {
                var (unicode, glyphIndex) = entries[i];

                if (characterLookupDictionary.ContainsKey(unicode))
                    continue;

                if (!glyphLookupDictionary.TryGetValue(glyphIndex, out var glyph))
                    continue;

                var character = new UniTextCharacter(unicode, glyphIndex) { glyph = glyph };
                characterTable.Add(character);
                characterLookupDictionary[unicode] = character;
            }
        }

        /// <summary>
        /// Filters glyph indices, removing zeros and already-known glyphs.
        /// Returns a reusable list of unique indices to add, or null if nothing to add.
        /// </summary>
        protected List<uint> FilterNewGlyphs(List<uint> glyphIndices)
        {
            toAddSet ??= new HashSet<uint>();
            toAddSet.Clear();
            for (var i = 0; i < glyphIndices.Count; i++)
            {
                var idx = glyphIndices[i];
                if (glyphLookupDictionary == null || !glyphLookupDictionary.ContainsKey(idx))
                    toAddSet.Add(idx);
            }

            if (toAddSet.Count == 0)
                return null;

            toAddList ??= new List<uint>(256);
            toAddList.Clear();
            foreach (var idx in toAddSet)
                toAddList.Add(idx);

            return toAddList;
        }

        /// <summary>
        /// Checks if a glyph is already rasterized in the atlas.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasGlyphInAtlas(uint glyphIndex)
        {
            return glyphLookupDictionary != null && glyphLookupDictionary.ContainsKey(glyphIndex);
        }

        /// <summary>
        /// Prepared batch data for the split rendering pipeline.
        /// Created by <see cref="PrepareGlyphBatch"/>, consumed by <see cref="RenderPreparedBatch"/> and <see cref="PackRenderedBatch"/>.
        /// </summary>
        public struct PreparedBatch
        {
            public List<uint> filteredGlyphs;
            public int pointSize;
            public int spread;
            public float metricsConversion;
        }

        /// <summary>
        /// Phase 1: Filters glyph indices and prepares rendering parameters.
        /// Must be called on main thread (reads glyphLookupDictionary, ensures font resources).
        /// </summary>
        /// <param name="glyphIndices">Glyph indices to add.</param>
        /// <returns>Prepared batch, or null if nothing to add.</returns>
        public virtual PreparedBatch? PrepareGlyphBatch(List<uint> glyphIndices)
        {
            if (glyphIndices == null || glyphIndices.Count == 0)
                return null;

            if (glyphLookupDictionary == null)
                ReadFontAssetDefinition();

            if (fontData == null || fontData.Length == 0)
                return null;

            var toAdd = FilterNewGlyphs(glyphIndices);
            if (toAdd == null)
                return null;

            var pointSize = faceInfo.pointSize > 0 ? faceInfo.pointSize : 90;
            var spread = AtlasPadding;
            var metricsConversion = pointSize > 0 && pointSize != unitsPerEm
                ? (float)unitsPerEm / pointSize
                : 1f;

            glyphLookupDictionary ??= new Dictionary<uint, Glyph>();
            glyphTable ??= new List<Glyph>();
            glyphIndexList ??= new List<uint>();

#if !UNITY_WEBGL || UNITY_EDITOR
            sdfFacePool ??= new FreeTypeFacePool(fontData, cachedFaceIndex < 0 ? 0 : cachedFaceIndex, pointSize);
#else
            if (EnsureFTFace() == IntPtr.Zero) return null;
#endif

            var owned = new List<uint>(toAdd.Count);
            owned.AddRange(toAdd);

            return new PreparedBatch
            {
                filteredGlyphs = owned,
                pointSize = pointSize,
                spread = spread,
                metricsConversion = metricsConversion
            };
        }

        /// <summary>
        /// Phase 2: Renders glyphs to SDF bitmaps. Can run on any thread.
        /// </summary>
        /// <param name="batch">Prepared batch from <see cref="PrepareGlyphBatch"/>.</param>
        /// <returns>Rendered glyph data (SdfRenderedGlyph[] for SDF fonts). Null on failure.</returns>
        public virtual object RenderPreparedBatch(PreparedBatch batch)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            return sdfFacePool.RenderSdfBatch(batch.filteredGlyphs, batch.pointSize,
                FT.LOAD_DEFAULT | FT.LOAD_NO_BITMAP, batch.spread);
#else
            var face = EnsureFTFace();
            if (face == IntPtr.Zero) return null;
            var rendered = new SdfRenderedGlyph[batch.filteredGlyphs.Count];
            for (int i = 0; i < batch.filteredGlyphs.Count; i++)
                SdfGlyphRenderer.TryRender(face, batch.filteredGlyphs[i], batch.pointSize,
                    FT.LOAD_DEFAULT | FT.LOAD_NO_BITMAP, batch.spread, out rendered[i]);
            return rendered;
#endif
        }

        /// <summary>
        /// Phase 3: Packs rendered glyphs into atlas textures and updates lookup dictionaries.
        /// Must be called on main thread (Unity API + dictionary mutation).
        /// </summary>
        /// <param name="renderedObj">Rendered glyph data from <see cref="RenderPreparedBatch"/>.</param>
        /// <param name="batch">Prepared batch from <see cref="PrepareGlyphBatch"/>.</param>
        /// <returns>Number of glyphs successfully added.</returns>
        public virtual int PackRenderedBatch(object renderedObj, PreparedBatch batch)
        {
            if (renderedObj == null) return 0;
            var rendered = (SdfRenderedGlyph[])renderedObj;

            var metricsConversion = batch.metricsConversion;
            var spread = batch.spread;
            int totalAdded = 0;

            unsafe
            {
            byte* cachedAtlasPtr = null;
            int cachedAtlasW = 0;
            Texture2D cachedAtlasTex = null;

            for (int i = 0; i < rendered.Length; i++)
            {
                ref var r = ref rendered[i];
                if (!r.isValid) continue;

                var glyphIndex = r.glyphIndex;
                if (glyphLookupDictionary.ContainsKey(glyphIndex))
                {
                    ReturnSdfPixels(ref r);
                    continue;
                }

                float advanceDU = (r.metricAdvanceX26_6 / 64f) * metricsConversion;

                if (r.sdfPixels == null)
                {
                    var glyph = new Glyph(glyphIndex,
                        new GlyphMetrics(
                            r.metricWidth * metricsConversion,
                            r.metricHeight * metricsConversion,
                            r.metricBearingX * metricsConversion,
                            r.metricBearingY * metricsConversion,
                            advanceDU),
                        GlyphRect.zero, 0);
                    glyphTable.Add(glyph);
                    glyphLookupDictionary[glyphIndex] = glyph;
                    glyphIndexList.Add(glyphIndex);
                    totalAdded++;
                    continue;
                }

                if (atlasTextures == null || atlasTextures.Count == 0)
                    CreateNewAtlasTexture();

                if (!TryPackGlyphShelf(r.bmpWidth, r.bmpHeight, out var packRect))
                {
                    atlasTextures[^1].Apply(false, false);
                    CreateNewAtlasTexture();
                    cachedAtlasTex = null;
                    if (!TryPackGlyphShelf(r.bmpWidth, r.bmpHeight, out packRect))
                    {
                        Cat.MeowWarnFormat("[PackRenderedBatch] {0}: glyph {1} too large for atlas ({2}x{3})",
                            name, glyphIndex, r.bmpWidth, r.bmpHeight);
                        ReturnSdfPixels(ref r);
                        continue;
                    }
                }

                var curAtlas = atlasTextures[^1];
                if (curAtlas != cachedAtlasTex)
                {
                    cachedAtlasTex = curAtlas;
                    cachedAtlasW = curAtlas.width;
                    var raw = curAtlas.GetRawTextureData<byte>();
                    cachedAtlasPtr = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(raw);
                }
                CopySdfBitmapToAtlas(r.sdfPixels, r.bmpWidth, r.bmpHeight, packRect.x, packRect.y, cachedAtlasW, cachedAtlasPtr);
                ReturnSdfPixels(ref r);

                int outlineW = r.bmpWidth - 2 * spread;
                int outlineH = r.bmpHeight - 2 * spread;
                if (outlineW < 0) outlineW = 0;
                if (outlineH < 0) outlineH = 0;

                float outlineBearingX = (r.bitmapLeft + spread) * metricsConversion;
                float outlineBearingY = (r.bitmapTop - spread) * metricsConversion;

                var glyphRect = new GlyphRect(packRect.x + spread, packRect.y + spread, outlineW, outlineH);

                var glyphObj = new Glyph(glyphIndex,
                    new GlyphMetrics(
                        outlineW * metricsConversion,
                        outlineH * metricsConversion,
                        outlineBearingX,
                        outlineBearingY,
                        advanceDU),
                    glyphRect, atlasTextures.Count - 1);

                glyphTable.Add(glyphObj);
                glyphLookupDictionary[glyphIndex] = glyphObj;
                glyphIndexList.Add(glyphIndex);
                totalAdded++;
            }
            }

            if (atlasTextures is { Count: > 0 })
                atlasTextures[^1].Apply(false, false);

            return totalAdded;
        }

        /// <summary>
        /// Renders glyphs as SDF using FreeType and packs them into atlas textures.
        /// Convenience wrapper that calls PrepareGlyphBatch → RenderPreparedBatch → PackRenderedBatch.
        /// </summary>
        /// <param name="glyphIndices">List of glyph indices to add.</param>
        /// <returns>Number of glyphs successfully added.</returns>
        public virtual int TryAddGlyphsBatch(List<uint> glyphIndices)
        {
            var batch = PrepareGlyphBatch(glyphIndices);
            if (batch == null) return 0;
            var rendered = RenderPreparedBatch(batch.Value);
            return PackRenderedBatch(rendered, batch.Value);
        }

        private static void ReturnSdfPixels(ref SdfRenderedGlyph r)
        {
            if (r.sdfPixels != null)
            {
                UniTextArrayPool<byte>.Return(r.sdfPixels);
                r.sdfPixels = null;
            }
        }

        /// <summary>
        /// Copies a pre-rendered SDF bitmap (Alpha8, already Y-flipped by native) to the atlas.
        /// </summary>
        private static unsafe void CopySdfBitmapToAtlas(byte[] sdfPixels, int bw, int bh,
            int packX, int packY, int atlasW, byte* atlasPtr)
        {
            fixed (byte* src = sdfPixels)
            {
                for (int y = 0; y < bh; y++)
                {
                    int dstOffset = (packY + y) * atlasW + packX;
                    Buffer.MemoryCopy(src + y * bw, atlasPtr + dstOffset, bw, bw);
                }
            }
        }

        /// <summary>
        /// Tries to pack a glyph bitmap into the current atlas using shelf-based packing.
        /// </summary>
        /// <param name="w">Bitmap width in pixels.</param>
        /// <param name="h">Bitmap height in pixels.</param>
        /// <param name="result">Output: position and size in the atlas.</param>
        /// <returns>True if packed successfully, false if atlas is full.</returns>
        protected bool TryPackGlyphShelf(int w, int h, out GlyphRect result)
        {
            result = default;
            int pw = w + PackingSpacing;
            int ph = h + PackingSpacing;

            if (shelfX + pw > atlasSize)
            {
                shelfY += shelfHeight + PackingSpacing;
                shelfX = 0;
                shelfHeight = 0;
            }

            if (shelfY + ph > atlasSize)
                return false;

            result = new GlyphRect(shelfX, shelfY, w, h);
            shelfX += pw;

            if (ph > shelfHeight)
                shelfHeight = ph;

            usedGlyphRects?.Add(result);
            return true;
        }

        protected unsafe void CreateNewAtlasTexture()
        {
            var texFormat = atlasRenderMode == UniTextRenderMode.SDF ? TextureFormat.Alpha8 : TextureFormat.RGBA32;
            var texture = new Texture2D(atlasSize, atlasSize, texFormat, false);

            var rawData = texture.GetRawTextureData<byte>();
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(
                Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(rawData),
                rawData.Length);

            atlasTextures ??= new List<Texture2D>();
            atlasTextures.Add(texture);

            texture.name = name + " Atlas " + (atlasTextures.Count - 1);
            texture.hideFlags = HideFlags.DontSave;

            freeGlyphRects ??= new List<GlyphRect>();
            freeGlyphRects.Clear();
            freeGlyphRects.Add(new GlyphRect(0, 0, atlasSize - PackingSpacing, atlasSize - PackingSpacing));

            usedGlyphRects ??= new List<GlyphRect>();
            usedGlyphRects.Clear();

            shelfX = 0;
            shelfY = 0;
            shelfHeight = 0;

            Cat.MeowFormat("[CreateNewAtlasTexture] {0}: created {1}x{2} {3} atlas (total: {4})", name, atlasSize, atlasSize, texFormat, atlasTextures.Count);
        }

        #endregion

        
        #region Static Creation Methods

        /// <summary>
        /// Creates a new font asset from raw font file bytes.
        /// </summary>
        /// <param name="fontBytes">TTF or OTF font file data.</param>
        /// <param name="samplingPointSize">Point size for rendering glyphs to atlas.</param>
        /// <param name="spreadStrength">SDF spread as fraction of point size (0-1). Padding = PointSize * SpreadStrength.</param>
        /// <param name="renderMode">Glyph rendering mode (SDF, bitmap, etc.).</param>
        /// <param name="atlasSize">Atlas texture size (square).</param>
        /// <returns>New font asset, or null if creation failed.</returns>
        public static UniTextFont CreateFontAsset(byte[] fontBytes, int samplingPointSize = 90, float spreadStrength = 0.25f,
            UniTextRenderMode renderMode = UniTextRenderMode.SDF, int atlasSize = 1024)
        {
            if (fontBytes == null || fontBytes.Length == 0)
            {
                Debug.LogError("UniTextFontAsset: Cannot create font asset from null or empty byte array.");
                return null;
            }

            if (!FT.IsInitialized) FT.Initialize();
            var face = FT.LoadFace(fontBytes, 0);
            if (face == IntPtr.Zero)
            {
                Debug.LogError("UniTextFontAsset: Failed to load font face from byte array.");
                return null;
            }

            var fontAsset = CreateInstance<UniTextFont>();
            fontAsset.fontData = fontBytes;
            fontAsset.fontDataHash = ComputeFontDataHash(fontBytes);

            int realUpem = Shaper.GetUpemFromFontData(fontBytes);
            fontAsset.unitsPerEm = realUpem;

            fontAsset.faceInfo = BuildFullFaceInfo(face, samplingPointSize);

            FT.UnloadFace(face);

            fontAsset.atlasSize = atlasSize;
            fontAsset.spreadStrength = Mathf.Clamp(spreadStrength, 0.1f, 1f);
            fontAsset.atlasRenderMode = renderMode;

            fontAsset.ReadFontAssetDefinition();

            return fontAsset;
        }

        /// <summary>
        /// Builds a complete FaceInfo from FreeType face data.
        /// Reads hhea (ascender/descender), OS/2 (cap height, x-height, strikeout, super/subscript),
        /// post (underline), and name (family/style) tables.
        /// </summary>
        internal static FaceInfo BuildFullFaceInfo(IntPtr face, int pointSize)
        {
            var ftInfo = FT.GetFaceInfo(face);
            var ext = FT.GetExtendedFaceInfo(face);

            var fi = new FaceInfo
            {
                faceIndex = ftInfo.faceIndex,
                familyName = ext.familyName,
                styleName = ext.styleName,
                pointSize = pointSize,
                unitsPerEm = ftInfo.unitsPerEm,
                ascentLine = ftInfo.ascender,
                descentLine = ftInfo.descender,
                lineHeight = ftInfo.height,
                underlineOffset = ext.underlinePosition,
                underlineThickness = ext.underlineThickness,
            };

            if (fi.lineHeight <= 0)
                fi.lineHeight = Mathf.RoundToInt((fi.ascentLine - fi.descentLine) * 1.2f);

            if (ext.hasOS2)
            {
                fi.capLine = ext.capHeight;
                fi.meanLine = ext.xHeight;
                fi.strikethroughOffset = ext.strikeoutPosition;
                fi.strikethroughThickness = ext.strikeoutSize;
                fi.superscriptOffset = ext.superscriptYOffset;
                fi.superscriptSize = ext.superscriptYSize;
                fi.subscriptOffset = ext.subscriptYOffset;
                fi.subscriptSize = ext.subscriptYSize;
            }
            else
            {
                int capBearingY = FT.GetGlyphBearingYUnscaled(face, 'H');
                fi.capLine = capBearingY > 0 ? capBearingY : Mathf.RoundToInt(fi.ascentLine * 0.75f);

                int xBearingY = FT.GetGlyphBearingYUnscaled(face, 'x');
                fi.meanLine = xBearingY > 0 ? xBearingY : Mathf.RoundToInt(fi.ascentLine * 0.5f);

                fi.strikethroughOffset = Mathf.RoundToInt(fi.meanLine * 0.5f);
                fi.strikethroughThickness = fi.underlineThickness > 0
                    ? fi.underlineThickness
                    : Mathf.RoundToInt(fi.ascentLine * 0.05f);

                fi.superscriptOffset = fi.ascentLine;
                fi.superscriptSize = pointSize;
                fi.subscriptOffset = fi.descentLine;
                fi.subscriptSize = pointSize;
            }

            int spaceAdvance = FT.GetGlyphAdvanceUnscaled(face, ' ');
            fi.tabWidth = spaceAdvance > 0 ? spaceAdvance : fi.ascentLine;

            return fi;
        }

        #endregion

        #region Dynamic Data Management

        /// <summary>
        /// Clears all dynamically generated glyph data and resets atlas textures.
        /// </summary>
        /// <remarks>
        /// Call this to force re-rendering of all glyphs. Useful when changing
        /// atlas parameters or for reducing memory usage.
        /// </remarks>
        public void ClearDynamicData()
        {
            Cat.MeowFormat("[ClearDynamicData] {0}: CALLED. glyphTable={1}, glyphLookup={2}, atlas={3}\n{4}",
                name,
                glyphTable?.Count ?? -1,
                glyphLookupDictionary?.Count ?? -1,
                atlasTextures?.Count ?? -1,
                UnityEngine.StackTraceUtility.ExtractStackTrace());

            glyphTable?.Clear();
            characterTable?.Clear();

            glyphLookupDictionary?.Clear();
            characterLookupDictionary?.Clear();
            glyphIndexList?.Clear();

            usedGlyphRects?.Clear();
            freeGlyphRects?.Clear();

            shelfX = 0;
            shelfY = 0;
            shelfHeight = 0;

            if (atlasTextures != null)
            {
                foreach (var texture in atlasTextures)
                    if (texture != null)
                        ObjectUtils.SafeDestroy(texture);

                atlasTextures.Clear();
            }

            ReleaseFTFace();

#if !UNITY_WEBGL || UNITY_EDITOR
            sdfFacePool?.Dispose();
            sdfFacePool = null;
#endif

            Shaper.ClearCache(GetCachedInstanceId());
            Cat.Meow($"UniTextFont [{name}]: Dynamic data cleared. Atlas will regenerate at runtime.");

            Changed?.Invoke();
        }

        public void InvokeChanged()
        {
            Changed?.Invoke();
        }
        
        #endregion

        #region Lifecycle

        private void OnEnable() => loadedFonts.Add(this);

        private void OnDisable() => loadedFonts.Remove(this);

        private void OnDestroy()
        {
            ReleaseFTFace();

#if !UNITY_WEBGL || UNITY_EDITOR
            sdfFacePool?.Dispose();
            sdfFacePool = null;
#endif

            if (atlasTextures != null)
            {
                foreach (var texture in atlasTextures)
                {
                    ObjectUtils.SafeDestroy(texture);
                }
                atlasTextures.Clear();
            }
        }

        /// <summary>
        /// Clears dynamic data for all loaded font assets and invalidates shared caches.
        /// </summary>
        public static void ClearRuntimeData()
        {
            foreach (var font in loadedFonts)
                font.ClearDynamicData();

            SharedFontCache.Clear();
        }

        #endregion

        #region Editor Support

    #if UNITY_EDITOR

        [SerializeField]
        [Tooltip("Unity Font asset to sync with (Editor only).")]
        public Font sourceFont;

        private void OnValidate()
        {
            Cat.MeowFormat("[UniTextFont.OnValidate] {0}: glyphLookup={1}, glyphTable={2}, atlas={3}",
                name, glyphLookupDictionary?.Count ?? -1, glyphTable?.Count ?? -1, atlasTextures?.Count ?? -1);
            Changed?.Invoke();
        }

        public void SetFontData(byte[] data)
        {
            ReleaseFTFace();
            fontData = data;
            fontDataHash = ComputeFontDataHash(data);

            if (data != null && data.Length > 0)
            {
                var face = EnsureFTFace();
                if (face != IntPtr.Zero)
                {
                    int ptSize = faceInfo.pointSize > 0 ? faceInfo.pointSize : 90;

                    faceInfo = BuildFullFaceInfo(face, ptSize);
                    unitsPerEm = faceInfo.unitsPerEm;
                }
            }
        }

        public void UpdateFromSourceFont()
        {
            if (sourceFont == null) return;

            var fontPath = UnityEditor.AssetDatabase.GetAssetPath(sourceFont);
            if (!string.IsNullOrEmpty(fontPath))
            {
                var bytes = System.IO.File.ReadAllBytes(fontPath);
                if (bytes.Length > 0)
                    SetFontData(bytes);
            }
        }
    #endif

        #endregion
    }

    /// <summary>
    /// Represents a character mapping from Unicode codepoint to glyph.
    /// </summary>
    /// <remarks>
    /// Stores the association between a Unicode codepoint and its corresponding
    /// glyph in the font. The glyph reference is resolved at runtime.
    /// </remarks>
    [Serializable]
    internal class UniTextCharacter
    {
        /// <summary>Unicode codepoint for this character.</summary>
        public uint unicode;
        /// <summary>Index of the glyph in the font's glyph table.</summary>
        public uint glyphIndex;
        /// <summary>Runtime reference to the glyph (not serialized).</summary>
        [NonSerialized] public Glyph glyph;

        /// <summary>Default constructor for serialization.</summary>
        public UniTextCharacter()
        {
        }

        /// <summary>
        /// Creates a character with the specified unicode and glyph index.
        /// </summary>
        public UniTextCharacter(uint unicode, uint glyphIndex)
        {
            this.unicode = unicode;
            this.glyphIndex = glyphIndex;
        }

        /// <summary>
        /// Creates a character with the specified unicode and glyph.
        /// </summary>
        public UniTextCharacter(uint unicode, Glyph glyph)
        {
            this.unicode = unicode;
            this.glyph = glyph;
            glyphIndex = glyph.index;
        }
    }
}
