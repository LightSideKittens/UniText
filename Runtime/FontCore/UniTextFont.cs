using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEditor;
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
    /// <item>Glyph table with metrics for text layout</item>
    /// <item>Glyph curve cache for SDF rendering</item>
    /// </list>
    /// </para>
    /// <para>
    /// Glyph curves are extracted at runtime when first needed and stored in a shared curve atlas.
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextFontProvider"/>
    /// <seealso cref="UniTextFontStack"/>
    [Serializable]
    public class UniTextFont : ScriptableObject
    {
        #region Serialized Fields

        [SerializeField]
        [Tooltip("Zstd-compressed font file data (TTF/OTF bytes).")]
        protected byte[] fontData;

        [NonSerialized]
        private byte[] decompressedFontData;

        [SerializeField]
        [Tooltip("Hash of font data for identification.")]
        protected int fontDataHash;

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

        [SerializeField]
        [Tooltip("SDF tile detail multiplier. Higher values force larger tiles for better quality on fonts with thin strokes (e.g. calligraphic). Default 1.0.")]
        [Range(0.25f, 8f)]
        internal float sdfDetailMultiplier = 1f;

        /// <summary>Per-glyph tile size override. 0 = auto (use ClassifyTileSize), 64/128/256 = forced size.</summary>
        [Serializable]
        public struct GlyphOverride
        {
            public uint glyphIndex;
            [Tooltip("0 = auto, 64/128/256 = forced tile size.")]
            public int tileSizeOverride;
        }

        [SerializeField]
        [Tooltip("Per-glyph tile size overrides for fine-tuning quality on specific glyphs.")]
        internal List<GlyphOverride> glyphOverrides;

        [NonSerialized] private Dictionary<uint, int> glyphOverrideLookup;

        /// <summary>Builds the runtime lookup dictionary from the serialized list (lazy, once).</summary>
        private Dictionary<uint, int> GlyphOverrideLookup
        {
            get
            {
                if (glyphOverrideLookup == null && glyphOverrides is { Count: > 0 })
                {
                    glyphOverrideLookup = new Dictionary<uint, int>(glyphOverrides.Count);
                    foreach (var ov in glyphOverrides)
                        if (ov.tileSizeOverride > 0)
                            glyphOverrideLookup[ov.glyphIndex] = ov.tileSizeOverride;
                }
                return glyphOverrideLookup;
            }
        }

        [NonSerialized]
        internal List<Glyph> glyphTable = new();

        [NonSerialized]
        internal List<UniTextCharacter> characterTable = new();


        #endregion

        #region Runtime Fields

        private static readonly HashSet<UniTextFont> loadedFonts = new();

        internal Dictionary<long, Glyph> glyphLookupDictionary;
        internal Dictionary<uint, UniTextCharacter> characterLookupDictionary;

        /// <summary>Default varHash48 for this font (no variation axes). Derived from fontDataHash.</summary>
        internal long DefaultVarHash48 => GlyphAtlas.DefaultVarHash(FontDataHash);

        /// <summary>Computes full glyph key for lookup in glyphLookupDictionary and atlas.</summary>
        internal long GlyphKey(uint glyphIndex) => GlyphAtlas.MakeKey(DefaultVarHash48, glyphIndex);

        /// <summary>Computes glyph key with specific variation hash.</summary>
        internal static long GlyphKey(long varHash48, uint glyphIndex) => GlyphAtlas.MakeKey(varHash48, glyphIndex);

        [NonSerialized] private HB.hb_ot_var_axis_info_t[] cachedVariableAxes;
        [NonSerialized] private bool variableAxesQueried;
        [NonSerialized] private int[] cachedDefaultFtCoords;

        /// <summary>Variable font axis infos from HarfBuzz, or null if not variable.</summary>
        internal HB.hb_ot_var_axis_info_t[] VariableAxes
        {
            get
            {
                if (!variableAxesQueried)
                {
                    variableAxesQueried = true;
                    cachedVariableAxes = Shaper.GetVariableAxisInfos(this);
                }
                return cachedVariableAxes;
            }
        }

        /// <summary>True if this font has any variable font axes.</summary>
        public bool IsVariable => VariableAxes != null;

        /// <summary>
        /// Default FreeType design coordinates (16.16 fixed-point) for all axes.
        /// Used to reset pooled FreeType faces to default state after variation use.
        /// Null for non-variable fonts.
        /// </summary>
        internal int[] DefaultFtCoords
        {
            get
            {
                if (cachedDefaultFtCoords != null) return cachedDefaultFtCoords;
                var axes = VariableAxes;
                if (axes == null) return null;
                cachedDefaultFtCoords = new int[axes.Length];
                for (int i = 0; i < axes.Length; i++)
                    cachedDefaultFtCoords[i] = (int)(axes[i].defaultValue * 65536f);
                return cachedDefaultFtCoords;
            }
        }

        /// <summary>
        /// Computes a varHash48 for this font with the given axis values.
        /// axisValues must have one entry per axis in VariableAxes (same order).
        /// </summary>
        internal long ComputeVarHash48(ReadOnlySpan<float> axisValues)
        {
            return GlyphAtlas.ComputeVarHash48(FontDataHash, axisValues);
        }

        protected List<uint> glyphIndexList = new();

        private int cachedFaceIndex = -1;
        private int cachedInstanceId;
        public string CachedName { get; private set; }

        [ThreadStatic] private static HashSet<uint> toAddSet;

        [NonSerialized] private IntPtr ftFace;
        [NonSerialized] private GlyphCurveCache glyphCurveCache;
        [NonSerialized] private PooledBuffer<uint> filteredForBatch;
        [NonSerialized] protected List<long> batchProtectedKeys;

        [ThreadStatic] private static List<uint> toAddList;

        internal event Action Changed;
        internal static bool IsAtlasClearing;

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

        /// <summary>Gets the raw font file data (TTF/OTF bytes). Decompresses from Zstd on first access.</summary>
        public virtual byte[] FontData
        {
            get
            {
                if (decompressedFontData != null)
                    return decompressedFontData;

                if (fontData == null || fontData.Length == 0)
                    return null;

                if (!Zstd.IsCompressed(fontData))
                    return fontData;

                decompressedFontData = Zstd.Decompress(fontData);
#if !UNITY_EDITOR
                fontData = null;
#endif
                return decompressedFontData;
            }
        }

        /// <summary>Gets the italic slant angle in degrees.</summary>
        public float ItalicStyle => italicStyle;

        /// <summary>Gets the hash of the font data for identification.</summary>
        public virtual int FontDataHash => fontDataHash;

        /// <summary>Returns true if font file data is available.</summary>
        public virtual bool HasFontData =>
            decompressedFontData != null || (fontData != null && fontData.Length > 0);

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

        /// <summary>Gets the padding between glyphs in the atlas (EmojiFont only).</summary>
        public virtual int AtlasPadding => 1;

        /// <summary>Gets the glyph curve cache for SDF rendering. Lazily initialized on first access.</summary>
        internal GlyphCurveCache CurveCache
        {
            get
            {
                if (glyphCurveCache != null) return glyphCurveCache;
                var face = EnsureFTFace();
                if (face == IntPtr.Zero) return null;
                int fi = cachedFaceIndex < 0 ? 0 : cachedFaceIndex;
                glyphCurveCache = new GlyphCurveCache(face, FontData, fi, faceInfo.unitsPerEm > 0 ? faceInfo.unitsPerEm : 1000);
                return glyphCurveCache;
            }
        }

        /// <summary>Gets the glyph lookup table (glyph key → Glyph).</summary>
        public Dictionary<long, Glyph> GlyphLookupTable
        {
            get
            {
                if (glyphLookupDictionary == null)
                {
                    Cat.MeowFormat("[GlyphLookupTable] {0}: dict is NULL, calling ReadFontAssetDefinition", CachedName);
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
                    Cat.MeowFormat("[CharacterLookupTable] {0}: dict is NULL, calling ReadFontAssetDefinition. glyphLookup={1}, glyphTable={2}",
                        CachedName, glyphLookupDictionary?.Count ?? -1, glyphTable?.Count ?? -1);
                    ReadFontAssetDefinition();
                }
                return characterLookupDictionary;
            }
        }
        
        public bool IsColor => this is EmojiFont;


        #endregion

        #region Initialization

        /// <summary>
        /// Initializes lookup dictionaries from serialized glyph and character tables.
        /// </summary>
        public void ReadFontAssetDefinition()
        {
            Cat.MeowFormat("[ReadFontAssetDefinition] {0}: CALLED. glyphTable={1}, glyphLookup={2}, charLookup={3}",
                CachedName,
                glyphTable?.Count ?? -1,
                glyphLookupDictionary?.Count ?? -1,
                characterLookupDictionary?.Count ?? -1);
            Cat.MeowFormat("[ReadFontAssetDefinition] {0}: stacktrace:\n{1}", CachedName, StackTraceUtility.ExtractStackTrace());
            InitializeGlyphLookupDictionary();
            InitializeCharacterLookupDictionary();
            AddSynthesizedCharacters();
            Cat.MeowFormat("[ReadFontAssetDefinition] {0}: DONE. glyphLookup={1}, charLookup={2}",
                CachedName, glyphLookupDictionary?.Count ?? -1, characterLookupDictionary?.Count ?? -1);
        }

        private void InitializeGlyphLookupDictionary()
        {
            glyphLookupDictionary ??= new Dictionary<long, Glyph>();
            glyphLookupDictionary.Clear();

            glyphIndexList ??= new List<uint>();
            glyphIndexList.Clear();

            if (glyphTable == null) return;

            int zeroRectCount = 0;
            for (var i = 0; i < glyphTable.Count; i++)
            {
                var glyph = glyphTable[i];
                var index = glyph.index;

                if (glyphLookupDictionary.TryAdd(GlyphKey(index), glyph))
                {
                    glyphIndexList.Add(index);
                    var r = glyph.glyphRect;
                    if (r.width == 0 || r.height == 0)
                        zeroRectCount++;
                }
            }

            if (glyphTable.Count > 0)
                Cat.MeowFormat("[InitGlyphLookup] {0}: read {1} from glyphTable, {2} zero-rect",
                    CachedName, glyphLookupDictionary.Count, zeroRectCount);
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
                    if (glyphLookupDictionary.TryGetValue(GlyphKey(character.glyphIndex), out var glyph))
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
                        FT.SetPixelSize(face, unitsPerEm);
                        if (FT.LoadGlyph(face, glyphIdx, FT.LOAD_DEFAULT | FT.LOAD_NO_BITMAP))
                        {
                            var ftMetrics = FT.GetGlyphMetrics(face);
                            var advance = ftMetrics.advanceX / 64f;
                            glyph = new Glyph(glyphIdx,
                                new GlyphMetrics(
                                    ftMetrics.width,
                                    ftMetrics.height,
                                    ftMetrics.bearingX,
                                    ftMetrics.bearingY,
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

            if (!HasFontData)
                return IntPtr.Zero;

            if (!FT.IsInitialized)
                FT.Initialize();

            if (cachedFaceIndex < 0)
                cachedFaceIndex = faceInfo.faceIndex;

            ftFace = FT.LoadFace(FontData, cachedFaceIndex < 0 ? 0 : cachedFaceIndex);
            Cat.MeowFormat("[EnsureFTFace] {0}: loaded face={1}", CachedName, ftFace != IntPtr.Zero);
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

                if (!glyphLookupDictionary.TryGetValue(GlyphKey(glyphIndex), out var glyph))
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
                if (glyphLookupDictionary == null || !glyphLookupDictionary.ContainsKey(GlyphKey(idx)))
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
        /// Checks if a glyph is already cached (curve data extracted or bitmap rendered).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasGlyphInAtlas(uint glyphIndex)
        {
            return glyphLookupDictionary != null && glyphLookupDictionary.ContainsKey(GlyphKey(glyphIndex));
        }

        /// <summary>
        /// Data for a prepared glyph batch, used by the split Prepare→Render→Pack pipeline.
        /// </summary>
        internal struct PreparedBatch
        {
            /// <summary>Filtered glyph indices to render (no duplicates, no already-cached).</summary>
            public PooledBuffer<uint> filteredGlyphs;
            /// <summary>VarHash48 for atlas keys. 0 = use DefaultVarHash48.</summary>
            public long varHash48;
            /// <summary>FT design coordinates for variable fonts. Null = default axes.</summary>
            public int[] ftCoords;
        }

        /// <summary>
        /// Prepares a glyph batch for the split rendering pipeline.
        /// Filters glyphs that need extraction (new or evicted from atlas).
        /// EmojiFont overrides with its own filtering logic.
        /// </summary>
        internal virtual PreparedBatch? PrepareGlyphBatch(
            List<uint> glyphIndices, UniTextBase.RenderModee mode,
            long varHash48 = 0, int[] ftCoords = null)
        {
            if (glyphIndices == null || glyphIndices.Count == 0) return null;

            if (glyphLookupDictionary == null)
                ReadFontAssetDefinition();

            var cache = CurveCache;
            if (cache == null) return null;

            var atlas = GlyphAtlas.GetInstance(mode);
            var varHash = varHash48 != 0 ? varHash48 : DefaultVarHash48;

            toAddSet ??= new HashSet<uint>();
            toAddSet.Clear();
            for (int i = 0; i < glyphIndices.Count; i++)
                toAddSet.Add(glyphIndices[i]);

            filteredForBatch.FakeClear();
            if (filteredForBatch.data == null)
                filteredForBatch.EnsureCapacity(toAddSet.Count);

            foreach (var glyphIndex in toAddSet)
            {
                bool isNew = !glyphLookupDictionary.ContainsKey(GlyphKey(varHash, glyphIndex));
                if (!isNew && atlas.TryGetEntry(varHash, glyphIndex, out var existingEntry))
                {
                    if (existingEntry.refCount == 0)
                    {
                        var key = GlyphAtlas.MakeKey(varHash, glyphIndex);
                        atlas.AddRef(key);
                        batchProtectedKeys ??= new List<long>();
                        batchProtectedKeys.Add(key);
                    }
                    continue;
                }
                filteredForBatch.Add(glyphIndex);
            }

            if (filteredForBatch.count == 0) return null;
            var owned = filteredForBatch;
            filteredForBatch = default;
            return new PreparedBatch
            {
                filteredGlyphs = owned,
                varHash48 = varHash,
                ftCoords = ftCoords
            };
        }

        internal virtual void ReleaseBatchProtectedKeys(UniTextBase.RenderModee mode)
        {
            if (batchProtectedKeys == null || batchProtectedKeys.Count == 0) return;
            var atlas = GlyphAtlas.GetInstance(mode);
            for (int i = 0; i < batchProtectedKeys.Count; i++)
                atlas.Release(batchProtectedKeys[i]);
            batchProtectedKeys.Clear();
        }

        private const int ParallelThreshold = 16;

        private struct ExtractedGlyph
        {
            public uint glyphIndex;
            public GlyphCurveCache.GlyphCurveData curveData;
            public int segmentBufferIndex;
            public int segmentOffset;
            public int segmentCount;
            public bool isNew;
            public int tileSize;
            public float aspect;
        }

        private int ApplyTileSizeOverride(uint glyphIndex, int tileSize)
        {
            var lookup = GlyphOverrideLookup;
            if (lookup != null && lookup.TryGetValue(glyphIndex, out int ov))
                return ov;
            return tileSize;
        }

        private class RenderedBatch
        {
            public ExtractedGlyph[] extracted;
            public int count;
            public PooledBuffer<GlyphCurveCache.Segment>[] segmentBuffers;
            public int segmentBufferCount;
        }

        /// <summary>
        /// Renders a prepared batch (can run on worker threads).
        /// Extracts glyph outlines via FreeType as raw quadratic Bézier curves.
        /// Uses glyph-level parallelism with face pool when batch is large enough.
        /// EmojiFont overrides with FreeType/COLRv1/CoreText rendering.
        /// </summary>
        internal virtual object RenderPreparedBatch(PreparedBatch batch)
        {
            var cache = CurveCache;
            if (cache == null) return null;

            var glyphs = batch.filteredGlyphs;
            var varHash = batch.varHash48 != 0 ? batch.varHash48 : DefaultVarHash48;
            bool useParallel =
#if UNITY_WEBGL && !UNITY_EDITOR
                false;
#else
                glyphs.count >= ParallelThreshold;
#endif
            int bufferCount = useParallel ? Math.Min(Environment.ProcessorCount, glyphs.count) : 1;

            var result = new RenderedBatch
            {
                extracted = new ExtractedGlyph[glyphs.count],
                count = glyphs.count,
                segmentBuffers = new PooledBuffer<GlyphCurveCache.Segment>[bufferCount],
                segmentBufferCount = bufferCount
            };

            Cat.Meow($"[UniText Render] font={CachedName}, glyphs={glyphs.count}, parallel={useParallel}");
            if (!useParallel)
                RenderSequential(cache, glyphs, result, varHash, batch.ftCoords);
            else
                RenderParallel(cache, glyphs, result, varHash, batch.ftCoords);
            Cat.Meow($"[UniText Render] font={CachedName} DONE");

            return result;
        }

        private void RenderSequential(GlyphCurveCache cache, PooledBuffer<uint> glyphs, RenderedBatch result,
            long varHash, int[] ftCoords)
        {
            var face = cache.RentFace();
            var buf = new PooledBuffer<GlyphCurveCache.Segment>();
            try
            {
                SetFTVariationCoords(face, ftCoords ?? DefaultFtCoords);

                for (int i = 0; i < glyphs.count; i++)
                {
                    if (i % 50 == 0)
                        Cat.Meow($"[UniText Extract] {CachedName}: glyph {i}/{glyphs.count}");
                    uint gi = glyphs[i];
                    int segStart = buf.count;
                    var data = cache.ExtractWithFace(face, gi, ref buf);
                    int segCount = buf.count - segStart;

                    float tileAspect = GlyphAtlas.ComputeAspect(in data);
                    float tileGlyphH = data.designHeight / (float)UnitsPerEm;
                    result.extracted[i] = new ExtractedGlyph
                    {
                        glyphIndex = gi,
                        curveData = data,
                        segmentBufferIndex = 0,
                        segmentOffset = segStart,
                        segmentCount = segCount,
                        isNew = !glyphLookupDictionary.ContainsKey(GlyphKey(varHash, gi)),
                        tileSize = ApplyTileSizeOverride(gi, GlyphAtlas.ClassifyTileSize(
                            buf.data.AsSpan(segStart, segCount), tileAspect, tileGlyphH, sdfDetailMultiplier)),
                        aspect = tileAspect
                    };
                }
            }
            finally
            {
                cache.ReturnFace(face);
            }
            result.segmentBuffers[0] = buf;
        }

        private void RenderParallel(GlyphCurveCache cache, PooledBuffer<uint> glyphs, RenderedBatch result,
            long varHash, int[] ftCoords)
        {
            int count = glyphs.count;
            int workerCount = result.segmentBufferCount;
            int chunkSize = (count + workerCount - 1) / workerCount;

            Parallel.For(0, workerCount, workerId =>
            {
                int start = workerId * chunkSize;
                int end = Math.Min(start + chunkSize, count);
                if (start >= end) return;

                var face = cache.RentFace();
                var buf = new PooledBuffer<GlyphCurveCache.Segment>();
                try
                {
                    SetFTVariationCoords(face, ftCoords ?? DefaultFtCoords);

                    for (int i = start; i < end; i++)
                    {
                        uint gi = glyphs[i];
                        int segStart = buf.count;
                        var data = cache.ExtractWithFace(face, gi, ref buf);
                        int segCount = buf.count - segStart;

                        float tileAspect = GlyphAtlas.ComputeAspect(in data);
                        float tileGlyphH = data.designHeight / (float)UnitsPerEm;
                        result.extracted[i] = new ExtractedGlyph
                        {
                            glyphIndex = gi,
                            curveData = data,
                            segmentBufferIndex = workerId,
                            segmentOffset = segStart,
                            segmentCount = segCount,
                            isNew = !glyphLookupDictionary.ContainsKey(GlyphKey(varHash, gi)),
                            tileSize = ApplyTileSizeOverride(gi, GlyphAtlas.ClassifyTileSize(
                                buf.data.AsSpan(segStart, segCount), tileAspect, tileGlyphH, sdfDetailMultiplier)),
                            aspect = tileAspect
                        };
                    }
                }
                finally
                {
                    cache.ReturnFace(face);
                }
                result.segmentBuffers[workerId] = buf;
            });
        }

        /// <summary>
        /// Sets FreeType variable font design coordinates on a face.
        /// </summary>
        private static unsafe void SetFTVariationCoords(IntPtr face, int[] coords)
        {
            if (face == IntPtr.Zero || coords == null || coords.Length == 0) return;
            fixed (int* ptr = coords)
            {
                FT.SetVarDesignCoordinates(face, ptr, coords.Length);
            }
        }

        public virtual long EstimateTileArea(object renderedObj)
        {
            if (renderedObj is not RenderedBatch rendered) return 0;
            long area = 0;
            for (int i = 0; i < rendered.count; i++)
            {
                int ts = rendered.extracted[i].tileSize;
                area += (long)ts * ts;
            }
            return area;
        }

        /// <summary>
        /// Packs rendered glyphs into the atlas and creates metrics (main thread only).
        /// Does NOT call FlushPendingGPU — caller should flush once after all fonts.
        /// EmojiFont overrides to copy pixels and register glyphs.
        /// </summary>
        internal virtual int PackRenderedBatch(object renderedObj, PreparedBatch batch, UniTextBase.RenderModee mode)
        {
            if (renderedObj is not RenderedBatch rendered) return 0;

            var atlas = GlyphAtlas.GetInstance(mode);
            var varHash = batch.varHash48 != 0 ? batch.varHash48 : DefaultVarHash48;
            var fontHash = FontDataHash;

            glyphLookupDictionary ??= new Dictionary<long, Glyph>();
            glyphTable ??= new List<Glyph>();

            int totalSegments = 0;
            for (int i = 0; i < rendered.count; i++)
                totalSegments += rendered.extracted[i].segmentCount;
            atlas.ReservePendingSegments(totalSegments);

            int added = 0;
            for (int i = 0; i < rendered.count; i++)
            {
                ref var eg = ref rendered.extracted[i];
                var buf = rendered.segmentBuffers[eg.segmentBufferIndex];
                var span = buf.data.AsSpan(eg.segmentOffset, eg.segmentCount);
                float glyphH = eg.curveData.designHeight / (float)UnitsPerEm;
                var upem = (float)UnitsPerEm;
                var metrics = new GlyphMetrics(
                    eg.curveData.bboxMaxX - eg.curveData.bboxMinX,
                    eg.curveData.bboxMaxY - eg.curveData.bboxMinY,
                    eg.curveData.bearingX * upem,
                    eg.curveData.bearingY * upem,
                    eg.curveData.advanceX * upem
                );
                atlas.EnsureGlyph(varHash, eg.glyphIndex, fontHash, in eg.curveData, span, eg.tileSize, glyphH, eg.aspect, in metrics);

                if (eg.isNew)
                {
                    var glyph = new Glyph(eg.glyphIndex, metrics, GlyphRect.zero, 0);
                    glyphTable.Add(glyph);
                    glyphLookupDictionary[GlyphKey(varHash, eg.glyphIndex)] = glyph;
                    added++;
                }
            }

            for (int i = 0; i < rendered.segmentBufferCount; i++)
                rendered.segmentBuffers[i].Return();

            return added;
        }

        /// <summary>
        /// Extracts glyph curves and adds them to the curve atlas for SDF rendering.
        /// EmojiFont overrides this with its own atlas-based pipeline.
        /// </summary>
        /// <param name="glyphIndices">List of glyph indices to add.</param>
        /// <returns>Number of glyphs successfully added.</returns>
        internal virtual int TryAddGlyphsBatch(
            List<uint> glyphIndices, UniTextBase.RenderModee mode,
            long varHash48 = 0, int[] ftCoords = null)
        {
            if (glyphIndices == null || glyphIndices.Count == 0) return 0;

            if (glyphLookupDictionary == null)
                ReadFontAssetDefinition();

            var cache = CurveCache;
            if (cache == null) return 0;

            var atlas = GlyphAtlas.GetInstance(mode);
            var varHash = varHash48 != 0 ? varHash48 : DefaultVarHash48;
            var fontHash = FontDataHash;

            glyphLookupDictionary ??= new Dictionary<long, Glyph>();
            glyphTable ??= new List<Glyph>();

            toAddSet ??= new HashSet<uint>();
            toAddSet.Clear();
            for (int i = 0; i < glyphIndices.Count; i++)
                toAddSet.Add(glyphIndices[i]);

            int added = 0;
            var face = cache.RentFace();
            var buf = new PooledBuffer<GlyphCurveCache.Segment>();
            try
            {
                SetFTVariationCoords(face, ftCoords ?? DefaultFtCoords);

                foreach (var glyphIndex in toAddSet)
                {
                    if (glyphIndex == 0) continue;

                    bool isNew = !glyphLookupDictionary.ContainsKey(GlyphKey(varHash, glyphIndex));

                    if (!isNew && atlas.TryGetEntry(varHash, glyphIndex, out _))
                        continue;

                    buf.FakeClear();
                    var curveData = cache.ExtractWithFace(face, glyphIndex, ref buf);
                    var span = buf.Span;
                    float tileAspect = GlyphAtlas.ComputeAspect(in curveData);
                    float glyphH = curveData.designHeight / (float)UnitsPerEm;
                    int tileSize = ApplyTileSizeOverride(glyphIndex,
                        GlyphAtlas.ClassifyTileSize(span, tileAspect, glyphH, sdfDetailMultiplier));
                    var upem = (float)UnitsPerEm;
                    var metrics = new GlyphMetrics(
                        curveData.bboxMaxX - curveData.bboxMinX,
                        curveData.bboxMaxY - curveData.bboxMinY,
                        curveData.bearingX * upem,
                        curveData.bearingY * upem,
                        curveData.advanceX * upem
                    );
                    atlas.EnsureGlyph(varHash, glyphIndex, fontHash, in curveData, span, tileSize, glyphH, tileAspect, in metrics);

                    if (isNew)
                    {
                        var glyph = new Glyph(glyphIndex, metrics, GlyphRect.zero, 0);
                        glyphTable.Add(glyph);
                        glyphLookupDictionary[GlyphKey(varHash, glyphIndex)] = glyph;
                        added++;
                    }
                }
            }
            finally
            {
                cache.ReturnFace(face);
                buf.Return();
            }

            atlas.FlushPending();
            return added;
        }

        internal void ReExtractForBandUpgrade(
            uint glyphIndex, long varHash48, int[] ftCoords,
            UniTextBase.RenderModee mode, int requiredBandPx)
        {
            var cache = CurveCache;
            if (cache == null) return;

            var atlas = GlyphAtlas.GetInstance(mode);
            var face = cache.RentFace();
            try
            {
                SetFTVariationCoords(face, ftCoords ?? DefaultFtCoords);

                var buf = new PooledBuffer<GlyphCurveCache.Segment>();
                var curveData = cache.ExtractWithFace(face, glyphIndex, ref buf);

                float glyphH = curveData.designHeight / (float)UnitsPerEm;
                float aspect = GlyphAtlas.ComputeAspect(in curveData);

                atlas.ReservePendingSegments(buf.count);
                atlas.UpgradeGlyphBand(
                    GlyphAtlas.MakeKey(varHash48, glyphIndex),
                    buf.Span, glyphH, aspect, requiredBandPx);

                buf.Return();
            }
            finally
            {
                cache.ReturnFace(face);
            }
        }

        #endregion


        #region Static Creation Methods

        /// <summary>
        /// Creates a new font asset from raw font file bytes.
        /// </summary>
        /// <param name="fontBytes">TTF or OTF font file data.</param>
        /// <returns>New font asset, or null if creation failed.</returns>
        public static UniTextFont CreateFontAsset(byte[] fontBytes)
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
            var fontDataBytes = fontBytes;
#if UNITY_EDITOR
            fontDataBytes = Zstd.Compress(fontBytes);
#endif
            fontAsset.fontData = fontDataBytes;
            fontAsset.fontDataHash = ComputeFontDataHash(fontBytes);

            int realUpem = Shaper.GetUpemFromFontData(fontBytes);
            fontAsset.unitsPerEm = realUpem;

            fontAsset.faceInfo = BuildFullFaceInfo(face);

            FT.UnloadFace(face);

            fontAsset.ReadFontAssetDefinition();

            return fontAsset;
        }

        /// <summary>
        /// Builds a complete FaceInfo from FreeType face data.
        /// Reads hhea (ascender/descender), OS/2 (cap height, x-height, strikeout, super/subscript),
        /// post (underline), and name (family/style) tables.
        /// </summary>
        internal static FaceInfo BuildFullFaceInfo(IntPtr face)
        {
            var ftInfo = FT.GetFaceInfo(face);
            var ext = FT.GetExtendedFaceInfo(face);

            var fi = new FaceInfo
            {
                faceIndex = ftInfo.faceIndex,
                familyName = ext.familyName,
                styleName = ext.styleName,
                unitsPerEm = ftInfo.unitsPerEm,
                ascentLine = ftInfo.ascender,
                descentLine = ftInfo.descender,
                lineHeight = ftInfo.height,
                underlineOffset = ext.underlinePosition,
                underlineThickness = ext.underlineThickness,
                weightClass = ext.weightClass > 0 ? ext.weightClass : 400,
                isItalic = (ext.styleFlags & 1) != 0,
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
                fi.superscriptSize = fi.unitsPerEm;
                fi.subscriptOffset = fi.descentLine;
                fi.subscriptSize = fi.unitsPerEm;
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
        public virtual void ClearDynamicData()
        {
            Cat.MeowFormat("[ClearDynamicData] {0}: CALLED. glyphTable={1}, glyphLookup={2}\n{3}",
                CachedName,
                glyphTable?.Count ?? -1,
                glyphLookupDictionary?.Count ?? -1,
                StackTraceUtility.ExtractStackTrace());

            glyphTable?.Clear();
            characterTable?.Clear();

            glyphLookupDictionary?.Clear();
            characterLookupDictionary?.Clear();
            glyphIndexList?.Clear();

            ReleaseFTFace();

            glyphCurveCache?.Dispose();
            glyphCurveCache = null;

            filteredForBatch.Return();

            ClearAtlasEntries();

            Shaper.ClearCache(GetCachedInstanceId());
            Cat.Meow($"UniTextFont [{CachedName}]: Dynamic data cleared.");
        }

        /// <summary>
        /// Clears glyph tables and atlas entries without disposing the curve cache,
        /// FreeType face, or shaper cache. Use when tile sizes changed (e.g. sdfDetailMultiplier)
        /// but glyph outlines remain the same.
        /// </summary>
        internal void InvalidateAtlasData()
        {
            Cat.MeowFormat("[InvalidateAtlasData] {0}: glyphTable={1}, glyphLookup={2}",
                CachedName,
                glyphTable?.Count ?? -1,
                glyphLookupDictionary?.Count ?? -1);

            glyphTable?.Clear();
            characterTable?.Clear();

            glyphLookupDictionary?.Clear();
            characterLookupDictionary?.Clear();
            glyphIndexList?.Clear();
            glyphOverrideLookup = null;

            ClearAtlasEntries();
        }

        public void InvokeChanged()
        {
            Changed?.Invoke();
        }

        private void ClearAtlasEntries()
        {
            IsAtlasClearing = true;
            Changed?.Invoke();
            IsAtlasClearing = false;

            GlyphAtlas.ForEachInstance(a => a.ClearForFont(FontDataHash));
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            loadedFonts.Add(this);
#if UNITY_EDITOR
            EnsureFaceInfoFromFont();
#endif
        }

        private void OnDisable() => loadedFonts.Remove(this);

        private void OnDestroy()
        {
            ReleaseFTFace();

            glyphCurveCache?.Dispose();
            glyphCurveCache = null;

            filteredForBatch.Return();

            ClearAtlasEntries();
            Shaper.ClearCache(GetCachedInstanceId());
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
            glyphOverrideLookup = null;
            EnsureFaceInfoFromFont();
            Cat.MeowFormat("[UniTextFont.OnValidate] {0}: glyphLookup={1}, glyphTable={2}",
                CachedName, glyphLookupDictionary?.Count ?? -1, glyphTable?.Count ?? -1);
            Changed?.Invoke();
        }
        
        /// <summary>
        /// Ensures familyName, weightClass, and isItalic are populated from font data.
        /// Runs in editor only (OnValidate) so existing assets get these fields on upgrade.
        /// </summary>
        private void EnsureFaceInfoFromFont()
        {
            if (!HasFontData) return;

            var face = EnsureFTFace();
            if (face == IntPtr.Zero) return;

            var fresh = BuildFullFaceInfo(face);
            var dirty = false;

            if (faceInfo.familyName != fresh.familyName)
            { faceInfo.familyName = fresh.familyName; dirty = true; }
            if (faceInfo.styleName != fresh.styleName)
            { faceInfo.styleName = fresh.styleName; dirty = true; }
            if (faceInfo.weightClass != fresh.weightClass)
            { faceInfo.weightClass = fresh.weightClass; dirty = true; }
            if (faceInfo.isItalic != fresh.isItalic)
            { faceInfo.isItalic = fresh.isItalic; dirty = true; }

            if (dirty)
                EditorUtility.SetDirty(this);
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
