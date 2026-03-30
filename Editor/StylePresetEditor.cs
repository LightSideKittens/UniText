using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomEditor(typeof(StylePreset))]
    [CanEditMultipleObjects]
    internal class StylePresetEditor : Editor
    {
        private SerializedProperty stylesProp;

        private void OnEnable()
        {
            stylesProp = serializedObject.FindProperty("styles");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var stylesItems = stylesProp.FindPropertyRelative("items");
            StyledListUtility.DrawStyledListLayout(stylesItems, new GUIContent("Styles"),
                new StyledListUtility.ListCallbacks
                {
                    onAddButtonClicked = ShowStylePresetSelector
                });

            serializedObject.ApplyModifiedProperties();
            UniTextBaseEditor.DrawLoveLabel();
        }

        private void ShowStylePresetSelector(Rect buttonRect, SerializedProperty listProperty)
        {
            UniTextBaseEditor.ShowStylePresetSelector(buttonRect, targets, serializedObject,
                (t, style) =>
                {
                    ((StylePreset)t).AddStyle_Editor(style);
                });
        }
    }
}
