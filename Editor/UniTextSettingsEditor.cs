using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomEditor(typeof(UniTextSettings))]
    internal sealed class UniTextSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var settings = (UniTextSettings)target;
            var isActive = UniTextSettings.Instance == settings;

            if (!isActive)
            {
                EditorGUILayout.HelpBox(
                    "This is not the active UniTextSettings.\n" +
                    "The active settings are loaded from a Resources/ folder at runtime.\n" +
                    "Use Edit → Project Settings → UniText to edit settings.",
                    MessageType.Warning);

                EditorGUILayout.Space(4);

                var active = UniTextSettings.Instance;
                if (active != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Active Settings:",
                            AssetDatabase.GetAssetPath(active), EditorStyles.miniLabel);

                        if (GUILayout.Button("Ping", GUILayout.Width(40)))
                            EditorGUIUtility.PingObject(active);

                        if (GUILayout.Button("Select", GUILayout.Width(50)))
                            Selection.activeObject = active;
                    }
                }

                if (GUILayout.Button("Open Project Settings"))
                    SettingsService.OpenProjectSettings("Project/UniText");

                EditorGUILayout.Space(8);
            }

            GUI.enabled = isActive;
            base.OnInspectorGUI();
            GUI.enabled = true;
        }
    }
}
