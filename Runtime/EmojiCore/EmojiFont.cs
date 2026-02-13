using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
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

        [NonSerialized] private int glyphDiagCount;

        /// <summary>Gets the shared material for emoji rendering (UI/Default shader).</summary>
        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    material = new Material(Shader.Find("UI/Default"))
                    {
                        name = "Emoji Material",
                        hideFlags = HideFlags.DontSave
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
                if (instance.atlasTextures != null)
                {
                    foreach (var texture in instance.atlasTextures)
                        if (texture != null)
                            DestroyImmediate(texture);

                    instance.atlasTextures.Clear();
                }

                instance.DisposeFacePool();
                instance.hbFontCache = null;

                DestroyImmediate(instance);
            }
            instance = null;

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
            font.hideFlags = HideFlags.DontSave;
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
            font.hideFlags = HideFlags.DontSave;
            font.fontLoaded = false;

            ConfigureFont(font, 2048, pixelSize, null);

            Cat.Meow($"[EmojiFont] Created browser-based emoji font, size={pixelSize}");
            return font;
        }
    #endif

        private static void ConfigureFont(EmojiFont font, int fontUpem, int pixelSize, int[] availableSizes,
            short fontAscender = 0, short fontDescender = 0)
        {
            font.atlasRenderMode = UniTextRenderMode.Smooth;
            font.atlasSize = 2048;

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
                pointSize = fontUpem,
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

        /// <summary>Clears the cached font data to free memory.</summary>
        public void ClearFontDataCache()
        {
            cachedFontData = null;
            cachedFontDataHash = 0;
            hbFontCache = null;
        }

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
        public override int TryAddGlyphsBatch(List<uint> glyphIndices)
        {
            if (glyphIndices == null || glyphIndices.Count == 0)
                return 0;

    #if UNITY_WEBGL && !UNITY_EDITOR
            return TryAddGlyphsBatchWebGL(glyphIndices);
    #else
            if (!fontLoaded || cachedFontData == null)
                return TryAddGlyphsBatchFreeTypeSequential(glyphIndices);

            var batch = PrepareGlyphBatch(glyphIndices);
            if (batch == null) return 0;
            var rendered = RenderPreparedBatch(batch.Value);
            return PackRenderedBatch(rendered, batch.Value);
    #endif
        }

        /// <inheritdoc/>
        public override PreparedBatch? PrepareGlyphBatch(List<uint> glyphIndices)
        {
            if (glyphIndices == null || glyphIndices.Count == 0)
                return null;

    #if UNITY_WEBGL && !UNITY_EDITOR
            return null;
    #else
            if (!fontLoaded || cachedFontData == null)
                return null;

            var toAdd = FilterNewGlyphs(glyphIndices);
            if (toAdd == null)
                return null;

            if (atlasTextures.Count == 0) CreateNewAtlasTexture();

            if (useCOLRv1)
                colrPool ??= new COLRv1RendererPool(cachedFontData, loadedFaceIndex);
            else
                facePool ??= new FreeTypeFacePool(cachedFontData, loadedFaceIndex, emojiPixelSize);

            var owned = new List<uint>(toAdd.Count);
            owned.AddRange(toAdd);

            return new PreparedBatch
            {
                filteredGlyphs = owned,
                pointSize = emojiPixelSize,
                spread = 0,
                metricsConversion = 0
            };
    #endif
        }

        /// <inheritdoc/>
        public override object RenderPreparedBatch(PreparedBatch batch)
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            return null;
    #elif UNITY_IOS && !UNITY_EDITOR
            var rendered = new FreeType.RenderedGlyph[batch.filteredGlyphs.Count];
            int pixelSize = batch.pointSize;
            System.Threading.Tasks.Parallel.For(0, batch.filteredGlyphs.Count,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (NativeFontReader.TryRenderEmojiGlyph(batch.filteredGlyphs[i], pixelSize, out var result))
                        rendered[i] = result;
                });
            return rendered;
    #else
            if (useCOLRv1)
                return colrPool.RenderGlyphsBatch(batch.filteredGlyphs, batch.pointSize);
            else
                return facePool.RenderGlyphsBatch(batch.filteredGlyphs);
    #endif
        }

        /// <inheritdoc/>
        public override int PackRenderedBatch(object renderedObj, PreparedBatch batch)
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            return 0;
    #else
            if (renderedObj == null) return 0;

            var toRender = batch.filteredGlyphs;

            if (packedGlyphs == null || packedGlyphs.Length < toRender.Count)
                packedGlyphs = new PackedGlyph[Math.Max(toRender.Count, 4096)];
            packedCount = 0;

            int startAtlasIndex = atlasTextures.Count - 1;

            if (useCOLRv1)
            {
                var rendered = (COLRv1RendererPool.RenderedGlyph[])renderedObj;
                for (int i = 0; i < toRender.Count; i++)
                {
                    var colrRendered = rendered[i];
                    if (!colrRendered.isValid) continue;

                    if (!TryPackGlyphShelf(colrRendered.width, colrRendered.height, out var rect))
                    {
                        CreateNewAtlasTexture();
                        TryPackGlyphShelf(colrRendered.width, colrRendered.height, out rect);
                    }

                    packedGlyphs[packedCount++] = new PackedGlyph
                    {
                        data = new RenderedGlyphData
                        {
                            width = colrRendered.width,
                            height = colrRendered.height,
                            bearingX = colrRendered.bearingX,
                            bearingY = colrRendered.bearingY,
                            advanceX = colrRendered.advanceX > 0 ? colrRendered.advanceX : colrRendered.width,
                            rgbaPixels = colrRendered.rgbaPixels,
                            isBGRA = false
                        },
                        rect = rect,
                        glyphIndex = toRender[i],
                        atlasIndex = atlasTextures.Count - 1
                    };
                }
            }
            else
            {
                var rendered = (FreeType.RenderedGlyph[])renderedObj;
                for (int i = 0; i < toRender.Count; i++)
                {
                    var ftRendered = rendered[i];
                    if (!ftRendered.isValid) continue;

                    if (!TryPackGlyphShelf(ftRendered.width, ftRendered.height, out var rect))
                    {
                        CreateNewAtlasTexture();
                        TryPackGlyphShelf(ftRendered.width, ftRendered.height, out rect);
                    }

                    packedGlyphs[packedCount++] = new PackedGlyph
                    {
                        data = new RenderedGlyphData
                        {
                            width = ftRendered.width,
                            height = ftRendered.height,
                            bearingX = ftRendered.bearingX,
                            bearingY = ftRendered.bearingY,
                            advanceX = ftRendered.advanceX > 0 ? ftRendered.advanceX : ftRendered.width,
                            rgbaPixels = ftRendered.rgbaPixels,
                            isBGRA = ftRendered.isBGRA
                        },
                        rect = rect,
                        glyphIndex = toRender[i],
                        atlasIndex = atlasTextures.Count - 1
                    };
                }
            }

            CopyAllGlyphsParallel(startAtlasIndex);
            return FinalizePackedGlyphs(startAtlasIndex);
    #endif
        }

    #if UNITY_WEBGL && !UNITY_EDITOR
        private int TryAddGlyphsBatchWebGL(List<uint> glyphIndices)
        {
            if (atlasTextures.Count == 0) CreateNewAtlasTexture();

            var filteredGlyphs = FilterNewGlyphs(glyphIndices);
            if (filteredGlyphs == null)
                return 0;

            if (!WebGLEmoji.TryRenderEmojiBatch(filteredGlyphs, emojiPixelSize, out var batchResult))
            {
                Cat.MeowWarn($"[EmojiFont] WebGL batch render failed for {filteredGlyphs.Count} glyphs");
                return 0;
            }

            int totalAdded = 0;
            var rawAtlasData = atlasTextures[^1].GetRawTextureData<Color32>();

            for (int i = 0; i < batchResult.count; i++)
            {
                WebGLEmoji.GetBatchMetrics(i, out int w, out int h, out int bearingX, out int bearingY, out float advanceX);

                if (w == 0 || h == 0)
                    continue;

                if (!TryPackGlyphShelf(w, h, out var rect))
                {
                    atlasTextures[^1].Apply(false, false);
                    CreateNewAtlasTexture();
                    rawAtlasData = atlasTextures[^1].GetRawTextureData<Color32>();
                    TryPackGlyphShelf(w, h, out rect);
                }

                int pixelOffset = WebGLEmoji.GetBatchPixelOffset(i);
                CopyPixelsToAtlasFromPtr(batchResult.pixelsPtr, pixelOffset, w, h, rect, rawAtlasData);

                var rendered = new RenderedGlyphData
                {
                    width = w,
                    height = h,
                    bearingX = bearingX,
                    bearingY = bearingY,
                    advanceX = advanceX > 0 ? advanceX : w
                };
                RegisterGlyph(filteredGlyphs[i], rendered, rect);
                totalAdded++;
            }

            WebGLEmoji.FreeBatchData();
            atlasTextures[^1].Apply(false, false);

            return totalAdded;
        }

        private unsafe void CopyPixelsToAtlasFromPtr(IntPtr srcPtr, int srcOffset, int w, int h,
            GlyphRect rect, Unity.Collections.NativeArray<Color32> rawData)
        {
            byte* src = (byte*)srcPtr + srcOffset;
            var dstBase = (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(rawData);
            int atlasW = atlasSize;
            int rowBytes = w * 4;

            for (int y = 0; y < h; y++)
            {
                int srcY = h - 1 - y; 
                byte* srcRow = src + srcY * rowBytes;
                byte* dstRow = (byte*)(dstBase + (rect.y + y) * atlasW + rect.x);

                Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
            }
        }
    #endif

    #if !UNITY_WEBGL || UNITY_EDITOR
        private struct PackedGlyph
        {
            public RenderedGlyphData data;
            public GlyphRect rect;
            public uint glyphIndex;
            public int atlasIndex;
        }

        private static PackedGlyph[] packedGlyphs;
        private static int packedCount;

        private int TryAddGlyphsBatchFreeType(List<uint> glyphIndices)
        {
            if (!fontLoaded || cachedFontData == null)
                return TryAddGlyphsBatchFreeTypeSequential(glyphIndices);

            if (useCOLRv1)
                return TryAddGlyphsBatchCOLRv1(glyphIndices);

            if (atlasTextures.Count == 0) CreateNewAtlasTexture();

            var toRender = FilterNewGlyphs(glyphIndices);
            if (toRender == null)
                return 0;

            facePool ??= new FreeTypeFacePool(cachedFontData, loadedFaceIndex, emojiPixelSize);

            var rendered = facePool.RenderGlyphsBatch(toRender);

            if (packedGlyphs == null || packedGlyphs.Length < toRender.Count)
                packedGlyphs = new PackedGlyph[Math.Max(toRender.Count, 4096)];
            packedCount = 0;

            int startAtlasIndex = atlasTextures.Count - 1;

            for (int i = 0; i < toRender.Count; i++)
            {
                var ftRendered = rendered[i];
                if (!ftRendered.isValid)
                    continue;

                if (!TryPackGlyphShelf(ftRendered.width, ftRendered.height, out var rect))
                {
                    CreateNewAtlasTexture();
                    TryPackGlyphShelf(ftRendered.width, ftRendered.height, out rect);
                }

                packedGlyphs[packedCount++] = new PackedGlyph
                {
                    data = new RenderedGlyphData
                    {
                        width = ftRendered.width,
                        height = ftRendered.height,
                        bearingX = ftRendered.bearingX,
                        bearingY = ftRendered.bearingY,
                        advanceX = ftRendered.advanceX > 0 ? ftRendered.advanceX : ftRendered.width,
                        rgbaPixels = ftRendered.rgbaPixels,
                        isBGRA = ftRendered.isBGRA
                    },
                    rect = rect,
                    glyphIndex = toRender[i],
                    atlasIndex = atlasTextures.Count - 1
                };
            }

            CopyAllGlyphsParallel(startAtlasIndex);
            return FinalizePackedGlyphs(startAtlasIndex);
        }

        private int TryAddGlyphsBatchCOLRv1(List<uint> glyphIndices)
        {
            if (atlasTextures.Count == 0) CreateNewAtlasTexture();

            var toRender = FilterNewGlyphs(glyphIndices);
            if (toRender == null)
                return 0;

            colrPool ??= new COLRv1RendererPool(cachedFontData, loadedFaceIndex);

            var rendered = colrPool.RenderGlyphsBatch(toRender, emojiPixelSize);

            if (packedGlyphs == null || packedGlyphs.Length < toRender.Count)
                packedGlyphs = new PackedGlyph[Math.Max(toRender.Count, 4096)];
            packedCount = 0;

            int startAtlasIndex = atlasTextures.Count - 1;

            for (int i = 0; i < toRender.Count; i++)
            {
                var colrRendered = rendered[i];
                if (!colrRendered.isValid)
                    continue;

                if (!TryPackGlyphShelf(colrRendered.width, colrRendered.height, out var rect))
                {
                    CreateNewAtlasTexture();
                    TryPackGlyphShelf(colrRendered.width, colrRendered.height, out rect);
                }

                packedGlyphs[packedCount++] = new PackedGlyph
                {
                    data = new RenderedGlyphData
                    {
                        width = colrRendered.width,
                        height = colrRendered.height,
                        bearingX = colrRendered.bearingX,
                        bearingY = colrRendered.bearingY,
                        advanceX = colrRendered.advanceX > 0 ? colrRendered.advanceX : colrRendered.width,
                        rgbaPixels = colrRendered.rgbaPixels,
                        isBGRA = false
                    },
                    rect = rect,
                    glyphIndex = toRender[i],
                    atlasIndex = atlasTextures.Count - 1
                };
            }

            CopyAllGlyphsParallel(startAtlasIndex);
            return FinalizePackedGlyphs(startAtlasIndex);
        }

    #if UNITY_IOS && !UNITY_EDITOR
        /// <summary>
        /// Renders emoji glyphs using iOS Core Text API.
        /// Required because Apple Color Emoji uses proprietary 'emjc' format that FreeType cannot decode.
        /// </summary>
        private int TryAddGlyphsBatchCoreText(List<uint> glyphIndices)
        {
            if (atlasTextures.Count == 0) CreateNewAtlasTexture();

            var toRender = FilterNewGlyphs(glyphIndices);
            if (toRender == null)
                return 0;

            var rendered = new FreeType.RenderedGlyph[toRender.Count];
            int pixelSize = emojiPixelSize;

            System.Threading.Tasks.Parallel.For(0, toRender.Count,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    if (NativeFontReader.TryRenderEmojiGlyph(toRender[i], pixelSize, out var result))
                        rendered[i] = result;
                });

            if (packedGlyphs == null || packedGlyphs.Length < toRender.Count)
                packedGlyphs = new PackedGlyph[Math.Max(toRender.Count, 4096)];
            packedCount = 0;

            int startAtlasIndex = atlasTextures.Count - 1;

            for (int i = 0; i < toRender.Count; i++)
            {
                var ctRendered = rendered[i];
                if (!ctRendered.isValid)
                    continue;

                if (!TryPackGlyphShelf(ctRendered.width, ctRendered.height, out var rect))
                {
                    CreateNewAtlasTexture();
                    TryPackGlyphShelf(ctRendered.width, ctRendered.height, out rect);
                }

                packedGlyphs[packedCount++] = new PackedGlyph
                {
                    data = new RenderedGlyphData
                    {
                        width = ctRendered.width,
                        height = ctRendered.height,
                        bearingX = ctRendered.bearingX,
                        bearingY = ctRendered.bearingY,
                        advanceX = ctRendered.advanceX > 0 ? ctRendered.advanceX : ctRendered.width,
                        rgbaPixels = ctRendered.rgbaPixels,
                        isBGRA = false  
                    },
                    rect = rect,
                    glyphIndex = toRender[i],
                    atlasIndex = atlasTextures.Count - 1
                };
            }

            CopyAllGlyphsParallel(startAtlasIndex);
            var totalAdded = FinalizePackedGlyphs(startAtlasIndex);
            Cat.MeowFormat("[EmojiFont] Core Text rendered {0} glyphs", totalAdded);
            return totalAdded;
        }
    #endif

        private unsafe void CopyAllGlyphsParallel(int startAtlasIndex)
        {
            int count = packedCount;
            if (count == 0)
                return;

            int atlasCount = atlasTextures.Count - startAtlasIndex;
            var atlasPtrs = stackalloc Color32*[atlasCount];
            for (int a = 0; a < atlasCount; a++)
                atlasPtrs[a] = (Color32*)atlasTextures[startAtlasIndex + a].GetRawTextureData<Color32>().GetUnsafePtr();

            int atlasW = atlasSize;
            var glyphs = packedGlyphs;
            int baseAtlas = startAtlasIndex;

            var partitioner = System.Collections.Concurrent.Partitioner.Create(0, count, Math.Max(1, count / Environment.ProcessorCount));

            System.Threading.Tasks.Parallel.ForEach(partitioner, range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    ref var packed = ref glyphs[i];
                    var dstBase = atlasPtrs[packed.atlasIndex - baseAtlas];
                    CopyPixelsToAtlasUnsafe(packed.data, packed.rect, dstBase, atlasW);
                }
            });
        }

        private static unsafe void CopyPixelsToAtlasUnsafe(RenderedGlyphData rendered, GlyphRect rect, Color32* dstBase, int atlasW)
        {
            int w = rendered.width;
            int h = rendered.height;
            int rowBytes = w * 4;

            fixed (byte* srcPtr = rendered.rgbaPixels)
            {
                if (rendered.isBGRA)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int srcY = h - 1 - y;
                        uint* src = (uint*)(srcPtr + srcY * rowBytes);
                        uint* dst = (uint*)(dstBase + (rect.y + y) * atlasW + rect.x);

                        for (int x = 0; x < w; x++)
                        {
                            uint pixel = src[x];
                            uint rb = ((pixel & 0x00FF0000) >> 16) | ((pixel & 0x000000FF) << 16);
                            dst[x] = (pixel & 0xFF00FF00) | rb;
                        }
                    }
                }
                else
                {
                    for (int y = 0; y < h; y++)
                    {
                        int srcY = h - 1 - y;
                        byte* src = srcPtr + srcY * rowBytes;
                        byte* dst = (byte*)(dstBase + (rect.y + y) * atlasW + rect.x);
                        Buffer.MemoryCopy(src, dst, rowBytes, rowBytes);
                    }
                }
            }
        }

        private int TryAddGlyphsBatchFreeTypeSequential(List<uint> glyphIndices)
        {
            if (fontLoaded)
                EnsureFreeTypeFontLoaded();

            if (atlasTextures.Count == 0) CreateNewAtlasTexture();

            int totalAdded = 0;
            int atlasW = atlasSize;

            unsafe
            {
            var rawAtlasData = atlasTextures[^1].GetRawTextureData<Color32>();
            var atlasPtr = (Color32*)rawAtlasData.GetUnsafePtr();

            for (int i = 0; i < glyphIndices.Count; i++)
            {
                var glyphIndex = glyphIndices[i];

                if (glyphIndex == 0 || (glyphLookupDictionary != null && glyphLookupDictionary.ContainsKey(glyphIndex)))
                    continue;

                if (!TryRenderGlyph(glyphIndex, out var rendered))
                    continue;

                if (!TryPackGlyphShelf(rendered.width, rendered.height, out var rect))
                {
                    atlasTextures[^1].Apply(false, false);
                    CreateNewAtlasTexture();
                    rawAtlasData = atlasTextures[^1].GetRawTextureData<Color32>();
                    atlasPtr = (Color32*)rawAtlasData.GetUnsafePtr();
                    TryPackGlyphShelf(rendered.width, rendered.height, out rect);
                }

                CopyPixelsToAtlasUnsafe(rendered, rect, atlasPtr, atlasW);
                RegisterGlyph(glyphIndex, rendered, rect);
                totalAdded++;
            }
            }

            atlasTextures[^1].Apply(false, false);

            return totalAdded;
        }
    #endif

        private void RegisterGlyph(uint glyphIndex, RenderedGlyphData rendered, GlyphRect rect, int atlasIdx = -1)
        {
            var glyph = CreateGlyph(glyphIndex, rendered, rect, atlasIdx);

            glyphTable.Add(glyph);
            glyphLookupDictionary ??= new Dictionary<uint, Glyph>();
            glyphLookupDictionary[glyphIndex] = glyph;
            glyphIndexList ??= new List<uint>();
            glyphIndexList.Add(glyphIndex);
        }

    #if !UNITY_WEBGL || UNITY_EDITOR
        private int FinalizePackedGlyphs(int startAtlasIndex)
        {
            int totalAdded = 0;
            for (int i = 0; i < packedCount; i++)
            {
                ref var packed = ref packedGlyphs[i];
                if (packed.data.rgbaPixels != null)
                    UniTextArrayPool<byte>.Return(packed.data.rgbaPixels);
                RegisterGlyph(packed.glyphIndex, packed.data, packed.rect, packed.atlasIndex);
                totalAdded++;
            }

            for (int a = startAtlasIndex; a < atlasTextures.Count; a++)
                atlasTextures[a].Apply(false, false);

            return totalAdded;
        }
    #endif

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

        private Glyph CreateGlyph(uint glyphIndex, RenderedGlyphData rendered, GlyphRect rect, int atlasIdx = -1)
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
                pixelsToDesign = (float)upem / emojiPixelSize;
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

    #if UNITY_WEBGL && !UNITY_EDITOR
            float advanceDesign = rendered.advanceX * ((float)upem / emojiPixelSize);
    #else
            float advanceDesign = hbAdvance > 0 ? hbAdvance : bitmapWidthDesign;
    #endif

            float bearingXDesign = rendered.bearingX * pixelsToDesign;

            if (glyphDiagCount < 1)
            {
                glyphDiagCount++;
                Cat.MeowFormat("[EmojiFont] DIAG glyph={0}: bmp={1}x{2} bX={3} bY={4} adv={5} | emojiPx={6} upem={7} ascent={8:F1} descent={9:F1} hbAdv={10} p2d={11:F2} | wD={12:F1} hD={13:F1} bxD={14:F1} byD={15:F1} advD={16:F1} | sbix={17} cbdt={18} colr={19}",
                    glyphIndex,
                    rendered.width, rendered.height, rendered.bearingX, rendered.bearingY, rendered.advanceX,
                    emojiPixelSize, upem, faceInfo.ascentLine, faceInfo.descentLine, hbAdvance, pixelsToDesign,
                    bitmapWidthDesign, bitmapHeightDesign, bearingXDesign, bearingYDesign, advanceDesign,
                    isSbix, isBitmapEmoji, useCOLRv1);
            }

            var metrics = new GlyphMetrics(
                bitmapWidthDesign,
                bitmapHeightDesign,
                bearingXDesign,
                bearingYDesign,
                advanceDesign);

            int finalAtlasIdx = atlasIdx >= 0 ? atlasIdx : atlasTextures.Count - 1;
            return new Glyph(glyphIndex, metrics, rect, finalAtlasIdx);
        }

        #endregion

        #region Atlas Management

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

        #endregion

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
