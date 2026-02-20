using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LightSide;

namespace LightSide
{
    /// <summary>
    /// Provides Project Settings UI for UniText configuration.
    /// </summary>
    internal class UniTextSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/UniText";
        private const string ResourcesPath = "Assets/UniText/Resources";
        private const string AssetPath = ResourcesPath + "/UniTextSettings.asset";

        private SerializedObject serializedSettings;
        private UnityEditor.Editor cachedEditor;

        public UniTextSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var settings = GetOrCreateSettings();
            if (settings != null)
            {
                serializedSettings = new SerializedObject(settings);
            }
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
                var settings = GetOrCreateSettings();
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
                {
                    DrawSettings();
                }
                GUILayout.Space(10);
            }

            if (serializedSettings.ApplyModifiedProperties())
            {
                UniTextSettingsBackup.Save(serializedSettings);
            }
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("UniText Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            var gradientsProp = serializedSettings.FindProperty("gradients");
            EditorGUILayout.PropertyField(gradientsProp, new GUIContent("Gradients",
                "Named gradients for <gradient=name> tags."));

            EditorGUILayout.Space(10);

            var fontsProp = serializedSettings.FindProperty("defaultFontStack");
            var appearanceProp = serializedSettings.FindProperty("defaultAppearance");

            EditorGUILayout.PropertyField(fontsProp, new GUIContent("Default Fonts",
                "Default fonts assigned to new UniText components."));
            EditorGUILayout.PropertyField(appearanceProp, new GUIContent("Default Appearance",
                "Default appearance assigned to new UniText components."));

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

        [InitializeOnLoadMethod]
        private static void EnsureSettingsExist()
        {
            UniTextSettings.Changed -= OnSettingsChanged;
            UniTextSettings.Changed += OnSettingsChanged;

            EditorApplication.delayCall += () => { GetOrCreateSettings(); };
        }

        private static void OnSettingsChanged()
        {
            if (UniTextSettings.IsNull) return;
            UniTextSettingsBackup.Save(new SerializedObject(UniTextSettings.Instance));
        }

        private static UniTextSettings GetOrCreateSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniTextSettings>(AssetPath);
            if (existing != null)
            {
                var so = new SerializedObject(existing);
                if (UniTextSettingsBackup.Restore(so))
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log("UniText: Restored settings from backup.");
                }
                return existing;
            }

            var templatePath = FindTemplatePath();

            if (!Directory.Exists(ResourcesPath))
                Directory.CreateDirectory(ResourcesPath);

            if (templatePath != null)
            {
                if (templatePath.StartsWith("Assets/"))
                {
                    var result = AssetDatabase.MoveAsset(templatePath, AssetPath);
                    if (!string.IsNullOrEmpty(result))
                        Debug.LogError($"UniText: Failed to move settings: {result}");
                }
                else
                {
                    if (!AssetDatabase.CopyAsset(templatePath, AssetPath))
                        Debug.LogError($"UniText: Failed to copy settings from {templatePath}");
                }
            }
            else
            {
                var empty = ScriptableObject.CreateInstance<UniTextSettings>();
                AssetDatabase.CreateAsset(empty, AssetPath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"UniText: Settings initialized at {AssetPath}");

            var created = AssetDatabase.LoadAssetAtPath<UniTextSettings>(AssetPath);
            if (created != null)
                UniTextSettingsBackup.Save(new SerializedObject(created));
            return created;
        }

        private static string FindTemplatePath()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniTextSettings"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Defaults/"))
                    return path;
            }
            return null;
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new UniTextSettingsProvider(SettingsPath, SettingsScope.Project)
            {
                keywords = new[] { "UniText", "Text", "Unicode", "RTL", "Arabic", "Hebrew", "Font" }
            };
            return provider;
        }
    }
}
