using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
namespace LightSide
{
    /// <summary>
    /// Font asset specialized for color emoji rendering using FreeType or browser APIs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EmojiFont extends <see cref="UniTextFont"/> to provide native color emoji support
    /// across all platforms. It uses FreeType for desktop/mobile and browser Canvas API for WebGL.
    /// </para>
    /// <para>
    /// The class provides a singleton <see cref="Instance"/> that automatically loads the
    /// system emoji font. Custom emoji fonts can be created via factory methods.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item>Automatic system emoji font detection on Windows, macOS, iOS, Android, Linux</item>
    /// <item>Dynamic atlas population with shelf-based packing</item>
    /// <item>Parallel glyph rendering using FreeType face pool</item>
    /// <item>WebGL support via browser Canvas 2D rendering</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextFont"/>
    /// <seealso cref="SystemEmojiFont"/>
    /// <seealso cref="FreeType"/>
    public class EmojiFont : UniTextFont
    {
        /// <summary>Reserved font ID for the emoji font (-1).</summary>
        public const int FontId = -1;

        private static EmojiFont instance;
        private static Material material;

        /// <summary>Raised when the <see cref="Disabled"/> property changes.</summary>
        public static event Action DisableChanged;

        private static bool disabled;

        /// <summary>Gets or sets whether emoji rendering is globally disabled.</summary>
        /// <remarks>When changed, invalidates all font caches and raises <see cref="DisableChanged"/>.</remarks>
        public static bool Disabled
        {
            get => disabled;
            set
            {
                if (disabled != value)
                {
                    disabled = value;
                    SharedFontCache.InvalidateAll();
                    DisableChanged?.Invoke();
                }
            }
        }

        /// <summary>Default emoji pixel size (128 on desktop/mobile, 64 on WebGL).</summary>
        public const int DefaultSize =
    #if !UNITY_WEBGL || UNITY_EDITOR
                128
    #else
                64
    #endif
            ;

        private int emojiPixelSize = DefaultSize;
        private int upem = 2048;
#pragma warning disable CS0414
        private bool fontLoaded;
#pragma warning restore CS0414
        private int loadedFaceIndex;

        [NonSerialized] private byte[] cachedFontData;
        [NonSerialized] private int cachedFontDataHash;

        [NonSerialized] private bool useCOLRv1;
        [NonSerialized] private bool isSbix;
        [NonSerialized] private bool isBitmapEmoji;

    #if !UNITY_WEBGL || UNITY_EDITOR
        [NonSerialized] private FreeTypeFacePool facePool;
        [NonSerialized] private COLRv1RendererPool colrPool;
    #endif

        [NonSerialized] private Shaper.FontCacheEntry hbFontCache;

        #region Atlas Fields

        /// <summary>Gets the atlas texture size in pixels (square).</summary>
        public int AtlasSize => GlyphAtlas.PageSize;

        /// <summary>Atlas padding for emoji glyph UV sampling (gutter inside tile prevents bilinear bleeding).</summary>
        public override int AtlasPadding => 1;

        /// <summary>
        /// Ensures the emoji material's mainTexture is in sync with the GlyphAtlas emoji texture.
        /// Called once per frame before rendering.
        /// </summary>
        private static bool subscribedToAtlasChange;

        internal void SyncMaterialTexture()
        {
            if (!subscribedToAtlasChange)
            {
                subscribedToAtlasChange = true;
                GlyphAtlas.AnyAtlasTextureChanged += OnEmojiAtlasTextureChanged;
            }

            var emojiAtlas = GlyphAtlas.Emoji;
            if (emojiAtlas == null) return;
            var atlasTex = emojiAtlas.AtlasTexture;
            if (material != null && atlasTex != null && material.mainTexture != atlasTex)
                Material.mainTexture = atlasTex;
        }

        private static void OnEmojiAtlasTextureChanged(Texture newTexture)
        {
            if (material == null) return;
            var emojiAtlas = GlyphAtlas.Emoji;
            if (emojiAtlas == null) return;
            var atlasTex = emojiAtlas.AtlasTexture;
            if (newTexture == atlasTex && atlasTex != null)
                material.mainTexture = atlasTex;
        }

