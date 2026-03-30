using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal class UniTextToolsWindow : EditorWindow
    {
        private enum Tab
        {
            CreateAsset,
            Subsetter,
            DictionaryBuilder
        }

        [MenuItem("Tools/UniText Tools")]
        public static void ShowWindow()
        {
            var window = GetWindow<UniTextToolsWindow>("UniText Tools");
            window.minSize = new Vector2(450, 550);
            window.wantsMouseMove = true;
        }

        #region Common

        private Tab currentTab = Tab.CreateAsset;
        private static readonly string[] tabLabels = { "Create Font Asset", "Font Subsetter", "Dictionary Builder" };
        private static readonly string[] modeLabels = { "Remove", "Keep" };
        private GUIStyle boxStyle;
        private GUIStyle headerStyle;

        private void OnGUI()
        {
            InitStyles();
            HandleFontPickerResult();

            EditorGUILayout.Space(8);

            currentTab = (Tab)GUILayout.Toolbar((int)currentTab, tabLabels, GUILayout.Height(25));

            EditorGUILayout.Space(8);

            switch (currentTab)
            {
                case Tab.CreateAsset:
                    DrawCreateAssetTab();
                    break;
                case Tab.Subsetter:
                    DrawSubsetterTab();
                    break;
                case Tab.DictionaryBuilder:
                    DrawDictionaryBuilderTab();
                    break;
            }

            UniTextEditor.DrawLoveLabel();
        }

        private GUIStyle richLabelStyle;

        private GUIStyle tableLabelStyle;
        private GUIStyle tableLabelOnStyle;
        private Color cellBgEven, cellBgOdd, cellBgHover, cellBgOn, cellBgOnHover, cellBorder;

        private void InitStyles()
        {
            boxStyle ??= new GUIStyle("helpBox") { padding = new RectOffset(10, 10, 8, 8) };
            headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            richLabelStyle ??= new GUIStyle(EditorStyles.label) { richText = true };

            bool pro = EditorGUIUtility.isProSkin;
            cellBgEven    = pro ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.84f, 0.84f, 0.84f);
            cellBgOdd     = pro ? new Color(0.26f, 0.26f, 0.26f) : new Color(0.86f, 0.86f, 0.86f);
            cellBgHover   = pro ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.74f, 0.74f, 0.74f);
            cellBgOn      = pro ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.45f, 0.65f, 0.88f);
            cellBgOnHover = pro ? new Color(0.20f, 0.40f, 0.58f) : new Color(0.50f, 0.70f, 0.92f);
            cellBorder    = pro ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.60f, 0.60f, 0.60f);

            if (tableLabelStyle == null)
            {
                tableLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                };
                tableLabelOnStyle = new GUIStyle(tableLabelStyle)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white },
                };
            }
        }

        private static void CopyAllCharacters(byte[] fontData)
        {
            var codepoints = FontSubsetter.GetCodepoints(fontData);
            if (codepoints == null || codepoints.Length == 0)
            {
                Debug.LogWarning("FontTools: No codepoints found in font.");
                return;
            }

            var sb = new StringBuilder(codepoints.Length);
            for (int i = 0; i < codepoints.Length; i++)
            {
                var cp = (int)codepoints[i];
                if (cp >= 32 && cp != 127 && cp <= 0x10FFFF)
                    sb.Append(char.ConvertFromUtf32(cp));
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log($"Copied <b>{codepoints.Length}</b> codepoints to clipboard.");
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024f * 1024f):F2} MB",
            >= 1024 => $"{bytes / 1024f:F1} KB",
            _ => $"{bytes} bytes"
        };

        [NonSerialized] private GUIStyle fileRowStyle;
        [NonSerialized] private GUIStyle fileSizeStyle;
        [NonSerialized] private GUIStyle fileRemoveStyle;
        [NonSerialized] private GUIStyle fileListBgStyle;
        [NonSerialized] private GUIStyle fileCountStyle;
        [NonSerialized] private GUIContent fileIcon;
        [NonSerialized] private GUIContent removeIcon;

        private void InitFileListStyles()
        {
            if (fileRowStyle != null) return;

            fileIcon = EditorGUIUtility.IconContent("d_TextAsset Icon");
            if (fileIcon == null || fileIcon.image == null)
                fileIcon = EditorGUIUtility.IconContent("TextAsset Icon");
            removeIcon = EditorGUIUtility.IconContent("d_TreeEditor.Trash");
            if (removeIcon == null || removeIcon.image == null)
                removeIcon = EditorGUIUtility.IconContent("TreeEditor.Trash");

            fileListBgStyle = new GUIStyle("RL Background");

            bool pro = EditorGUIUtility.isProSkin;

            fileRowStyle = new GUIStyle(EditorStyles.label);
            fileRowStyle.padding = new RectOffset(0, 4, 0, 0);
            fileRowStyle.margin = new RectOffset(0, 0, 0, 0);
            fileRowStyle.fixedHeight = 0f;
            fileRowStyle.alignment = TextAnchor.MiddleLeft;
            fileRowStyle.normal.textColor = pro
                ? new Color(0.82f, 0.82f, 0.82f)
                : new Color(0.1f, 0.1f, 0.1f);

            fileSizeStyle = new GUIStyle(EditorStyles.miniLabel);
            fileSizeStyle.alignment = TextAnchor.MiddleRight;
            fileSizeStyle.normal.textColor = pro
                ? new Color(0.55f, 0.55f, 0.55f)
                : new Color(0.45f, 0.45f, 0.45f);

            fileRemoveStyle = new GUIStyle(GUIStyle.none);
            fileRemoveStyle.alignment = TextAnchor.MiddleCenter;
            fileRemoveStyle.padding = new RectOffset(0, 0, 0, 0);

            fileCountStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            fileCountStyle.padding = new RectOffset(0, 0, 2, 2);
        }

        private struct FileRowInfo
        {
            public string name;
            public long size;
            public bool pinned;
        }

        private int DrawFileList(ref Vector2 scrollPos, int count, Func<int, FileRowInfo> getRow, float maxHeight = 200f)
        {
            InitFileListStyles();

            const float rowHeight = 24f;
            const float iconSize = 16f;
            int removeIndex = -1;

            float listHeight = Mathf.Min(count * rowHeight, maxHeight);
            var listRect = EditorGUILayout.GetControlRect(false, listHeight + 4);

            if (Event.current.type == EventType.Repaint)
                fileListBgStyle.Draw(listRect, false, false, false, false);

            var innerRect = new Rect(listRect.x + 2, listRect.y + 2, listRect.width - 4, listRect.height - 4);
            var viewRect = new Rect(0, 0, innerRect.width - (count * rowHeight > maxHeight ? 13f : 0f), count * rowHeight);

            scrollPos = GUI.BeginScrollView(innerRect, scrollPos, viewRect);

            for (int i = 0; i < count; i++)
            {
                var row = getRow(i);
                var rowRect = new Rect(0, i * rowHeight, viewRect.width, rowHeight);

                if (Event.current.type == EventType.Repaint)
                {
                    var bgColor = i % 2 == 0 ? cellBgEven : cellBgOdd;
                    EditorGUI.DrawRect(rowRect, bgColor);
                }

                if (rowRect.Contains(Event.current.mousePosition))
                {
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.04f));
                }

                var iconRect = new Rect(rowRect.x + 6, rowRect.y + (rowHeight - iconSize) / 2, iconSize, iconSize);
                EditorGUI.LabelField(iconRect, fileIcon);

                float rightEdge = rowRect.xMax;
                if (!row.pinned)
                {
                    var btnRect = new Rect(rightEdge - 26, rowRect.y + (rowHeight - 18) / 2, 18, 18);
                    if (GUI.Button(btnRect, removeIcon, fileRemoveStyle))
                        removeIndex = i;
                    rightEdge = btnRect.x - 2;
                }

                var sizeText = FormatSize(row.size);
                var sizeWidth = Mathf.Max(50f, fileSizeStyle.CalcSize(new GUIContent(sizeText)).x + 4);
                var sizeRect = new Rect(rightEdge - sizeWidth, rowRect.y, sizeWidth, rowHeight);
                EditorGUI.LabelField(sizeRect, sizeText, fileSizeStyle);

                var nameRect = new Rect(iconRect.xMax + 4, rowRect.y, sizeRect.x - iconRect.xMax - 8, rowHeight);
                EditorGUI.LabelField(nameRect, row.name, fileRowStyle);
            }

            GUI.EndScrollView();

            if (Event.current.type == EventType.Repaint)
            {
                var b = cellBorder;
                EditorGUI.DrawRect(new Rect(listRect.x, listRect.y, listRect.width, 1), b);
                EditorGUI.DrawRect(new Rect(listRect.x, listRect.yMax - 1, listRect.width, 1), b);
                EditorGUI.DrawRect(new Rect(listRect.x, listRect.y, 1, listRect.height), b);
                EditorGUI.DrawRect(new Rect(listRect.xMax - 1, listRect.y, 1, listRect.height), b);
            }

            EditorGUILayout.LabelField($"{count} file(s)", fileCountStyle);

            return removeIndex;
        }

        private static bool TryLoadFontFromPath(string path, out byte[] bytes, out string name, out long size)
        {
            bytes = null;
            name = null;
            size = 0;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".ttf" or ".otf" or ".ttc"))
                return false;

            try
            {
                bytes = File.ReadAllBytes(path);
                name = Path.GetFileName(path);
                size = bytes.Length;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load font: {e.Message}");
                return false;
            }
        }

        private static bool IsValidFontData(byte[] data)
        {
            if (data == null || data.Length < 12)
                return false;

            uint magic = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
            return magic is
                0x00010000 or 0x74727565 or 0x4F54544F or 0x74746366;
        }

        private static UniTextFont CreateAndSaveAsset(byte[] fontBytes, string assetPath)
        {
            var fontAsset = UniTextFont.CreateFontAsset(fontBytes);
            if (fontAsset == null)
                return null;

            AssetDatabase.CreateAsset(fontAsset, assetPath);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        #endregion

        #region Font Source

        private class FontSource
        {
            public UnityEngine.Object asset;
            public string path = "";
            public byte[] bytes;
            public string name;
            public long size;

            public bool HasData => bytes != null && bytes.Length > 0;

            public void LoadFromAsset()
            {
                bytes = null;
                name = null;
                size = 0;

                if (asset == null) return;

                if (asset is UniTextFont uniFont && uniFont.HasFontData)
                {
                    bytes = uniFont.FontData;
                    name = uniFont.name;
                    size = bytes.Length;
                    return;
                }

                var assetPath = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(assetPath))
                    TryLoadFontFromPath(Path.GetFullPath(assetPath), out bytes, out name, out size);
            }

            public void LoadFromPath()
            {
                TryLoadFontFromPath(path, out bytes, out name, out size);
            }
        }

        private readonly FontSource subsetSource = new();

        private int fontPickerControlId;
        private FontSource fontPickerSource;

        private const string PrefCreateSave = "UniText_CreateAsset_SaveDir";
        private const string PrefSubsetBrowse = "UniText_Subsetter_BrowseDir";
        private const string PrefSubsetSave = "UniText_Subsetter_SaveDir";

        private static string GetPrefDir(string key) => EditorPrefs.GetString(key, "");

        private static void SavePrefDir(string key, string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
                EditorPrefs.SetString(key, Path.GetDirectoryName(filePath));
        }

        private void HandleFontPickerResult()
        {
            var cmd = Event.current.commandName;
            if (string.IsNullOrEmpty(cmd) || fontPickerSource == null) return;
            if (EditorGUIUtility.GetObjectPickerControlID() != fontPickerControlId) return;

            if (cmd == "ObjectSelectorClosed")
            {
                var picked = EditorGUIUtility.GetObjectPickerObject();
                if (picked != fontPickerSource.asset)
                {
                    if (picked == null || IsValidFontObject(picked))
                    {
                        fontPickerSource.asset = picked;
                        fontPickerSource.path = "";
                        fontPickerSource.LoadFromAsset();
                    }
                }
                fontPickerSource = null;
                Repaint();
            }
        }

        private void DrawFontAssetField(FontSource source)
        {
            var rect = EditorGUILayout.GetControlRect(true);
            var fieldRect = new Rect(
                rect.x + EditorGUIUtility.labelWidth, rect.y,
                rect.width - EditorGUIUtility.labelWidth, rect.height);

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                var pickerBtnRect = new Rect(fieldRect.xMax - 19, fieldRect.y, 19, fieldRect.height);
                if (pickerBtnRect.Contains(evt.mousePosition))
                {
                    fontPickerControlId = source.GetHashCode();
                    fontPickerSource = source;
                    EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(
                        source.asset, false, "t:Font t:UniTextFont", fontPickerControlId);
                    evt.Use();
                }
            }

            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUI.ObjectField(rect, "Font Asset", source.asset, typeof(UnityEngine.Object), false);
            if (EditorGUI.EndChangeCheck() && newObj != source.asset)
            {
                if (newObj == null || IsValidFontObject(newObj))
                {
                    source.asset = newObj;
                    source.path = "";
                    source.LoadFromAsset();
                }
            }
        }

        private static bool IsValidFontObject(UnityEngine.Object obj)
        {
            if (obj is UniTextFont || obj is Font) return true;
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".ttf" or ".otf" or ".ttc";
        }

        private void DrawFontSourceSection(FontSource source, string prefBrowseKey)
        {
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Source Font", headerStyle);
            EditorGUILayout.Space(4);

            DrawFontAssetField(source);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("\u2014 or \u2014", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            source.path = EditorGUILayout.TextField("File Path", source.path);
            if (EditorGUI.EndChangeCheck())
            {
                source.asset = null;
                source.LoadFromPath();
            }
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                string filePath = EditorUtility.OpenFilePanel("Select Font File", GetPrefDir(prefBrowseKey), "ttf,otf,ttc");
                if (!string.IsNullOrEmpty(filePath))
                {
                    SavePrefDir(prefBrowseKey, filePath);
                    source.path = filePath;
                    source.asset = null;
                    source.LoadFromPath();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            if (source.HasData)
            {
                UniTextEditor.DrawHelpBox($"{source.name}\n{FormatSize(source.size)}", MessageType.Info);

                if (GUILayout.Button("Copy All Characters"))
                    CopyAllCharacters(source.bytes);
            }
            else if (!string.IsNullOrEmpty(source.path) || source.asset != null)
            {
                UniTextEditor.DrawHelpBox("Failed to load font file.", MessageType.Error);
            }
            else
            {
                UniTextEditor.DrawHelpBox(
                    "Drag a Font (.ttf/.otf) or UniText Font asset, or select a file from disk.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Create Asset Tab

        private readonly List<BatchEntry> batchEntries = new();
        private Vector2 batchScrollPos;
        private bool isCreating;
        private const string PrefBrowseDir = "UniText_CreateAsset_BrowseDir";

        private class BatchEntry
        {
            public string name;
            public long size;
            public byte[] bytes;
            public string assetPath;
            public Font sourceFont;
            public bool fromSelection;
        }

        private void OnSelectionChange()
        {
            if (isCreating || currentTab != Tab.CreateAsset) return;

            for (int i = batchEntries.Count - 1; i >= 0; i--)
                if (batchEntries[i].fromSelection)
                    batchEntries.RemoveAt(i);

            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                    TryAddFont(path, obj is Font f ? f : null, true);
            }

            Repaint();
        }

        private void DrawCreateAssetTab()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Source Fonts", headerStyle);
            EditorGUILayout.Space(4);

            DrawDropArea();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse Files..."))
                EditorApplication.delayCall += BrowseFiles;
            GUI.enabled = batchEntries.Count > 0;
            if (GUILayout.Button("Clear"))
            {
                batchEntries.Clear();
                Selection.objects = Array.Empty<UnityEngine.Object>();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (batchEntries.Count > 0)
            {
                var ri = DrawFileList(ref batchScrollPos, batchEntries.Count, i =>
                    new FileRowInfo { name = batchEntries[i].name, size = batchEntries[i].size, pinned = batchEntries[i].fromSelection });
                if (ri >= 0) batchEntries.RemoveAt(ri);
            }
            else
            {
                UniTextEditor.DrawHelpBox(
                    "Drag font files (.ttf/.otf/.ttc) here, select fonts in the Project window, " +
                    "or use \"Browse Files\" to add from disk.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Create Assets", headerStyle);
            EditorGUILayout.Space(4);

            GUI.enabled = batchEntries.Count > 0;
            if (GUILayout.Button($"Create {batchEntries.Count} UniText Font Asset(s)", GUILayout.Height(30)))
                CreateBatchAssets();
            GUI.enabled = true;

            EditorGUILayout.Space(4);
            UniTextEditor.DrawHelpBox(
                "Project fonts: asset saved next to the source file.\n" +
                "External fonts: you will be asked for an output folder.\n" +
                "Font bytes are embedded directly \u2014 no external file dependency.",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawDropArea()
        {
            var rect = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic
            };
            GUI.Box(rect, "Drag font files here (.ttf / .otf / .ttc)", style);

            var evt = Event.current;
            if (evt.type == EventType.DragUpdated && rect.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform && rect.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();

                if (DragAndDrop.objectReferences != null)
                {
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj == null) continue;
                        var path = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(path))
                            TryAddFont(path, obj is Font f ? f : null, false);
                    }
                }

                if (DragAndDrop.paths != null)
                {
                    foreach (var path in DragAndDrop.paths)
                    {
                        if (Path.IsPathRooted(path))
                            TryAddFont(path, null, false);
                    }
                }

                evt.Use();
                Repaint();
            }
        }

        private void BrowseFiles()
        {
            var paths = NativeFileDialog.OpenFiles("Select Font Files", "ttf,otf,ttc", GetPrefDir(PrefBrowseDir));
            if (paths == null || paths.Length == 0) return;

            SavePrefDir(PrefBrowseDir, paths[0]);

            foreach (var path in paths)
                TryAddFont(path, null, false);

            Repaint();
        }

        private void TryAddFont(string path, Font sourceFont, bool fromSelection)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".ttf" or ".otf" or ".ttc")) return;

            var fullPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            if (!File.Exists(fullPath)) return;

            var name = Path.GetFileName(path);
            for (int i = 0; i < batchEntries.Count; i++)
                if (batchEntries[i].name == name) return;

            byte[] bytes;
            try { bytes = File.ReadAllBytes(fullPath); }
            catch { return; }

            if (!IsValidFontData(bytes)) return;

            batchEntries.Add(new BatchEntry
            {
                name = name,
                size = bytes.Length,
                bytes = bytes,
                assetPath = !Path.IsPathRooted(path) ? path : null,
                sourceFont = sourceFont,
                fromSelection = fromSelection
            });
        }

        private void CreateBatchAssets()
        {
            if (batchEntries.Count == 0) return;

            isCreating = true;
            try
            {
                string externalFolder = null;
                bool hasExternal = false;
                for (int i = 0; i < batchEntries.Count; i++)
                    if (batchEntries[i].assetPath == null) { hasExternal = true; break; }

                if (hasExternal)
                {
                    externalFolder = EditorUtility.SaveFolderPanel(
                        "Save External Font Assets To",
                        GetPrefDir(PrefCreateSave).Length > 0 ? GetPrefDir(PrefCreateSave) : Application.dataPath,
                        "");
                    if (string.IsNullOrEmpty(externalFolder)) return;
                    if (!externalFolder.StartsWith(Application.dataPath))
                    {
                        Debug.LogError("UniText Font Assets must be saved inside the Assets folder.");
                        return;
                    }
                    externalFolder = "Assets" + externalFolder.Substring(Application.dataPath.Length);
                    SavePrefDir(PrefCreateSave, externalFolder + Path.DirectorySeparatorChar);
                }

                var created = new List<UnityEngine.Object>();

                for (int i = 0; i < batchEntries.Count; i++)
                {
                    var entry = batchEntries[i];
                    var baseName = Path.GetFileNameWithoutExtension(entry.name) + ".asset";

                    string savePath;
                    if (entry.assetPath != null)
                    {
                        var dir = Path.GetDirectoryName(entry.assetPath);
                        savePath = Path.Combine(dir, baseName).Replace("\\", "/");
                    }
                    else
                    {
                        savePath = Path.Combine(externalFolder, baseName).Replace("\\", "/");
                    }

                    savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

                    var fontAsset = UniTextFont.CreateFontAsset(entry.bytes);
                    if (fontAsset == null)
                    {
                        Debug.LogError($"Failed to create font asset from {entry.name}");
                        continue;
                    }

                    if (entry.sourceFont != null)
                        fontAsset.sourceFont = entry.sourceFont;

                    AssetDatabase.CreateAsset(fontAsset, savePath);
                    created.Add(fontAsset);
                }

                if (created.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                    Selection.objects = created.ToArray();
                    EditorGUIUtility.PingObject(created[^1]);
                    Debug.Log($"Created {created.Count} UniText Font Asset(s)");
                    batchEntries.Clear();
                }
            }
            finally
            {
                isCreating = false;
            }
        }

        #endregion

        #region Subsetter Tab

        private enum SubsetMode { Remove, Keep }

        [Flags]
        private enum CharacterSet
        {
            None = 0,

            BasicLatin      = 1 << 0,
            LatinExtended   = 1 << 1,
            Vietnamese      = 1 << 2,

            Cyrillic        = 1 << 3,
            Greek           = 1 << 4,
            Arabic          = 1 << 5,
            Hebrew          = 1 << 6,
            Thai            = 1 << 7,
            Hiragana        = 1 << 8,
            Katakana        = 1 << 9,

            Digits          = 1 << 10,
            Punctuation     = 1 << 11,
            Currency        = 1 << 12,
            Math            = 1 << 13,
            Arrows          = 1 << 14,
            BoxDrawing      = 1 << 15,

            Devanagari      = 1 << 16,
            Bengali         = 1 << 17,
            Tamil           = 1 << 18,
            Telugu          = 1 << 19,
            Kannada         = 1 << 20,
            Malayalam       = 1 << 21,
            Gujarati        = 1 << 22,
            Gurmukhi        = 1 << 23,
            Sinhala         = 1 << 24,
            Myanmar         = 1 << 25,
            Khmer           = 1 << 26,
            Lao             = 1 << 27,
            Georgian        = 1 << 28,
            Armenian        = 1 << 29,
            Tibetan         = 1 << 30,
        }

        private SubsetMode subsetMode;
        private string subsetInputText = "";
        private Vector2 subsetTextScrollPos;
        private CharacterSet selectedSets;

        private HashSet<int> collectedCodepoints = new();
        private int removeCodepointCount;
        private bool previewDirty = true;

        private void DrawSubsetterTab()
        {
            DrawFontSourceSection(subsetSource, PrefSubsetBrowse);
            EditorGUILayout.Space(4);
            DrawCharactersSection();
            EditorGUILayout.Space(4);
            DrawPreviewSection();
            EditorGUILayout.Space(4);
            DrawSubsetOutputSection();
        }

        private void DrawCharactersSection()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField(
                subsetMode == SubsetMode.Keep ? "Characters to Keep" : "Characters to Remove",
                headerStyle);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            subsetMode = (SubsetMode)GUILayout.Toolbar((int)subsetMode, modeLabels, GUILayout.Height(22));
            if (EditorGUI.EndChangeCheck()) previewDirty = true;

            EditorGUILayout.Space(2);
            if (subsetMode == SubsetMode.Keep)
                UniTextEditor.DrawHelpBox("Only selected characters will be kept in the subset font.", MessageType.None);
            else
                UniTextEditor.DrawHelpBox(
                    "Selected scripts and characters will be removed from the font.\n" +
                    "Composite characters (emoji sequences) are removed as glyphs \u2014 their component characters are preserved.",
                    MessageType.None);
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(
                subsetMode == SubsetMode.Keep ? "Custom Text" : "Custom Text (remove these too)");
            EditorGUI.BeginChangeCheck();
            subsetTextScrollPos = EditorGUILayout.BeginScrollView(subsetTextScrollPos, GUILayout.Height(60));
            subsetInputText = EditorGUILayout.TextArea(subsetInputText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (EditorGUI.EndChangeCheck()) previewDirty = true;

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            DrawScriptRangesTable();
        }

        private int scriptTableRow;
        private const int MaxColumns = 4;
        private const float GroupLabelWidth = 70f;
        private const float RowHeight = 26f;

        private static readonly (string label, CharacterSet[] sets)[] scriptTableRows =
        {
            ("Latin",    new[] { CharacterSet.BasicLatin, CharacterSet.LatinExtended, CharacterSet.Vietnamese }),
            ("European", new[] { CharacterSet.Cyrillic, CharacterSet.Greek, CharacterSet.Armenian, CharacterSet.Georgian }),
            ("Semitic",  new[] { CharacterSet.Arabic, CharacterSet.Hebrew }),
            ("N. Indic", new[] { CharacterSet.Devanagari, CharacterSet.Bengali, CharacterSet.Gujarati, CharacterSet.Gurmukhi }),
            ("S. Indic", new[] { CharacterSet.Tamil, CharacterSet.Telugu, CharacterSet.Kannada, CharacterSet.Malayalam }),
            ("SE Asian", new[] { CharacterSet.Thai, CharacterSet.Lao, CharacterSet.Myanmar, CharacterSet.Khmer }),
            ("E. Asian", new[] { CharacterSet.Hiragana, CharacterSet.Katakana }),
            ("Other",    new[] { CharacterSet.Sinhala, CharacterSet.Tibetan }),
            ("Symbols",  new[] { CharacterSet.Digits, CharacterSet.Punctuation, CharacterSet.Currency, CharacterSet.Math }),
            ("Symbols",  new[] { CharacterSet.Arrows, CharacterSet.BoxDrawing }),
        };

        private void DrawScriptRangesTable()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Script Ranges", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select All", EditorStyles.miniButtonLeft, GUILayout.Width(70)))
            {
                selectedSets = (CharacterSet)~0;
                previewDirty = true;
            }
            if (GUILayout.Button("Deselect All", EditorStyles.miniButtonRight, GUILayout.Width(80)))
            {
                selectedSets = CharacterSet.None;
                previewDirty = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);

            int rowCount = scriptTableRows.Length;
            float tableHeight = rowCount * RowHeight + 1 + rowCount;
            var tableRect = EditorGUILayout.GetControlRect(false, tableHeight);

            float sepX = tableRect.x + GroupLabelWidth;
            float cellsX = sepX + 1;
            float cellsTotalWidth = tableRect.xMax - cellsX;
            float cellWidth = cellsTotalWidth / MaxColumns;

            float y = tableRect.y + 1;
            for (int row = 0; row < rowCount; row++)
            {
                var (label, sets) = scriptTableRows[row];

                EditorGUI.DrawRect(
                    new Rect(tableRect.x + 1, y, tableRect.width - 2, RowHeight),
                    row % 2 == 0 ? cellBgEven : cellBgOdd);

                GUI.Label(new Rect(tableRect.x + 2, y, GroupLabelWidth - 2, RowHeight), label, EditorStyles.boldLabel);

                for (int i = 0; i < MaxColumns; i++)
                {
                    float cx = cellsX + i * cellWidth + 1;
                    var cellRect = new Rect(cx, y, cellWidth - 1, RowHeight);

                    if (i < sets.Length)
                    {
                        var set = sets[i];
                        bool isOn = (selectedSets & set) != 0;
                        bool hovered = cellRect.Contains(Event.current.mousePosition);

                        if (isOn)
                            EditorGUI.DrawRect(cellRect, hovered ? cellBgOnHover : cellBgOn);
                        else if (hovered)
                            EditorGUI.DrawRect(cellRect, cellBgHover);

                        GUI.Label(cellRect, FormatSetName(set), isOn ? tableLabelOnStyle : tableLabelStyle);

                        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                            cellRect.Contains(Event.current.mousePosition))
                        {
                            selectedSets = isOn ? selectedSets & ~set : selectedSets | set;
                            previewDirty = true;
                            Event.current.Use();
                        }

                        if (hovered)
                            EditorGUIUtility.AddCursorRect(cellRect, MouseCursor.Link);
                    }
                }

                y += RowHeight + 1;
            }

            EditorGUI.DrawRect(new Rect(tableRect.x, tableRect.y, tableRect.width, 1), cellBorder);
            EditorGUI.DrawRect(new Rect(tableRect.x, tableRect.y, 1, tableHeight), cellBorder);
            EditorGUI.DrawRect(new Rect(tableRect.xMax - 1, tableRect.y, 1, tableHeight), cellBorder);
            EditorGUI.DrawRect(new Rect(sepX, tableRect.y, 1, tableHeight), cellBorder);
            for (int i = 1; i < MaxColumns; i++)
                EditorGUI.DrawRect(new Rect(cellsX + i * cellWidth, tableRect.y, 1, tableHeight), cellBorder);
            y = tableRect.y + 1;
            for (int row = 0; row < rowCount; row++)
            {
                y += RowHeight;
                EditorGUI.DrawRect(new Rect(tableRect.x, y, tableRect.width, 1), cellBorder);
                y += 1;
            }

            EditorGUILayout.EndVertical();
        }

        private static string FormatSetName(CharacterSet set) => set switch
        {
            CharacterSet.BasicLatin => "Basic",
            CharacterSet.LatinExtended => "Extended",
            CharacterSet.Vietnamese => "Vietnamese",
            CharacterSet.BoxDrawing => "Box Drawing",
            _ => set.ToString()
        };

        private void DrawPreviewSection()
        {
            if (previewDirty)
            {
                CollectCodepoints();
                previewDirty = false;
            }

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Preview", headerStyle);
            EditorGUILayout.Space(4);

            if (subsetMode == SubsetMode.Keep)
            {
                int count = collectedCodepoints.Count;
                EditorGUILayout.LabelField($"Characters to keep: <b>{count:N0}</b>", richLabelStyle);

                if (count > 0 && count <= 200)
                {
                    var sb = new StringBuilder();
                    foreach (var cp in collectedCodepoints.OrderBy(x => x))
                        if (cp >= 32 && cp != 127)
                            sb.Append(char.ConvertFromUtf32(cp));

                    EditorGUILayout.LabelField(sb.ToString(), EditorStyles.wordWrappedLabel);
                }
                else if (count > 200)
                {
                    EditorGUILayout.LabelField("(too many to display)");
                }
            }
            else
            {
                if (removeCodepointCount > 0)
                    EditorGUILayout.LabelField($"Codepoints to remove: <b>{removeCodepointCount:N0}</b>", richLabelStyle);

                int compositionCount = 0;
                var clusters = ParseTextIntoClusters(subsetInputText);
                for (int i = 0; i < clusters.Count; i++)
                    if (clusters[i].Length > 1) compositionCount++;

                if (compositionCount > 0)
                    EditorGUILayout.LabelField($"Compositions to remove: <b>{compositionCount}</b>", richLabelStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSubsetOutputSection()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Output", headerStyle);
            EditorGUILayout.Space(4);

            bool hasInput = subsetMode == SubsetMode.Keep
                ? collectedCodepoints.Count > 0
                : selectedSets != CharacterSet.None || subsetInputText.Length > 0;
            GUI.enabled = subsetSource.HasData && hasInput;

            if (GUILayout.Button("Create Subset Font...", GUILayout.Height(30)))
                CreateSubset();

            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        #region Codepoint Collection

        private void CollectCodepoints()
        {
            collectedCodepoints.Clear();

            if (subsetMode == SubsetMode.Keep)
            {
                ParseCustomTextAsCodepoints(subsetInputText, collectedCodepoints);
                AddSelectedRanges(collectedCodepoints);
            }
            else
            {
                var removedCodepoints = new HashSet<int>();
                AddSelectedRanges(removedCodepoints);
                ParseCustomTextAsCodepoints(subsetInputText, removedCodepoints);
                removeCodepointCount = removedCodepoints.Count;
            }
        }

        private static void ParseCustomTextAsCodepoints(string text, HashSet<int> target)
        {
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
                target.Add(cp);
            }
        }

        /// <summary>
        /// Parses text into grapheme clusters using GraphemeBreaker.
        /// Each cluster is an int[] of codepoints.
        /// </summary>
        private static List<int[]> ParseTextIntoClusters(string text)
        {
            var result = new List<int[]>();
            if (string.IsNullOrEmpty(text))
                return result;

            var codepoints = new List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codepoints.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                    i++;
                }
                else
                {
                    codepoints.Add(text[i]);
                }
            }

            if (codepoints.Count == 0)
                return result;

            UnicodeData.EnsureInitialized();
            var provider = UnicodeData.Provider;

            if (provider != null)
            {
                var breaker = new GraphemeBreaker(provider);
                var cpArray = codepoints.ToArray();
                var breaks = new bool[cpArray.Length + 1];
                breaker.GetBreakOpportunities(cpArray, breaks);

                int clusterStart = 0;
                for (int i = 1; i <= cpArray.Length; i++)
                {
                    if (breaks[i])
                    {
                        int len = i - clusterStart;
                        var cluster = new int[len];
                        Array.Copy(cpArray, clusterStart, cluster, 0, len);
                        result.Add(cluster);
                        clusterStart = i;
                    }
                }
            }
            else
            {
                for (int i = 0; i < codepoints.Count; i++)
                    result.Add(new[] { codepoints[i] });
            }

            return result;
        }

        private void AddSelectedRanges(HashSet<int> target)
        {
            if (Has(CharacterSet.BasicLatin))     AddRangeTo(target, 0x0020, 0x007E);
            if (Has(CharacterSet.LatinExtended))  { AddRangeTo(target, 0x00A0, 0x00FF); AddRangeTo(target, 0x0100, 0x017F); AddRangeTo(target, 0x0180, 0x024F); }
            if (Has(CharacterSet.Vietnamese))     { AddRangeTo(target, 0x1EA0, 0x1EF9); AddRangeTo(target, 0x0300, 0x0303); AddRangeTo(target, 0x0306, 0x0323); }

            if (Has(CharacterSet.Cyrillic))       AddRangeTo(target, 0x0400, 0x04FF);
            if (Has(CharacterSet.Greek))          AddRangeTo(target, 0x0370, 0x03FF);
            if (Has(CharacterSet.Armenian))       AddRangeTo(target, 0x0530, 0x058F);
            if (Has(CharacterSet.Georgian))       { AddRangeTo(target, 0x10A0, 0x10FF); AddRangeTo(target, 0x2D00, 0x2D2F); AddRangeTo(target, 0x1C90, 0x1CBF); }

            if (Has(CharacterSet.Arabic))         { AddRangeTo(target, 0x0600, 0x06FF); AddRangeTo(target, 0x0750, 0x077F); }
            if (Has(CharacterSet.Hebrew))         AddRangeTo(target, 0x0590, 0x05FF);

            if (Has(CharacterSet.Devanagari))     { AddRangeTo(target, 0x0900, 0x097F); AddRangeTo(target, 0xA8E0, 0xA8FF); AddRangeTo(target, 0x1CD0, 0x1CFF); }
            if (Has(CharacterSet.Bengali))        AddRangeTo(target, 0x0980, 0x09FF);
            if (Has(CharacterSet.Gujarati))       AddRangeTo(target, 0x0A80, 0x0AFF);
            if (Has(CharacterSet.Gurmukhi))       AddRangeTo(target, 0x0A00, 0x0A7F);
            if (Has(CharacterSet.Tamil))          AddRangeTo(target, 0x0B80, 0x0BFF);
            if (Has(CharacterSet.Telugu))         AddRangeTo(target, 0x0C00, 0x0C7F);
            if (Has(CharacterSet.Kannada))        AddRangeTo(target, 0x0C80, 0x0CFF);
            if (Has(CharacterSet.Malayalam))       AddRangeTo(target, 0x0D00, 0x0D7F);
            if (Has(CharacterSet.Sinhala))        AddRangeTo(target, 0x0D80, 0x0DFF);

            if (Has(CharacterSet.Thai))           AddRangeTo(target, 0x0E00, 0x0E7F);
            if (Has(CharacterSet.Lao))            AddRangeTo(target, 0x0E80, 0x0EFF);
            if (Has(CharacterSet.Myanmar))        { AddRangeTo(target, 0x1000, 0x109F); AddRangeTo(target, 0xAA60, 0xAA7F); AddRangeTo(target, 0xA9E0, 0xA9FF); }
            if (Has(CharacterSet.Khmer))          { AddRangeTo(target, 0x1780, 0x17FF); AddRangeTo(target, 0x19E0, 0x19FF); }

            if (Has(CharacterSet.Hiragana))       AddRangeTo(target, 0x3040, 0x309F);
            if (Has(CharacterSet.Katakana))       { AddRangeTo(target, 0x30A0, 0x30FF); AddRangeTo(target, 0x31F0, 0x31FF); }

            if (Has(CharacterSet.Tibetan))        AddRangeTo(target, 0x0F00, 0x0FFF);

            if (Has(CharacterSet.Digits))         AddRangeTo(target, 0x0030, 0x0039);
            if (Has(CharacterSet.Punctuation))    { AddRangeTo(target, 0x0021, 0x002F); AddRangeTo(target, 0x003A, 0x0040); AddRangeTo(target, 0x005B, 0x0060); AddRangeTo(target, 0x007B, 0x007E); AddRangeTo(target, 0x2000, 0x206F); }
            if (Has(CharacterSet.Currency))       { AddRangeTo(target, 0x20A0, 0x20CF); AddCodepointsTo(target, 0x24, 0xA2, 0xA3, 0xA4, 0xA5); }
            if (Has(CharacterSet.Math))           { AddRangeTo(target, 0x2200, 0x22FF); AddRangeTo(target, 0x2070, 0x209F); AddCodepointsTo(target, 0xB1, 0xD7, 0xF7); }
            if (Has(CharacterSet.Arrows))         { AddRangeTo(target, 0x2190, 0x21FF); AddRangeTo(target, 0x27F0, 0x27FF); }
            if (Has(CharacterSet.BoxDrawing))     { AddRangeTo(target, 0x2500, 0x257F); AddRangeTo(target, 0x2580, 0x259F); }
        }

        private bool Has(CharacterSet set) => (selectedSets & set) != 0;

        private static void AddRangeTo(HashSet<int> set, int start, int end)
        {
            for (int i = start; i <= end; i++)
                set.Add(i);
        }

        private static void AddCodepointsTo(HashSet<int> set, params int[] cps)
        {
            foreach (int cp in cps)
                set.Add(cp);
        }

        #endregion

        #region Subset Creation

        private void CreateSubset()
        {
            bool hasInput = subsetMode == SubsetMode.Keep
                ? collectedCodepoints.Count > 0
                : selectedSets != CharacterSet.None || subsetInputText.Length > 0;
            if (!subsetSource.HasData || !hasInput)
                return;

            string defaultName = string.IsNullOrEmpty(subsetSource.name)
                ? "subset.ttf"
                : Path.GetFileNameWithoutExtension(subsetSource.name) + "_subset.ttf";

            var subsetSaveDir = GetPrefDir(PrefSubsetSave);
            if (string.IsNullOrEmpty(subsetSaveDir))
                subsetSaveDir = Application.dataPath;

            string savePath = EditorUtility.SaveFilePanel("Save Subset Font", subsetSaveDir, defaultName, "ttf");
            if (string.IsNullOrEmpty(savePath))
                return;

            SavePrefDir(PrefSubsetSave, savePath);

            try
            {
                byte[] subsetBytes;
                if (subsetMode == SubsetMode.Keep)
                    subsetBytes = CreateKeepSubset();
                else
                    subsetBytes = CreateRemoveSubset();

                if (subsetBytes == null || subsetBytes.Length == 0)
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create subset font.", "OK");
                    return;
                }

                File.WriteAllBytes(savePath, subsetBytes);

                long reduction = 100 - (subsetBytes.Length * 100 / subsetSource.size);
                Debug.Log($"Subset font created!\n" +
                          $"Original: <b>{FormatSize(subsetSource.size)}</b>\n" +
                          $"Subset: <b>{FormatSize(subsetBytes.Length)}</b>\n" +
                          $"Reduction: <b>{reduction}%</b>\n" +
                          $"Path: {savePath}");

                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create subset: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Keep mode: single-pass codepoint-based subset.
        /// GSUB closure automatically includes all needed composed glyphs.
        /// </summary>
        private byte[] CreateKeepSubset()
        {
            return FontSubsetter.Subset(subsetSource.bytes, collectedCodepoints.ToList());
        }

        /// <summary>
        /// Remove mode: two-pass subset.
        /// Pass 1: Remove codepoints (scripts + non-composition custom text) with GSUB closure.
        /// Pass 2: Remove composition glyphs (unique to multi-codepoint clusters) without GSUB closure.
        /// Classification uses shape-comparison: shape(cluster) vs shape(each codepoint individually).
        /// Glyphs unique to the cluster = composition. No hardcoded character lists.
        /// </summary>
        private byte[] CreateRemoveSubset()
        {
            var fontData = subsetSource.bytes;

            var codepointsToRemove = new HashSet<int>();
            AddSelectedRanges(codepointsToRemove);

            var compositionClusters = new List<int[]>();
            var clusters = ParseTextIntoClusters(subsetInputText);

            foreach (var cluster in clusters)
                ClassifyCluster(fontData, cluster, codepointsToRemove, compositionClusters);

            if (codepointsToRemove.Count > 0)
            {
                fontData = FontSubsetter.RemoveCodepoints(fontData, codepointsToRemove);
                if (fontData == null || fontData.Length == 0)
                    return null;
            }

            if (compositionClusters.Count > 0)
            {
                var glyphsToRemove = new HashSet<uint>();

                foreach (var cluster in compositionClusters)
                {
                    var unique = FindCompositionGlyphs(fontData, cluster);
                    if (unique != null)
                        glyphsToRemove.UnionWith(unique);
                }

                if (glyphsToRemove.Count > 0)
                {
                    var arr = new uint[glyphsToRemove.Count];
                    glyphsToRemove.CopyTo(arr);

                    fontData = FontSubsetter.RemoveGlyphs(fontData, arr);
                    if (fontData == null || fontData.Length == 0)
                        return null;
                }
            }

            return fontData;
        }

        /// <summary>
        /// Classifies a grapheme cluster by comparing shape(cluster) vs shape(each codepoint).
        /// If the cluster produces glyphs not found in any individual codepoint — it's a composition.
        /// Otherwise, the visible codepoints go to codepoint removal.
        /// </summary>
        private static void ClassifyCluster(byte[] fontData, int[] cluster,
            HashSet<int> codepointsToRemove, List<int[]> compositionClusters)
        {
            var clusterGlyphs = ShapeToGlyphSet(fontData, cluster);

            var componentGlyphs = new HashSet<uint>();
            var visibleCodepoints = new List<int>();

            for (int i = 0; i < cluster.Length; i++)
            {
                var gs = FontSubsetter.ShapeText(fontData, new[] { cluster[i] });
                if (gs == null) continue;
                bool visible = false;
                for (int j = 0; j < gs.Length; j++)
                {
                    if (gs[j] != 0) { componentGlyphs.Add(gs[j]); visible = true; }
                }
                if (visible) visibleCodepoints.Add(cluster[i]);
            }

            bool isComposition = false;
            foreach (var g in clusterGlyphs)
            {
                if (!componentGlyphs.Contains(g)) { isComposition = true; break; }
            }

            if (isComposition)
                compositionClusters.Add(cluster);
            else
                for (int i = 0; i < visibleCodepoints.Count; i++)
                    codepointsToRemove.Add(visibleCodepoints[i]);
        }

        /// <summary>
        /// Finds glyphs unique to a composition (present in shaped cluster but not in individual codepoints).
        /// </summary>
        private static HashSet<uint> FindCompositionGlyphs(byte[] fontData, int[] cluster)
        {
            var clusterGlyphs = ShapeToGlyphSet(fontData, cluster);

            var componentGlyphs = new HashSet<uint>();
            for (int i = 0; i < cluster.Length; i++)
            {
                var gs = FontSubsetter.ShapeText(fontData, new[] { cluster[i] });
                if (gs != null)
                    for (int j = 0; j < gs.Length; j++)
                        if (gs[j] != 0) componentGlyphs.Add(gs[j]);
            }

            clusterGlyphs.ExceptWith(componentGlyphs);
            return clusterGlyphs.Count > 0 ? clusterGlyphs : null;
        }

        private static HashSet<uint> ShapeToGlyphSet(byte[] fontData, int[] codepoints)
        {
            var result = new HashSet<uint>();
            var gs = FontSubsetter.ShapeText(fontData, codepoints);
            if (gs != null)
                for (int i = 0; i < gs.Length; i++)
                    if (gs[i] != 0) result.Add(gs[i]);
            return result;
        }


        #endregion

        #endregion

        #region Dictionary Builder Tab

        private struct DictFileEntry
        {
            public string name;
            public string fullPath;
            public long size;
        }

        private const string PrefDictBrowseDir = "UniText_DictBuilder_BrowseDir";
        private const string PrefDictSaveDir = "UniText_DictBuilder_SaveDir";
        private UnicodeScript dictScript = UnicodeScript.Thai;
        private readonly List<DictFileEntry> dictFiles = new();
        private Vector2 dictScrollPos;
        private string dictStatus;

        private void DrawDictionaryBuilderTab()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Word Lists", headerStyle);
            EditorGUILayout.Space(4);

            DrawDictDropArea();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse Files..."))
                EditorApplication.delayCall += BrowseDictFiles;
            GUI.enabled = dictFiles.Count > 0;
            if (GUILayout.Button("Clear"))
                dictFiles.Clear();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (dictFiles.Count > 0)
            {
                var ri = DrawFileList(ref dictScrollPos, dictFiles.Count, i =>
                    new FileRowInfo { name = dictFiles[i].name, size = dictFiles[i].size });
                if (ri >= 0) dictFiles.RemoveAt(ri);
            }
            else
            {
                UniTextEditor.DrawHelpBox(
                    "Drag .txt word list files here, or use \"Browse Files\" to add from disk.\n" +
                    "Each file should contain one word per line (UTF-8). Lines starting with # are skipped.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Build Dictionary", headerStyle);
            EditorGUILayout.Space(4);

            var scriptRect = EditorGUILayout.GetControlRect();
            var scriptLabel = new GUIContent("Target Script");
            var scriptLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.LabelField(new Rect(scriptRect.x, scriptRect.y, scriptLabelWidth, scriptRect.height), scriptLabel);
            var scriptButtonRect = new Rect(scriptRect.x + scriptLabelWidth + 2, scriptRect.y,
                scriptRect.width - scriptLabelWidth - 2, scriptRect.height);
            if (EditorGUI.DropdownButton(scriptButtonRect, new GUIContent(ObjectNames.NicifyVariableName(dictScript.ToString())), FocusType.Keyboard))
            {
                Selector.ShowForEnum(scriptButtonRect, dictScript, v => { dictScript = v; Repaint(); });
            }

            EditorGUILayout.Space(4);

            GUI.enabled = dictFiles.Count > 0;
            if (GUILayout.Button($"Build Dictionary Asset", GUILayout.Height(30)))
                BuildDictionaryAsset();
            GUI.enabled = true;

            EditorGUILayout.Space(4);
            UniTextEditor.DrawHelpBox(
                "Reads all word lists, builds a double-array trie, and saves a\n" +
                "WordSegmentationDictionary asset. Add it to UniText Settings to enable.",
                MessageType.None);

            EditorGUILayout.EndVertical();

            if (!string.IsNullOrEmpty(dictStatus))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(dictStatus, MessageType.Info);
            }
        }

        private void DrawDictDropArea()
        {
            var rect = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic
            };
            GUI.Box(rect, "Drag word list files here (.txt)", style);

            var evt = Event.current;
            if (evt.type == EventType.DragUpdated && rect.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform && rect.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();

                if (DragAndDrop.objectReferences != null)
                {
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj == null) continue;
                        var path = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(path))
                            TryAddDictFile(Path.GetFullPath(path));
                    }
                }

                if (DragAndDrop.paths != null)
                {
                    foreach (var path in DragAndDrop.paths)
                    {
                        if (Path.IsPathRooted(path))
                            TryAddDictFile(path);
                    }
                }

                evt.Use();
                Repaint();
            }
        }

        private void BrowseDictFiles()
        {
            var paths = NativeFileDialog.OpenFiles("Select Word List Files", "txt", GetPrefDir(PrefDictBrowseDir));
            if (paths == null || paths.Length == 0) return;

            SavePrefDir(PrefDictBrowseDir, paths[0]);

            foreach (var path in paths)
                TryAddDictFile(path);

            Repaint();
        }

        private void TryAddDictFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".txt") return;

            var fullPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            if (!File.Exists(fullPath)) return;

            for (int i = 0; i < dictFiles.Count; i++)
                if (dictFiles[i].fullPath == fullPath) return;

            var name = Path.GetFileName(fullPath);

            dictFiles.Add(new DictFileEntry
            {
                name = name,
                fullPath = fullPath,
                size = new FileInfo(fullPath).Length
            });
        }

        private void BuildDictionaryAsset()
        {
            try
            {
                var words = new HashSet<string>();

                foreach (var entry in dictFiles)
                {
                    if (!File.Exists(entry.fullPath)) continue;
                    foreach (var line in File.ReadLines(entry.fullPath, Encoding.UTF8))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length > 0 && trimmed[0] != '#')
                            words.Add(trimmed);
                    }
                }

                if (words.Count == 0)
                {
                    EditorUtility.DisplayDialog("Error", "No words found in the provided word lists.", "OK");
                    return;
                }

                var wordCount = words.Count;

                int cpMin = int.MaxValue, cpMax = int.MinValue;
                foreach (var word in words)
                {
                    foreach (var cp in EnumerateCodepoints(word))
                    {
                        if (cp < cpMin) cpMin = cp;
                        if (cp > cpMax) cpMax = cp;
                    }
                }

                var codepointBase = cpMin;
                var codepointRange = cpMax - cpMin + 1;

                var trieData = BuildDoubleArrayTrie(words, codepointBase, codepointRange);

                var dictSaveDir = GetPrefDir(PrefDictSaveDir);
                var savePath = EditorUtility.SaveFilePanelInProject(
                    "Save Dictionary Asset",
                    $"{dictScript}Dictionary",
                    "asset",
                    "Choose where to save the dictionary asset.",
                    dictSaveDir.Length > 0 ? dictSaveDir : "Assets");

                if (string.IsNullOrEmpty(savePath)) return;
                SavePrefDir(PrefDictSaveDir, savePath);

                var existing = AssetDatabase.LoadAssetAtPath<WordSegmentationDictionary>(savePath);
                if (existing != null)
                {
                    existing.script = dictScript;
                    existing.trieData = trieData;
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    var asset = CreateInstance<WordSegmentationDictionary>();
                    asset.script = dictScript;
                    asset.trieData = trieData;
                    AssetDatabase.CreateAsset(asset, savePath);
                }

                AssetDatabase.SaveAssets();

                dictStatus = $"Built successfully: {wordCount:N0} words, " +
                             $"{trieData.Length:N0} bytes, script: {dictScript}";

                Debug.Log($"UniText Dictionary Builder: {dictStatus}\nPath: {savePath}");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to build dictionary: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Builds a double-array trie from a set of words.
        /// Returns the binary data in the format expected by <see cref="DoubleArrayTrie.Load"/>.
        /// </summary>
        private static byte[] BuildDoubleArrayTrie(HashSet<string> words, int codepointBase, int codepointRange)
        {
            var nodes = new List<int[]> { new int[codepointRange] };
            var isEnd = new List<bool> { false };

            var root = nodes[0];
            for (var i = 0; i < codepointRange; i++) root[i] = -1;

            foreach (var word in words)
            {
                var current = 0;
                foreach (var cp in EnumerateCodepoints(word))
                {
                    var idx = cp - codepointBase;
                    var next = nodes[current][idx];
                    if (next < 0)
                    {
                        next = nodes.Count;
                        nodes[current][idx] = next;
                        var newNode = new int[codepointRange];
                        for (var j = 0; j < codepointRange; j++) newNode[j] = -1;
                        nodes.Add(newNode);
                        isEnd.Add(false);
                    }
                    current = next;
                }
                isEnd[current] = true;
            }

            var trieNodeCount = nodes.Count;

            var capacity = trieNodeCount * codepointRange + codepointRange + 1;
            capacity = Math.Max(capacity, 256);
            var baseArr = new int[capacity];
            var checkArr = new int[capacity];
            var used = new bool[capacity];
            var stateCount = 1;

            for (var i = 0; i < capacity; i++) checkArr[i] = -1;

            var nodeToState = new int[trieNodeCount];
            nodeToState[0] = 0;
            used[0] = true;

            var queue = new Queue<int>();
            queue.Enqueue(0);

            var childIndices = new List<int>();
            var nextFreeBase = 1;

            while (queue.Count > 0)
            {
                var nodeIdx = queue.Dequeue();
                var stateIdx = nodeToState[nodeIdx];
                var children = nodes[nodeIdx];

                childIndices.Clear();
                for (var c = 0; c < codepointRange; c++)
                    if (children[c] >= 0)
                        childIndices.Add(c);

                if (childIndices.Count == 0)
                {
                    baseArr[stateIdx] = isEnd[nodeIdx] ? ~0 : 0;
                    continue;
                }

                var baseValue = FindBase(used, childIndices, capacity, ref nextFreeBase);

                var maxNeeded = baseValue + codepointRange;
                if (maxNeeded >= capacity)
                {
                    var newCap = Math.Max(capacity * 2, maxNeeded + 1);
                    Array.Resize(ref baseArr, newCap);
                    Array.Resize(ref checkArr, newCap);
                    Array.Resize(ref used, newCap);
                    for (var i = capacity; i < newCap; i++) checkArr[i] = -1;
                    capacity = newCap;
                }

                baseArr[stateIdx] = isEnd[nodeIdx] ? ~baseValue : baseValue;

                foreach (var c in childIndices)
                {
                    var pos = baseValue + c;
                    checkArr[pos] = stateIdx;
                    used[pos] = true;

                    var childNode = children[c];
                    nodeToState[childNode] = pos;
                    if (pos >= stateCount) stateCount = pos + 1;

                    queue.Enqueue(childNode);
                }
            }

            var headerSize = 12;
            var dataSize = headerSize + stateCount * 8;
            var result = new byte[dataSize];

            WriteInt32(result, 0, stateCount);
            WriteInt32(result, 4, codepointBase);
            WriteInt32(result, 8, codepointRange);

            Buffer.BlockCopy(baseArr, 0, result, headerSize, stateCount * 4);
            Buffer.BlockCopy(checkArr, 0, result, headerSize + stateCount * 4, stateCount * 4);

            return result;
        }

        private static int FindBase(bool[] used, List<int> childIndices, int capacity, ref int nextFree)
        {
            for (var baseValue = nextFree; ; baseValue++)
            {
                var fits = true;
                foreach (var c in childIndices)
                {
                    var pos = baseValue + c;
                    if (pos >= capacity || used[pos])
                    {
                        fits = false;
                        break;
                    }
                }

                if (fits)
                {
                    nextFree = baseValue + 1;
                    return baseValue;
                }
            }
        }

        private static IEnumerable<int> EnumerateCodepoints(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
                {
                    yield return char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else
                {
                    yield return text[i];
                }
            }
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        #endregion
    }
}
