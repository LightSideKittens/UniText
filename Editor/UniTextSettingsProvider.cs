using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal class UniTextSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/UniText";
        private const string ResourcesPath = "Assets/UniText/Resources";
        internal const string AssetPath = ResourcesPath + "/UniTextSettings.asset";

        private SerializedObject serializedSettings;
        private Editor cachedEditor;

        public UniTextSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        #region Settings UI

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var settings = EnsureDefaults();
            if (settings != null)
                serializedSettings = new SerializedObject(settings);
        }

        public override void OnDeactivate()
        {
            if (cachedEditor != null)
            {
                Object.DestroyImmediate(cachedEditor);
                cachedEditor = null;
            }
        }

        public override void OnGUI(string searchContext)
        {
            if (serializedSettings == null || serializedSettings.targetObject == null)
            {
                var settings = EnsureDefaults();
                if (settings != null)
                    serializedSettings = new SerializedObject(settings);
                else
                {
                    EditorGUILayout.HelpBox("Failed to load or create UniTextSettings.", MessageType.Error);
                    return;
                }
            }

            serializedSettings.Update();

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                    DrawSettings();
                GUILayout.Space(10);
            }

            if (serializedSettings.ApplyModifiedProperties())
                UniTextSettingsBackup.Save(serializedSettings);
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("UniText Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("gradients"),
                new GUIContent("Gradients", "Named gradients for <gradient=name> tags."));

            EditorGUILayout.Space(10);

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("defaultFontStack"),
                new GUIContent("Default Fonts", "Default fonts assigned to new UniText components."));

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("UI Creation Prefabs", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("textPrefab"),
                new GUIContent("Text Prefab", "Prefab for GameObject > UI > UniText - Text."));

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("buttonPrefab"),
                new GUIContent("Button Prefab", "Prefab for GameObject > UI > UniText - Button."));

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Word Segmentation", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("dictionaries"),
                new GUIContent("Dictionaries",
                    "Dictionary assets for SA-class scripts (Thai, Lao, Khmer, Myanmar)."), true);

            EditorGUILayout.Space(15);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select Settings Asset", GUILayout.Width(150)))
                {
                    Selection.activeObject = serializedSettings.targetObject;
                    EditorGUIUtility.PingObject(serializedSettings.targetObject);
                }
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new UniTextSettingsProvider(SettingsPath, SettingsScope.Project)
            {
                keywords = new[]
                {
                    "UniText", "Text", "Unicode", "RTL", "Arabic", "Hebrew",
                    "Font", "Thai", "Dictionary", "Segmentation"
                }
            };
        }

        #endregion

        #region Initialization

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            UniTextSettings.Changed -= OnSettingsChanged;
            UniTextSettings.Changed += OnSettingsChanged;
            EditorApplication.delayCall += () => EnsureDefaults();
        }

        private static void OnSettingsChanged()
        {
            if (UniTextSettings.IsNull) return;
            EditorApplication.delayCall += () => FixReferences(FindDefaultsFolder());
            UniTextSettingsBackup.Save(new SerializedObject(UniTextSettings.Instance));
        }

        #endregion

        #region EnsureDefaults — main pipeline

        internal static UniTextSettings EnsureDefaults()
        {
            var defaultsFolder = FindDefaultsFolder();

            if (defaultsFolder != null && !defaultsFolder.StartsWith("Assets/"))
                CopyMissingDefaults(defaultsFolder);

            var settings = EnsureSettingsInResources(defaultsFolder);
            if (settings == null) return null;

            var so = new SerializedObject(settings);
            if (UniTextSettingsBackup.Restore(so))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("UniText: Restored settings from backup.");
            }

            FixReferences(defaultsFolder);

            EnsureShaders(settings);

            UniTextSettingsBackup.Save(new SerializedObject(settings));

            return settings;
        }

        #endregion

        #region Step 1: Copy missing defaults (PM only)

        private static void CopyMissingDefaults(string defaultsFolder)
        {
            var guids = AssetDatabase.FindAssets("", new[] { defaultsFolder });
            var copied = 0;

            foreach (var guid in guids)
            {
                var sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(sourcePath)) continue;

                var relativePath = sourcePath.Substring(defaultsFolder.Length + 1);
                var destPath = relativePath == "UniTextSettings.asset"
                    ? AssetPath
                    : "Assets/UniText/" + relativePath;

                if (AssetDatabase.LoadAssetAtPath<Object>(destPath) != null)
                    continue;

                EnsureAssetFolder(Path.GetDirectoryName(destPath).Replace('\\', '/'));

                if (AssetDatabase.CopyAsset(sourcePath, destPath))
                    copied++;
                else
                    Debug.LogError($"UniText: Failed to copy {sourcePath}");
            }

            if (copied > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"UniText: Copied {copied} default asset(s) to Assets/UniText/.");
            }
        }

        #endregion

        #region Step 2: Ensure UniTextSettings in Resources

        private static UniTextSettings EnsureSettingsInResources(string defaultsFolder)
        {
            var settings = AssetDatabase.LoadAssetAtPath<UniTextSettings>(AssetPath);
            if (settings != null) return settings;

            if (defaultsFolder != null)
            {
                var source = defaultsFolder + "/UniTextSettings.asset";
                if (AssetDatabase.LoadAssetAtPath<UniTextSettings>(source) != null)
                {
                    EnsureAssetFolder(ResourcesPath);
                    AssetDatabase.CopyAsset(source, AssetPath);
                    AssetDatabase.SaveAssets();
                    settings = AssetDatabase.LoadAssetAtPath<UniTextSettings>(AssetPath);
                    if (settings != null) return settings;
                }
            }

            EnsureAssetFolder(ResourcesPath);
            var empty = ScriptableObject.CreateInstance<UniTextSettings>();
            AssetDatabase.CreateAsset(empty, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.LogWarning("[UniText] Default assets not found. Please reimport the UniText package.");
            return AssetDatabase.LoadAssetAtPath<UniTextSettings>(AssetPath);
        }

        #endregion

        #region Step 4: Fix null/broken references

        private static readonly (string field, string filter, string exactName)[] fieldMappings =
        {
            ("gradients", "t:UniTextGradients", null),
            ("defaultFontStack", "t:UniTextFontStack", null),
            ("textPrefab", "t:Prefab", "Text (UniText)"),
            ("buttonPrefab", "t:Prefab", "Button (UniText)"),
        };

        private static void FixReferences(string defaultsFolder)
        {
            var settings = AssetDatabase.LoadAssetAtPath<UniTextSettings>(AssetPath);
            if (settings == null) return;

            var searchFolders = BuildSearchFolders(defaultsFolder);
            var so = new SerializedObject(settings);
            var changed = false;
            var unresolved = 0;

            foreach (var (field, filter, exactName) in fieldMappings)
            {
                var prop = so.FindProperty(field);
                if (prop == null) continue;

                if (prop.objectReferenceValue != null)
                {
                    var refPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                    if (refPath.StartsWith("Assets/")) continue;
                }

                var guids = AssetDatabase.FindAssets(filter, searchFolders);
                Object found = null;
                foreach (var guid in guids)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset == null) continue;
                    if (exactName != null && asset.name != exactName) continue;
                    found = asset;
                    break;
                }

                if (found == null) { unresolved++; continue; }

                prop.objectReferenceValue = found;
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                UniTextSettingsBackup.Save(so);
            }

            if (unresolved > 0)
                Debug.LogWarning("[UniText] Some default assets could not be restored. " +
                                 "Please reimport the UniText package.");
        }

        private static string[] BuildSearchFolders(string defaultsFolder)
        {
            var folders = new List<string>();
            if (AssetDatabase.IsValidFolder("Assets/UniText"))
                folders.Add("Assets/UniText");
            if (defaultsFolder != null && !folders.Contains(defaultsFolder))
                folders.Add(defaultsFolder);
            return folders.ToArray();
        }

        #endregion

        #region Step 5: Ensure shaders

        private static readonly string[] requiredShaderNames =
        {
            "UniText/SDF",
            "UniText/Emoji"
        };

        private static void EnsureShaders(UniTextSettings settings)
        {
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("requiredShaders");

            if (prop.arraySize != UniTextSettings.ShaderCount)
                prop.arraySize = UniTextSettings.ShaderCount;

            var changed = false;
            for (var i = 0; i < requiredShaderNames.Length; i++)
            {
                var elem = prop.GetArrayElementAtIndex(i);
                var shader = Shader.Find(requiredShaderNames[i]);
                if (shader == null)
                {
                    Debug.LogWarning($"UniText: Shader not found: {requiredShaderNames[i]}");
                    continue;
                }
                if (elem.objectReferenceValue != shader)
                {
                    elem.objectReferenceValue = shader;
                    changed = true;
                }
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
        }

        #endregion

        #region Utilities

        private static string FindDefaultsFolder()
        {
            if (AssetDatabase.IsValidFolder("Assets/UniText/Defaults"))
                return "Assets/UniText/Defaults";

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(UniTextSettings).Assembly);
            if (packageInfo != null)
            {
                var path = packageInfo.assetPath + "/Defaults";
                if (AssetDatabase.IsValidFolder(path))
                    return path;
            }

            return null;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folderPath));
        }

        #endregion
    }
}