        #endregion

        [NonSerialized] private int glyphDiagCount;

        /// <summary>Gets the shared material for emoji rendering (Texture2DArray shader).</summary>
        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = UniTextSettings.GetShader(UniTextSettings.ShaderEmoji);
                    if (shader == null)
                        shader = Shader.Find("UniText/Emoji");
                    material = new Material(shader)
                    {
                        name = "Emoji Material",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
                return material;
            }
        }

        /// <summary>Gets the singleton emoji font instance, creating it if necessary.</summary>
        /// <remarks>Returns null if <see cref="Disabled"/> is true.</remarks>
        public static EmojiFont Instance
        {
            get
            {
                if (Disabled)
                    return null;

                if (instance is null)
                    instance = CreateSystemEmojiFont();

                return instance;
            }
        }

        /// <summary>Returns true if emoji rendering is available on this platform.</summary>
        public static bool IsAvailable => Instance != null;

        /// <summary>Gets the pixel size used for rendering emoji glyphs.</summary>
        public int EmojiPixelSize => emojiPixelSize;

        /// <summary>Ensures the singleton instance and material are initialized.</summary>
        public static void EnsureInitialized()
        {
            var i = Instance;
            var m = Material;
        }

    #if UNITY_EDITOR
        static EmojiFont()
        {
            Reseter.UnmanagedCleaning += DisposeAll;
        }
    #endif

        /// <summary>Disposes the singleton instance and releases all resources.</summary>
        private static void DisposeAll()
        {
            if (instance != null)
            {
                instance.DisposeFacePool();
                instance.hbFontCache = null;

                DestroyImmediate(instance);
            }
            instance = null;
            currentlyLoadedData = null;
            currentlyLoadedFace = -1;

            if (material != null)
            {
                DestroyImmediate(material);
                material = null;
            }
        }

        #region RenderedGlyphData

        /// <summary>Internal structure holding rendered glyph bitmap data.</summary>
        private struct RenderedGlyphData
        {
            /// <summary>Bitmap width in pixels.</summary>
            public int width;
            /// <summary>Bitmap height in pixels.</summary>
            public int height;
            /// <summary>Horizontal bearing (offset from origin to left edge).</summary>
            public float bearingX;
            /// <summary>Vertical bearing (offset from baseline to top edge).</summary>
            public float bearingY;
            /// <summary>Horizontal advance width.</summary>
            public float advanceX;
            /// <summary>RGBA pixel data (4 bytes per pixel).</summary>
            public byte[] rgbaPixels;
            /// <summary>True if pixel format is BGRA (requires swizzling).</summary>
            public bool isBGRA;
        }

        #endregion

        #region Factory Methods

        /// <summary>Creates an emoji font from a file path.</summary>
        /// <param name="fontPath">Path to the font file (.ttf, .ttc).</param>
        /// <param name="faceIndex">Face index for TTC collections.</param>
        /// <param name="pixelSize">Desired emoji pixel size.</param>
        /// <returns>The created EmojiFont or null on failure.</returns>
        public static EmojiFont CreateFromPath(string fontPath, int faceIndex = 0, int pixelSize = DefaultSize)
        {
            if (string.IsNullOrEmpty(fontPath))
                return null;

            byte[] fontData;
            try
            {
                fontData = System.IO.File.ReadAllBytes(fontPath);
            }
            catch (Exception ex)
            {
                Cat.MeowError($"[EmojiFont] Failed to read font file '{fontPath}': {ex.Message}");
                return null;
            }

            var font = CreateFromData(fontData, faceIndex, pixelSize);
            if (font != null)
                Cat.MeowFormat("[EmojiFont] Loaded from: {0}", fontPath);
            return font;
        }

