using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    
    /// <summary>
    /// Pre-computed face matching table for a font family.
    /// Built once from primary + faces on initialization. All runtime matching is O(log N) on ints.
    /// </summary>
    internal struct FontFaceLookup
    {
        internal int[] uprightWeights;
        internal UniTextFont[] uprightFonts;
        internal int[] italicWeights;
        internal UniTextFont[] italicFonts;

        internal UniTextFont variableUpright;
        internal UniTextFont variableItalic;

        internal bool hasFaces;

        /// <summary>
        /// Builds lookup from primary font + faces array.
        /// Primary is included: if variable → variableUpright/Italic, if static → weight arrays.
        /// </summary>
        internal static FontFaceLookup Build(UniTextFont primary, UniTextFont[] faces)
        {
            var lookup = new FontFaceLookup();
            var upright = new List<(int weight, UniTextFont font)>();
            var italic = new List<(int weight, UniTextFont font)>();

            if (primary != null)
                ClassifyFont(primary, ref lookup, upright, italic);

            if (faces != null)
            {
                for (int i = 0; i < faces.Length; i++)
                    if (faces[i] != null && faces[i] != primary)
                        ClassifyFont(faces[i], ref lookup, upright, italic);
            }

            BuildSortedArrays(upright, out lookup.uprightWeights, out lookup.uprightFonts);
            BuildSortedArrays(italic, out lookup.italicWeights, out lookup.italicFonts);

            lookup.hasFaces = lookup.uprightWeights != null || lookup.italicWeights != null
                || lookup.variableUpright != null || lookup.variableItalic != null;

            return lookup;
        }

        /// <summary>
        /// Finds the best static face matching target weight and italic.
        /// Implements CSS Fonts Level 4 §5.2 directional preference.
        /// </summary>
        internal UniTextFont FindFace(int targetWeight, bool italic)
        {
            var weights = italic ? italicWeights : uprightWeights;
            var fonts = italic ? italicFonts : uprightFonts;

            if (weights == null || weights.Length == 0) return null;

            int idx = Array.BinarySearch(weights, targetWeight);
            if (idx >= 0) return fonts[idx];

            int ins = ~idx;

            if (targetWeight < 400)
            {
                if (ins > 0) return fonts[ins - 1];
                return fonts[ins];
            }

            if (targetWeight > 500)
            {
                if (ins < weights.Length) return fonts[ins];
                return fonts[ins - 1];
            }

            if (targetWeight == 400)
            {
                int idx500 = Array.BinarySearch(weights, 500);
                if (idx500 >= 0) return fonts[idx500];
                if (ins > 0) return fonts[ins - 1];
                return fonts[ins];
            }

            if (targetWeight == 500)
            {
                int idx400 = Array.BinarySearch(weights, 400);
                if (idx400 >= 0) return fonts[idx400];
                if (ins < weights.Length) return fonts[ins];
                return fonts[ins - 1];
            }

            if (ins == 0) return fonts[0];
            if (ins >= weights.Length) return fonts[weights.Length - 1];

            int distL = targetWeight - weights[ins - 1];
            int distH = weights[ins] - targetWeight;
            return distH <= distL ? fonts[ins] : fonts[ins - 1];
        }

        private static void ClassifyFont(UniTextFont f, ref FontFaceLookup lookup,
            List<(int, UniTextFont)> upright, List<(int, UniTextFont)> italic)
        {
            if (f.IsVariable)
            {
                if (f.FaceInfo.isItalic)
                    lookup.variableItalic ??= f;
                else
                    lookup.variableUpright ??= f;
                return;
            }

            var entry = (f.FaceInfo.weightClass, f);
            if (f.FaceInfo.isItalic)
                italic.Add(entry);
            else
                upright.Add(entry);
        }

        private static void BuildSortedArrays(List<(int weight, UniTextFont font)> list,
            out int[] weights, out UniTextFont[] fonts)
        {
            if (list.Count == 0) { weights = null; fonts = null; return; }

            list.Sort((a, b) => a.weight.CompareTo(b.weight));
            weights = new int[list.Count];
            fonts = new UniTextFont[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                weights[i] = list[i].weight;
                fonts[i] = list[i].font;
            }
        }
    }

    /// <summary>
    /// A font family: a primary font plus optional variant faces (Bold, Italic, Variable, etc.).
    /// </summary>
    [Serializable]
    public struct FontFamily
    {
        /// <summary>Primary font. Provides strut metrics (if first family) and codepoint lookup.</summary>
        public UniTextFont primary;

        /// <summary>Additional faces: Bold, Italic, BoldItalic, Variable, etc.</summary>
        public UniTextFont[] faces;

        /// <summary>Pre-built lookup table for face matching. Not serialized.</summary>
        [NonSerialized] internal FontFaceLookup lookup;
    }

    /// <summary>
    /// ScriptableObject container for font families with fallback chain support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordered list of font families. The first family's primary font is the primary font
    /// and provides strut metrics (line height, ascent, descent).
    /// </para>
    /// <para>
    /// Each family contains a primary font and optional faces (Bold, Italic, Variable, etc.).
    /// Face matching uses pre-computed lookup tables with CSS Fonts Level 4 §5.2 weight matching.
    /// </para>
    /// <para>
    /// Create via Assets menu: Create → UniText → Fonts
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextFont"/>
    /// <seealso cref="UniTextFontProvider"/>
    public class UniTextFontStack : ScriptableObject
    {
        /// <summary>Ordered list of font families. First family's primary = MainFont (strut metrics).</summary>
        public FontFamily[] families;

        /// <summary>Optional fallback stack. Searched after this stack's own families.</summary>
        public UniTextFontStack fallbackStack;

        /// <summary>Gets the main (primary) font, or null if empty.</summary>
        public UniTextFont PrimaryFont => families is { Length: > 0 } ? families[0].primary : null;

        /// <summary>Whether any family in this stack has faces (variants or variable fonts).</summary>
        public bool HasFaces { get; private set; }

        private UniTextFont[] resolvedFonts;

        /// <summary>
        /// Finds a font that can render the specified Unicode codepoint.
        /// </summary>
        /// <param name="unicode">Unicode codepoint to find a font for.</param>
        /// <param name="searched">Set of already-searched font IDs (to prevent loops).</param>
        /// <returns>Font that can render the codepoint, or null if none found.</returns>
        public UniTextFont FindFontForCodepoint(uint unicode, HashSet<int> searched = null)
        {
            resolvedFonts ??= BuildResolvedFonts();

            if (resolvedFonts.Length == 0)
                return null;
            
            searched ??= new HashSet<int>();

            if (UnicodeData.Provider.IsEmojiPresentation((int)unicode) && EmojiFont.IsAvailable)
            {
                var emojiFont = EmojiFont.Instance;
                if (searched.Add(EmojiFont.FontId))
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    return emojiFont;
#else
                    var glyphIndex = Shaper.GetGlyphIndex(emojiFont, unicode);
                    if (glyphIndex != 0) return emojiFont;
#endif
                }
            }

            for (var i = 0; i < resolvedFonts.Length; i++)
            {
                var font = resolvedFonts[i];

                if (!searched.Add(font.GetCachedInstanceId()))
                    continue;

                var glyphIndex = Shaper.GetGlyphIndex(font, unicode);
                if (glyphIndex != 0) return font;
            }

            return null;
        }

        private UniTextFont[] BuildResolvedFonts()
        {
            TryInit();
            var list = new List<UniTextFont>();
            var visitedStacks = new HashSet<UniTextFontStack>();
            CollectPrimaries(this, list, visitedStacks);
            return list.ToArray();
        }

        private static void CollectPrimaries(UniTextFontStack stack, List<UniTextFont> list,
            HashSet<UniTextFontStack> visited)
        {
            while (true)
            {
                if (stack == null || !visited.Add(stack)) return;

                if (stack.families != null)
                {
                    for (int i = 0; i < stack.families.Length; i++)
                        if (stack.families[i].primary != null)
                            list.Add(stack.families[i].primary);
                }

                stack = stack.fallbackStack;
            }
        }

        /// <summary>
        /// Builds a flattened array of all families from this stack and all fallback stacks.
        /// Ensures TryInit is called on each stack so lookups are built.
        /// </summary>
        [NonSerialized] private FontFamily[] cachedResolvedFamilies;

        internal FontFamily[] BuildResolvedFamilies()
        {
            if (cachedResolvedFamilies != null)
                return cachedResolvedFamilies;

            var list = new List<FontFamily>();
            var visited = new HashSet<UniTextFontStack>();
            CollectFamilies(this, list, visited);
            cachedResolvedFamilies = list.ToArray();
            return cachedResolvedFamilies;
        }

        private static void CollectFamilies(UniTextFontStack stack, List<FontFamily> list,
            HashSet<UniTextFontStack> visited)
        {
            while (true)
            {
                if (stack == null || !visited.Add(stack)) return;
                stack.TryInit();
                if (stack.families != null)
                {
                    for (int i = 0; i < stack.families.Length; i++)
                        list.Add(stack.families[i]);
                }
                stack = stack.fallbackStack;
            }
        }

        internal event Action Changed;
        [NonSerialized] private bool isInitialized;

        private void TryInit()
        {
            if (isInitialized) return;
            isInitialized = true;
            HasFaces = false;

            if (families != null)
            {
                for (var i = 0; i < families.Length; i++)
                {
                    families[i].lookup = FontFaceLookup.Build(families[i].primary, families[i].faces);
                    if (families[i].lookup.hasFaces) HasFaces = true;

                    if (families[i].primary != null)
                        families[i].primary.Changed += CallChanged;

                    if (families[i].faces != null)
                        for (var j = 0; j < families[i].faces.Length; j++)
                            if (families[i].faces[j] != null)
                                families[i].faces[j].Changed += CallChanged;
                }
            }

            if (fallbackStack != null)
                fallbackStack.Changed += CallChanged;
        }

        private void OnDisable()
        {
            DeInit();
        }

        private void OnDestroy()
        {
            DeInit();
        }

        private void DeInit()
        {
            isInitialized = false;
            cachedResolvedFamilies = null;

            if (families != null)
            {
                for (var i = 0; i < families.Length; i++)
                {
                    if (families[i].primary != null)
                        families[i].primary.Changed -= CallChanged;

                    if (families[i].faces != null)
                        for (var j = 0; j < families[i].faces.Length; j++)
                            if (families[i].faces[j] != null)
                                families[i].faces[j].Changed -= CallChanged;
                }
            }

            if (fallbackStack != null)
                fallbackStack.Changed -= CallChanged;
        }

#if UNITY_EDITOR

        private void OnValidate()
        {
            resolvedFonts = null;
            HasFaces = false;

            if (families != null)
            {
                for (var i = 0; i < families.Length; i++)
                {
                    families[i].lookup = FontFaceLookup.Build(families[i].primary, families[i].faces);
                    if (families[i].lookup.hasFaces) HasFaces = true;

                    if (families[i].primary != null)
                    {
                        families[i].primary.Changed -= CallChanged;
                        families[i].primary.Changed += CallChanged;
                    }

                    if (families[i].faces != null)
                    {
                        for (var j = 0; j < families[i].faces.Length; j++)
                        {
                            if (families[i].faces[j] == null) continue;
                            families[i].faces[j].Changed -= CallChanged;
                            families[i].faces[j].Changed += CallChanged;
                        }
                    }
                }
            }

            if (fallbackStack != null)
            {
                fallbackStack.Changed -= CallChanged;
                fallbackStack.Changed += CallChanged;
            }

            CallChanged();
        }
#endif

        private void CallChanged()
        {
            resolvedFonts = null;
            cachedResolvedFamilies = null;
            Changed?.Invoke();
        }
    }
}
