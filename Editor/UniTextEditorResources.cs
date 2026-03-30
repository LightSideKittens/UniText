using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class UniTextEditorResources
    {
        private static readonly Dictionary<string, Texture2D> textureCache = new();
        private static readonly Dictionary<string, GUIContent> iconCache = new();
        private static readonly Dictionary<string, Texture2D> tintedCache = new();
        private static bool tintedForProSkin;

        private static readonly Color darkThemeTint = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color lightThemeTint = new(0.2f, 0.2f, 0.2f, 1f);

        private static readonly Dictionary<string, string> groupIconMap = new()
        {
            { "Text Style", "text-style" },
            { "Decoration", "decoration" },
            { "Appearance", "color" },
            { "Interactive", "interactive" },
            { "Layout", "layout" },
            { "Inline", "inline-object" },
            { "Tags", "tag" },
            { "Markdown", "markdown" },
            { "Auto-detect", "auto-detect" },
            { "Utility", "utility" },
            { "Animation", "animation" },
        };

        public static Texture2D GetGroupIcon(string groupName)
        {
            return !string.IsNullOrEmpty(groupName) && groupIconMap.TryGetValue(groupName, out var iconName)
                ? GetTintedTexture(iconName)
                : null;
        }

        private static readonly Dictionary<Type, string> typeIconMap = new()
        {
            { typeof(BoldModifier), "bold" },
            { typeof(ItalicModifier), "italic" },
            { typeof(UnderlineModifier), "underline" },
            { typeof(StrikethroughModifier), "strikethrough" },
            { typeof(UppercaseModifier), "uppercase" },
            { typeof(LowercaseModifier), "lowercase" },
            { typeof(SizeModifier), "size" },
            { typeof(ColorModifier), "color" },
            { typeof(GradientModifier), "gradient" },
            { typeof(LetterSpacingModifier), "letter-spacing" },
            { typeof(LineHeightModifier), "line-height" },
            { typeof(OutlineModifier), "outline" },
            { typeof(ShadowModifier), "shadow" },
            { typeof(LinkModifier), "link" },
            { typeof(EllipsisModifier), "ellipsis" },
            { typeof(ListModifier), "list" },
            { typeof(ObjModifier), "inline-object" },
            { typeof(SmallCapsModifier), "smallcaps" },
            { typeof(TagRule), "tag" },
            { typeof(MarkdownWrapRule), "markdown" },
            { typeof(MarkdownLinkParseRule), "markdown" },
            { typeof(MarkdownListParseRule), "markdown" },
            { typeof(RawUrlParseRule), "link" },
            { typeof(VariationModifier), "tune" },
            { typeof(ScriptPositionModifier), "script-position" },
            { typeof(CompositeModifier), "shapes" }
        };

        public static Texture2D GetTypeIcon(Type type)
        {
            return type != null && typeIconMap.TryGetValue(type, out var iconName)
                ? GetTintedTexture(iconName)
                : null;
        }

        public static Texture2D GetTexture(string name)
        {
            if (textureCache.TryGetValue(name, out var cached))
                return cached;

            var tex = Resources.Load<Texture2D>($"UniText/Icons/{name}");
            textureCache[name] = tex;
            return tex;
        }

        public static Texture2D GetTintedTexture(string name)
        {
            if (tintedForProSkin != EditorGUIUtility.isProSkin)
            {
                foreach (var tex in tintedCache.Values)
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                tintedCache.Clear();
                tintedForProSkin = EditorGUIUtility.isProSkin;
            }

            if (tintedCache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            var source = GetTexture(name);
            if (source == null) return null;

            var tint = EditorGUIUtility.isProSkin ? darkThemeTint : lightThemeTint;
            var tinted = TintTexture(source, tint);
            tinted.name = name;
            tintedCache[name] = tinted;
            return tinted;
        }
        

        public static GUIContent GetIcon(string name, string tooltip = null)
        {
            var key = tooltip != null ? $"{name}:{tooltip}" : name;

            if (iconCache.TryGetValue(key, out var cached))
                return cached;

            var tex = GetTexture(name);
            var content = tex != null
                ? new GUIContent(tex, tooltip)
                : new GUIContent(name, tooltip);

            iconCache[key] = content;
            return content;
        }

        public static void ClearCache()
        {
            textureCache.Clear();
            iconCache.Clear();
            foreach (var tex in tintedCache.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            tintedCache.Clear();
        }

        private static Texture2D TintTexture(Texture2D source, Color tint)
        {
            var pixels = source.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(
                    pixels[i].r * tint.r,
                    pixels[i].g * tint.g,
                    pixels[i].b * tint.b,
                    pixels[i].a * tint.a
                );
            }

            var tinted = new Texture2D(source.width, source.height, TextureFormat.ARGB32, true);
            tinted.SetPixels(pixels);
            tinted.Apply(true);
            tinted.filterMode = FilterMode.Trilinear;
            return tinted;
        }
    }
}
