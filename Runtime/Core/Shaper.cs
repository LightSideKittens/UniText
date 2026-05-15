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
        private static readonly object fontCacheLock = new();

        [ThreadStatic] private static ShapedGlyph[] outputBuffer;
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
            }

            outputBuffer = null;

            if (reusableBuffer != IntPtr.Zero)
            {
                HB.DestroyBuffer(reusableBuffer);
                reusableBuffer = IntPtr.Zero;
            }
        }

        private static ShapedGlyph[] OutputBuffer => outputBuffer ??= new ShapedGlyph[256];

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

            private readonly IntPtr unmanagedData;
            private readonly IntPtr hbBlob;
            private readonly IntPtr hbFace;

            public FontCacheEntry(byte[] fontData)
            {
                int dataLength = fontData.Length;
                unmanagedData = Marshal.AllocHGlobal(dataLength);
                Marshal.Copy(fontData, 0, unmanagedData, dataLength);

                hbFont = HB.CreateFont(IntPtr.Zero, unmanagedData, dataLength, out hbBlob, out hbFace, out upem);
                if (hbFont == IntPtr.Zero)
                    throw new Exception("[HarfBuzz] Failed to create font");
            }

            public void Dispose()
            {
                if (isDisposed) return;
                isDisposed = true;
                HB.DestroyFont(hbFont, hbBlob, hbFace);
                if (unmanagedData != IntPtr.Zero)
                    Marshal.FreeHGlobal(unmanagedData);
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
            }
        }

        #endregion

        #region Shaping

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IntPtr AcquireBuffer()
        {
            if (reusableBuffer != IntPtr.Zero)
            {
                HB.ClearBuffer(reusableBuffer);
                return reusableBuffer;
            }

            reusableBuffer = HB.CreateBuffer();
            return reusableBuffer;
        }

        public ShapingResult Shape(
            ReadOnlySpan<int> context,
            int itemOffset,
            int itemLength,
            UniTextFontProvider fontProvider,
            int fontId,
            UnicodeScript script,
            TextDirection direction)
        {
            if (itemLength == 0)
                return new ShapingResult(ReadOnlySpan<ShapedGlyph>.Empty, 0);

#if UNITY_WEBGL && !UNITY_EDITOR
            if (fontId == EmojiFont.FontId)
                return WebGLEmojiShaper.Shape(context.Slice(itemOffset, itemLength), fontProvider.FontSize, 2048);
#endif

            FontCacheEntry fontEntry;
            lock (fontCacheLock)
            {
                if (!fontCache.TryGetValue(fontId, out fontEntry))
                {
                    var fontData = fontProvider.GetFontData(fontId);
                    fontEntry = new FontCacheEntry(fontData);
                    fontCache[fontId] = fontEntry;
                }
            }

            IntPtr buffer = AcquireBuffer();
            if (buffer == IntPtr.Zero)
                return new ShapingResult(ReadOnlySpan<ShapedGlyph>.Empty, 0);

            HB.SetDirection(buffer, direction == TextDirection.RightToLeft
                ? HB.DIRECTION_RTL
                : HB.DIRECTION_LTR);
            HB.SetScript(buffer, MapScript(script));
            HB.SetFlags(buffer, HB.BUFFER_FLAG_REMOVE_DEFAULT_IGNORABLES);
            HB.AddCodepoints(buffer, context, itemOffset, itemLength);

            HB.Shape(fontEntry.hbFont, buffer);

            var glyphInfos = HB.GetGlyphInfos(buffer);
            int glyphCount = glyphInfos.Length;

            if (glyphCount == 0)
                return new ShapingResult(ReadOnlySpan<ShapedGlyph>.Empty, 0);

            var outBuf = OutputBuffer;
            if (outBuf.Length < glyphCount)
            {
                outBuf = new ShapedGlyph[Math.Max(glyphCount, outBuf.Length * 2)];
                outputBuffer = outBuf;
            }

            float totalAdvance = 0;
            float fontSize = fontProvider?.FontSize ?? 36f;
            var fontAsset = fontProvider?.GetFontAsset(fontId);
            float fontScaleMul = fontAsset?.FontScale ?? 1f;
            float scale = fontSize * fontScaleMul / fontEntry.upem;

            for (int i = 0; i < glyphCount; i++)
            {
                ref readonly var info = ref glyphInfos[i];

                float advanceX = info.xAdvance * scale;
                outBuf[i] = new ShapedGlyph
                {
                    glyphId = (int)info.glyphId,
                    cluster = (int)info.cluster,
                    advanceX = advanceX,
                    advanceY = info.yAdvance * scale,
                    offsetX = info.xOffset * scale,
                    offsetY = info.yOffset * scale
                };
                totalAdvance += advanceX;
            }

            return new ShapingResult(outBuf.AsSpan(0, glyphCount), totalAdvance);
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
