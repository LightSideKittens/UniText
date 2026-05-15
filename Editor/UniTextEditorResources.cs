using System.Collections.Generic;
using UnityEngine;
using LightSide;

namespace LightSide
{
    internal static class UniTextEditorResources
    {
        private static readonly Dictionary<string, Texture2D> textureCache = new();
        private static readonly Dictionary<string, GUIContent> iconCache = new();

        public static Texture2D GetTexture(string name)
        {
            if (textureCache.TryGetValue(name, out var cached))
                return cached;

            var tex = Resources.Load<Texture2D>($"Icons/{name}");
            textureCache[name] = tex;
            return tex;
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
        }
    }

}
