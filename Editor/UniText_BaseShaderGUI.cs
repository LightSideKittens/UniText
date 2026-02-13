using UnityEngine;
using UnityEditor;
using LightSide;


namespace LightSide
{
    public abstract class UniText_BaseShaderGUI : ShaderGUI
    {
        protected class ShaderFeature
        {
            public string undoLabel;
            public GUIContent label;
            public GUIContent[] keywordLabels;
            public string[] keywords;
            int m_State;

            public bool Active => m_State >= 0;
            public int State => m_State;

            public void ReadState(Material material)
            {
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (material.IsKeywordEnabled(keywords[i]))
                    {
                        m_State = i;
                        return;
                    }
                }
                m_State = -1;
            }

            public void SetActive(bool active, Material material)
            {
                m_State = active ? 0 : -1;
                SetStateKeywords(material);
            }

            public void DoPopup(MaterialEditor editor, Material material)
            {
                EditorGUI.BeginChangeCheck();
                int selection = EditorGUILayout.Popup(label, m_State + 1, keywordLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    m_State = selection - 1;
                    editor.RegisterPropertyChangeUndo(undoLabel);
                    SetStateKeywords(material);
                }
            }

            void SetStateKeywords(Material material)
            {
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (i == m_State)
                        material.EnableKeyword(keywords[i]);
                    else
                        material.DisableKeyword(keywords[i]);
                }
            }
        }

        static GUIContent s_TempLabel = new();
        protected static bool s_DebugExtended;

        static float[][] s_TempFloats =
        {
            null, new float[1], new float[2], new float[3], new float[4]
        };

        protected static GUIContent[] s_XywhVectorLabels =
        {
            new("X"), new("Y"), new("W", "Width"), new("H", "Height")
        };

        protected static GUIContent[] s_LbrtVectorLabels =
        {
            new("L", "Left"), new("B", "Bottom"), new("R", "Right"), new("T", "Top")
        };

        protected static GUIContent[] s_CullingTypeLabels =
        {
            new("Off"), new("Front"), new("Back")
        };

        static GUIStyle s_PanelTitle;
        static GUIStyle s_RightLabel;

        static GUIStyle PanelTitle
        {
            get
            {
                if (s_PanelTitle == null)
                {
                    s_PanelTitle = new GUIStyle(EditorStyles.label)
                    {
                        fontStyle = UnityEngine.FontStyle.Bold,
                        alignment = TextAnchor.MiddleLeft
                    };
                }
                return s_PanelTitle;
            }
        }

        static GUIStyle RightLabel
        {
            get
            {
                if (s_RightLabel == null)
                {
                    s_RightLabel = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleRight,
                        fontStyle = UnityEngine.FontStyle.Italic,
                        fontSize = 10
                    };
                    s_RightLabel.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                }
                return s_RightLabel;
            }
        }

        protected MaterialEditor m_Editor;
        protected Material m_Material;
        protected MaterialProperty[] m_Properties;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            m_Editor = materialEditor;
            m_Material = materialEditor.target as Material;
            m_Properties = properties;

            EditorGUI.BeginChangeCheck();
            DoGUI();
            EditorGUI.EndChangeCheck();
        }

        protected abstract void DoGUI();

        static string[] s_PanelStateLabel = { "\t- <i>Click to collapse</i> -", "\t- <i>Click to expand</i>  -" };

        protected bool BeginPanel(string panel, bool expanded)
        {
            EditorGUI.indentLevel = 0;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect r = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(20, 18));
            r.x += 20;
            r.width += 6;

            bool enabled = GUI.enabled;
            GUI.enabled = true;

            if (GUI.Button(r, new GUIContent(panel), PanelTitle))
                expanded = !expanded;

            r.width -= 30;
            GUI.Label(r, new GUIContent(expanded ? s_PanelStateLabel[0] : s_PanelStateLabel[1]), RightLabel);

            GUI.enabled = enabled;

            EditorGUI.indentLevel += 1;
            EditorGUI.BeginDisabledGroup(false);

            return expanded;
        }

        protected bool BeginPanel(string panel, ShaderFeature feature, bool expanded, bool readState = true)
        {
            EditorGUI.indentLevel = 0;

            if (readState)
                feature.ReadState(m_Material);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();

            Rect r = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20f)));
            bool active = EditorGUI.Toggle(r, feature.Active);

            if (EditorGUI.EndChangeCheck())
            {
                m_Editor.RegisterPropertyChangeUndo(feature.undoLabel);
                feature.SetActive(active, m_Material);
            }

            r = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(20, 18));
            r.width += 6;

            bool enabled = GUI.enabled;
            GUI.enabled = true;

            if (GUI.Button(r, new GUIContent(panel), PanelTitle))
                expanded = !expanded;

            r.width -= 10;
            GUI.Label(r, new GUIContent(expanded ? s_PanelStateLabel[0] : s_PanelStateLabel[1]), RightLabel);

            GUI.enabled = enabled;

            GUILayout.EndHorizontal();

            EditorGUI.indentLevel += 1;
            EditorGUI.BeginDisabledGroup(!active);

            return expanded;
        }

        public void EndPanel()
        {
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel -= 1;
            EditorGUILayout.EndVertical();
        }

        MaterialProperty BeginProperty(string name)
        {
            MaterialProperty property = FindProperty(name, m_Properties);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = property.hasMixedValue;
            m_Editor.BeginAnimatedCheck(Rect.zero, property);
            return property;
        }

        bool EndProperty()
        {
            m_Editor.EndAnimatedCheck();
            EditorGUI.showMixedValue = false;
            return EditorGUI.EndChangeCheck();
        }

        protected void DoPopup(string name, string label, GUIContent[] options)
        {
            MaterialProperty property = BeginProperty(name);
            s_TempLabel.text = label;
            int index = EditorGUILayout.Popup(s_TempLabel, (int)property.floatValue, options);
            if (EndProperty())
                property.floatValue = index;
        }

        protected void DoCubeMap(string name, string label)
        {
            DoTexture(name, label, typeof(Cubemap));
        }

        protected void DoTexture2D(string name, string label, bool withTilingOffset = false)
        {
            DoTexture(name, label, typeof(Texture2D), withTilingOffset);
        }

        void DoTexture(string name, string label, System.Type type, bool withTilingOffset = false)
        {
            float objFieldSize = 60f;
            float controlHeight = objFieldSize;

            MaterialProperty property = FindProperty(name, m_Properties);
            m_Editor.BeginAnimatedCheck(Rect.zero, property);

            Rect rect = EditorGUILayout.GetControlRect(true, controlHeight);
            float totalWidth = rect.width;
            rect.width = EditorGUIUtility.labelWidth + objFieldSize;
            rect.height = objFieldSize;
            s_TempLabel.text = label;

            EditorGUI.BeginChangeCheck();
            Object tex = EditorGUI.ObjectField(rect, s_TempLabel, property.textureValue, type, false);
            if (EditorGUI.EndChangeCheck())
                property.textureValue = tex as Texture;

            m_Editor.EndAnimatedCheck();
        }

        protected void DoToggle(string name, string label)
        {
            MaterialProperty property = BeginProperty(name);
            s_TempLabel.text = label;
            bool value = EditorGUILayout.Toggle(s_TempLabel, property.floatValue == 1f);
            if (EndProperty())
                property.floatValue = value ? 1f : 0f;
        }

        protected void DoFloat(string name, string label)
        {
            MaterialProperty property = BeginProperty(name);
            Rect rect = EditorGUILayout.GetControlRect();
            rect.width = EditorGUIUtility.labelWidth + 55f;
            s_TempLabel.text = label;
            float value = EditorGUI.FloatField(rect, s_TempLabel, property.floatValue);
            if (EndProperty())
                property.floatValue = value;
        }

        protected void DoColor(string name, string label)
        {
            MaterialProperty property = BeginProperty(name);
            s_TempLabel.text = label;
            Color value = EditorGUI.ColorField(EditorGUILayout.GetControlRect(), s_TempLabel, property.colorValue, false, true, true);
            if (EndProperty())
                property.colorValue = value;
        }

        protected void DoSlider(string name, string label)
        {
            MaterialProperty property = BeginProperty(name);
            Vector2 range = property.rangeLimits;
            s_TempLabel.text = label;
            float value = EditorGUI.Slider(EditorGUILayout.GetControlRect(), s_TempLabel, property.floatValue, range.x, range.y);
            if (EndProperty())
                property.floatValue = value;
        }

        protected void DoOffset(string name, string label)
        {
            MaterialProperty property = BeginProperty(name);
            s_TempLabel.text = label;
            Vector2 value = EditorGUI.Vector2Field(EditorGUILayout.GetControlRect(), s_TempLabel, property.vectorValue);
            if (EndProperty())
                property.vectorValue = value;
        }

        protected void DoVector3(string name, string label)
        {
            MaterialProperty property = BeginProperty(name);
            s_TempLabel.text = label;
            Vector4 value = EditorGUILayout.Vector3Field(s_TempLabel, property.vectorValue);
            if (EndProperty())
                property.vectorValue = value;
        }

        protected void DoVector(string name, string label, GUIContent[] subLabels)
        {
            MaterialProperty property = BeginProperty(name);
            Rect rect = EditorGUILayout.GetControlRect();
            s_TempLabel.text = label;
            rect = EditorGUI.PrefixLabel(rect, s_TempLabel);
            Vector4 vector = property.vectorValue;

            float[] values = s_TempFloats[subLabels.Length];
            for (int i = 0; i < subLabels.Length; i++)
                values[i] = vector[i];

            EditorGUI.MultiFloatField(rect, subLabels, values);
            if (EndProperty())
            {
                for (int i = 0; i < subLabels.Length; i++)
                    vector[i] = values[i];
                property.vectorValue = vector;
            }
        }
    }

}
