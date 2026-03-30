using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace LightSide
{
    [CustomEditor(typeof(UniTextFont))]
    [CanEditMultipleObjects]
    internal class UniTextFontEditor : Editor
    {
        private SerializedProperty fontDataProp;
        private SerializedProperty sourceFontProp;
        private SerializedProperty faceInfoProp;
        private SerializedProperty italicStyleProp;
        private SerializedProperty fontScaleProp;
        private SerializedProperty sdfDetailMultiplierProp;
        private SerializedProperty glyphOverridesProp;

        private bool faceInfoFoldout;
        private bool glyphOverridesFoldout;

        private float pendingSdfDetail;
        private int pendingGlyphOverridesHash;
        private bool pendingInitialized;

        private string glyphPickerText = "";
        private string glyphPickerPrevText;
        private List<GlyphPickerEntry> glyphPickerEntries = new();
        private HashSet<int> glyphPickerSelection = new();
        private Vector2 glyphPickerScroll;

        private struct GlyphPickerEntry
        {
            public int glyphIndex;
            public string label;
            public Texture2D preview;
        }

#if UNITEXT_DEBUG
        private int debugAtlasIndex;
        private int debugSdfSlice;
        private int debugMsdfSlice;
#endif

        private void OnEnable()
        {
            fontDataProp = serializedObject.FindProperty("fontData");
            sourceFontProp = serializedObject.FindProperty("sourceFont");
            faceInfoProp = serializedObject.FindProperty("faceInfo");
            italicStyleProp = serializedObject.FindProperty("italicStyle");
            fontScaleProp = serializedObject.FindProperty("fontScale");
            sdfDetailMultiplierProp = serializedObject.FindProperty("sdfDetailMultiplier");
            glyphOverridesProp = serializedObject.FindProperty("glyphOverrides");

            InitializePendingValues();
        }

        private void InitializePendingValues()
        {
            pendingSdfDetail = sdfDetailMultiplierProp.floatValue;
            pendingGlyphOverridesHash = ComputeGlyphOverridesHash();
            pendingInitialized = true;
        }

        private bool HasPendingChanges()
        {
            if (!pendingInitialized) return false;
            return !Mathf.Approximately(pendingSdfDetail, sdfDetailMultiplierProp.floatValue)
                || pendingGlyphOverridesHash != ComputeGlyphOverridesHash();
        }

        private int ComputeGlyphOverridesHash()
        {
            int hash = glyphOverridesProp.arraySize;
            for (int i = 0; i < glyphOverridesProp.arraySize; i++)
            {
                var element = glyphOverridesProp.GetArrayElementAtIndex(i);
                hash = hash * 31 + element.FindPropertyRelative("glyphIndex").intValue;
                hash = hash * 31 + element.FindPropertyRelative("tileSizeOverride").intValue;
            }
            return hash;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            bool isMultiEdit = targets.Length > 1;

            if (sourceFontProp.objectReferenceValue != null || sourceFontProp.hasMultipleDifferentValues)
            {
                BeginSection("Source Font (Editor Only)");
                DrawSourceFontContent();
                EndSection();
            }

            BeginSection("Font Data Status");
            if (isMultiEdit)
                DrawFontDataStatusMulti();
            else
                DrawFontDataStatusContent((UniTextFont)target);
            EndSection();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(new GUIContent("Face Info"), EditorStyles.foldout, GUILayout.Height(20));
            rect.xMin += 14;
            var boldFoldout = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            faceInfoFoldout = EditorGUI.Foldout(rect, faceInfoFoldout, "Face Info", true, boldFoldout);
            if (faceInfoFoldout)
            {
                DrawFlatProperties(faceInfoProp, "familyName", "styleName", "weightClass", "isItalic");

                using (new EditorGUI.DisabledScope(true))
                {
                    var familyProp = faceInfoProp.FindPropertyRelative("familyName");
                    var styleProp = faceInfoProp.FindPropertyRelative("styleName");
                    var weightProp = faceInfoProp.FindPropertyRelative("weightClass");
                    var italicProp = faceInfoProp.FindPropertyRelative("isItalic");
                    if (familyProp != null) EditorGUILayout.PropertyField(familyProp);
                    if (styleProp != null) EditorGUILayout.PropertyField(styleProp);
                    if (weightProp != null) EditorGUILayout.PropertyField(weightProp);
                    if (italicProp != null) EditorGUILayout.PropertyField(italicProp);
                }

                EditorGUILayout.PropertyField(italicStyleProp);
            }
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            if (!isMultiEdit)
            {
                var fontAsset = (UniTextFont)target;
                if (fontAsset.HasFontData && fontAsset.IsVariable)
                    DrawVariableAxesInfo(fontAsset);
            }

            BeginSection("Settings");
            EditorGUILayout.PropertyField(fontScaleProp, new GUIContent("Font Scale",
                "Visual scale multiplier. Use to normalize fonts that appear too small or too large by design."));
            EditorGUILayout.PropertyField(sdfDetailMultiplierProp, new GUIContent("SDF Detail",
                "SDF tile detail multiplier. Higher values force larger atlas tiles for better quality on fonts with thin strokes (e.g. calligraphic)."));
            if (sdfDetailMultiplierProp.floatValue > 1.01f)
                EditorGUILayout.HelpBox("Values above 1 increase atlas tile sizes, which improves quality but increases rasterization time and atlas memory usage.", MessageType.Info);
            EndSection();

            if (!isMultiEdit)
                DrawGlyphOverridesSection();

            if (HasPendingChanges())
            {
                EditorGUILayout.Space(4);
                UniTextEditor.DrawHelpBox(
                    "Settings changed. Apply to rebuild with new values.",
                    MessageType.Warning);

                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
                if (GUILayout.Button("Apply", GUILayout.Height(25)))
                {
                    serializedObject.ApplyModifiedProperties();
                    foreach (var t in targets)
                    {
                        ((UniTextFont)t).ClearDynamicData();
                        EditorUtility.SetDirty(t);
                    }
                    InitializePendingValues();
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Revert", GUILayout.Height(25), GUILayout.Width(60)))
                {
                    sdfDetailMultiplierProp.floatValue = pendingSdfDetail;
                    serializedObject.ApplyModifiedProperties();
                    InitializePendingValues();
                }
                EditorGUILayout.EndHorizontal();
            }

            BeginSection("Runtime Data");
            if (isMultiEdit)
                DrawDynamicDataMulti();
            else
                DrawDynamicDataContent((UniTextFont)target);
            EndSection();

#if UNITEXT_DEBUG
            if (!isMultiEdit)
            {
                BeginSection("Debug");
                DrawDebugContent((UniTextFont)target);
                EndSection();
            }
#endif

            serializedObject.ApplyModifiedProperties();
            UniTextEditor.DrawLoveLabel();
        }

        private static readonly string[] TileSizeLabels = { "Auto", "64", "128", "256" };
        private static readonly int[] TileSizeValues = { 0, 64, 128, 256 };

        private void DrawGlyphOverridesSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.foldout, GUILayout.Height(20));
            rect.xMin += 14;
            var boldFoldout = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            glyphOverridesFoldout = EditorGUI.Foldout(rect, glyphOverridesFoldout, "Glyph Overrides", true, boldFoldout);

            if (glyphOverridesFoldout)
            {
                DrawGlyphPicker();

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Overrides", EditorStyles.boldLabel);

                if (glyphOverridesProp.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("No glyph overrides. Type text above and select glyphs, or add manually.", MessageType.None);
                }

                for (int i = 0; i < glyphOverridesProp.arraySize; i++)
                {
                    var element = glyphOverridesProp.GetArrayElementAtIndex(i);
                    var glyphIndexProp = element.FindPropertyRelative("glyphIndex");
                    var tileSizeProp = element.FindPropertyRelative("tileSizeOverride");

                    EditorGUILayout.BeginHorizontal();

                    EditorGUILayout.LabelField("Glyph", GUILayout.Width(40));
                    glyphIndexProp.intValue = EditorGUILayout.IntField(glyphIndexProp.intValue, GUILayout.Width(60));

                    EditorGUILayout.LabelField("Tile", GUILayout.Width(28));
                    int currentIdx = System.Array.IndexOf(TileSizeValues, tileSizeProp.intValue);
                    if (currentIdx < 0) currentIdx = 0;
                    int newIdx = EditorGUILayout.Popup(currentIdx, TileSizeLabels, GUILayout.Width(60));
                    tileSizeProp.intValue = TileSizeValues[newIdx];

                    GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
                    if (GUILayout.Button("\u2715", GUILayout.Width(20)))
                    {
                        glyphPickerSelection.Remove(glyphIndexProp.intValue);
                        glyphOverridesProp.DeleteArrayElementAtIndex(i);
                        GUI.backgroundColor = Color.white;
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Add Manual", GUILayout.Width(90)))
                {
                    glyphOverridesProp.InsertArrayElementAtIndex(glyphOverridesProp.arraySize);
                    var newElement = glyphOverridesProp.GetArrayElementAtIndex(glyphOverridesProp.arraySize - 1);
                    newElement.FindPropertyRelative("glyphIndex").intValue = 0;
                    newElement.FindPropertyRelative("tileSizeOverride").intValue = 0;
                }
                if (glyphOverridesProp.arraySize > 0)
                {
                    GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
                    if (GUILayout.Button("Clear All", GUILayout.Width(70)))
                    {
                        glyphOverridesProp.ClearArray();
                        glyphPickerSelection.Clear();
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            if (serializedObject.hasModifiedProperties)
                serializedObject.ApplyModifiedProperties();
        }

        private void DrawGlyphPicker()
        {
            EditorGUILayout.LabelField("Glyph Picker", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Input Text");
            EditorGUI.BeginChangeCheck();
            glyphPickerText = EditorGUILayout.TextArea(glyphPickerText, GUILayout.MinHeight(40));
            if (EditorGUI.EndChangeCheck() || glyphPickerPrevText != glyphPickerText)
            {
                glyphPickerPrevText = glyphPickerText;
                RebuildGlyphPicker();
            }

            if (glyphPickerEntries.Count == 0) return;

            const int cellSize = 72;
            const int previewSize = 48;
            const int padding = 4;
            const int labelHeight = 16;

            var totalWidth = EditorGUIUtility.currentViewWidth - 40;
            var columns = Mathf.Max(1, (int)(totalWidth / (cellSize + padding)));
            var rows = Mathf.CeilToInt((float)glyphPickerEntries.Count / columns);
            var gridHeight = Mathf.Min(rows * (cellSize + padding), 300);

            glyphPickerScroll = EditorGUILayout.BeginScrollView(glyphPickerScroll,
                GUILayout.Height(gridHeight + 8));

            var baseRect = EditorGUILayout.GetControlRect(false,
                rows * (cellSize + padding));

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            };
            var selectedLabelStyle = new GUIStyle(labelStyle)
            {
                normal = { textColor = Color.white },
            };

            for (int i = 0; i < glyphPickerEntries.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                var cellRect = new Rect(
                    baseRect.x + col * (cellSize + padding),
                    baseRect.y + row * (cellSize + padding),
                    cellSize, cellSize);

                var entry = glyphPickerEntries[i];
                bool isSelected = glyphPickerSelection.Contains(entry.glyphIndex);

                if (isSelected)
                    GUI.backgroundColor = new Color(0.3f, 0.55f, 0.9f);

                if (GUI.Button(cellRect, ""))
                {
                    if (isSelected)
                    {
                        glyphPickerSelection.Remove(entry.glyphIndex);
                        RemoveOverride(entry.glyphIndex);
                    }
                    else
                    {
                        glyphPickerSelection.Add(entry.glyphIndex);
                        AddOverride(entry.glyphIndex);
                    }
                }

                if (isSelected)
                    GUI.backgroundColor = Color.white;

                if (entry.preview != null)
                {
                    var previewRect = new Rect(
                        cellRect.x + (cellSize - previewSize) * 0.5f,
                        cellRect.y + (cellSize - labelHeight - previewSize) * 0.5f + 1,
                        previewSize, previewSize);
                    GUI.DrawTexture(previewRect, entry.preview, ScaleMode.ScaleToFit);
                }

                var labelRect = new Rect(cellRect.x, cellRect.yMax - labelHeight, cellRect.width, labelHeight);
                GUI.Label(labelRect, entry.label, isSelected ? selectedLabelStyle : labelStyle);
            }

            EditorGUILayout.EndScrollView();
        }

        private void RebuildGlyphPicker()
        {
            foreach (var entry in glyphPickerEntries)
                if (entry.preview != null) DestroyImmediate(entry.preview);
            glyphPickerEntries.Clear();

            if (string.IsNullOrEmpty(glyphPickerText)) return;

            var font = (UniTextFont)target;
            if (!font.HasFontData) return;

            var codepoints = StringToCodepoints(glyphPickerText);
            var glyphIndices = ShapeToGlyphIndices(font, codepoints);

            var seen = new HashSet<int>();
            var uniqueGlyphs = new List<(int glyphIndex, string label)>();

            int cpIdx = 0;
            for (int i = 0; i < glyphIndices.Count; i++)
            {
                int gid = glyphIndices[i];
                if (gid == 0 || !seen.Add(gid)) { cpIdx++; continue; }

                string label = $"#{gid}";
                uniqueGlyphs.Add((gid, label));
                cpIdx++;
            }

            var fontData = font.FontData;
            var face = FT.LoadFace(fontData, font.FaceInfo.faceIndex);
            if (face == System.IntPtr.Zero) return;

            FT.SetPixelSize(face, 40);

            foreach (var (glyphIndex, label) in uniqueGlyphs)
            {
                Texture2D preview = null;
                if (FT.LoadGlyph(face, (uint)glyphIndex) && FT.RenderGlyph(face))
                {
                    var bitmap = FT.GetBitmapData(face);
                    var top = FT.GetBitmapTop(face);
                    var left = FT.GetBitmapLeft(face);
                    if (bitmap.width > 0 && bitmap.height > 0 && bitmap.buffer != System.IntPtr.Zero)
                        preview = BitmapToFixedTexture(bitmap, left, top, 48);
                }

                glyphPickerEntries.Add(new GlyphPickerEntry
                {
                    glyphIndex = glyphIndex,
                    label = label,
                    preview = preview,
                });
            }

            FT.UnloadFace(face);

            glyphPickerSelection.Clear();
            for (int i = 0; i < glyphOverridesProp.arraySize; i++)
            {
                var gid = glyphOverridesProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("glyphIndex").intValue;
                glyphPickerSelection.Add(gid);
            }
        }

        private static List<int> StringToCodepoints(string text)
        {
            var result = new List<int>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                int cp;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else
                {
                    cp = text[i];
                }
                result.Add(cp);
            }
            return result;
        }

        private static List<int> ShapeToGlyphIndices(UniTextFont font, List<int> codepoints)
        {
            var result = new List<int>(codepoints.Count);
            for (int i = 0; i < codepoints.Count; i++)
            {
                var gid = Shaper.GetGlyphIndex(font, (uint)codepoints[i]);
                result.Add((int)gid);
            }
            return result;
        }

        private static Texture2D BitmapToFixedTexture(FT.BitmapData bitmap, int bitmapLeft, int bitmapTop, int texSize)
        {
            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[texSize * texSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            int bw = bitmap.width;
            int bh = bitmap.height;

            int baseline = texSize * 3 / 4;
            int ox = (texSize - bw) / 2;
            int oy = baseline - bitmapTop;

            unsafe
            {
                byte* src = (byte*)bitmap.buffer;
                for (int y = 0; y < bh; y++)
                {
                    byte* row = src + y * bitmap.pitch;
                    int dstY = oy + y;
                    if (dstY < 0 || dstY >= texSize) continue;
                    int flippedY = texSize - 1 - dstY;
                    for (int x = 0; x < bw; x++)
                    {
                        int dstX = ox + x;
                        if (dstX < 0 || dstX >= texSize) continue;
                        byte a = row[x];
                        pixels[flippedY * texSize + dstX] = new Color32(255, 255, 255, a);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private void AddOverride(int glyphIndex)
        {
            for (int i = 0; i < glyphOverridesProp.arraySize; i++)
            {
                if (glyphOverridesProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("glyphIndex").intValue == glyphIndex)
                    return;
            }

            int idx = glyphOverridesProp.arraySize;
            glyphOverridesProp.InsertArrayElementAtIndex(idx);
            var element = glyphOverridesProp.GetArrayElementAtIndex(idx);
            element.FindPropertyRelative("glyphIndex").intValue = glyphIndex;
            element.FindPropertyRelative("tileSizeOverride").intValue = 0;
        }

        private void RemoveOverride(int glyphIndex)
        {
            for (int i = 0; i < glyphOverridesProp.arraySize; i++)
            {
                if (glyphOverridesProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("glyphIndex").intValue == glyphIndex)
                {
                    glyphOverridesProp.DeleteArrayElementAtIndex(i);
                    return;
                }
            }
        }

        private void OnDisable()
        {
            foreach (var entry in glyphPickerEntries)
                if (entry.preview != null) DestroyImmediate(entry.preview);
            glyphPickerEntries.Clear();
        }

        private void BeginSection(string label)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        private void DrawFlatProperties(SerializedProperty property, params string[] skipProperties)
        {
            var iterator = property.Copy();
            var endProperty = property.GetEndProperty();

            iterator.NextVisible(true);

            while (!SerializedProperty.EqualContents(iterator, endProperty))
            {
                bool skip = false;
                foreach (var skipProp in skipProperties)
                {
                    if (iterator.name == skipProp)
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                    EditorGUILayout.PropertyField(iterator, true);

                if (!iterator.NextVisible(false))
                    break;
            }
        }

        private void DrawSourceFontContent()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(sourceFontProp, new GUIContent("Source Font File"));
            }
        }

        private void DrawFontDataStatusContent(UniTextFont font)
        {
            var hasData = font.HasFontData;
            var statusColor = hasData ? new Color(0.33f, 1f, 0.39f) : new Color(1f, 0.35f, 0.28f);
            var statusText = hasData
                ? $"✓ Font data loaded ({font.FontData.Length:N0} bytes)"
                : "✗ No font data - TEXT WILL NOT RENDER!";

            var statusStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = statusColor } };
            EditorGUILayout.LabelField(statusText, statusStyle);

            if (hasData)
            {
                var sourceFont = sourceFontProp.objectReferenceValue as Font;
                if (sourceFont != null)
                {
                    UniTextEditor.DrawHelpBox(
                        "Font bytes are embedded directly in this asset. " +
                        "The Source Font File reference is editor-only and will NOT be included in the build — " +
                        "no duplicate data, no extra build size.",
                        MessageType.Info);
                }
                else
                {
                    UniTextEditor.DrawHelpBox(
                        "Font bytes are embedded directly in this asset and will be included in the build.",
                        MessageType.Info);
                }
            }
            else
            {
                UniTextEditor.DrawHelpBox(
                    "No font data embedded. To fix this, create a new UniText Font Asset:\n" +
                    "Right-click a Font → Create → UniText → Font Asset",
                    MessageType.Warning);
            }
        }

        private void DrawFontDataStatusMulti()
        {
            int withData = 0;
            foreach (var t in targets)
                if (((UniTextFont)t).HasFontData) withData++;

            Color statusColor;
            string statusText;
            if (withData == targets.Length)
            {
                statusColor = new Color(0.33f, 1f, 0.39f);
                statusText = $"\u2713 All {targets.Length} fonts have data";
            }
            else if (withData == 0)
            {
                statusColor = new Color(1f, 0.35f, 0.28f);
                statusText = "\u2717 No font data on any selected font";
            }
            else
            {
                statusColor = new Color(1f, 0.8f, 0.2f);
                statusText = $"\u26a0 {withData}/{targets.Length} fonts have data";
            }

            var statusStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = statusColor } };
            EditorGUILayout.LabelField(statusText, statusStyle);
        }

        private void DrawVariableAxesInfo(UniTextFont font)
        {
            var axes = font.VariableAxes;
            if (axes == null || axes.Length == 0) return;

            BeginSection("Variable Font Axes");
            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < axes.Length; i++)
                {
                    var axis = axes[i];
                    var tag = System.Text.Encoding.ASCII.GetString(new[]
                    {
                        (byte)(axis.tag >> 24), (byte)(axis.tag >> 16),
                        (byte)(axis.tag >> 8), (byte)axis.tag
                    });
                    EditorGUILayout.LabelField(tag,
                        $"min: {axis.minValue}  default: {axis.defaultValue}  max: {axis.maxValue}");
                }
            }
            EndSection();
        }

        private void DrawDynamicDataContent(UniTextFont font)
        {
            int glyphCount = font.GlyphLookupTable?.Count ?? 0;
            int charCount = font.CharacterLookupTable?.Count ?? 0;

            EditorGUILayout.LabelField("Statistics (Runtime)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Glyphs: {glyphCount}  |  Characters: {charCount}");

            EditorGUILayout.Space(5);

            GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
            if (GUILayout.Button("Clear Runtime Data", GUILayout.Height(25)))
            {
                font.ClearDynamicData();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawDynamicDataMulti()
        {
            int totalGlyphs = 0, totalChars = 0;
            foreach (var t in targets)
            {
                var f = (UniTextFont)t;
                totalGlyphs += f.GlyphLookupTable?.Count ?? 0;
                totalChars += f.CharacterLookupTable?.Count ?? 0;
            }

            EditorGUILayout.LabelField("Statistics (Runtime)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"{targets.Length} fonts  |  Glyphs: {totalGlyphs}  |  Characters: {totalChars}");

            EditorGUILayout.Space(5);

            GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
            if (GUILayout.Button($"Clear Runtime Data ({targets.Length} fonts)", GUILayout.Height(25)))
            {
                foreach (var t in targets)
                    ((UniTextFont)t).ClearDynamicData();
            }
            GUI.backgroundColor = Color.white;
        }

#if UNITEXT_DEBUG
        private void DrawDebugContent(UniTextFont font)
        {
            var emojiAtlas = GlyphAtlas.Emoji;
            if (emojiAtlas == null || emojiAtlas.AtlasTexture == null || emojiAtlas.PageCount == 0)
            {
                EditorGUILayout.LabelField("No atlas textures available.");
                return;
            }

            debugAtlasIndex = EditorGUILayout.IntSlider("Atlas Index", debugAtlasIndex, 0, emojiAtlas.PageCount - 1);

            var tex = emojiAtlas.AtlasTexture;
            if (tex == null)
            {
                EditorGUILayout.LabelField("Texture at this index is null.");
                return;
            }

            EditorGUILayout.LabelField($"Emoji: slice {debugAtlasIndex+1}/{emojiAtlas.PageCount}  {tex.width}x{tex.height}  RGBA32");

            EditorGUILayout.Space(10);

            DrawGlyphAtlasDebug("SDF Atlas", GlyphAtlas.GetInstance(UniText.RenderModee.SDF), false,
                ref debugSdfSlice, font.name);
            DrawGlyphAtlasDebug("MSDF Atlas", GlyphAtlas.GetInstance(UniText.RenderModee.MSDF), true,
                ref debugMsdfSlice, font.name);
        }

        private void DrawGlyphAtlasDebug(string label, GlyphAtlas atlas, bool isMsdf,
            ref int sliceIndex, string fontName)
        {
            if (atlas == null || atlas.AtlasTexture == null || atlas.PageCount == 0)
            {
                EditorGUILayout.LabelField($"{label}: empty");
                return;
            }

            var arr = atlas.AtlasTexture as Texture2DArray;
            if (arr == null) return;

            EditorGUILayout.LabelField($"{label}: {arr.width}x{arr.height}  Pages: {atlas.PageCount}  Format: {arr.format}");
            sliceIndex = EditorGUILayout.IntSlider("Page", sliceIndex, 0, atlas.PageCount - 1);

            if (GUILayout.Button($"Save Page {sliceIndex} as PNG", GUILayout.Height(25)))
            {
                var mode = isMsdf ? "msdf" : "sdf";
                SaveAtlasSliceAsPng(arr, sliceIndex, isMsdf, $"{fontName}_{mode}_page{sliceIndex}.png");
            }
        }

        private static void SaveTexture2DAsPng(Texture2D tex, string defaultName)
        {
            var path = EditorUtility.SaveFilePanel("Save Atlas Texture as PNG", "", defaultName, "png");
            if (string.IsNullOrEmpty(path)) return;

            var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(path, readable.EncodeToPNG());
            DestroyImmediate(readable);
            Debug.Log($"Atlas texture saved to: {path}");
        }

        private static void SaveAtlasSliceAsPng(Texture2DArray arr, int slice, bool isMsdf, string defaultName)
        {
            var path = EditorUtility.SaveFilePanel("Save Atlas Page as PNG", "", defaultName, "png");
            if (string.IsNullOrEmpty(path)) return;

            var mat = GetPreviewMaterial();
            if (mat == null)
            {
                Debug.LogError("AtlasPreview shader not found");
                return;
            }

            mat.SetFloat(prop_SliceIndex, slice);
            mat.SetFloat(prop_Mode, isMsdf ? 1f : 0f);

            var rt = RenderTexture.GetTemporary(arr.width, arr.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(arr, rt, mat);

            var readable = new Texture2D(arr.width, arr.height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readable.ReadPixels(new Rect(0, 0, arr.width, arr.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(path, readable.EncodeToPNG());
            DestroyImmediate(readable);
            Debug.Log($"Atlas page saved to: {path}");
        }
#endif

        #region Preview

        private int previewTab;
        private int previewPageIndex;
        private readonly int[] previewTabIds = new int[3];
        private readonly int[] previewTabPages = new int[3];
        private static Material atlasPreviewMat;
        private static readonly int prop_SliceIndex = Shader.PropertyToID("_SliceIndex");
        private static readonly int prop_Mode = Shader.PropertyToID("_Mode");
        private static readonly int prop_Rendered = Shader.PropertyToID("_Rendered");
        private static bool previewRendered;

        public override bool HasPreviewGUI()
        {
            bool hasAtlas = false;
            GlyphAtlas.ForEachInstance(a => { if (a.PageCount > 0) hasAtlas = true; });
            if (hasAtlas) return true;
            return GlyphAtlas.Emoji?.PageCount > 0;
        }

        public override GUIContent GetPreviewTitle() => new("Atlas Preview");

        public override void OnPreviewSettings()
        {
            var sdfPages = GlyphAtlas.GetInstance(UniText.RenderModee.SDF).PageCount;
            var msdfPages = GlyphAtlas.GetInstance(UniText.RenderModee.MSDF).PageCount;
            var emojiPages = GlyphAtlas.Emoji?.PageCount ?? 0;

            int tabCount = 0;
            var tabIds = previewTabIds;
            var tabPages = previewTabPages;
            if (sdfPages > 0)  { tabIds[tabCount] = 0; tabPages[tabCount] = sdfPages;  tabCount++; }
            if (msdfPages > 0) { tabIds[tabCount] = 1; tabPages[tabCount] = msdfPages; tabCount++; }
            if (emojiPages > 0){ tabIds[tabCount] = 2; tabPages[tabCount] = emojiPages; tabCount++; }

            if (tabCount == 0) return;

            if (tabCount > 1)
            {
                var labels = new string[tabCount];
                int selectedIdx = 0;
                for (int t = 0; t < tabCount; t++)
                {
                    labels[t] = tabIds[t] switch { 0 => "SDF", 1 => "MSDF", _ => "Emoji" };
                    if (tabIds[t] == previewTab) selectedIdx = t;
                }

                var widths = new[] { 90, 120, 150 };
                int newIdx = GUILayout.Toolbar(selectedIdx, labels,
                    EditorStyles.miniButton, GUILayout.Width(widths[tabCount - 1]));
                previewTab = tabIds[newIdx];
            }
            else
            {
                previewTab = tabIds[0];
            }

            int currentTabIdx = 0;
            for (int t = 0; t < tabCount; t++)
                if (tabIds[t] == previewTab) { currentTabIdx = t; break; }

            int pageCount = tabPages[currentTabIdx];
            previewPageIndex = Mathf.Clamp(previewPageIndex, 0, pageCount - 1);

            if (pageCount > 1)
            {
                GUILayout.Label($"{previewPageIndex + 1}/{pageCount}", EditorStyles.miniLabel, GUILayout.Width(35));
                previewPageIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                    previewPageIndex, 0, pageCount - 1, GUILayout.Width(80)));
            }

            if (previewTab < 2)
                previewRendered = GUILayout.Toggle(previewRendered, "Rendered", EditorStyles.miniButton, GUILayout.Width(65));
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            Texture texture;
            string info;
            Material mat = null;

            if (previewTab == 0)
            {
                var atlas = GlyphAtlas.GetInstance(UniText.RenderModee.SDF);
                if (atlas.AtlasTexture == null || atlas.PageCount == 0) return;
                var idx = Mathf.Clamp(previewPageIndex, 0, atlas.PageCount - 1);
                texture = atlas.AtlasTexture;
                info = $"SDF: slice {idx+1}/{atlas.PageCount}  {texture.width}x{texture.height}  RHalf";
                mat = GetPreviewMaterial();
                if (mat != null) { mat.SetFloat(prop_SliceIndex, idx); mat.SetFloat(prop_Mode, 0); }
            }
            else if (previewTab == 1)
            {
                var atlas = GlyphAtlas.GetInstance(UniText.RenderModee.MSDF);
                if (atlas.AtlasTexture == null || atlas.PageCount == 0) return;
                var idx = Mathf.Clamp(previewPageIndex, 0, atlas.PageCount - 1);
                texture = atlas.AtlasTexture;
                info = $"MSDF: slice {idx+1}/{atlas.PageCount}  {texture.width}x{texture.height}  RGBAHalf";
                mat = GetPreviewMaterial();
                if (mat != null) { mat.SetFloat(prop_SliceIndex, idx); mat.SetFloat(prop_Mode, 1); }
            }
            else
            {
                var emojiAtlas = GlyphAtlas.Emoji;
                if (emojiAtlas == null || emojiAtlas.AtlasTexture == null || emojiAtlas.PageCount == 0) return;
                var idx = Mathf.Clamp(previewPageIndex, 0, emojiAtlas.PageCount - 1);
                texture = emojiAtlas.AtlasTexture;
                info = $"Emoji: slice {idx+1}/{emojiAtlas.PageCount}  {texture.width}x{texture.height}  RGBA32";
                mat = GetPreviewMaterial();
                if (mat != null) { mat.SetFloat(prop_SliceIndex, idx); mat.SetFloat(prop_Mode, 2); }
            }

            if (mat != null)
                mat.SetFloat(prop_Rendered, previewRendered && previewTab < 2 ? 1 : 0);

            float texAspect = (float)texture.width / texture.height;
            float rectAspect = r.width / r.height;

            Rect texRect;
            if (texAspect > rectAspect)
            {
                float h = r.width / texAspect;
                texRect = new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h);
            }
            else
            {
                float w = r.height * texAspect;
                texRect = new Rect(r.x + (r.width - w) * 0.5f, r.y, w, r.height);
            }

            EditorGUI.DrawPreviewTexture(texRect, texture, mat, ScaleMode.ScaleToFit);

            var infoRect = new Rect(r.x + 4, r.yMax - 18, r.width - 8, 16);
            EditorGUI.DropShadowLabel(infoRect, info, EditorStyles.miniLabel);
        }

        private static Material GetPreviewMaterial()
        {
            if (atlasPreviewMat == null)
            {
                var shader = Shader.Find("Hidden/UniText/AtlasPreview");
                if (shader != null)
                    atlasPreviewMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return atlasPreviewMat;
        }

        #endregion

        [MenuItem("Assets/Create/UniText/Font Asset", true)]
        private static bool CreateFontAssetValidate()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Font) return true;
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is ".ttf" or ".otf" or ".ttc") return true;
                }
            }
            return false;
        }

        [MenuItem("Assets/Create/UniText/Font Asset", false, 100)]
        private static void CreateFontAsset()
        {
            var created = new List<Object>();

            foreach (var obj in Selection.objects)
            {
                var assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath)) continue;

                bool isFont = obj is Font;
                if (!isFont)
                {
                    var ext = Path.GetExtension(assetPath).ToLowerInvariant();
                    if (ext is not (".ttf" or ".otf" or ".ttc")) continue;
                }

                var fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath)) continue;

                byte[] fontBytes;
                try { fontBytes = File.ReadAllBytes(fullPath); }
                catch { continue; }

                var fontAsset = UniTextFont.CreateFontAsset(fontBytes);
                if (fontAsset == null)
                {
                    Debug.LogError($"Failed to create font asset from {Path.GetFileName(assetPath)}");
                    continue;
                }

                if (obj is Font font)
                    fontAsset.sourceFont = font;

                var dir = Path.GetDirectoryName(assetPath);
                var name = Path.GetFileNameWithoutExtension(assetPath);
                var savePath = Path.Combine(dir, name + ".asset").Replace("\\", "/");
                savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

                AssetDatabase.CreateAsset(fontAsset, savePath);
                created.Add(fontAsset);
            }

            if (created.Count == 0) return;

            AssetDatabase.SaveAssets();
            Selection.objects = created.ToArray();
            EditorGUIUtility.PingObject(created[^1]);
            Debug.Log($"Created {created.Count} UniText Font Asset(s)");
        }

        [MenuItem("Assets/Create/UniText/Font Stack (Combined)", true)]
        private static bool CreateFontsCombinedAssetValidate()
        {
            bool firstFound = false;
            
            foreach (var obj in Selection.objects)
            {
                if (obj is UniTextFont)
                {
                    if (firstFound)
                    {
                        return true;
                    }
                    
                    firstFound = true;
                }
            }
            
            return false;
        }
        
        [MenuItem("Assets/Create/UniText/Font Stack (Per Font)", true)]
        private static bool CreateFontsAssetValidate()
        {
            foreach (var obj in Selection.objects)
                if (obj is UniTextFont) return true;
            return false;
        }

        [MenuItem("Assets/Create/UniText/Font Stack (Combined)", false, 101)]
        private static void CreateFontsCombined()
        {
            var fonts = new List<UniTextFont>();
            foreach (var obj in Selection.objects)
                if (obj is UniTextFont font)
                    fonts.Add(font);

            if (fonts.Count == 0) return;

            var groups = new Dictionary<string, List<UniTextFont>>();
            var groupOrder = new List<string>();
            foreach (var font in fonts)
            {
                var key = font.FaceInfo.familyName ?? "";
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<UniTextFont>();
                    groups[key] = list;
                    groupOrder.Add(key);
                }
                list.Add(font);
            }

            var familyList = new List<FontFamily>();
            foreach (var key in groupOrder)
            {
                var group = groups[key];
                int bestIdx = 0;
                int bestScore = int.MaxValue;
                for (int i = 0; i < group.Count; i++)
                {
                    var fi = group[i].FaceInfo;
                    var w = fi.weightClass > 0 ? fi.weightClass : 400;
                    var score = System.Math.Abs(w - 400) + (fi.isItalic ? 1000 : 0);
                    if (score < bestScore) { bestScore = score; bestIdx = i; }
                }

                var primary = group[bestIdx];
                UniTextFont[] faces = null;
                if (group.Count > 1)
                {
                    faces = new UniTextFont[group.Count - 1];
                    int fi2 = 0;
                    for (int i = 0; i < group.Count; i++)
                        if (i != bestIdx) faces[fi2++] = group[i];
                }

                familyList.Add(new FontFamily { primary = primary, faces = faces });
            }

            var fontsAsset = CreateInstance<UniTextFontStack>();
            fontsAsset.families = familyList.ToArray();

            bool allVariable = true, anyVariable = false;
            foreach (var family in familyList)
            {
                if (family.primary != null && family.primary.IsVariable) anyVariable = true;
                else allVariable = false;
            }
            var suffix = allVariable ? "Variable" : anyVariable ? "Mixed" : "Static";
            var assetName = string.Join("+", groupOrder).Replace(" ", "-") + "-" + suffix;
            var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(fonts[0]));
            var savePath = Path.Combine(dir, assetName + ".asset").Replace("\\", "/");
            savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

            AssetDatabase.CreateAsset(fontsAsset, savePath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = fontsAsset;
            EditorGUIUtility.PingObject(fontsAsset);
        }

        [MenuItem("Assets/Create/UniText/Font Stack (Per Font)", false, 102)]
        private static void CreateFontsPerFont()
        {
            var created = new List<Object>();

            foreach (var obj in Selection.objects)
            {
                if (obj is not UniTextFont font) continue;

                var fontsAsset = CreateInstance<UniTextFontStack>();
                fontsAsset.families = new[] { new FontFamily { primary = font } };

                var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(font));
                var savePath = Path.Combine(dir, font.name + " FontStack.asset").Replace("\\", "/");
                savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

                AssetDatabase.CreateAsset(fontsAsset, savePath);
                created.Add(fontsAsset);
            }

            if (created.Count == 0) return;

            AssetDatabase.SaveAssets();
            Selection.objects = created.ToArray();
            EditorGUIUtility.PingObject(created[^1]);
        }
    }

}
