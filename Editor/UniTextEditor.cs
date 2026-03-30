using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomEditor(typeof(UniText))]
    [CanEditMultipleObjects]
    internal class UniTextEditor : UniTextBaseEditor
    {
        private SerializedProperty highlighterProp;
        private SerializedProperty raycastTargetProp;

        protected override void OnEnable()
        {
            base.OnEnable();
            highlighterProp = serializedObject.FindProperty("highlighter");
            raycastTargetProp = serializedObject.FindProperty("m_RaycastTarget");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            uniText = (UniText)target;

            DrawTextSection();
            DrawFontSection();
            DrawLayoutSection();
            DrawStyleSection();

            BeginSection("Interaction");
            EditorGUILayout.PropertyField(raycastTargetProp, new GUIContent("Raycast Target"));
            EditorGUILayout.PropertyField(highlighterProp, new GUIContent("Highlighter"));
            EndSection();

            serializedObject.ApplyModifiedProperties();

#if UNITEXT_DEBUG
            DrawDebugSection();
#endif
            DrawLoveLabel();
        }

#if UNITEXT_DEBUG
        private void DrawDebugSection()
        {
            var ut = (UniText)uniText;
            BeginSection("Debug");

            var index = 0;
            foreach (var cr in ut.CanvasRenderers)
            {
                if (cr == null) { index++; continue; }

                var go = cr.gameObject;
                var mat = cr.materialCount > 0 ? cr.GetMaterial(0) : null;
                var shaderName = mat != null ? mat.shader.name : "no material";

                if (shaderName.StartsWith("Hidden/UniText/"))
                    shaderName = shaderName.Substring("Hidden/UniText/".Length);
                else if (shaderName.StartsWith("UniText/"))
                    shaderName = shaderName.Substring("UniText/".Length);

                var active = go.activeSelf ? "" : " [inactive]";
                var info = $"CR {index}:  {shaderName}  |  mats: {cr.materialCount}{active}";

                EditorGUILayout.BeginHorizontal();
                var visible = !cr.cull;
                var newVisible = EditorGUILayout.Toggle(visible, GUILayout.Width(16));
                if (newVisible != visible) cr.cull = !newVisible;
                EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                index++;
            }

            if (index == 0)
                EditorGUILayout.LabelField("No active sub-mesh renderers", EditorStyles.miniLabel);

            EndSection();
        }
#endif
    }
}
