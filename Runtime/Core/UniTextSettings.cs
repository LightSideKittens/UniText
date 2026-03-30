using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Global settings ScriptableObject for UniText configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Access via Edit → Project Settings → UniText.
    /// Contains editor-only default configurations for new UniText components.
    /// </para>
    /// </remarks>
    public sealed class UniTextSettings : ScriptableObject
    {
        private const string ResourcePath = "UniTextSettings";
        private const string UnicodeDataPath = "UnicodeData";

        private static TextAsset cachedUnicodeData;

        [Header("Runtime Assets")]
        [SerializeField]
        [Tooltip("Named gradients for <gradient=name> tags.")]
        private UniTextGradients gradients;

        /// <summary>Gets or sets the named gradients asset.</summary>
        public static UniTextGradients Gradients
        {
            get => Instance.gradients;
            set
            {
                if (value != Instance.gradients)
                { 
                    Instance.gradients = value;
                    Changed?.Invoke();
                }
            }
        }

        public static event Action Changed;

        internal const int ShaderSdf = 0;
        internal const int ShaderEmoji = 1;
        internal const int ShaderCount = 2;

        [SerializeField, HideInInspector]
        private Shader[] requiredShaders = new Shader[ShaderCount];

        internal static Shader GetShader(int index)
        {
            var inst = Instance;
            if (inst == null || inst.requiredShaders == null ||
                (uint)index >= (uint)inst.requiredShaders.Length)
                return null;
            return inst.requiredShaders[index];
        }

    #if UNITY_EDITOR
        [Header("Editor Defaults")]
        [SerializeField]
        [Tooltip("Default fonts assigned to new UniText components.")]
        private UniTextFontStack defaultFontStack;

        /// <summary>Gets the default fonts for new UniText components (Editor only).</summary>
        public static UniTextFontStack DefaultFontStack => Instance?.defaultFontStack;

        [SerializeField]
        [Tooltip("Prefab instantiated by GameObject > UI > UniText - Text. Falls back to code creation if null.")]
        private GameObject textPrefab;

        [SerializeField]
        [Tooltip("Prefab instantiated by GameObject > UI > UniText - Button. Falls back to code creation if null.")]
        private GameObject buttonPrefab;

        /// <summary>Gets the prefab for creating Text UI objects (Editor only).</summary>
        public static GameObject TextPrefab => Instance?.textPrefab;

        /// <summary>Gets the prefab for creating Button UI objects (Editor only).</summary>
        public static GameObject ButtonPrefab => Instance?.buttonPrefab;
    #endif

        /// <summary>Gets the compiled Unicode data asset, loaded from Resources.</summary>
        internal static TextAsset UnicodeDataAsset
        {
            get
            {
                if (cachedUnicodeData == null)
                {
                    cachedUnicodeData = Resources.Load<TextAsset>(UnicodeDataPath);
                    if (cachedUnicodeData == null)
                        Debug.LogError($"UnicodeData not found at Resources/{UnicodeDataPath}.bytes");
                }
                return cachedUnicodeData;
            }
        }

        private static UniTextSettings instance;

        /// <summary>Returns true if the instance is already loaded (without triggering load).</summary>
        internal static bool IsNull => instance == null;

        /// <summary>Gets the singleton settings instance, loading from Resources if needed.</summary>
        public static UniTextSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<UniTextSettings>(ResourcePath);

                    if (instance == null)
                        Debug.LogError(
                            $"UniTextSettings not found at Resources/{ResourcePath}.asset. " +
                            "Create it via Assets > Create > UniText > Settings and place in Resources folder.");
                }

                return instance;
            }
        }

        /// <summary>Manually sets the singleton instance (used for testing or custom initialization).</summary>
        /// <param name="settings">The settings instance to use.</param>
        public static void SetInstance(UniTextSettings settings)
        {
            instance = settings;
            Changed?.Invoke();
        }

        [SerializeField]
        [Tooltip("Dictionary assets for SA-class scripts (Thai, Lao, Khmer, Myanmar). Drag & drop to enable.")]
        private StyledList<WordSegmentationDictionary> dictionaries;

        /// <summary>Gets the configured word segmentation dictionaries.</summary>
        public static StyledList<WordSegmentationDictionary> Dictionaries
            => Instance != null ? Instance.dictionaries : null;

        internal void InvokeChanged() => Changed?.Invoke();
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            Changed?.Invoke();
        }
#endif
    }
}
