using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace LightSide
{
    [CustomEditor(typeof(UniTextFont))]
    internal class UniTextFontEditor : Editor
    {
        private SerializedProperty fontDataProp;
        private SerializedProperty sourceFontProp;
        private SerializedProperty sourceFontFilePathProp;
        private SerializedProperty faceInfoProp;
        private SerializedProperty pointSizeProp;
        private SerializedProperty atlasSizeProp;
        private SerializedProperty spreadStrengthProp;
        private SerializedProperty atlasRenderModeProp;
        private SerializedProperty italicStyleProp;
        private SerializedProperty fontScaleProp;

        private int pendingPointSize;
        private int pendingAtlasSize;
        private float pendingSpreadStrength;
        private UniTextRenderMode pendingRenderMode;
        private bool pendingInitialized;

        private bool faceInfoFoldout;

#if UNITEXT_DEBUG
        private int debugAtlasIndex;
#endif

        private void OnEnable()
        {
            fontDataProp = serializedObject.FindProperty("fontData");
            sourceFontProp = serializedObject.FindProperty("sourceFont");
            sourceFontFilePathProp = serializedObject.FindProperty("sourceFontFilePath");
            faceInfoProp = serializedObject.FindProperty("faceInfo");
            pointSizeProp = faceInfoProp.FindPropertyRelative("pointSize");
            atlasSizeProp = serializedObject.FindProperty("atlasSize");
            spreadStrengthProp = serializedObject.FindProperty("spreadStrength");
            atlasRenderModeProp = serializedObject.FindProperty("atlasRenderMode");
            italicStyleProp = serializedObject.FindProperty("italicStyle");
            fontScaleProp = serializedObject.FindProperty("fontScale");

            InitializePendingValues();
        }

        private void InitializePendingValues()
        {
            pendingPointSize = pointSizeProp.propertyType == SerializedPropertyType.Float
                ? (int)pointSizeProp.floatValue
                : pointSizeProp.intValue;
            pendingAtlasSize = atlasSizeProp.intValue;
            pendingSpreadStrength = spreadStrengthProp.floatValue;
            pendingRenderMode = (UniTextRenderMode)atlasRenderModeProp.intValue;
            pendingInitialized = true;
        }

        private bool HasPendingChanges()
        {
            if (!pendingInitialized) return false;
            int currentPointSize = pointSizeProp.propertyType == SerializedPropertyType.Float
                ? (int)pointSizeProp.floatValue
                : pointSizeProp.intValue;
            return pendingPointSize != currentPointSize ||
                   pendingAtlasSize != atlasSizeProp.intValue ||
                   !Mathf.Approximately(pendingSpreadStrength, spreadStrengthProp.floatValue) ||
                   (int)pendingRenderMode != atlasRenderModeProp.intValue;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var fontAsset = (UniTextFont)target;

            if (sourceFontProp.objectReferenceValue != null)
            {
                BeginSection("Source Font (Editor Only)");
                DrawSourceFontContent(fontAsset);
                EndSection();
            }

            BeginSection("Font Data Status");
            DrawFontDataStatusContent(fontAsset);
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
                DrawFlatProperties(faceInfoProp, "m_PointSize");
                EditorGUILayout.PropertyField(italicStyleProp);
                EditorGUILayout.PropertyField(fontScaleProp, new GUIContent("Font Scale",
                    "Visual scale multiplier. Use to normalize fonts that appear too small or too large by design."));
            }
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            BeginSection("Atlas Settings");
            DrawAtlasSettings(fontAsset);
            EndSection();

            BeginSection("Runtime Data");
            DrawDynamicDataContent(fontAsset);
            EndSection();

#if UNITEXT_DEBUG
            BeginSection("Debug");
            DrawDebugContent(fontAsset);
            EndSection();
#endif

            serializedObject.ApplyModifiedProperties();
            UniTextEditor.DrawLoveLabel();
        }

        private void DrawAtlasSettings(UniTextFont fontAsset)
        {
            EditorGUI.BeginChangeCheck();

            pendingPointSize = EditorGUILayout.IntField("Sampling Point Size", pendingPointSize);
            pendingPointSize = Mathf.Clamp(pendingPointSize, 8, 256);

            pendingAtlasSize = EditorGUILayout.IntPopup("Atlas Size", pendingAtlasSize,
                new[] { "256", "512", "1024", "2048", "4096" },
                new[] { 256, 512, 1024, 2048, 4096 });

            pendingSpreadStrength = EditorGUILayout.Slider("Spread Strength", pendingSpreadStrength, 0.1f, 1f);
            int computedPadding = Mathf.Max(1, Mathf.RoundToInt(pendingPointSize * pendingSpreadStrength));
            EditorGUILayout.LabelField("  Atlas Padding", $"{computedPadding} px", EditorStyles.miniLabel);

            pendingRenderMode = (UniTextRenderMode)EditorGUILayout.EnumPopup("Render Mode", pendingRenderMode);

            if (HasPendingChanges())
            {
                EditorGUILayout.Space(5);
                UniTextEditor.DrawHelpBox(
                    "Atlas settings changed. Apply to rebuild the atlas with new settings.",
                    MessageType.Warning);

                EditorGUILayout.BeginHorizontal();

                GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
                if (GUILayout.Button("Apply", GUILayout.Height(25)))
                {
                    ApplyAtlasSettings(fontAsset);
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("Revert", GUILayout.Height(25), GUILayout.Width(60)))
                {
                    InitializePendingValues();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void ApplyAtlasSettings(UniTextFont fontAsset)
        {
            Undo.RecordObject(fontAsset, "Apply Atlas Settings");

            if (pointSizeProp.propertyType == SerializedPropertyType.Float)
                pointSizeProp.floatValue = pendingPointSize;
            else
                pointSizeProp.intValue = pendingPointSize;
            atlasSizeProp.intValue = pendingAtlasSize;
            spreadStrengthProp.floatValue = pendingSpreadStrength;
            atlasRenderModeProp.intValue = (int)pendingRenderMode;

            serializedObject.ApplyModifiedProperties();

            fontAsset.ClearDynamicData();
            EditorUtility.SetDirty(fontAsset);
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

        private void DrawSourceFontContent(UniTextFont uniTextFont)
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

        private void DrawDynamicDataContent(UniTextFont font)
        {
            int glyphCount = font.GlyphLookupTable?.Count ?? 0;
            int charCount = font.CharacterLookupTable?.Count ?? 0;
            int atlasCount = font.AtlasTextures?.Count ?? 0;

            EditorGUILayout.LabelField("Statistics (Runtime)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Glyphs: {glyphCount}  |  Characters: {charCount}  |  Atlas textures: {atlasCount}");

            if (font.AtlasTexture != null)
            {
                var tex = font.AtlasTexture;
                long sizeBytes = tex.width * tex.height;
                if (tex.format == TextureFormat.RGBA32)
                    sizeBytes *= 4;
                string sizeStr = sizeBytes > 1024 * 1024
                    ? $"{sizeBytes / (1024f * 1024f):F1} MB"
                    : $"{sizeBytes / 1024f:F1} KB";
                EditorGUILayout.LabelField($"Atlas size: {tex.width}x{tex.height} ({sizeStr})");
            }

            EditorGUILayout.Space(5);

            GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
            if (GUILayout.Button("Clear Runtime Data", GUILayout.Height(25)))
            {
                font.ClearDynamicData();
            }
            GUI.backgroundColor = Color.white;
        }

#if UNITEXT_DEBUG
        private void DrawDebugContent(UniTextFont font)
        {
            var textures = font.AtlasTextures;
            if (textures == null || textures.Count == 0)
            {
                EditorGUILayout.LabelField("No atlas textures available.");
                return;
            }

            debugAtlasIndex = EditorGUILayout.IntSlider("Atlas Index", debugAtlasIndex, 0, textures.Count - 1);

            var tex = textures[debugAtlasIndex];
            if (tex == null)
            {
                EditorGUILayout.LabelField("Texture at this index is null.");
                return;
            }

            EditorGUILayout.LabelField($"Texture: {tex.width}x{tex.height}  Format: {tex.format}");

            if (GUILayout.Button("Save as PNG", GUILayout.Height(25)))
            {
                var path = EditorUtility.SaveFilePanel(
                    "Save Atlas Texture as PNG",
                    "",
                    $"{font.name}_atlas_{debugAtlasIndex}.png",
                    "png");

                if (!string.IsNullOrEmpty(path))
                {
                    var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(tex, rt);

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    readable.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);

                    var pngBytes = readable.EncodeToPNG();
                    DestroyImmediate(readable);

                    File.WriteAllBytes(path, pngBytes);
                    Debug.Log($"Atlas texture saved to: {path}");
                }
            }
        }
#endif

        #region Preview

        private int previewAtlasIndex;
        private bool previewShowEmoji;

        public override bool HasPreviewGUI()
        {
            var font = (UniTextFont)target;
            bool hasFont = font.AtlasTextures != null && font.AtlasTextures.Count > 0;
            bool hasEmoji = EmojiFont.Instance != null &&
                            EmojiFont.Instance.AtlasTextures != null &&
                            EmojiFont.Instance.AtlasTextures.Count > 0;
            return hasFont || hasEmoji;
        }

        public override GUIContent GetPreviewTitle()
        {
            if (previewShowEmoji)
            {
                int count = EmojiFont.Instance?.AtlasTextures?.Count ?? 0;
                return new GUIContent(count > 1 ? $"Emoji Atlas ({count})" : "Emoji Atlas");
            }

            var font = (UniTextFont)target;
            int fontCount = font.AtlasTextures?.Count ?? 0;
            return new GUIContent(fontCount > 1 ? $"Atlas ({fontCount})" : "Atlas");
        }

        public override void OnPreviewSettings()
        {
            var font = (UniTextFont)target;
            var emoji = EmojiFont.Instance;

            bool hasFont = font.AtlasTextures != null && font.AtlasTextures.Count > 0;
            bool hasEmoji = emoji != null && emoji.AtlasTextures != null && emoji.AtlasTextures.Count > 0;

            if (hasFont && hasEmoji)
            {
                if (GUILayout.Toggle(!previewShowEmoji, "Font", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
                    previewShowEmoji = false;
                if (GUILayout.Toggle(previewShowEmoji, "Emoji", EditorStyles.miniButtonRight, GUILayout.Width(45)))
                    previewShowEmoji = true;
            }
            else if (hasEmoji && !hasFont)
            {
                previewShowEmoji = true;
            }

            var textures = previewShowEmoji ? emoji?.AtlasTextures : font.AtlasTextures;
            if (textures != null && textures.Count > 1)
            {
                GUILayout.Space(8);
                GUILayout.Label($"{previewAtlasIndex + 1}/{textures.Count}", EditorStyles.miniLabel, GUILayout.Width(35));
                previewAtlasIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(previewAtlasIndex, 0, textures.Count - 1, GUILayout.Width(80)));
            }
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            var font = (UniTextFont)target;
            var emoji = EmojiFont.Instance;

            var textures = previewShowEmoji ? emoji?.AtlasTextures : font.AtlasTextures;
            if (textures == null || textures.Count == 0)
                return;

            previewAtlasIndex = Mathf.Clamp(previewAtlasIndex, 0, textures.Count - 1);
            var texture = textures[previewAtlasIndex];
            if (texture == null)
                return;

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

            if (texture.format == TextureFormat.Alpha8)
                EditorGUI.DrawTextureAlpha(texRect, texture, ScaleMode.ScaleToFit);
            else
                EditorGUI.DrawPreviewTexture(texRect, texture, null, ScaleMode.ScaleToFit);

            string info = $"{texture.width}x{texture.height} {texture.format}";
            if (previewShowEmoji)
                info = $"EmojiFont: {info}";

            var infoRect = new Rect(r.x + 4, r.yMax - 18, r.width - 8, 16);
            EditorGUI.DropShadowLabel(infoRect, info, EditorStyles.miniLabel);
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
            var created = new List<UnityEngine.Object>();

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

            var fontsAsset = ScriptableObject.CreateInstance<UniTextFontStack>();
            foreach (var font in fonts)
                fontsAsset.fonts.items.Add(font);

            var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(fonts[0]));
            var savePath = Path.Combine(dir, "New UniTextFontStack Combined.asset").Replace("\\", "/");
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

                var fontsAsset = ScriptableObject.CreateInstance<UniTextFontStack>();
                fontsAsset.fonts.items.Add(font);

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
