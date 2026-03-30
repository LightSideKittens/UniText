using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Manages font assets and provides font lookup services for text rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The font provider handles:
    /// <list type="bullet">
    /// <item>Primary font and fallback font resolution</item>
    /// <item>Font scaling based on requested font size</item>
    /// <item>Glyph caching in texture atlases</item>
    /// <item>Font data access (glyph lookup, font data bytes)</item>
    /// <item>Font family lookup for variant/variable font resolution</item>
    /// </list>
    /// </para>
    /// <para>
    /// Uses <see cref="SharedFontCache"/> for fast codepoint-to-font mapping.
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextFont"/>
    /// <seealso cref="UniTextFontStack"/>
    public sealed class UniTextFontProvider
    {
        private readonly FastIntDictionary<UniTextFont> fontAssets = new();

        private UniTextFontStack fontStackAsset;
        private UniTextFont primaryFont;
        private int prinaryFontId;

        private float fontSize = 36f;
        private float fontScale = 1f;

        [ThreadStatic] private static HashSet<int> searchedFontAssets;

        /// <summary>Flattened family array from entire fallback chain.</summary>
        private FontFamily[] resolvedFamilies;

        /// <summary>fontId → familyIndex mapping for O(1) family lookup.</summary>
        private readonly FastIntDictionary<ushort> fontIdToFamilyIndex = new();

        /// <summary>Gets or sets the current font size in points.</summary>
        public float FontSize
        {
            get => fontSize;
            set
            {
                fontSize = value;
                UpdateFontScale();
            }
        }

        /// <summary>Sets the font size in points.</summary>
        /// <param name="size">Font size in points.</param>
        public void SetFontSize(float size)
        {
            FontSize = size;
        }

        /// <summary>Gets the main (primary) font asset.</summary>
        public UniTextFont PrimaryFont => primaryFont;
        /// <summary>Gets the unique identifier for the primary font.</summary>
        public int PrinaryFontId => prinaryFontId;

        /// <summary>Whether any family in the resolved chain has faces (variants or variable fonts).</summary>
        public bool HasFaces { get; private set; }

        /// <summary>
        /// Initializes the font provider with the specified fonts.
        /// </summary>
        /// <param name="fontStack">Font collection containing main and fallback fonts.</param>
        /// <param name="fontSize">Initial font size in points.</param>
        public UniTextFontProvider(UniTextFontStack fontStack, float fontSize = 36f)
        {
            if (fontStack == null || fontStack.PrimaryFont == null)
                throw new ArgumentNullException(nameof(fontStack));

            fontStackAsset = fontStack;
            primaryFont = fontStack.PrimaryFont;
            this.fontSize = fontSize;

            prinaryFontId = GetFontId(primaryFont);
            RegisterFontAsset(prinaryFontId, primaryFont);
            UpdateFontScale();

            resolvedFamilies = fontStack.BuildResolvedFamilies();
            HasFaces = false;

            for (ushort i = 0; i < resolvedFamilies.Length; i++)
            {
                ref var family = ref resolvedFamilies[i];
                if (family.lookup.hasFaces) HasFaces = true;

                if (family.primary != null)
                {
                    var fid = GetFontId(family.primary);
                    RegisterFontAsset(fid, family.primary);
                    fontIdToFamilyIndex[fid] = i;
                    family.primary.GetCachedInstanceId();
                }
            }

            if (EmojiFont.IsAvailable)
                RegisterFontAsset(EmojiFont.FontId, EmojiFont.Instance);

            Cat.MeowFormat("[FontProvider] Created: mainFont={0} (id={1}), families={2}",
                primaryFont.CachedName, prinaryFontId, resolvedFamilies.Length);
        }

        private void UpdateFontScale()
        {
            fontScale = fontSize * primaryFont.FontScale / primaryFont.UnitsPerEm;
        }

        /// <summary>
        /// Gets the unique font identifier for a font asset.
        /// </summary>
        /// <param name="font">The font asset.</param>
        /// <returns>Font ID based on font data hash, or 0 if null.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFontId(UniTextFont font)
        {
            if (font == null) return 0;
            if (font is EmojiFont) return EmojiFont.FontId;
            return font.FontDataHash;
        }
        
        /// <summary>
        /// Registers a font asset with the provider.
        /// </summary>
        /// <param name="fontId">Unique font identifier.</param>
        /// <param name="font">Font asset to register.</param>
        public void RegisterFontAsset(int fontId, UniTextFont font)
        {
            if (font == null || fontId == 0) return;
            fontAssets[fontId] = font;
        }

        /// <summary>
        /// Gets a font asset by its identifier.
        /// </summary>
        /// <param name="fontId">The font identifier.</param>
        /// <returns>The font asset, or primary font if not found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTextFont GetFontAsset(int fontId)
        {
            if (fontId == prinaryFontId)
                return primaryFont;

            if (fontId == EmojiFont.FontId)
                return EmojiFont.Instance;

            if (fontAssets.TryGetValue(fontId, out var asset))
                return asset;

            return primaryFont;
        }

        /// <summary>
        /// Gets line metrics scaled to the specified font size.
        /// </summary>
        /// <param name="size">Target font size in points.</param>
        /// <param name="ascender">Output: distance from baseline to top of tallest glyph.</param>
        /// <param name="descender">Output: distance from baseline to bottom (typically negative).</param>
        /// <param name="lineHeight">Output: total line height.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetLineMetrics(float size, out float ascender, out float descender, out float lineHeight)
        {
            var faceInfo = primaryFont.FaceInfo;
            var scale = size * primaryFont.FontScale / primaryFont.UnitsPerEm;
            ascender = faceInfo.ascentLine * scale;
            descender = faceInfo.descentLine * scale;
            lineHeight = faceInfo.lineHeight * scale;

            if (lineHeight <= 0)
                lineHeight = (ascender - descender) * 1.2f;
        }

        /// <summary>
        /// Gets the cap height (top of capital letters) scaled to the specified font size.
        /// </summary>
        /// <param name="size">Target font size in points.</param>
        /// <returns>Scaled cap height, or 0 if unavailable.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCapHeight(float size)
        {
            var faceInfo = primaryFont.FaceInfo;
            if (faceInfo.capLine <= 0) return 0f;
            return faceInfo.capLine * (size * primaryFont.FontScale / primaryFont.UnitsPerEm);
        }

        /// <summary>
        /// Finds the best font to render a codepoint, using fallback chain if needed.
        /// </summary>
        /// <param name="codepoint">Unicode codepoint to find a font for.</param>
        /// <returns>Font ID of the font that can render this codepoint.</returns>
        public int FindFontForCodepoint(int codepoint)
        {
            if (SharedFontCache.TryGet(codepoint, prinaryFontId, out var cachedFontId))
            {
                if (cachedFontId == prinaryFontId || fontAssets.ContainsKey(cachedFontId))
                    return cachedFontId;
                Cat.MeowWarnFormat("[FontProvider] Cache hit but fontAssets miss: cp=U+{0:X4}, cachedFontId={1}",
                    codepoint, cachedFontId);
            }

            searchedFontAssets ??= new HashSet<int>();
            searchedFontAssets.Clear();

            var unicode = (uint)codepoint;
            var foundFont = fontStackAsset?.FindFontForCodepoint(unicode, searchedFontAssets);

            if (foundFont == null)
                return prinaryFontId;

            var fontId = GetFontId(foundFont);
            if (!fontAssets.ContainsKey(fontId))
            {
                RegisterFontAsset(fontId, foundFont);
                Cat.MeowFormat("[FontProvider] Fallback font registered: {0}", foundFont.CachedName);
            }

            SharedFontCache.Set(codepoint, prinaryFontId, fontId);
            return fontId;
        }

        /// <summary>
        /// Gets the family index for a font ID. Returns 0 (first family) if not found.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetFamilyIndex(int fontId)
        {
            return fontIdToFamilyIndex.TryGetValue(fontId, out var idx) ? idx : (ushort)0;
        }

        /// <summary>
        /// Gets the family lookup table for the given family index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref FontFaceLookup GetFamilyLookup(ushort familyIndex)
        {
            return ref resolvedFamilies[familyIndex].lookup;
        }

        /// <summary>
        /// Gets the raw font data (TTF/OTF bytes) for a font.
        /// </summary>
        /// <param name="fontId">Font identifier.</param>
        /// <returns>Font file data, or null if not available.</returns>
        public byte[] GetFontData(int fontId)
        {
            return GetFontAsset(fontId)?.FontData;
        }
    }
}
