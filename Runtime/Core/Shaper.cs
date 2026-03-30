using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Text shaper that converts codepoints to positioned glyphs via HarfBuzz.
    /// Performs full OpenType shaping (GSUB/GPOS) including kerning, ligatures and mark positioning.
    /// </summary>
    public sealed class Shaper
    {
        private static Shaper instance;

        private static readonly FastIntDictionary<FontCacheEntry> fontCache = new();
        private static readonly FastIntDictionary<int> instanceIdToFontHash = new();

        private static readonly FastIntDictionary<byte> featureSupportCache = new();
        private static readonly FastIntDictionary<FastIntDictionary<byte>> supsCodepointCache = new();
        private static readonly FastIntDictionary<FastIntDictionary<byte>> subsCodepointCache = new();
        private static readonly object fontCacheLock = new();

        private const int SmcpBit = 0;
        private const int SupsBit = 2;
        private const int SubsBit = 4;

        [ThreadStatic] private static IntPtr reusableBuffer;

    #if UNITY_EDITOR
        static Shaper()
        {
            Reseter.UnmanagedCleaning += DisposeAll;
            Reseter.ManagedCleaning += DisposeAll;
        }
    #endif

        private static void DisposeAll()
        {
            instance = null;

            lock (fontCacheLock)
            {
                foreach (var kvp in fontCache)
                    kvp.Value?.Dispose();
                fontCache.Clear();
                instanceIdToFontHash.Clear();
                featureSupportCache.Clear();
                supsCodepointCache.Clear();
                subsCodepointCache.Clear();
            }

            if (reusableBuffer != IntPtr.Zero)
            {
                HB.DestroyBuffer(reusableBuffer);
                reusableBuffer = IntPtr.Zero;
            }
        }

        /// <summary>Gets the singleton shaper instance.</summary>
        public static Shaper Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => instance ??= new Shaper();
        }

        #region FontCacheEntry

        internal sealed class FontCacheEntry : IDisposable
        {
            public readonly IntPtr hbFont;
            public readonly int upem;
            private bool isDisposed;

            public bool IsValid => !isDisposed && hbFont != IntPtr.Zero;

            private readonly FastIntDictionary<uint> glyphCache = new();
            private readonly FastIntDictionary<int> advanceCache = new();
            private readonly object cacheLock = new();

            private readonly GCHandle pinnedData;
            private readonly IntPtr hbBlob;
            private readonly IntPtr hbFace;

            public FontCacheEntry(byte[] fontData)
            {
                pinnedData = GCHandle.Alloc(fontData, GCHandleType.Pinned);
                var ptr = pinnedData.AddrOfPinnedObject();

                hbFont = HB.CreateFont(IntPtr.Zero, ptr, fontData.Length, out hbBlob, out hbFace, out upem);
                if (hbFont == IntPtr.Zero)
                {
                    pinnedData.Free();
                    throw new Exception("[HarfBuzz] Failed to create font");
                }
            }

            public void Dispose()
            {
                if (isDisposed) return;
                isDisposed = true;
                HB.DestroyFont(hbFont, hbBlob, hbFace);
                if (pinnedData.IsAllocated)
                    pinnedData.Free();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryGetGlyph(uint codepoint, out uint glyphIndex)
            {
                var key = (int)codepoint;
                if (glyphCache.TryGetValue(key, out glyphIndex))
                    return glyphIndex != 0;

                HB.TryGetGlyph(hbFont, codepoint, out glyphIndex);
                lock (cacheLock) { glyphCache[key] = glyphIndex; }
                return glyphIndex != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetGlyphAdvance(uint glyphIndex)
            {
                if (!IsValid)
                    return 0;

                var key = (int)glyphIndex;
                if (advanceCache.TryGetValue(key, out var cached))
                    return cached;

                var advance = HB.GetGlyphAdvance(hbFont, glyphIndex);
                lock (cacheLock) { advanceCache[key] = advance; }
                return advance;
            }

            private HB.hb_ot_var_axis_info_t[] cachedAxisInfos;
            private bool axisInfosQueried;

            public int GetAxisCount()
            {
                if (!IsValid) return 0;
                return (int)HB.GetAxisCount(hbFace);
            }

            public HB.hb_ot_var_axis_info_t[] GetAxisInfos()
            {
                if (axisInfosQueried)
                    return cachedAxisInfos;
                axisInfosQueried = true;

                if (!IsValid) return null;
                int count = GetAxisCount();
                if (count == 0) return null;

                var buffer = new HB.hb_ot_var_axis_info_t[count];
                int actual = HB.GetAxisInfos(hbFace, buffer);
                if (actual == 0) return null;

                if (actual < count)
                    Array.Resize(ref buffer, actual);
                cachedAxisInfos = buffer;
                return cachedAxisInfos;
            }
        }

        #endregion

        #region Cache Management

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FontCacheEntry GetOrCreateCacheByInstanceId(UniTextFont font)
        {
            if (font == null || !font.HasFontData)
                return null;

            var instanceId = font.GetCachedInstanceId();

            lock (fontCacheLock)
            {
                if (instanceIdToFontHash.TryGetValue(instanceId, out var fontHash))
                {
                    if (fontCache.TryGetValue(fontHash, out var cached))
                        return cached;
                }

                fontHash = font.FontDataHash;
                if (fontHash == 0)
                    return null;

                var fontData = font.FontData;
                if (fontData == null || fontData.Length == 0)
                    return null;

                if (!fontCache.TryGetValue(fontHash, out var entry))
                {
                    entry = new FontCacheEntry(fontData);
                    fontCache[fontHash] = entry;
                }

                instanceIdToFontHash[instanceId] = fontHash;
                return entry;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryGetCacheByHash(int fontHash, out FontCacheEntry entry)
        {
            lock (fontCacheLock)
            {
                return fontCache.TryGetValue(fontHash, out entry);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FontCacheEntry CreateCacheByHash(int fontHash, byte[] fontData)
        {
            if (fontHash == 0 || fontData == null || fontData.Length == 0)
                return null;

            lock (fontCacheLock)
            {
                if (fontCache.TryGetValue(fontHash, out var entry))
                    return entry;

                entry = new FontCacheEntry(fontData);
                fontCache[fontHash] = entry;
                return entry;
            }
        }

        #endregion

        #region Static API

        /// <summary>Returns the variable font axis infos for a font, or null if not variable.</summary>
        internal static HB.hb_ot_var_axis_info_t[] GetVariableAxisInfos(UniTextFont font)
        {
            var cache = GetOrCreateCacheByInstanceId(font);
            return cache?.GetAxisInfos();
        }

        /// <summary>Gets the glyph index for a codepoint in the specified font.</summary>
        /// <param name="font">The font asset.</param>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <returns>Glyph index, or 0 if not found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetGlyphIndex(UniTextFont font, uint codepoint)
        {
            var cache = GetOrCreateCacheByInstanceId(font);
            if (cache == null)
                return 0;

            return cache.TryGetGlyph(codepoint, out var glyphIndex) ? glyphIndex : 0u;
        }

        /// <summary>Gets glyph index and advance width for a codepoint.</summary>
        /// <param name="font">The font asset.</param>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <param name="fontSize">Font size for advance calculation.</param>
        /// <param name="glyphIndex">Output glyph index.</param>
        /// <param name="advance">Output horizontal advance in font units.</param>
        /// <returns>True if the glyph was found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetGlyphInfo(UniTextFont font, uint codepoint, float fontSize,
            out uint glyphIndex, out float advance)
        {
            glyphIndex = 0;
            advance = 0;

            var cache = GetOrCreateCacheByInstanceId(font);
            if (cache == null)
                return false;

            if (!cache.TryGetGlyph(codepoint, out glyphIndex))
                return false;

            var advanceUnits = cache.GetGlyphAdvance(glyphIndex);
            advance = advanceUnits * fontSize * font.FontScale / cache.upem;
            return true;
        }

        /// <summary>Gets the units per em value for the font.</summary>
        /// <param name="font">The font asset.</param>
        /// <returns>Units per em, typically 1000 or 2048.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetUnitsPerEm(UniTextFont font)
        {
            var cache = GetOrCreateCacheByInstanceId(font);
            return cache?.upem ?? 1000;
        }

        /// <summary>Gets upem directly from font data without caching.</summary>
        public static int GetUpemFromFontData(byte[] fontData)
        {
            if (fontData == null || fontData.Length == 0)
            {
                Debug.LogWarning("[GetUpemFromFontData] fontData is null or empty");
                return 0;
            }

            try
            {
                var entry = new FontCacheEntry(fontData);
                var upem = entry.upem;
                entry.Dispose();
                return upem;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GetUpemFromFontData] Exception: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Clears the shaping cache for a specific font asset.</summary>
        /// <param name="fontAssetInstanceId">Instance ID of the font asset to clear.</param>
        public static void ClearCache(int fontAssetInstanceId)
        {
            lock (fontCacheLock)
            {
                if (instanceIdToFontHash.TryGetValue(fontAssetInstanceId, out var fontHash))
                {
                    instanceIdToFontHash.Remove(fontAssetInstanceId);

                    var stillUsed = false;
                    foreach (var kvp in instanceIdToFontHash)
                    {
                        if (kvp.Value == fontHash)
                        {
                            stillUsed = true;
                            break;
                        }
                    }

                    if (!stillUsed && fontCache.TryGetValue(fontHash, out var cache))
                    {
                        cache.Dispose();
                        fontCache.Remove(fontHash);
                        featureSupportCache.Remove(fontHash);
                        supsCodepointCache.Remove(fontHash);
                        subsCodepointCache.Remove(fontHash);
                    }
                }
            }
        }

        /// <summary>Clears all shaping caches for all fonts.</summary>
        public static void ClearAllCaches()
        {
            lock (fontCacheLock)
            {
                foreach (var kvp in fontCache)
                    kvp.Value?.Dispose();
                fontCache.Clear();
                instanceIdToFontHash.Clear();
                featureSupportCache.Clear();
                supsCodepointCache.Clear();
                subsCodepointCache.Clear();
            }
        }

        #endregion

        #region Shaping


        /// <summary>Computes the glyph scale factor for a font.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeScale(UniTextFontProvider fontProvider, int fontId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (fontId == EmojiFont.FontId)
                return 1f;
#endif
            if (!fontCache.TryGetValue(fontId, out var entry))
            {
                lock (fontCacheLock)
                {
                    if (!fontCache.TryGetValue(fontId, out entry))
                    {
                        var fontData = fontProvider.GetFontData(fontId);
                        entry = new FontCacheEntry(fontData);
                        fontCache[fontId] = entry;
                    }
                }
            }

            var fontAsset = fontProvider.GetFontAsset(fontId);
            return fontProvider.FontSize * (fontAsset?.FontScale ?? 1f) / entry.upem;
        }

        /// <summary>
        /// Shapes text directly into the target buffer. No intermediate copy.
        /// Returns the number of glyphs written.
        /// </summary>
        internal unsafe int ShapeInto(
            ref PooledBuffer<ShapedGlyph> output,
            ReadOnlySpan<int> context,
            int itemOffset,
            int itemLength,
            UniTextFontProvider fontProvider,
            int fontId,
            UnicodeScript script,
            TextDirection direction,
            float scale,
            out float totalAdvanceOut,
            HB.hb_variation_t[] variations = null,
            HB.hb_feature_t[] features = null,
            int featureCount = -1)
        {
            totalAdvanceOut = 0;

            if (itemLength == 0)
                return 0;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (fontId == EmojiFont.FontId)
            {
                var result = WebGLEmojiShaper.Shape(context.Slice(itemOffset, itemLength), fontProvider.FontSize, 2048);
                var glyphs = result.Glyphs;
                var emojiStart = output.count;
                output.EnsureCapacity(emojiStart + glyphs.Length);
                glyphs.CopyTo(output.data.AsSpan(emojiStart));
                output.count = emojiStart + glyphs.Length;
                totalAdvanceOut = result.TotalAdvance;
                return glyphs.Length;
            }
#endif

            if (!fontCache.TryGetValue(fontId, out var fontEntry))
            {
                lock (fontCacheLock)
                {
                    if (!fontCache.TryGetValue(fontId, out fontEntry))
                    {
                        var fontData = fontProvider.GetFontData(fontId);
                        fontEntry = new FontCacheEntry(fontData);
                        fontCache[fontId] = fontEntry;
                    }
                }
            }

            if (variations != null && variations.Length > 0)
            {
                fixed (HB.hb_variation_t* ptr = variations)
                {
                    HB.SetVariations(fontEntry.hbFont, ptr, variations.Length);
                }
            }

            IntPtr buffer = EnsureBuffer();
            if (buffer == IntPtr.Zero)
                return 0;

            var fCount = featureCount >= 0 ? featureCount : (features?.Length ?? 0);

            int glyphCount = HB.ShapeRun(
                fontEntry.hbFont, buffer,
                context, itemOffset, itemLength,
                direction == TextDirection.RightToLeft ? HB.DIRECTION_RTL : HB.DIRECTION_LTR,
                MapScript(script),
                HB.BUFFER_FLAG_REMOVE_DEFAULT_IGNORABLES,
                features, fCount,
                out var nativeInfos, out var nativePositions);

            if (glyphCount == 0)
                return 0;

            var writeStart = output.count;
            var required = writeStart + glyphCount;
            if (output.Capacity < required)
                output.EnsureCapacity(required);

            var data = output.data;
            float totalAdvance = 0;

            for (int i = 0; i < glyphCount; i++)
            {
                float advanceX = nativePositions[i].x_advance * scale;
                data[writeStart + i] = new ShapedGlyph
                {
                    glyphId = (int)nativeInfos[i].codepoint,
                    cluster = (int)nativeInfos[i].cluster,
                    advanceX = advanceX,
                    advanceY = nativePositions[i].y_advance * scale,
                    offsetX = nativePositions[i].x_offset * scale,
                    offsetY = nativePositions[i].y_offset * scale
                };
                totalAdvance += advanceX;
            }

            output.count = required;
            totalAdvanceOut = totalAdvance;
            return glyphCount;
        }

        /// <summary>Ensures a reusable buffer exists. Does NOT clear — ShapeRun handles clearing internally.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IntPtr EnsureBuffer()
        {
            if (reusableBuffer != IntPtr.Zero)
                return reusableBuffer;

            reusableBuffer = HB.CreateBuffer();
            return reusableBuffer;
        }

        internal static readonly uint SmcpTag = HB.MakeTag('s', 'm', 'c', 'p');
        internal static readonly uint SupsTag = HB.MakeTag('s', 'u', 'p', 's');
        internal static readonly uint SubsTag = HB.MakeTag('s', 'u', 'b', 's');

        internal static readonly HB.hb_feature_t[] SmcpFeatures =
        {
            new HB.hb_feature_t { tag = SmcpTag, value = 1, start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END }
        };

        internal static readonly HB.hb_feature_t[] SupsFeatures =
        {
            new HB.hb_feature_t { tag = SupsTag, value = 1, start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END }
        };

        internal static readonly HB.hb_feature_t[] SubsFeatures =
        {
            new HB.hb_feature_t { tag = SubsTag, value = 1, start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END }
        };

        internal bool HasSmcpFeature(UniTextFont font) => HasFeature(font, SmcpBit, SmcpFeatures, 'a');
        internal bool HasSupsFeature(UniTextFont font) => HasFeature(font, SupsBit, SupsFeatures, '2');
        internal bool HasSubsFeature(UniTextFont font) => HasFeature(font, SubsBit, SubsFeatures, '2');

        /// <summary>
        /// Checks if a font has an OpenType 'sups' alternate for a specific codepoint.
        /// Results are cached per (fontId, codepoint). Call HasSupsFeature first as a fast-path.
        /// </summary>
        internal bool HasSupsForCodepoint(UniTextFont font, int codepoint)
            => HasFeatureForCodepoint(font, codepoint, SupsFeatures, supsCodepointCache);

        /// <summary>
        /// Checks if a font has an OpenType 'subs' alternate for a specific codepoint.
        /// Results are cached per (fontId, codepoint). Call HasSubsFeature first as a fast-path.
        /// </summary>
        internal bool HasSubsForCodepoint(UniTextFont font, int codepoint)
            => HasFeatureForCodepoint(font, codepoint, SubsFeatures, subsCodepointCache);

        /// <summary>
        /// Checks if a font supports an OpenType feature by test-shaping a character with and without it.
        /// Result is cached per fontId using bit flags.
        /// </summary>
        private bool HasFeature(UniTextFont font, int bitOffset, HB.hb_feature_t[] testFeatures, int testChar)
        {
            if (font == null) return false;
            var fontId = UniTextFontProvider.GetFontId(font);
            if (fontId == 0) return false;

            int checkedBit = 1 << bitOffset;
            int supportedBit = 1 << (bitOffset + 1);

            FontCacheEntry fontEntry;
            lock (fontCacheLock)
            {
                if (featureSupportCache.TryGetValue(fontId, out var flags) && (flags & checkedBit) != 0)
                    return (flags & supportedBit) != 0;

                if (!fontCache.TryGetValue(fontId, out fontEntry))
                {
                    var fontData = font.FontData;
                    if (fontData == null) return false;
                    fontEntry = new FontCacheEntry(fontData);
                    fontCache[fontId] = fontEntry;
                }
            }

            bool supported = TestFeatureSubstitution(fontEntry.hbFont, testChar, testFeatures);
            lock (fontCacheLock)
            {
                featureSupportCache.TryGetValue(fontId, out var current);
                current |= (byte)checkedBit;
                if (supported)
                    current |= (byte)supportedBit;
                featureSupportCache[fontId] = current;
            }
            return supported;
        }

        private bool HasFeatureForCodepoint(
            UniTextFont font, int codepoint,
            HB.hb_feature_t[] testFeatures,
            FastIntDictionary<FastIntDictionary<byte>> cache)
        {
            if (font == null) return false;
            var fontId = UniTextFontProvider.GetFontId(font);
            if (fontId == 0) return false;

            FontCacheEntry fontEntry;
            lock (fontCacheLock)
            {
                if (cache.TryGetValue(fontId, out var cpCache) && cpCache.TryGetValue(codepoint, out var cached))
                    return cached == 1;

                if (!fontCache.TryGetValue(fontId, out fontEntry))
                {
                    var fontData = font.FontData;
                    if (fontData == null) return false;
                    fontEntry = new FontCacheEntry(fontData);
                    fontCache[fontId] = fontEntry;
                }
            }

            bool supported = TestFeatureSubstitution(fontEntry.hbFont, codepoint, testFeatures);
            lock (fontCacheLock)
            {
                if (!cache.TryGetValue(fontId, out var cpCache))
                {
                    cpCache = new FastIntDictionary<byte>();
                    cache[fontId] = cpCache;
                }
                cpCache[codepoint] = supported ? (byte)1 : (byte)2;
            }
            return supported;
        }

        /// <summary>
        /// Test-shapes a single codepoint with and without a feature.
        /// Returns true if the feature caused a glyph substitution.
        /// </summary>
        private static bool TestFeatureSubstitution(IntPtr hbFont, int codepoint, HB.hb_feature_t[] testFeatures)
        {
            Span<int> testCp = stackalloc int[] { codepoint };

            var buf1 = HB.CreateBuffer();
            HB.SetDirection(buf1, HB.DIRECTION_LTR);
            HB.SetScript(buf1, HB.Script.Latin);
            HB.AddCodepoints(buf1, testCp, 0, 1);
            HB.Shape(hbFont, buf1);
            var infos1 = HB.GetGlyphInfos(buf1);
            uint glyph1 = infos1.Length > 0 ? infos1[0].glyphId : 0;
            HB.DestroyBuffer(buf1);

            var buf2 = HB.CreateBuffer();
            HB.SetDirection(buf2, HB.DIRECTION_LTR);
            HB.SetScript(buf2, HB.Script.Latin);
            HB.AddCodepoints(buf2, testCp, 0, 1);
            HB.Shape(hbFont, buf2, testFeatures);
            var infos2 = HB.GetGlyphInfos(buf2);
            uint glyph2 = infos2.Length > 0 ? infos2[0].glyphId : 0;
            HB.DestroyBuffer(buf2);

            return glyph1 != glyph2 && glyph2 != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MapScript(UnicodeScript script)
        {
            return script switch
            {
                UnicodeScript.Arabic => HB.Script.Arabic,
                UnicodeScript.Armenian => HB.Script.Armenian,
                UnicodeScript.Bengali => HB.Script.Bengali,
                UnicodeScript.Cyrillic => HB.Script.Cyrillic,
                UnicodeScript.Devanagari => HB.Script.Devanagari,
                UnicodeScript.Georgian => HB.Script.Georgian,
                UnicodeScript.Greek => HB.Script.Greek,
                UnicodeScript.Gujarati => HB.Script.Gujarati,
                UnicodeScript.Gurmukhi => HB.Script.Gurmukhi,
                UnicodeScript.Han => HB.Script.Han,
                UnicodeScript.Hangul => HB.Script.Hangul,
                UnicodeScript.Hebrew => HB.Script.Hebrew,
                UnicodeScript.Hiragana => HB.Script.Hiragana,
                UnicodeScript.Kannada => HB.Script.Kannada,
                UnicodeScript.Katakana => HB.Script.Katakana,
                UnicodeScript.Khmer => HB.Script.Khmer,
                UnicodeScript.Lao => HB.Script.Lao,
                UnicodeScript.Latin => HB.Script.Latin,
                UnicodeScript.Malayalam => HB.Script.Malayalam,
                UnicodeScript.Myanmar => HB.Script.Myanmar,
                UnicodeScript.Oriya => HB.Script.Oriya,
                UnicodeScript.Sinhala => HB.Script.Sinhala,
                UnicodeScript.Tamil => HB.Script.Tamil,
                UnicodeScript.Telugu => HB.Script.Telugu,
                UnicodeScript.Thai => HB.Script.Thai,
                UnicodeScript.Tibetan => HB.Script.Tibetan,
                _ => HB.Script.Common
            };
        }

        #endregion
    }
}
