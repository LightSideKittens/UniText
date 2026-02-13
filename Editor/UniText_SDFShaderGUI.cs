using UnityEngine;
using UnityEditor;
using LightSide;


namespace LightSide
{
        public class UniText_SDFShaderGUI : UniText_BaseShaderGUI
        {
            static ShaderFeature s_OutlineFeature, s_UnderlayFeature;

            static bool s_Face = true, s_Outline = true, s_Underlay = true;

            static UniText_SDFShaderGUI()
            {
                s_OutlineFeature = new ShaderFeature()
                {
                    undoLabel = "Outline",
                    keywords = new[] { "OUTLINE_ON" }
                };

                s_UnderlayFeature = new ShaderFeature()
                {
                    undoLabel = "Underlay",
                    keywords = new[] { "UNDERLAY_ON", "UNDERLAY_INNER" },
                    label = new GUIContent("Underlay Type"),
                    keywordLabels = new[]
                    {
                        new GUIContent("None"), new GUIContent("Normal"), new GUIContent("Inner")
                    }
                };
            }

            protected override void DoGUI()
            {
                if (m_Material.HasProperty("_FaceColor"))
                {
                    s_Face = BeginPanel("Face", s_Face);
                    if (s_Face)
                        DoFacePanel();
                    EndPanel();
                }

                if (m_Material.HasProperty("_OutlineColor"))
                {
                    s_Outline = BeginPanel("Outline", s_OutlineFeature, s_Outline);
                    if (s_Outline)
                        DoOutlinePanel();
                    EndPanel();
                }

                if (m_Material.HasProperty("_UnderlayColor"))
                {
                    s_Underlay = BeginPanel("Underlay", s_UnderlayFeature, s_Underlay);
                    if (s_Underlay)
                        DoUnderlayPanel();
                    EndPanel();
                }

                s_DebugExtended = BeginPanel("Debug Settings", s_DebugExtended);
                if (s_DebugExtended)
                    DoDebugPanel();
                EndPanel();

                EditorGUILayout.Space();
            }

            void DoFacePanel()
            {
                EditorGUI.indentLevel += 1;

                if (m_Material.HasProperty("_FaceColor"))
                    DoColor("_FaceColor", "Color");

                if (m_Material.HasProperty("_FaceDilate"))
                    DoFloat("_FaceDilate", "Dilate");

                if (m_Material.HasProperty("_WeightNormal"))
                    DoFloat("_WeightNormal", "Weight Normal");

                if (m_Material.HasProperty("_WeightBold"))
                    DoFloat("_WeightBold", "Weight Bold");

                EditorGUI.indentLevel -= 1;
                EditorGUILayout.Space();
            }

            void DoOutlinePanel()
            {
                EditorGUI.indentLevel += 1;

                if (m_Material.HasProperty("_OutlineColor"))
                    DoColor("_OutlineColor", "Color");

                if (m_Material.HasProperty("_OutlineDilate"))
                    DoFloat("_OutlineDilate", "Dilate");
                else if (m_Material.HasProperty("_OutlineWidth"))
                    DoFloat("_OutlineWidth", "Thickness");

                if (m_Material.HasProperty("_OutlineSoftness"))
                    DoFloat("_OutlineSoftness", "Softness");

                EditorGUI.indentLevel -= 1;
                EditorGUILayout.Space();
            }

            void DoUnderlayPanel()
            {
                EditorGUI.indentLevel += 1;
                s_UnderlayFeature.DoPopup(m_Editor, m_Material);
                DoColor("_UnderlayColor", "Color");
                DoFloat("_UnderlayOffsetX", "Offset X");
                DoFloat("_UnderlayOffsetY", "Offset Y");
                DoFloat("_UnderlayDilate", "Dilate");
                DoFloat("_UnderlaySoftness", "Softness");
                EditorGUI.indentLevel -= 1;
                EditorGUILayout.Space();
            }

            void DoDebugPanel()
            {
                EditorGUI.indentLevel += 1;
                if (m_Material.HasProperty("_ScaleX"))
                    DoFloat("_ScaleX", "Scale X");
                DoFloat("_ScaleY", "Scale Y");

                if (m_Material.HasProperty("_Sharpness"))
                    DoSlider("_Sharpness", "Sharpness");

                if (m_Material.HasProperty("_PerspectiveFilter"))
                    DoSlider("_PerspectiveFilter", "Perspective Filter");

                EditorGUILayout.Space();
                DoFloat("_VertexOffsetX", "Offset X");
                DoFloat("_VertexOffsetY", "Offset Y");

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
                EditorGUI.BeginDisabledGroup(true);
                if (m_Material.HasProperty("_ScaleRatioA"))
                    DoFloat("_ScaleRatioA", "Scale Ratio A");
                if (m_Material.HasProperty("_ScaleRatioB"))
                    DoFloat("_ScaleRatioB", "Scale Ratio B");
                if (m_Material.HasProperty("_ScaleRatioC"))
                    DoFloat("_ScaleRatioC", "Scale Ratio C");
                EditorGUI.EndDisabledGroup();

                EditorGUI.indentLevel -= 1;
                EditorGUILayout.Space();
            }
        }


}
