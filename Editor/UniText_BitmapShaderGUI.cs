using UnityEngine;
using UnityEditor;
using LightSide;


namespace LightSide
{
        public class UniText_BitmapShaderGUI : UniText_BaseShaderGUI
        {
            static bool s_Face = true;

            protected override void DoGUI()
            {
                s_Face = BeginPanel("Face", s_Face);
                if (s_Face)
                    DoFacePanel();
                EndPanel();

                s_DebugExtended = BeginPanel("Debug Settings", s_DebugExtended);
                if (s_DebugExtended)
                    DoDebugPanel();
                EndPanel();
            }

            void DoFacePanel()
            {
                EditorGUI.indentLevel += 1;

                if (m_Material.HasProperty("_FaceColor"))
                {
                    DoColor("_FaceColor", "Color");
                    if (m_Material.HasProperty("_FaceTex"))
                        DoTexture2D("_FaceTex", "Texture", true);
                }
                else if (m_Material.HasProperty("_Color"))
                {
                    DoColor("_Color", "Color");
                    if (m_Material.HasProperty("_DiffusePower"))
                        DoSlider("_DiffusePower", "Diffuse Power");
                }

                EditorGUI.indentLevel -= 1;
                EditorGUILayout.Space();
            }

            void DoDebugPanel()
            {
                EditorGUI.indentLevel += 1;
                DoTexture2D("_MainTex", "Font Atlas");

                if (m_Material.HasProperty("_VertexOffsetX"))
                {
                    if (m_Material.HasProperty("_Padding"))
                    {
                        EditorGUILayout.Space();
                        DoFloat("_Padding", "Padding");
                    }

                    EditorGUILayout.Space();
                    DoFloat("_VertexOffsetX", "Offset X");
                    DoFloat("_VertexOffsetY", "Offset Y");
                }

                if (m_Material.HasProperty("_MaskSoftnessX"))
                {
                    EditorGUILayout.Space();
                    DoFloat("_MaskSoftnessX", "Softness X");
                    DoFloat("_MaskSoftnessY", "Softness Y");
                    DoVector("_ClipRect", "Clip Rect", s_LbrtVectorLabels);
                }

                if (m_Material.HasProperty("_Stencil"))
                {
                    EditorGUILayout.Space();
                    DoFloat("_Stencil", "Stencil ID");
                    DoFloat("_StencilComp", "Stencil Comp");
                }

                if (m_Material.HasProperty("_CullMode"))
                {
                    EditorGUILayout.Space();
                    DoPopup("_CullMode", "Cull Mode", s_CullingTypeLabels);
                }

                EditorGUILayout.Space();
                EditorGUI.indentLevel -= 1;
                EditorGUILayout.Space();
            }
        }


}
