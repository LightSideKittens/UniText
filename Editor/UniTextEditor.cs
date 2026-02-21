using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    [CustomEditor(typeof(UniText))]
    [CanEditMultipleObjects]
    internal class UniTextEditor : Editor
    {
        private UniText uniText;
        private SerializedProperty textProp;
        private SerializedProperty fontStackProp;
        private SerializedProperty appearanceProp;
        private SerializedProperty fontSizeProp;
        private SerializedProperty baseDirectionProp;
        private SerializedProperty wordWrapProp;
        private SerializedProperty horizontalAlignmentProp;
        private SerializedProperty verticalAlignmentProp;
        private SerializedProperty overEdgeProp;
        private SerializedProperty underEdgeProp;
        private SerializedProperty leadingDistributionProp;
        private SerializedProperty autoSizeProp;
        private SerializedProperty minFontSizeProp;
        private SerializedProperty maxFontSizeProp;
        private SerializedProperty colorProp;
        private SerializedProperty modRegistersProp;
        private SerializedProperty modRegisterConfigsProp;
        private SerializedProperty highlighterProp;
        private SerializedProperty raycastTargetProp;

        private static bool textAreaExpand;
        private static int textAreaFontSize = 14;
        private static GUIStyle textAreaStyle = null;
        private static bool enableHighlight = true;

        private static readonly Color32[] tagColors =
        {
            new(102, 187, 255, 255),
            new(91, 255, 186, 255),
            new(255, 251, 93, 255),
            new(255, 179, 99, 255),
            new(255, 146, 248, 255),
            new(255, 114, 107, 255),
            new(150, 88, 255, 255),
            new(72, 139, 255, 255),
            new(113, 255, 87, 255),
        };

        private void OnEnable()
        {
            textProp = serializedObject.FindProperty("text");
            fontStackProp = serializedObject.FindProperty("fontStack");
            appearanceProp = serializedObject.FindProperty("appearance");
            fontSizeProp = serializedObject.FindProperty("fontSize");
            baseDirectionProp = serializedObject.FindProperty("baseDirection");
            wordWrapProp = serializedObject.FindProperty("wordWrap");
            horizontalAlignmentProp = serializedObject.FindProperty("horizontalAlignment");
            verticalAlignmentProp = serializedObject.FindProperty("verticalAlignment");
            overEdgeProp = serializedObject.FindProperty("overEdge");
            underEdgeProp = serializedObject.FindProperty("underEdge");
            leadingDistributionProp = serializedObject.FindProperty("leadingDistribution");
            autoSizeProp = serializedObject.FindProperty("autoSize");
            minFontSizeProp = serializedObject.FindProperty("minFontSize");
            maxFontSizeProp = serializedObject.FindProperty("maxFontSize");
            colorProp = serializedObject.FindProperty("m_Color");
            modRegistersProp = serializedObject.FindProperty("modRegisters");
            modRegisterConfigsProp = serializedObject.FindProperty("modRegisterConfigs");
            highlighterProp = serializedObject.FindProperty("highlighter");
            raycastTargetProp = serializedObject.FindProperty("m_RaycastTarget");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            uniText = (UniText)target;
            BeginSection("Text", textProp);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Expand", GUILayout.Width(50));
            textAreaExpand = EditorGUILayout.Toggle(textAreaExpand, GUILayout.Width(25));
            EditorGUILayout.LabelField("Highlight", GUILayout.Width(60));
            enableHighlight = EditorGUILayout.Toggle(enableHighlight, GUILayout.Width(25));
            EditorGUILayout.LabelField("Size", GUILayout.Width(50));
            textAreaFontSize = EditorGUILayout.IntSlider(textAreaFontSize, 8, 24);
            EditorGUILayout.EndHorizontal();

            if (textAreaStyle == null || textAreaStyle.fontSize != textAreaFontSize)
            {
                textAreaStyle = new GUIStyle(EditorStyles.textArea) { fontSize = textAreaFontSize };
            }

            DrawTextAreaField();
            EndSection();

            BeginSection("Font");
            DrawField(fontStackProp, "Font Stack", ut => ut.FontStack, (ut, v) => ut.FontStack = v);
            DrawField(appearanceProp, "Appearance", ut => ut.Appearance, (ut, v) => ut.Appearance = v);
            DrawField(fontSizeProp, "Font Size", ut => ut.FontSize, (ut, v) => ut.FontSize = v);
            DrawField(autoSizeProp, "Auto Size", ut => ut.AutoSize, (ut, v) => ut.AutoSize = v);
            if (!autoSizeProp.hasMultipleDifferentValues && autoSizeProp.boolValue)
            {
                DrawField(minFontSizeProp, "Min Size", ut => ut.MinFontSize, (ut, v) => ut.MinFontSize = v);
                DrawField(maxFontSizeProp, "Max Size", ut => ut.MaxFontSize, (ut, v) => ut.MaxFontSize = v);
                GUI.enabled = false;
                EditorGUILayout.FloatField("Current Size", uniText.CurrentFontSize);
                GUI.enabled = true;
            }
            DrawField(colorProp, "Color", ut => ut.color, (ut, v) => ut.color = v);
            EndSection();

            BeginSection("Layout");
            DrawField(baseDirectionProp, "Base Direction", ut => ut.BaseDirection, (ut, v) => ut.BaseDirection = v);
            DrawField(wordWrapProp, "Word Wrap", ut => ut.WordWrap, (ut, v) => ut.WordWrap = v);
            EditorGUILayout.Space(4);
            DrawAlignmentButtons();
            EditorGUILayout.Space(4);
            DrawField(overEdgeProp, "Over Edge", ut => ut.OverEdge, (ut, v) => ut.OverEdge = v);
            DrawField(underEdgeProp, "Under Edge", ut => ut.UnderEdge, (ut, v) => ut.UnderEdge = v);
            DrawField(leadingDistributionProp, "Leading Distribution", ut => ut.LeadingDistribution, (ut, v) => ut.LeadingDistribution = v);
            EndSection();

            BeginSection("Modifiers");
            StyledListUtility.DrawStyledListLayout(modRegistersProp, new GUIContent("Mod Registers"));
            StyledListUtility.DrawStyledListLayout(modRegisterConfigsProp, new GUIContent("Mod Register Configs"));
            EndSection();

            BeginSection("Interaction");
            EditorGUILayout.PropertyField(raycastTargetProp, new GUIContent("Raycast Target"));
            EditorGUILayout.PropertyField(highlighterProp, new GUIContent("Highlighter"));
            EndSection();

            serializedObject.ApplyModifiedProperties();

            DrawLoveLabel();
        }

        public static void DrawLoveLabel()
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField("Made with ❤️ by Light Side", style);
            EditorGUILayout.Space(-4);
        }

        private static GUIStyle _largeHelpBox;
        private static GUIStyle LargeHelpBox
        {
            get
            {
                if (_largeHelpBox == null)
                {
                    _largeHelpBox = new GUIStyle(EditorStyles.helpBox)
                    {
                        fontSize = 12,
                        richText = true,
                        padding = new RectOffset(8, 8, 6, 6)
                    };
                }
                return _largeHelpBox;
            }
        }

        public static void DrawHelpBox(string message, MessageType type)
        {
            var icon = type switch
            {
                MessageType.Info => EditorGUIUtility.IconContent("console.infoicon").image,
                MessageType.Warning => EditorGUIUtility.IconContent("console.warnicon").image,
                MessageType.Error => EditorGUIUtility.IconContent("console.erroricon").image,
                _ => null
            };

            var content = icon != null ? new GUIContent("  " + message, icon) : new GUIContent(message);
            EditorGUILayout.LabelField(content, LargeHelpBox);
        }

        private void DrawTextAreaField()
        {
            EditorGUI.showMixedValue = textProp.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            var option = textAreaExpand ? GUILayout.ExpandHeight(true) : GUILayout.Height(72 * (textAreaFontSize / 14f));
            textScrollPos = EditorGUILayout.BeginScrollView(textScrollPos, option);

            var displayText = textProp.hasMultipleDifferentValues ? "" : textProp.stringValue;
            var result = EditorGUILayout.TextArea(displayText, textAreaStyle, GUILayout.ExpandHeight(true));

            if (Event.current.type == EventType.Repaint && enableHighlight && !textProp.hasMultipleDifferentValues)
            {
                lastTextAreaRect = GUILayoutUtility.GetLastRect();
                HighlightTags(textProp.stringValue);
            }
            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in targets)
                {
                    Undo.RecordObject(t, "Change Text");
                    ((UniText)t).Text = result;
                    EditorUtility.SetDirty(t);
                }
            }

            EditorGUI.showMixedValue = false;
        }

        private void BeginSection(string label, SerializedProperty prop = null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var rect = EditorGUILayout.GetControlRect(true);
            if (prop != null)
                EditorGUI.BeginProperty(rect, GUIContent.none, prop);

            EditorGUI.LabelField(rect, label, EditorStyles.boldLabel);

            if (prop != null)
                EditorGUI.EndProperty();
        }

        private void EndSection()
        {
            EditorGUILayout.EndVertical();
        }


        private static readonly string[] alignIconNames =
            { "left-align", "h-center-align", "right-align", "top-align", "middle-align", "bottom-align" };

        private static GUIStyle alignButtonStyle;
        private static GUIStyle alignButtonSelectedStyle;

        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        private void DrawAlignmentButtons()
        {
            if (alignButtonStyle == null || alignButtonStyle.normal.background == null)
            {
                alignButtonStyle = new GUIStyle(EditorStyles.miniButton) { fixedHeight = 26 };
                alignButtonSelectedStyle = new GUIStyle(alignButtonStyle);
                var selTex = MakeTex(new Color(0.29f, 0.59f, 0.32f));
                var deselTex = MakeTex(new Color(0.3f, 0.3f, 0.38f));
                alignButtonSelectedStyle.normal.background = selTex;
                alignButtonSelectedStyle.normal.scaledBackgrounds = null;
                alignButtonSelectedStyle.hover.background = selTex;
                alignButtonSelectedStyle.hover.scaledBackgrounds = null;
                alignButtonStyle.normal.background = deselTex;
                alignButtonStyle.normal.scaledBackgrounds = null;
                alignButtonStyle.hover.background = deselTex;
                alignButtonStyle.hover.scaledBackgrounds = null;
            }

            const float buttonWidth = 30f;
            const float buttonHeight = 26f;
            const float spacing = 8f;
            const float labelHeight = 18f;
            var groupWidth = buttonWidth * 3;
            var totalHeight = labelHeight + buttonHeight;

            var rowRect = EditorGUILayout.GetControlRect(false, totalHeight);

            var hGroupRect = new Rect(rowRect.x, rowRect.y, groupWidth, totalHeight);
            var vGroupRect = new Rect(rowRect.x + groupWidth + spacing, rowRect.y, groupWidth, totalHeight);

            var hLabelRect = new Rect(hGroupRect.x, hGroupRect.y, groupWidth, labelHeight);
            var hButtonsRect = new Rect(hGroupRect.x, hGroupRect.y + labelHeight, groupWidth, buttonHeight);

            var vLabelRect = new Rect(vGroupRect.x, vGroupRect.y, groupWidth, labelHeight);
            var vButtonsRect = new Rect(vGroupRect.x, vGroupRect.y + labelHeight, groupWidth, buttonHeight);

            EditorGUI.BeginProperty(hGroupRect, GUIContent.none, horizontalAlignmentProp);
            EditorGUI.LabelField(hLabelRect, "H Alignment", EditorStyles.label);

            var hMixed = horizontalAlignmentProp.hasMultipleDifferentValues;
            var h = uniText.HorizontalAlignment;

            EditorGUI.BeginChangeCheck();
            if (DrawAlignButton(hButtonsRect, 0, !hMixed && h == HorizontalAlignment.Left)) h = HorizontalAlignment.Left;
            if (DrawAlignButton(hButtonsRect, 1, !hMixed && h == HorizontalAlignment.Center)) h = HorizontalAlignment.Center;
            if (DrawAlignButton(hButtonsRect, 2, !hMixed && h == HorizontalAlignment.Right)) h = HorizontalAlignment.Right;
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in targets)
                {
                    Undo.RecordObject(t, "Change Horizontal Alignment");
                    ((UniText)t).HorizontalAlignment = h;
                    EditorUtility.SetDirty(t);
                }
            }
            EditorGUI.EndProperty();

            EditorGUI.BeginProperty(vGroupRect, GUIContent.none, verticalAlignmentProp);
            EditorGUI.LabelField(vLabelRect, "V Alignment", EditorStyles.label);

            var vMixed = verticalAlignmentProp.hasMultipleDifferentValues;
            var v = uniText.VerticalAlignment;

            EditorGUI.BeginChangeCheck();
            if (DrawAlignButton(vButtonsRect, 0, 3, !vMixed && v == VerticalAlignment.Top)) v = VerticalAlignment.Top;
            if (DrawAlignButton(vButtonsRect, 1, 4, !vMixed && v == VerticalAlignment.Middle)) v = VerticalAlignment.Middle;
            if (DrawAlignButton(vButtonsRect, 2, 5, !vMixed && v == VerticalAlignment.Bottom)) v = VerticalAlignment.Bottom;
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in targets)
                {
                    Undo.RecordObject(t, "Change Vertical Alignment");
                    ((UniText)t).VerticalAlignment = v;
                    EditorUtility.SetDirty(t);
                }
            }
            EditorGUI.EndProperty();
        }

        private bool DrawAlignButton(Rect groupRect, int indexInGroup, bool isSelected)
        {
            return DrawAlignButton(groupRect, indexInGroup, indexInGroup, isSelected);
        }

        private bool DrawAlignButton(Rect groupRect, int indexInGroup, int iconIndex, bool isSelected)
        {
            const float buttonWidth = 30f;
            var buttonRect = new Rect(groupRect.x + indexInGroup * buttonWidth, groupRect.y, buttonWidth, groupRect.height);
            var style = isSelected ? alignButtonSelectedStyle : alignButtonStyle;

            if (Event.current.type == EventType.MouseDown &&
                buttonRect.Contains(Event.current.mousePosition) &&
                Event.current.button != 0)
                return false;

            return GUI.Button(buttonRect, UniTextEditorResources.GetIcon(alignIconNames[iconIndex]), style);
        }

        private void DrawField<T>(SerializedProperty prop, string label, Func<UniText, T> getter, Action<UniText, T> setter)
        {
            if (prop == null) return;

            var rect = EditorGUILayout.GetControlRect(true);
            var labelContent = new GUIContent(label);

            EditorGUI.BeginProperty(rect, labelContent, prop);
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;

            var value = getter(uniText);
            EditorGUI.BeginChangeCheck();
            T newValue = DrawValue(rect, labelContent, value);

            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in targets)
                {
                    Undo.RecordObject(t, $"Change {label}");
                    setter((UniText)t, newValue);
                    EditorUtility.SetDirty(t);
                }
            }

            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
        }

        private T DrawValue<T>(Rect rect, GUIContent label, T value)
        {
            if (typeof(Object).IsAssignableFrom(typeof(T)))
            {
                var obj = value as Object;
                return (T)(object)EditorGUI.ObjectField(rect, label, obj, typeof(T), false);
            }

            return value switch
            {
                string s => (T)(object)EditorGUI.TextField(rect, label, s),
                float f => (T)(object)EditorGUI.FloatField(rect, label, f),
                bool b => (T)(object)EditorGUI.Toggle(rect, label, b),
                Enum e => (T)(object)EditorGUI.EnumPopup(rect, label, e),
                Color c => (T)(object)EditorGUI.ColorField(rect, label, c),
                _ => default,
            };
        }

        private readonly PooledList<ParsedRange> tempRanges = new(32);
        private readonly List<(int start, int end, int colorIndex)> highlightRanges = new();
        private readonly List<IParseRule> allRules = new();
        private Rect lastTextAreaRect;

        private string cachedText;
        private int cachedTextHash;
        private Rect cachedRect;
        private Vector2 textScrollPos;

        private static GUIStyle charStyle;

        private void HighlightTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var textHash = text.GetHashCode();
            var needRebuild = cachedText != text || cachedTextHash != textHash;

            if (needRebuild)
            {
                CollectHighlightRanges(text);
                cachedText = text;
                cachedTextHash = textHash;
            }

            DrawCharLabels(text);
        }

        private void CollectHighlightRanges(string text)
        {
            highlightRanges.Clear();
            CollectAllRules();

            var colorIndex = 0;
            for (var r = 0; r < allRules.Count; r++)
            {
                var rule = allRules[r];
                tempRanges.Clear();
                rule.Reset();

                var idx = 0;
                while (idx < text.Length)
                {
                    var newIdx = rule.TryMatch(text, idx, tempRanges);
                    idx = newIdx > idx ? newIdx : idx + 1;
                }
                rule.Finalize(text, tempRanges);

                for (var i = 0; i < tempRanges.Count; i++)
                {
                    var range = tempRanges[i];
                    if (range.HasTags)
                    {
                        if (range.tagStart < range.tagEnd)
                            highlightRanges.Add((range.tagStart, range.tagEnd, colorIndex));
                        if (range.closeTagStart < range.closeTagEnd)
                            highlightRanges.Add((range.closeTagStart, range.closeTagEnd, colorIndex));
                    }
                    else if (range.start < range.end)
                    {
                        highlightRanges.Add((range.start, range.end, colorIndex));
                    }
                    colorIndex++;
                }
            }
        }

        private void CollectAllRules()
        {
            allRules.Clear();

            var modRegs = uniText.ModRegisters;
            for (var m = 0; m < modRegs.Count; m++)
                if (modRegs[m]?.Rule != null)
                    allRules.Add(modRegs[m].Rule);

            var configs = uniText.ModRegisterConfigs;
            for (var c = 0; c < configs.Count; c++)
            {
                var regs = configs[c]?.modRegisters;
                if (regs == null) continue;

                for (var m = 0; m < regs.Count; m++)
                    if (regs[m]?.Rule != null)
                        allRules.Add(regs[m].Rule);
            }
        }

        private void DrawCharLabels(string text)
        {
            if (charStyle == null || charStyle.fontSize != textAreaFontSize)
            {
                charStyle = new GUIStyle
                {
                    fontSize = textAreaFontSize,
                    font = textAreaStyle.font,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                    normal = { background = null }
                };
            }

            if (highlightRanges.Count == 0) return;

            var content = new GUIContent(text);
            var lineHeight = textAreaStyle.lineHeight;

            foreach (var (start, end, colorIndex) in highlightRanges)
            {
                charStyle.normal.textColor = tagColors[colorIndex % tagColors.Length];

                var rangeEnd = Math.Min(end, text.Length);
                var segStart = -1;

                for (var i = start; i < rangeEnd; i++)
                {
                    var c = text[i];
                    if (c == '\n' || c == '\r')
                    {
                        if (segStart >= 0)
                        {
                            DrawSegment(text, segStart, i, content, lineHeight);
                            segStart = -1;
                        }
                    }
                    else if (segStart < 0)
                    {
                        segStart = i;
                    }
                }

                if (segStart >= 0)
                    DrawSegment(text, segStart, rangeEnd, content, lineHeight);
            }
        }

        private void DrawSegment(string text, int start, int end, GUIContent content, float lineHeight)
        {
            var visualStart = ToVisualIndex(text, start);
            var visualEnd = ToVisualIndex(text, end);
            var startPos = textAreaStyle.GetCursorPixelPosition(lastTextAreaRect, content, visualStart);
            var endPos = textAreaStyle.GetCursorPixelPosition(lastTextAreaRect, content, visualEnd);

            if (Mathf.Abs(endPos.y - startPos.y) > 0.1f)
            {
                for (var i = start; i < end; i++)
                {
                    var c = text[i];
                    var visualI = ToVisualIndex(text, i);
                    var visualNext = ToVisualIndex(text, i + 1);
                    var pos = textAreaStyle.GetCursorPixelPosition(lastTextAreaRect, content, visualI);
                    var nextPos = textAreaStyle.GetCursorPixelPosition(lastTextAreaRect, content, visualNext);
                    GUI.Label(new Rect(pos.x, pos.y, nextPos.x - pos.x, lineHeight), c.ToString(), charStyle);
                }
                return;
            }

            var seg = text.Substring(start, end - start);
            GUI.Label(new Rect(startPos.x, startPos.y, endPos.x - startPos.x, lineHeight), seg, charStyle);
        }

        /// <summary>
        /// Converts code unit index (string index) to visual index (grapheme cluster index).
        /// Unity's GetCursorPixelPosition works with visual positions, not UTF-16 code units.
        /// Handles surrogate pairs, variation selectors, and other combining sequences.
        /// </summary>
        private static int ToVisualIndex(string text, int codeUnitIndex)
        {
            var visualIndex = 0;
            var i = 0;

            while (i < codeUnitIndex && i < text.Length)
            {
                visualIndex++;

                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    i += 2;
                else
                    i++;

                while (i < codeUnitIndex && i < text.Length && IsVariationSelector(text[i]))
                    i++;
            }

            return visualIndex;
        }

        private static bool IsVariationSelector(char c)
        {
            return c == UnicodeData.VariationSelector15 || c == UnicodeData.VariationSelector16;
        }
    }

}
