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

        private static UniTextSettings GetOrCreateSettings()
        {
            var settings = Resources.Load<UniTextSettings>("UniTextSettings");

            if (settings == null)
            {
                var guids = AssetDatabase.FindAssets("t:UniTextSettings");
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    settings = AssetDatabase.LoadAssetAtPath<UniTextSettings>(path);
                }
            }

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<UniTextSettings>();

                if (!Directory.Exists(ResourcesPath))
                    Directory.CreateDirectory(ResourcesPath);

                AssetDatabase.CreateAsset(settings, AssetPath);
                AssetDatabase.SaveAssets();

                Debug.Log($"Created UniTextSettings at {AssetPath}");
            }

            return settings;
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
