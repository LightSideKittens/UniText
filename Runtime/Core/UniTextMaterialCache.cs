using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Lazily creates and caches shared materials for auto-material management.
    /// </summary>
    /// <remarks>
    /// Materials are created on first access (must be main thread) and persist for the application lifetime.
    /// All UniText components share the same material instances. Atlas textures are set directly on
    /// the materials via <c>SetSdfAtlasTexture</c>. Uses the unified SDF shader that handles both
    /// face and effect modes via UV2 (zeros = face, non-zero = effect).
    /// </remarks>
    internal static class UniTextMaterialCache
    {
        private static Material sdfUnified;
        private static Material msdfUnified;

        private static bool subscribedToSdfAtlas;
        private static bool subscribedToMsdfAtlas;

        private static Texture currentSdfAtlas;
        private static Texture currentMsdfAtlas;

        /// <summary>Unified SDF material (face + effects in one pass).</summary>
        public static Material Sdf => sdfUnified ??= CreateAndSync(
            CreateUnifiedMaterial(false),
            currentSdfAtlas);

        /// <summary>Unified MSDF material (face + effects in one pass).</summary>
        public static Material Msdf => msdfUnified ??= CreateAndSync(
            CreateUnifiedMaterial(true),
            currentMsdfAtlas);

        /// <summary>
        /// Sets _MainTex on all SDF materials to the given atlas texture.
        /// Called when the SDF atlas Texture2DArray is created or replaced.
        /// </summary>
        internal static void SetSdfAtlasTexture(Texture atlas)
        {
            currentSdfAtlas = atlas;
            ApplyTexture(sdfUnified, atlas);
        }

        /// <summary>
        /// Sets _MainTex on all MSDF materials to the given atlas texture.
        /// Called when the MSDF atlas Texture2DArray is created or replaced.
        /// </summary>
        internal static void SetMsdfAtlasTexture(Texture atlas)
        {
            currentMsdfAtlas = atlas;
            ApplyTexture(msdfUnified, atlas);
        }

        /// <summary>
        /// Ensures subscription to atlas texture change events. Called lazily on first material access.
        /// </summary>
        internal static void EnsureAtlasSubscription()
        {
            if (subscribedToSdfAtlas) return;
            subscribedToSdfAtlas = true;
            var atlas = GlyphAtlas.GetInstance(UniTextBase.RenderModee.SDF);
            atlas.AtlasTextureChanged += SetSdfAtlasTexture;
            if (atlas.AtlasTexture != null)
                SetSdfAtlasTexture(atlas.AtlasTexture);
        }

        /// <summary>
        /// Ensures subscription to MSDF atlas texture change events.
        /// </summary>
        internal static void EnsureMsdfAtlasSubscription()
        {
            if (subscribedToMsdfAtlas) return;
            subscribedToMsdfAtlas = true;
            var atlas = GlyphAtlas.GetInstance(UniTextBase.RenderModee.MSDF);
            atlas.AtlasTextureChanged += SetMsdfAtlasTexture;
            if (atlas.AtlasTexture != null)
                SetMsdfAtlasTexture(atlas.AtlasTexture);
        }

        private static Material CreateAndSync(Material mat, Texture atlas)
        {
            if (atlas != null)
                mat.mainTexture = atlas;
            return mat;
        }

        private static void ApplyTexture(Material mat, Texture atlas)
        {
            if (mat == null) return;
            mat.mainTexture = atlas;
        }

        private static Material CreateUnifiedMaterial(bool msdf)
        {
            var shader = UniTextSettings.GetShader(UniTextSettings.ShaderSdf);
            if (shader == null)
            {
                Cat.MeowWarn("[UniTextMaterialCache] Unified SDF shader not found in UniTextSettings");
                return null;
            }
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (msdf) mat.EnableKeyword("UNITEXT_MSDF");
            return mat;
        }
    }
}
