using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// ScriptableObject that maps fonts to their rendering materials.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Allows different fonts to use different materials (e.g., for different
    /// SDF shaders, colors, or effects). The default material is used when
    /// no specific material is assigned to a font.
    /// </para>
    /// <para>
    /// Create via Assets menu: Create → UniText → Appearance
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextFontProvider"/>
    [CreateAssetMenu(fileName = "UniTextAppearance", menuName = "UniText/Appearance")]
    public class UniTextAppearance : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>
        /// Associates a font with materials for rendering.
        /// </summary>
        /// <remarks>
        /// For single-pass rendering, use only materials[0].
        /// For 2-pass rendering (outline first, then face), use materials[0] for outline and materials[1] for face.
        /// </remarks>
        [Serializable]
        internal struct FontMaterialPair
        {
            /// <summary>The font asset.</summary>
            public UniTextFont font;
            /// <summary>Materials for this font. Single material for normal rendering, two for 2-pass (outline + face).</summary>
            public StyledList<Material> materials;
        }

        [SerializeField]
        [Tooltip("Default materials used when no font-specific material is assigned. Single for normal, two for 2-pass.")]
        private StyledList<Material> defaultMaterials;

        [SerializeField]
        [Tooltip("Font-specific material overrides.")]
        private StyledList<FontMaterialPair> fontMaterials;

        private Material[] defaultMaterialsArr;
        private Dictionary<int, Material[]> materialsByFontId;
        private static Material[] emojiMaterials;

        private void OnEnable()
        {
            RebuildLookup();
        }

    #if UNITY_EDITOR
        public event Action Changed;
        private void OnValidate()
        {
            RebuildLookup();
            Changed?.Invoke();
        }
    #endif

        private void RebuildLookup()
        {
            var newDict = new Dictionary<int, Material[]>();

            if (fontMaterials != null)
            {
                for (var i = 0; i < fontMaterials.Length; i++)
                {
                    var pair = fontMaterials[i];
                    if (pair.font != null && pair.materials != null && pair.materials.Length > 0)
                        newDict[pair.font.GetCachedInstanceId()] = pair.materials.ToArray();
                }
            }

            materialsByFontId = newDict;
            defaultMaterialsArr = defaultMaterials.ToArray();
        }

        /// <summary>
        /// Gets all materials for rendering the specified font.
        /// </summary>
        /// <param name="font">The font to get materials for.</param>
        /// <returns>Font-specific materials array, or default materials. For 2-pass: [0]=outline, [1]=face.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Material[] GetMaterials(UniTextFont font)
        {
            if (font is EmojiFont)
            {
                emojiMaterials ??= new[] { EmojiFont.Material };
                return emojiMaterials;
            }

            return materialsByFontId.TryGetValue(font.GetCachedInstanceId(), out var mats) ? mats : defaultMaterialsArr;
        }

        private Dictionary<int, float> cachedPropertyDeltas;
        private int cachedDeltaFrame = -1;

        /// <summary>
        /// Caches the delta of two float shader properties for all materials in this appearance.
        /// Must be called from the main thread. Caches once per frame (subsequent calls are no-ops).
        /// </summary>
        /// <param name="propertyIdA">First shader property ID.</param>
        /// <param name="propertyIdB">Second shader property ID.</param>
        internal void CachePropertyDelta(int propertyIdA, int propertyIdB)
        {
            var frame = Time.frameCount;
            if (cachedDeltaFrame == frame)
                return;

            cachedDeltaFrame = frame;
            cachedPropertyDeltas ??= new Dictionary<int, float>(8);
            cachedPropertyDeltas.Clear();

            CacheArrayDelta(defaultMaterialsArr, cachedPropertyDeltas, propertyIdA, propertyIdB);
            if (materialsByFontId != null)
                foreach (var kvp in materialsByFontId)
                    CacheArrayDelta(kvp.Value, cachedPropertyDeltas, propertyIdA, propertyIdB);
        }

        /// <summary>
        /// Gets a previously cached property delta for a material.
        /// Thread-safe for reading after <see cref="CachePropertyDelta"/> completes on the main thread.
        /// </summary>
        /// <param name="materialIdentityHash">The material's identity hash from <see cref="RuntimeHelpers.GetHashCode"/>.</param>
        /// <returns>The cached delta, or 0 if not found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float GetCachedPropertyDelta(int materialIdentityHash)
        {
            return cachedPropertyDeltas != null &&
                   cachedPropertyDeltas.TryGetValue(materialIdentityHash, out var delta)
                ? delta
                : 0f;
        }

        private static void CacheArrayDelta(Material[] mats, Dictionary<int, float> cache, int propA, int propB)
        {
            if (mats == null) return;
            for (var i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;
                var id = RuntimeHelpers.GetHashCode(mat);
                if (cache.ContainsKey(id)) continue;
                cache[id] = mat.GetFloat(propA) - mat.GetFloat(propB);
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            materialsByFontId = null;
        }
    }
}
