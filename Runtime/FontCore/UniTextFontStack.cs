using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// ScriptableObject container for font collections with fallback chain support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first font in the list is the main font. Subsequent fonts serve as
    /// fallbacks for characters not found in the main font.
    /// </para>
    /// <para>
    /// Create via Assets menu: Create → UniText → Fonts
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextFont"/>
    /// <seealso cref="UniTextFontProvider"/>
    public class UniTextFontStack : ScriptableObject
    {
        /// <summary>List of fonts in fallback order. First font is the main font.</summary>
        public StyledList<UniTextFont> fonts = new();

        /// <summary>Optional fallback stack. Searched after this stack's own fonts.</summary>
        public UniTextFontStack fallbackStack;

        /// <summary>Gets the main (primary) font, or null if the list is empty.</summary>
        public UniTextFont MainFont => fonts is { Count: > 0 } ? fonts[0] : null;

        private UniTextFont[] resolvedFonts;

        /// <summary>
        /// Finds a font that can render the specified Unicode codepoint.
        /// </summary>
        /// <param name="unicode">Unicode codepoint to find a font for.</param>
        /// <param name="searched">Set of already-searched font IDs (to prevent loops).</param>
        /// <returns>Font that can render the codepoint, or null if none found.</returns>
        /// <remarks>
        /// Automatically checks emoji fonts for extended pictographic codepoints.
        /// </remarks>
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
            CollectFonts(this, list, visitedStacks);
            return list.ToArray();
        }

        private static void CollectFonts(UniTextFontStack stack, List<UniTextFont> list, HashSet<UniTextFontStack> visited)
        {
            while (true)
            {
                if (stack == null || !visited.Add(stack)) return;

                for (int i = 0; i < stack.fonts.Count; i++)
                    if (stack.fonts[i] != null)
                        list.Add(stack.fonts[i]);

                stack = stack.fallbackStack;
            }
        }

        internal event Action Changed;
        [NonSerialized] private bool isInitialized;
        
        private void TryInit()
        {
            if(isInitialized) return;

            isInitialized = true;
            
            for (var i = 0; i < fonts.Count; i++)
            {
                if (fonts[i] != null)
                    fonts[i].Changed += CallChanged;
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
            for (var i = 0; i < fonts.Count; i++)
            {
                if (fonts[i] != null)
                    fonts[i].Changed -= CallChanged;
            }

            if (fallbackStack != null)
                fallbackStack.Changed -= CallChanged;
        }
        
#if UNITY_EDITOR

        private void OnValidate()
        {
            resolvedFonts = null;

            for (var i = 0; i < fonts.Count; i++)
            {
                if (fonts[i] == null) continue;
                fonts[i].Changed -= CallChanged;
                fonts[i].Changed += CallChanged;
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
            Changed?.Invoke();
        }
    }
}
