using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Base editor for UniTextBase-derived components.
    /// Draws shared sections: Text, Font, Layout, Modifiers.
    /// </summary>
    internal abstract class UniTextBaseEditor : Editor
    {
        protected UniTextBase uniText;
        protected SerializedProperty textProp;
        protected SerializedProperty fontStackProp;
        protected SerializedProperty fontSizeProp;
        protected SerializedProperty baseDirectionProp;
        protected SerializedProperty wordWrapProp;
        protected SerializedProperty horizontalAlignmentProp;
        protected SerializedProperty verticalAlignmentProp;
        protected SerializedProperty overEdgeProp;
        protected SerializedProperty underEdgeProp;
        protected SerializedProperty leadingDistributionProp;
        protected SerializedProperty autoSizeProp;
        protected SerializedProperty minFontSizeProp;
        protected SerializedProperty maxFontSizeProp;
        protected SerializedProperty colorProp;
        protected SerializedProperty stylesProp;
        protected SerializedProperty stylePresetsProp;
        protected SerializedProperty renderModeProp;

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

        protected virtual void OnEnable()
        {
            textProp = serializedObject.FindProperty("text");
            fontStackProp = serializedObject.FindProperty("fontStack");
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
            stylesProp = serializedObject.FindProperty("styles");
            stylePresetsProp = serializedObject.FindProperty("stylePresets");
            renderModeProp = serializedObject.FindProperty("renderMode");
        }

        protected void DrawTextSection()
        {
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
        }

        protected void DrawFontSection()
        {
            BeginSection("Font");
            DrawField(fontStackProp, "Font Stack", ut => ut.FontStack, (ut, v) => ut.FontStack = v);
            DrawField(renderModeProp, "Render Mode", ut => ut.RenderMode, (ut, v) => ut.RenderMode = v);
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
        }

        protected void DrawLayoutSection()
        {
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
        }

        protected void DrawStyleSection()
        {
            BeginSection("Style");
            DrawPresetToggles();
            var stylesItems = stylesProp.FindPropertyRelative("items");
            StyledListUtility.DrawStyledListLayout(stylesItems, new GUIContent("Styles"),
                new StyledListUtility.ListCallbacks
                {
                    onAddButtonClicked = ShowStylePresetSelector
                });
            StyledListUtility.DrawStyledListLayout(stylePresetsProp, new GUIContent("Style Presets"));
            EndSection();
        }

        #region Text Area

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
                    ((UniTextBase)t).Text = result;
                    EditorUtility.SetDirty(t);
                }
            }

            EditorGUI.showMixedValue = false;
        }

        #endregion

        #region Sections

        protected void BeginSection(string label, SerializedProperty prop = null)
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

        protected void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Alignment

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
                    ((UniTextBase)t).HorizontalAlignment = h;
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
                    ((UniTextBase)t).VerticalAlignment = v;
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

        #endregion

        #region DrawField

        protected void DrawField<T>(SerializedProperty prop, string label, Func<UniTextBase, T> getter, Action<UniTextBase, T> setter)
        {
            if (prop == null) return;

            var rect = EditorGUILayout.GetControlRect(true);
            var labelContent = new GUIContent(label, prop.tooltip);

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
                    setter((UniTextBase)t, newValue);
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
                int n => (T)(object)EditorGUI.IntField(rect, label, n),
                float f => (T)(object)EditorGUI.FloatField(rect, label, f),
                bool b => (T)(object)EditorGUI.Toggle(rect, label, b),
                Enum e => (T)(object)EditorGUI.EnumPopup(rect, label, e),
                Color c => (T)(object)EditorGUI.ColorField(rect, label, c),
                _ => default,
            };
        }

        #endregion

        #region Tag Highlighting

        private readonly PooledList<ParsedRange> tempRanges = new(32);
        private readonly List<(int start, int end, int colorIndex)> highlightRanges = new();
        private readonly List<IParseRule> allRules = new();
        private Rect lastTextAreaRect;

        private string cachedText;
        private int cachedTextHash;
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

            var modRegs = uniText.Styles;
            for (var m = 0; m < modRegs.Count; m++)
                if (modRegs[m]?.Rule != null)
                    allRules.Add(modRegs[m].Rule);

            var configs = uniText.StylePresets;
            for (var c = 0; c < configs.Count; c++)
            {
                var regs = configs[c]?.styles;
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

        #endregion

        #region Utility

        public static void DrawLoveLabel()
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField("Made with \u2764\ufe0f by Light Side", style);
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

        #endregion

        #region Style Presets

        private static Selector.SelectorItem[] stylePresetItems;

        internal static Selector.SelectorItem[] GetStylePresetItems()
        {
            if (stylePresetItems != null) return stylePresetItems;

            stylePresetItems = new[]
            {
                MakePreset("Custom...", "", -2,
                    null, null, "CustomTool"),
                MakePreset<BoldModifier, RangeRule>("Bold", "", -1,
                    () => new BoldModifier(), MakeFullTextRangeRule, "bold", 0),
                MakePreset<ItalicModifier, RangeRule>("Italic", "", -1,
                    () => new ItalicModifier(), MakeFullTextRangeRule, "italic", 0),
                MakePreset<UppercaseModifier, RangeRule>("Uppercase", "", -1,
                    () => new UppercaseModifier(), MakeFullTextRangeRule, "uppercase", 1),
                MakePreset<LowercaseModifier, RangeRule>("Lowercase", "", -1,
                    () => new LowercaseModifier(), MakeFullTextRangeRule, "lowercase", 1),
                MakePreset<SmallCapsModifier, RangeRule>("Small Caps", "", -1,
                    () => new SmallCapsModifier(), MakeFullTextRangeRule, "smallcaps", 1),
                MakePreset<LetterSpacingModifier, RangeRule>("Letter Spacing", "", -1,
                    () => new LetterSpacingModifier(), MakeFullTextRangeRule, "letter-spacing", 2),
                MakePreset<UnderlineModifier, RangeRule>("Underline", "", -1,
                    () => new UnderlineModifier(), MakeFullTextRangeRule, "underline", 3),
                MakePreset<StrikethroughModifier, RangeRule>("Strikethrough", "", -1,
                    () => new StrikethroughModifier(), MakeFullTextRangeRule, "strikethrough", 3),
                MakePreset<OutlineModifier, RangeRule>("Outline", "", -1,
                    () => new OutlineModifier(), MakeFullTextRangeRule, "outline", 4),
                MakePreset<ShadowModifier, RangeRule>("Shadow", "", -1,
                    () => new ShadowModifier(), MakeFullTextRangeRule, "shadow", 4),
                MakePreset<VariationModifier, RangeRule>("Variation", "", -1,
                    () => new VariationModifier(), MakeFullTextRangeRule, "tune", 5),
                MakePreset<BoldModifier, TagRule>("Bold", "Tags", 0,
                    () => new BoldModifier(), () => new TagRule("b"), "bold"),
                MakePreset<ItalicModifier, TagRule>("Italic", "Tags", 0,
                    () => new ItalicModifier(), () => new TagRule("i"), "italic"),
                MakePreset<SizeModifier, TagRule>("Size", "Tags", 0,
                    () => new SizeModifier(), () => new TagRule("size"), "size"),
                MakePreset<UppercaseModifier, TagRule>("Uppercase", "Tags", 0,
                    () => new UppercaseModifier(), () => new TagRule("upper"), "uppercase"),
                MakePreset<LowercaseModifier, TagRule>("Lowercase", "Tags", 0,
                    () => new LowercaseModifier(), () => new TagRule("lower"), "lowercase"),
                MakePreset<ColorModifier, TagRule>("Color", "Tags", 0,
                    () => new ColorModifier(), () => new TagRule("color"), "color"),
                MakePreset<GradientModifier, TagRule>("Gradient", "Tags", 0,
                    () => new GradientModifier(), () => new TagRule("gradient"), "gradient"),
                MakePreset<LetterSpacingModifier, TagRule>("Letter Spacing", "Tags", 0,
                    () => new LetterSpacingModifier(), () => new TagRule("cspace"), "letter-spacing"),
                MakePreset<LineHeightModifier, TagRule>("Line Height", "Tags", 0,
                    () => new LineHeightModifier(), () => new TagRule("line-height"), "line-height"),
                MakePreset<UnderlineModifier, TagRule>("Underline", "Tags", 0,
                    () => new UnderlineModifier(), () => new TagRule("u"), "underline"),
                MakePreset<StrikethroughModifier, TagRule>("Strikethrough", "Tags", 0,
                    () => new StrikethroughModifier(), () => new TagRule("s"), "strikethrough"),
                MakePreset<LinkModifier, TagRule>("Link", "Tags", 0,
                    () => new LinkModifier(), () => new TagRule("link"), "link"),
                MakePreset<EllipsisModifier, TagRule>("Ellipsis", "Tags", 0,
                    () => new EllipsisModifier(), () => new TagRule("ellipsis"), "ellipsis"),
                MakePreset<ObjModifier, TagRule>("Inline Object", "Tags", 0,
                    () => new ObjModifier(), () => new TagRule("obj"), "inline-object"),
                MakePreset<SmallCapsModifier, TagRule>("Small Caps", "Tags", 0,
                    () => new SmallCapsModifier(), () => new TagRule("smallcaps"), "smallcaps"),
                MakePreset<VariationModifier, TagRule>("Variation", "Tags", 0,
                    () => new VariationModifier(), () => new TagRule("var"), "tune"),
                MakePreset<BoldModifier, MarkdownWrapRule>("Bold", "Markdown", 1,
                    () => new BoldModifier(), () => new MarkdownWrapRule { marker = "**" }, "bold"),
                MakePreset<ItalicModifier, MarkdownWrapRule>("Italic", "Markdown", 1,
                    () => new ItalicModifier(), () => new MarkdownWrapRule { marker = "*" }, "italic"),
                MakePreset<UnderlineModifier, MarkdownWrapRule>("Underline", "Markdown", 1,
                    () => new UnderlineModifier(), () => new MarkdownWrapRule { marker = "++" }, "underline"),
                MakePreset<StrikethroughModifier, MarkdownWrapRule>("Strikethrough", "Markdown", 1,
                    () => new StrikethroughModifier(), () => new MarkdownWrapRule { marker = "~~" }, "strikethrough"),
                MakePreset<LinkModifier, MarkdownLinkParseRule>("Link", "Markdown", 1,
                    () => new LinkModifier(), () => new MarkdownLinkParseRule(), "link"),
                MakePreset<LinkModifier, RawUrlParseRule>("Link (Raw URL)", "Markdown", 1,
                    () => new LinkModifier(), () => new RawUrlParseRule(), "link"),
                MakePreset<ListModifier, MarkdownListParseRule>("List", "Markdown", 1,
                    () => new ListModifier(), () => new MarkdownListParseRule(), "list"),
                MakePreset<OutlineModifier, RangeRule>("Outline", "Range", 2,
                    () => new OutlineModifier(), MakeFullTextRangeRule, "outline"),
                MakePreset<ShadowModifier, RangeRule>("Shadow", "Range", 2,
                    () => new ShadowModifier(), MakeFullTextRangeRule, "shadow"),
            };

            return stylePresetItems;
        }

        private static IParseRule MakeFullTextRangeRule()
        {
            return new RangeRule { data = new List<RangeRule.Data>
            {
                new() { range = "..", parameter = "" }
            }};
        }

        internal sealed class StylePresetEntry
        {
            public Type modifierType;
            public Type ruleType;
            public string iconName;
            public Func<BaseModifier> createModifier;
            public Func<IParseRule> createRule;
            public int visualGroup;
        }

        private static Selector.SelectorItem MakePreset<TModifier, TRule>(string name, string group, int order,
            Func<BaseModifier> createModifier, Func<IParseRule> createRule, string iconName = null, int visualGroup = -1)
            where TModifier : BaseModifier where TRule : IParseRule
        {
            return new Selector.SelectorItem
            {
                displayName = name,
                groupName = group,
                groupOrder = order,
                description = BuildDescription(typeof(TModifier), typeof(TRule)),
                value = new StylePresetEntry { modifierType = typeof(TModifier), ruleType = typeof(TRule), iconName = iconName, createModifier = createModifier, createRule = createRule, visualGroup = visualGroup }
            };
        }

        private static Selector.SelectorItem MakePreset(string name, string group, int order,
            Func<BaseModifier> createModifier, Func<IParseRule> createRule, string iconName = null)
        {
            return new Selector.SelectorItem
            {
                displayName = name,
                groupName = group,
                groupOrder = order,
                value = new StylePresetEntry { iconName = iconName, createModifier = createModifier, createRule = createRule }
            };
        }

        private static string BuildDescription(Type modifierType, Type ruleType)
        {
            var modDesc = modifierType?.GetCustomAttribute<TypeDescriptionAttribute>()?.Description;
            var ruleDesc = ruleType?.GetCustomAttribute<TypeDescriptionAttribute>()?.Description;

            if (modDesc != null && ruleDesc != null)
                return modDesc + "\n\n" + ruleDesc;

            return modDesc ?? ruleDesc;
        }

        private void ShowStylePresetSelector(Rect buttonRect, SerializedProperty listProperty)
        {
            ShowStylePresetSelector(buttonRect, targets, serializedObject,
                (t, style) => ((UniTextBase)t).AddStyle_Editor(style));
        }

        internal static void ShowStylePresetSelector(Rect buttonRect, Object[] targets,
            SerializedObject so, Action<Object, Style> addStyle)
        {
            var presets = GetStylePresetItems();
            for (int i = 0; i < presets.Length; i++)
            {
                var mp = (StylePresetEntry)presets[i].value;
                presets[i].icon = mp.iconName != null ? UniTextEditorResources.GetTintedTexture(mp.iconName) : null;
                presets[i].groupIcon = UniTextEditorResources.GetGroupIcon(presets[i].groupName);
            }
            var capturedTargets = targets;
            Selector.Show(buttonRect, presets, null, value =>
            {
                var preset = (StylePresetEntry)value;
                foreach (var t in capturedTargets)
                {
                    if (t == null) continue;
                    Undo.RecordObject(t, "Add Style");
                    var style = new Style();
                    var mod = preset.createModifier?.Invoke();
                    var rule = preset.createRule?.Invoke();
                    if (mod != null) style.Modifier = mod;
                    if (rule != null) style.Rule = rule;
                    addStyle(t, style);
                    EditorUtility.SetDirty(t);
                }
                so.Update();
            });
        }

        private static GUIStyle presetToggleStyle;
        private static GUIStyle presetToggleActiveStyle;
        private static GUIStyle presetToggleMixedStyle;

        private static void InitPresetToggleStyles()
        {
            if (presetToggleStyle != null && presetToggleStyle.normal.background != null) return;

            presetToggleStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 30,
                padding = new RectOffset(5, 5, 5, 5),
                border = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0)
            };
            var deselTex = MakeTex(new Color(0.3f, 0.3f, 0.38f));
            presetToggleStyle.normal.background = deselTex;
            presetToggleStyle.normal.scaledBackgrounds = null;
            presetToggleStyle.hover.background = deselTex;
            presetToggleStyle.hover.scaledBackgrounds = null;

            presetToggleActiveStyle = new GUIStyle(presetToggleStyle);
            var selTex = MakeTex(new Color(0.29f, 0.59f, 0.32f));
            presetToggleActiveStyle.normal.background = selTex;
            presetToggleActiveStyle.normal.scaledBackgrounds = null;
            presetToggleActiveStyle.hover.background = selTex;
            presetToggleActiveStyle.hover.scaledBackgrounds = null;

            presetToggleMixedStyle = new GUIStyle(presetToggleStyle);
            var mixedTex = MakeTex(new Color(0.38f, 0.48f, 0.35f));
            presetToggleMixedStyle.normal.background = mixedTex;
            presetToggleMixedStyle.normal.scaledBackgrounds = null;
            presetToggleMixedStyle.hover.background = mixedTex;
            presetToggleMixedStyle.hover.scaledBackgrounds = null;
        }

        private void DrawPresetToggles()
        {
            InitPresetToggleStyles();

            var presets = GetStylePresetItems();
            var availWidth = EditorGUIUtility.currentViewWidth - 48;
            bool inRow = false;
            float rowX = 0;
            int lastVisualGroup = -1;

            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                var modPreset = (StylePresetEntry)preset.value;
                if (modPreset.modifierType == null) continue;
                if (!string.IsNullOrEmpty(preset.groupName)) continue;

                const float groupGap = 6f;
                if (lastVisualGroup >= 0 && modPreset.visualGroup != lastVisualGroup && inRow)
                {
                    GUILayout.Space(groupGap);
                    rowX += groupGap;
                }
                lastVisualGroup = modPreset.visualGroup;

                var state = GetPresetState(modPreset);
                var style = state switch
                {
                    PresetState.All => presetToggleActiveStyle,
                    PresetState.Mixed => presetToggleMixedStyle,
                    _ => presetToggleStyle
                };
                var icon = modPreset.iconName != null ? UniTextEditorResources.GetTintedTexture(modPreset.iconName) : null;
                var content = new GUIContent(icon, preset.displayName);
                const float btnSize = 32;

                if (rowX + btnSize > availWidth && rowX > 0)
                {
                    if (inRow) { GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); inRow = false; }
                    rowX = 0;
                }

                if (!inRow)
                {
                    EditorGUILayout.BeginHorizontal();
                    inRow = true;
                }

                if (GUILayout.Button(content, style, GUILayout.Width(30), GUILayout.Height(30)))
                {
                    if (state == PresetState.All)
                        RemovePreset(modPreset);
                    else
                        AddPreset(modPreset);
                }

                rowX += btnSize;
            }

            if (inRow) { GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); }
            EditorGUILayout.Space(4);
        }

        private enum PresetState { None, All, Mixed }

        private PresetState GetPresetState(StylePresetEntry preset)
        {
            int activeCount = 0;
            foreach (var t in targets)
                if (HasPreset((UniTextBase)t, preset))
                    activeCount++;
            if (activeCount == 0) return PresetState.None;
            if (activeCount == targets.Length) return PresetState.All;
            return PresetState.Mixed;
        }

        private static bool HasPreset(UniTextBase ut, StylePresetEntry preset)
        {
            var styles = ut.Styles;
            for (int i = 0; i < styles.Count; i++)
            {
                var s = styles[i];
                if (s?.Modifier != null && s?.Rule != null &&
                    s.Modifier.GetType() == preset.modifierType &&
                    s.Rule.GetType() == preset.ruleType)
                    return true;
            }
            return false;
        }

        private void AddPreset(StylePresetEntry preset)
        {
            foreach (var t in targets)
            {
                var ut = (UniTextBase)t;
                if (HasPreset(ut, preset)) continue;
                Undo.RecordObject(t, "Add Style");
                var style = new Style { Modifier = preset.createModifier(), Rule = preset.createRule() };
                ut.AddStyle(style);
                EditorUtility.SetDirty(t);
            }
            serializedObject.Update();
        }

        private void RemovePreset(StylePresetEntry preset)
        {
            foreach (var t in targets)
            {
                var ut = (UniTextBase)t;
                var styles = ut.Styles;
                for (int i = styles.Count - 1; i >= 0; i--)
                {
                    var s = styles[i];
                    if (s?.Modifier != null && s?.Rule != null &&
                        s.Modifier.GetType() == preset.modifierType &&
                        s.Rule.GetType() == preset.ruleType)
                    {
                        Undo.RecordObject(t, "Remove Style");
                        ut.RemoveStyle(s);
                        EditorUtility.SetDirty(t);
                        break;
                    }
                }
            }
            serializedObject.Update();
        }

        #endregion
    }
}