        /// <summary>Creates an emoji font from raw font data bytes.</summary>
        /// <param name="fontData">Font file content as byte array.</param>
        /// <param name="faceIndex">Face index for TTC collections.</param>
        /// <param name="pixelSize">Desired emoji pixel size.</param>
        /// <param name="sourceName">Optional name for logging purposes.</param>
        /// <returns>The created EmojiFont or null on failure.</returns>
        public static EmojiFont CreateFromData(byte[] fontData, int faceIndex = 0, int pixelSize = DefaultSize, string sourceName = null)
        {
            if (fontData == null || fontData.Length == 0)
                return null;

            if (!FreeType.LoadFontFromData(fontData, faceIndex))
            {
                Cat.MeowError("[EmojiFont] Failed to load font from data");
                return null;
            }

            var ftInfo = FreeType.GetFaceInfo();

            var font = CreateInstance<EmojiFont>();
            font.name = $"EmojiFont ({sourceName ?? ftInfo.familyName ?? "Data"})";
            font.hideFlags = HideFlags.HideAndDontSave;
            font.fontLoaded = true;
            font.loadedFaceIndex = faceIndex;
            font.cachedFontData = fontData;

            int fontUpem = Shaper.GetUpemFromFontData(fontData);
            if (fontUpem <= 0)
                fontUpem = ftInfo.unitsPerEm > 0 ? ftInfo.unitsPerEm : 2048;
            int[] availableSizes = ftInfo.hasFixedSizes ? ftInfo.availableSizes : null;

            var rawFtInfo = FT.GetFaceInfo(FreeType.GetCurrentFacePtr());
            ConfigureFont(font, fontUpem, pixelSize, availableSizes, rawFtInfo.ascender, rawFtInfo.descender);
            font.isSbix = ftInfo.hasSbix;
            font.isBitmapEmoji = ftInfo.hasFixedSizes && !ftInfo.hasSbix;

        #if !UNITY_WEBGL || UNITY_EDITOR
            if (BL.IsSupported && ftInfo.hasColor && !ftInfo.hasFixedSizes)
            {
                var tempFace = FT.LoadFace(fontData, faceIndex);
                if (tempFace != IntPtr.Zero)
                {
                    uint testGlyph = FT.GetCharIndex(tempFace, 0x1F600);
                    if (testGlyph != 0 && FT.HasColorGlyphPaint(tempFace, testGlyph))
                    {
                        font.useCOLRv1 = true;
                        Cat.Meow("[EmojiFont] COLRv1 detected, using Blend2D renderer");
                    }
                    FT.UnloadFace(tempFace);
                }
            }
        #endif

            var sizesStr = availableSizes != null ? string.Join(",", availableSizes) : "none";
            Cat.MeowFormat(
                "[EmojiFont] Created: {0}\n" +
                "  family={1} style={2} | glyphs={3} faces={4} faceIdx={5}\n" +
                "  upem={6} ascender={7} descender={8} height={9}\n" +
                "  requestedPx={10} selectedPx={11} strikes=[{12}]\n" +
                "  color={13} scalable={14} sbix={15} cbdt={16} COLRv1={17}\n" +
                "  dataSize={18} bytes",
                font.name,
                ftInfo.familyName ?? "?", ftInfo.styleName ?? "?", ftInfo.numGlyphs, ftInfo.numFaces, faceIndex,
                fontUpem, rawFtInfo.ascender, rawFtInfo.descender, rawFtInfo.height,
                pixelSize, font.emojiPixelSize, sizesStr,
                ftInfo.hasColor, ftInfo.isScalable, font.isSbix, font.isBitmapEmoji,
        #if !UNITY_WEBGL || UNITY_EDITOR
                font.useCOLRv1,
        #else
                false,
        #endif
                fontData.Length
            );

            GlyphAtlas.CreateEmojiInstance(font.emojiPixelSize);
            return font;
        }

    #if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>Creates a browser-based emoji font for WebGL builds.</summary>
        /// <param name="pixelSize">Desired emoji pixel size.</param>
        /// <returns>The created EmojiFont or null if browser rendering is unsupported.</returns>
        /// <remarks>Uses the browser's Canvas 2D API to render emoji.</remarks>
        public static EmojiFont CreateBrowserBased(int pixelSize = DefaultSize)
        {
            if (!WebGLEmoji.IsSupported)
            {
                Cat.MeowWarn("[EmojiFont] Browser emoji rendering not supported");
                return null;
            }

            var font = CreateInstance<EmojiFont>();
            font.name = "EmojiFont (Browser)";
            font.hideFlags = HideFlags.HideAndDontSave;
            font.fontLoaded = false;

            ConfigureFont(font, 2048, pixelSize, null);

            GlyphAtlas.CreateEmojiInstance(font.emojiPixelSize);
            Cat.Meow($"[EmojiFont] Created browser-based emoji font, size={pixelSize}");
            return font;
        }
    #endif

