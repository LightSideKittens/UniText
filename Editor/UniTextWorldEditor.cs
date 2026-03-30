using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomEditor(typeof(UniTextWorld))]
    [CanEditMultipleObjects]
    internal class UniTextWorldEditor : UniTextBaseEditor
    {
        private SerializedProperty sortingOrderProp;
        private SerializedProperty sortingLayerIDProp;

        protected override void OnEnable()
        {
            base.OnEnable();
            sortingOrderProp = serializedObject.FindProperty("sortingOrder");
            sortingLayerIDProp = serializedObject.FindProperty("sortingLayerID");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            uniText = (UniTextWorld)target;

            DrawTextSection();
            DrawFontSection();
            DrawLayoutSection();
            DrawStyleSection();
            DrawRenderingSection();

            serializedObject.ApplyModifiedProperties();

            DrawLoveLabel();
        }

        private void DrawRenderingSection()
        {
            BeginSection("Rendering");

            var layers = SortingLayer.layers;
            var layerNames = new string[layers.Length];
            var selected = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                layerNames[i] = layers[i].name;
                if (layers[i].id == sortingLayerIDProp.intValue)
                    selected = i;
            }

            EditorGUI.BeginProperty(EditorGUILayout.GetControlRect(false, 0), GUIContent.none, sortingLayerIDProp);
            EditorGUI.showMixedValue = sortingLayerIDProp.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var newSelected = EditorGUILayout.Popup("Sorting Layer", selected, layerNames);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in targets)
                {
                    Undo.RecordObject(t, "Change Sorting Layer");
                    ((UniTextWorld)t).SortingLayerID = layers[newSelected].id;
                    EditorUtility.SetDirty(t);
                }
            }
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();

            DrawField(sortingOrderProp, "Sorting Order",
                ut => ((UniTextWorld)ut).SortingOrder,
                (ut, v) => ((UniTextWorld)ut).SortingOrder = v);

            EndSection();
        }
    }
}