        private static void ConfigureFont(EmojiFont font, int fontUpem, int pixelSize, int[] availableSizes,
            short fontAscender = 0, short fontDescender = 0)
        {
            font.upem = fontUpem;
            font.unitsPerEm = fontUpem;

            if (availableSizes != null && availableSizes.Length > 0)
            {
                int bestSize = pixelSize;
                int bestDiff = int.MaxValue;
                foreach (var size in availableSizes)
                {
                    int diff = Math.Abs(size - pixelSize);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestSize = size;
                    }
                }
                font.emojiPixelSize = bestSize;
            }
            else
            {
                font.emojiPixelSize = pixelSize;
            }

            float ascent = fontAscender > 0 ? fontAscender : fontUpem * 0.8f;
            float descent = fontDescender < 0 ? fontDescender : -fontUpem * 0.2f;

            font.faceInfo = new FaceInfo
            {
                unitsPerEm = fontUpem,
                lineHeight = Mathf.RoundToInt(ascent - descent),
                ascentLine = Mathf.RoundToInt(ascent),
                descentLine = Mathf.RoundToInt(descent)
            };

            font.ReadFontAssetDefinition();
        }

        private static EmojiFont CreateSystemEmojiFont()
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            return CreateBrowserBased(DefaultSize);
    #elif UNITY_IOS && !UNITY_EDITOR
            var fontData = NativeFontReader.GetEmojiFontData();
            if (fontData == null || fontData.Length == 0)
            {
                Debug.LogWarning("[EmojiFont] iOS emoji font not available");
                return null;
            }
            return CreateFromData(fontData, 0, DefaultSize, "Apple Color Emoji");
    #else
            var path = SystemEmojiFont.GetDefaultEmojiFont();
            return string.IsNullOrEmpty(path) ? null : CreateFromPath(path);
    #endif
        }

        #endregion

        #region Font Data

        /// <inheritdoc/>
        public override int GetCachedInstanceId() => FontId;

        /// <inheritdoc/>
        public override bool HasFontData => cachedFontData != null;

        /// <inheritdoc/>
        public override int FontDataHash
        {
            get
            {
                if (cachedFontDataHash != 0)
                    return cachedFontDataHash;

                if (cachedFontData == null)
                    return 0;

                cachedFontDataHash = cachedFontData.Length.GetHashCode();
                return cachedFontDataHash;
            }
        }

        /// <inheritdoc/>
        public override byte[] FontData => cachedFontData;

        /// <summary>Gets glyph advance from HarfBuzz (hmtx table) in design units.</summary>
        /// <param name="glyphIndex">Glyph index.</param>
        /// <returns>Advance in design units, or -1 if unavailable.</returns>
        private int GetHarfBuzzAdvance(uint glyphIndex)
        {
            if (cachedFontData == null)
                return -1;

            if (hbFontCache == null || !hbFontCache.IsValid)
            {
                var fontHash = FontDataHash;
                if (!Shaper.TryGetCacheByHash(fontHash, out hbFontCache))
                    hbFontCache = Shaper.CreateCacheByHash(fontHash, cachedFontData);
            }

            return hbFontCache?.GetGlyphAdvance(glyphIndex) ?? -1;
        }

        #endregion

        #region Glyph Rendering

        /// <inheritdoc/>
        public override UniTextFontError LoadFontFace()
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            return UniTextFontError.Success;
    #else
            return fontLoaded ? UniTextFontError.Success : UniTextFontError.InvalidFile;
    #endif
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Uses parallel FreeType rendering on desktop/mobile, Core Text on iOS, browser Canvas API on WebGL.
        /// WebGL and sequential fallback paths bypass the split pipeline.
        /// </remarks>
        internal override int TryAddGlyphsBatch(List<uint> glyphIndices, UniTextBase.RenderModee mode,
            long varHash48 = 0, int[] ftCoords = null)
        {
            if (glyphIndices == null || glyphIndices.Count == 0)
                return 0;

    #if UNITY_WEBGL && !UNITY_EDITOR
            return TryAddGlyphsBatchWebGL(glyphIndices);
    #else
            if (!fontLoaded || cachedFontData == null)
                return TryAddGlyphsBatchFreeTypeSequential(glyphIndices);

            var batch = PrepareGlyphBatch(glyphIndices, mode);
            if (batch == null) return 0;
            var b = batch.Value;
            var rendered = RenderPreparedBatch(b);
            var result = PackRenderedBatch(rendered, b, mode);
            b.filteredGlyphs.Return();
            return result;
    #endif
        }

        /// <inheritdoc/>
        internal override PreparedBatch? PrepareGlyphBatch(List<uint> glyphIndices, UniTextBase.RenderModee mode,
            long varHash48 = 0, int[] ftCoords = null)
        {
            if (glyphIndices == null || glyphIndices.Count == 0)
                return null;

    #if UNITY_WEBGL && !UNITY_EDITOR
            return null;
    #else
            if (!fontLoaded || cachedFontData == null)
                return null;

            var atlas = GlyphAtlas.Emoji;
            var varHash = DefaultVarHash48;

            var filtered = new PooledBuffer<uint>();
            filtered.EnsureCapacity(glyphIndices.Count);

            for (int i = 0; i < glyphIndices.Count; i++)
            {
                var glyphIndex = glyphIndices[i];
                bool isNew = glyphLookupDictionary == null || !glyphLookupDictionary.ContainsKey(GlyphKey(glyphIndex));

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

                if (!isNew)
                    glyphLookupDictionary.Remove(GlyphKey(glyphIndex));

                filtered.Add(glyphIndex);
            }

            if (filtered.count == 0) return null;

            if (useCOLRv1)
                colrPool ??= new COLRv1RendererPool(cachedFontData, loadedFaceIndex);
            else
                facePool ??= new FreeTypeFacePool(cachedFontData, loadedFaceIndex, emojiPixelSize);

            return new PreparedBatch
            {
                filteredGlyphs = filtered,
                varHash48 = varHash
            };
    #endif
        }

        public override long EstimateTileArea(object renderedObj)
        {
            if (renderedObj == null) return 0;
            int tileSize = emojiPixelSize;
            long tilePx = (long)tileSize * tileSize;

#if !UNITY_WEBGL || UNITY_EDITOR
            if (useCOLRv1)
            {
                var rendered = (COLRv1RendererPool.RenderedGlyph[])renderedObj;
                long area = 0;
                for (int i = 0; i < rendered.Length; i++)
                    if (rendered[i].isValid) area += tilePx;
                return area;
            }
            else
#endif
            {
                var rendered = (FreeType.RenderedGlyph[])renderedObj;
                long area = 0;
                for (int i = 0; i < rendered.Length; i++)
                    if (rendered[i].isValid) area += tilePx;
                return area;
            }
        }

        /// <inheritdoc/>
        internal override object RenderPreparedBatch(PreparedBatch batch)
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            return null;
    #elif UNITY_IOS && !UNITY_EDITOR
            var glyphs = batch.filteredGlyphs;
            var rendered = new FreeType.RenderedGlyph[glyphs.count];
            int pixelSize = emojiPixelSize;
            System.Threading.Tasks.Parallel.For(0, glyphs.count,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (NativeFontReader.TryRenderEmojiGlyph(glyphs[i], pixelSize, out var result))
                        rendered[i] = result;
                });
            return rendered;
    #else
            if (useCOLRv1)
            {
                int renderSize = GlyphAtlas.Emoji.MaxContentSize;
                var colrResults = colrPool.RenderGlyphsBatch(batch.filteredGlyphs, renderSize);

                return colrResults;
            }
            else
                return facePool.RenderGlyphsBatch(batch.filteredGlyphs);
    #endif
        }

        internal override void ReleaseBatchProtectedKeys(UniTextBase.RenderModee mode)
        {
            if (batchProtectedKeys == null || batchProtectedKeys.Count == 0) return;
            var atlas = GlyphAtlas.Emoji;
            for (int i = 0; i < batchProtectedKeys.Count; i++)
                atlas.Release(batchProtectedKeys[i]);
            batchProtectedKeys.Clear();
        }

        /// <inheritdoc/>
        internal override int PackRenderedBatch(object renderedObj, PreparedBatch batch, UniTextBase.RenderModee mode)
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            return 0;
    #else
            if (renderedObj == null) return 0;

            var toRender = batch.filteredGlyphs;
            var atlas = GlyphAtlas.Emoji;
            long varHash = GlyphAtlas.DefaultVarHash(FontDataHash);
            int totalAdded = 0;

            if (useCOLRv1)
            {
                var rendered = (COLRv1RendererPool.RenderedGlyph[])renderedObj;
                for (int i = 0; i < toRender.count; i++)
                {
                    var colrRendered = rendered[i];
                    if (!colrRendered.isValid) continue;

                    float rs = colrRendered.renderScale;
                    var metricsData = new RenderedGlyphData
                    {
                        width = rs < 1f ? (int)Math.Ceiling(colrRendered.width / rs) : colrRendered.width,
                        height = rs < 1f ? (int)Math.Ceiling(colrRendered.height / rs) : colrRendered.height,
                        bearingX = colrRendered.bearingX,
                        bearingY = colrRendered.bearingY,
                        advanceX = colrRendered.advanceX > 0 ? colrRendered.advanceX / rs : colrRendered.width / rs,
                    };

                    var glyphId = toRender[i];
                    var metrics = ComputeGlyphMetrics(glyphId, metricsData);
                    var entry = atlas.EnsureEmojiGlyph(varHash, glyphId, FontDataHash,
                        colrRendered.rgbaPixels, colrRendered.width, colrRendered.height, false, metrics);

                    if (entry.encodedTile >= 0)
                    {
                        var pixelData = new RenderedGlyphData { width = colrRendered.width, height = colrRendered.height };
                        RegisterGlyphFromAtlas(glyphId, pixelData, entry);
                        totalAdded++;
                    }
                }
            }
            else
            {
                var rendered = (FreeType.RenderedGlyph[])renderedObj;
                for (int i = 0; i < toRender.count; i++)
                {
                    var ftRendered = rendered[i];
                    if (!ftRendered.isValid) continue;

                    var data = new RenderedGlyphData
                    {
                        width = ftRendered.width,
                        height = ftRendered.height,
                        bearingX = ftRendered.bearingX,
                        bearingY = ftRendered.bearingY,
                        advanceX = ftRendered.advanceX > 0 ? ftRendered.advanceX : ftRendered.width,
                        rgbaPixels = ftRendered.rgbaPixels,
                        isBGRA = ftRendered.isBGRA
                    };

                    var glyphId = toRender[i];
                    var metrics = ComputeGlyphMetrics(glyphId, data);
                    var entry = atlas.EnsureEmojiGlyph(varHash, glyphId, FontDataHash,
                        data.rgbaPixels, data.width, data.height, data.isBGRA, metrics);

                    if (entry.encodedTile >= 0)
                    {
                        RegisterGlyphFromAtlas(glyphId, data, entry);
                        totalAdded++;
                    }
                }
            }

            return totalAdded;
    #endif
        }

    #if UNITY_WEBGL && !UNITY_EDITOR
        private unsafe int TryAddGlyphsBatchWebGL(List<uint> glyphIndices)
        {
            var filteredGlyphs = FilterNewGlyphs(glyphIndices);
            if (filteredGlyphs == null)
                return 0;

            if (!WebGLEmoji.TryRenderEmojiBatch(filteredGlyphs, emojiPixelSize, out var batchResult))
            {
                Cat.MeowWarn($"[EmojiFont] WebGL batch render failed for {filteredGlyphs.Count} glyphs");
                return 0;
            }

            var atlas = GlyphAtlas.Emoji;
            long varHash = GlyphAtlas.DefaultVarHash(FontDataHash);
            int totalAdded = 0;

            for (int i = 0; i < batchResult.count; i++)
            {
                WebGLEmoji.GetBatchMetrics(i, out int w, out int h, out int bearingX, out int bearingY, out float advanceX);

                if (w == 0 || h == 0)
                    continue;

                int pixelOffset = WebGLEmoji.GetBatchPixelOffset(i);
                int pixelBytes = w * h * 4;
                var pixels = UniTextArrayPool<byte>.Rent(pixelBytes);
                byte* srcBase = (byte*)batchResult.pixelsPtr + pixelOffset;
                fixed (byte* dst = &pixels[0])
                {
                    Buffer.MemoryCopy(srcBase, dst, pixelBytes, pixelBytes);
                }

                var rendered = new RenderedGlyphData
                {
                    width = w,
                    height = h,
                    bearingX = bearingX,
                    bearingY = bearingY,
                    advanceX = advanceX > 0 ? advanceX : w,
                    rgbaPixels = pixels,
                    isBGRA = false
                };

                var glyphId = filteredGlyphs[i];
                var metrics = ComputeGlyphMetrics(glyphId, rendered);
                var entry = atlas.EnsureEmojiGlyph(varHash, glyphId, FontDataHash,
                    pixels, w, h, false, metrics);

                if (entry.encodedTile >= 0)
                {
                    RegisterGlyphFromAtlas(glyphId, rendered, entry);
                    totalAdded++;
                }
            }

            WebGLEmoji.FreeBatchData();
            return totalAdded;
        }

    #endif

    #if !UNITY_WEBGL || UNITY_EDITOR

        private int TryAddGlyphsBatchFreeTypeSequential(List<uint> glyphIndices)
        {
            if (fontLoaded)
                EnsureFreeTypeFontLoaded();

            var atlas = GlyphAtlas.Emoji;
            long varHash = GlyphAtlas.DefaultVarHash(FontDataHash);
            int totalAdded = 0;

            for (int i = 0; i < glyphIndices.Count; i++)
            {
                var glyphIndex = glyphIndices[i];

                if (glyphIndex == 0 || (glyphLookupDictionary != null && glyphLookupDictionary.ContainsKey(GlyphKey(glyphIndex))))
                    continue;

                if (!TryRenderGlyph(glyphIndex, out var rendered))
                    continue;
                
                int pixelBytes = rendered.width * rendered.height * 4;
                var pooledPixels = UniTextArrayPool<byte>.Rent(pixelBytes);
                Buffer.BlockCopy(rendered.rgbaPixels, 0, pooledPixels, 0, pixelBytes);

                var metrics = ComputeGlyphMetrics(glyphIndex, rendered);
                var entry = atlas.EnsureEmojiGlyph(varHash, glyphIndex, FontDataHash,
                    pooledPixels, rendered.width, rendered.height, rendered.isBGRA, metrics);

                if (entry.encodedTile >= 0)
                {
                    RegisterGlyphFromAtlas(glyphIndex, rendered, entry);
                    totalAdded++;
                }
            }

            return totalAdded;
        }
    #endif

        private GlyphMetrics ComputeGlyphMetrics(uint glyphIndex, RenderedGlyphData rendered)
        {
            float pixelsToDesign;
            float bearingYDesign;

    #if !UNITY_WEBGL || UNITY_EDITOR
            int hbAdvance = GetHarfBuzzAdvance(glyphIndex);
    #else
            int hbAdvance = -1;
    #endif

            if (isSbix)
            {
                int actualBitmapSize = Math.Max(rendered.width, rendered.height);
                pixelsToDesign = actualBitmapSize > 0
                    ? (float)upem / actualBitmapSize
                    : (float)upem / emojiPixelSize;

                if (rendered.bearingY >= rendered.height)
                {
                    float heightD = rendered.height * pixelsToDesign;
                    float lineExtent = faceInfo.ascentLine - faceInfo.descentLine;
                    bearingYDesign = faceInfo.ascentLine - (lineExtent - heightD) * 0.5f;
                }
                else
                {
                    bearingYDesign = rendered.bearingY * pixelsToDesign;
                }
            }
            else if (useCOLRv1)
            {
                int renderSize = GlyphAtlas.Emoji.MaxContentSize;
                pixelsToDesign = (float)upem / renderSize;
                bearingYDesign = rendered.bearingY * pixelsToDesign;
            }
            else if (isBitmapEmoji && hbAdvance > 0 && rendered.width > 0)
            {
                pixelsToDesign = (float)hbAdvance / rendered.width;
                bearingYDesign = rendered.bearingY * pixelsToDesign;
            }
            else
            {
                pixelsToDesign = (float)upem / emojiPixelSize;
                bearingYDesign = rendered.bearingY * pixelsToDesign;
            }

            float bitmapWidthDesign = rendered.width * pixelsToDesign;
            float bitmapHeightDesign = rendered.height * pixelsToDesign;
            float bearingXDesign = rendered.bearingX * pixelsToDesign;

    #if UNITY_WEBGL && !UNITY_EDITOR
            float advanceDesign = rendered.advanceX * ((float)upem / emojiPixelSize);
    #else
            float advanceDesign = hbAdvance > 0 ? hbAdvance : bitmapWidthDesign;
    #endif

            return new GlyphMetrics(bitmapWidthDesign, bitmapHeightDesign, bearingXDesign, bearingYDesign, advanceDesign);
        }

        private void RegisterGlyphFromAtlas(uint glyphIndex, RenderedGlyphData rendered, GlyphAtlas.GlyphEntry entry)
        {
            var atlas = GlyphAtlas.Emoji;
            atlas.DecodeTileXY(entry.encodedTile, atlas.TileSizeFromEncoded(entry.encodedTile), out int tileX, out int tileY);
            int g = atlas.TileGutter;
            var rect = new GlyphRect(tileX + g, tileY + g, rendered.width, rendered.height);
            var glyph = new Glyph(glyphIndex, entry.metrics, rect, entry.pageIndex);

            glyphTable.Add(glyph);
            glyphLookupDictionary ??= new Dictionary<long, Glyph>();
            glyphLookupDictionary[GlyphKey(glyphIndex)] = glyph;
            glyphIndexList ??= new List<uint>();
            glyphIndexList.Add(glyphIndex);
        }

    #if !UNITY_WEBGL || UNITY_EDITOR
        private bool TryRenderGlyph(uint glyphIndex, out RenderedGlyphData rendered)
        {
            rendered = default;

            if (!fontLoaded)
                return false;

            EnsureFreeTypeFontLoaded();

            if (!FreeType.TryRenderGlyph(glyphIndex, emojiPixelSize, out var ftRendered, out var failReason))
            {
                Cat.MeowWarn($"[EmojiFont] Render failed glyph {glyphIndex}: {failReason}");
                return false;
            }

            rendered = new RenderedGlyphData
            {
                width = ftRendered.width,
                height = ftRendered.height,
                bearingX = ftRendered.bearingX,
                bearingY = ftRendered.bearingY,
                advanceX = ftRendered.advanceX > 0 ? ftRendered.advanceX : ftRendered.width,
                rgbaPixels = ftRendered.rgbaPixels
            };
            return true;
        }
    #endif

        #endregion

        public override void ClearDynamicData()
        {
            GlyphAtlas.Emoji.ClearForFont(FontDataHash);
            base.ClearDynamicData();
        }

        private static byte[] currentlyLoadedData;
        private static int currentlyLoadedFace = -1;

        private void EnsureFreeTypeFontLoaded()
        {
            if (currentlyLoadedData == cachedFontData && currentlyLoadedFace == loadedFaceIndex)
                return;

            if (cachedFontData != null)
            {
                FreeType.LoadFontFromData(cachedFontData, loadedFaceIndex);
                currentlyLoadedData = cachedFontData;
                currentlyLoadedFace = loadedFaceIndex;
            }
        }

        /// <summary>Disposes the FreeType face pool and COLRv1 renderer pool used for parallel glyph rendering.</summary>
        public void DisposeFacePool()
        {
    #if !UNITY_WEBGL || UNITY_EDITOR
            facePool?.Dispose();
            facePool = null;

            colrPool?.Dispose();
            colrPool = null;
    #endif
        }
    }

}
