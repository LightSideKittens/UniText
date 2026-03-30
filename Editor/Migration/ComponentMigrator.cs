using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightSide
{
    /// <summary>
    /// Migrates TMP components to UniText equivalents using reflection (no compile-time TMPro dependency)
    /// and SerializedObject (proper undo, prefab override, and dirty tracking support).
    /// </summary>
    internal class ComponentMigrator
    {
        Type tmpTextUiType;
        Type tmpText3dType;
        Type tmpInputFieldType;
        Type tmpSubMeshUiType;
        Type tmpSubMeshType;

        bool isInitialized;
        public bool IsTmpAvailable => isInitialized;

        readonly List<LogEntry> log;
        readonly FontMappingsData fontMappings;
        readonly MigrationStateData migrationState;

        public ComponentMigrator(List<LogEntry> log, FontMappingsData fontMappings, MigrationStateData migrationState)
        {
            this.log = log;
            this.fontMappings = fontMappings;
            this.migrationState = migrationState;
        }

        public bool Initialize()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                tmpTextUiType ??= asm.GetType("TMPro.TextMeshProUGUI");
                tmpText3dType ??= asm.GetType("TMPro.TextMeshPro");
                tmpInputFieldType ??= asm.GetType("TMPro.TMP_InputField");
                tmpSubMeshUiType ??= asm.GetType("TMPro.TMP_SubMeshUI");
                tmpSubMeshType ??= asm.GetType("TMPro.TMP_SubMesh");
            }

            isInitialized = tmpTextUiType != null;
            return isInitialized;
        }

        public bool MigratePrefab(string prefabPath, List<MigrationFinding> findings)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Log(LogSeverity.Error, $"Cannot load prefab: {prefabPath}");
                return false;
            }

            try
            {
                int migrated = MigrateHierarchy(root, prefabPath, findings);

                if (migrated > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                Log(LogSeverity.Info, $"Prefab '{prefabPath}': {migrated} component(s) migrated");
                return migrated > 0;
            }
            catch (Exception ex)
            {
                Log(LogSeverity.Error, $"Error migrating prefab '{prefabPath}': {ex.Message}");
                return false;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public bool MigrateScene(string scenePath, List<MigrationFinding> findings)
        {
            Scene scene = default;
            bool wasAlreadyOpen = false;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == scenePath)
                {
                    scene = s;
                    wasAlreadyOpen = true;
                    break;
                }
            }

            if (!wasAlreadyOpen)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid())
                {
                    Log(LogSeverity.Error, $"Cannot open scene: {scenePath}");
                    return false;
                }
            }

            try
            {
                Undo.SetCurrentGroupName($"Migrate TMP → UniText in {System.IO.Path.GetFileName(scenePath)}");

                int totalMigrated = 0;
                var rootObjects = scene.GetRootGameObjects();
                foreach (var root in rootObjects)
                {
                    totalMigrated += MigrateHierarchy(root, scenePath, findings);
                }

                if (totalMigrated > 0)
                    EditorSceneManager.SaveScene(scene);

                Log(LogSeverity.Info, $"Scene '{scenePath}': {totalMigrated} component(s) migrated");
                return totalMigrated > 0;
            }
            catch (Exception ex)
            {
                Log(LogSeverity.Error, $"Error migrating scene '{scenePath}': {ex.Message}");
                return false;
            }
            finally
            {
                if (!wasAlreadyOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        int MigrateHierarchy(GameObject root, string filePath, List<MigrationFinding> findings)
        {
            int count = 0;

            var tmpComponents = new List<(Component component, Type tmpType, string targetGuid)>();

            CollectTmpComponents(root, tmpComponents);

            foreach (var (component, tmpType, targetGuid) in tmpComponents)
            {
                if (MigrateComponent(component, tmpType, targetGuid, filePath, findings))
                    count++;
            }

            CleanUpSubMeshes(root);

            return count;
        }

        void CollectTmpComponents(GameObject go, List<(Component, Type, string)> result)
        {
            if (tmpTextUiType != null)
            {
                var comp = go.GetComponent(tmpTextUiType);
                if (comp != null)
                    result.Add((comp, tmpTextUiType, MigrationMapping.UniTextGuid));
            }
            if (tmpText3dType != null)
            {
                var comp = go.GetComponent(tmpText3dType);
                if (comp != null)
                    result.Add((comp, tmpText3dType, MigrationMapping.UniTextWorldGuid));
            }
            if (tmpInputFieldType != null)
            {
                var comp = go.GetComponent(tmpInputFieldType);
                if (comp != null)
                    result.Add((comp, tmpInputFieldType, MigrationMapping.UniTextInputFieldGuid));
            }

            for (int i = 0; i < go.transform.childCount; i++)
                CollectTmpComponents(go.transform.GetChild(i).gameObject, result);
        }

        bool MigrateComponent(Component tmpComponent, Type tmpType, string targetGuid, string filePath, List<MigrationFinding> findings)
        {
            var go = tmpComponent.gameObject;
            var so = new SerializedObject(tmpComponent);

            var text = so.FindProperty("m_text")?.stringValue ?? "";
            var fontSizeBase = so.FindProperty("m_fontSizeBase")?.floatValue ?? 36f;
            var fontSize = so.FindProperty("m_fontSize")?.floatValue ?? -99f;
            float effectiveFontSize = fontSize < 0 ? fontSizeBase : fontSize;

            var fontColor = so.FindProperty("m_fontColor")?.colorValue ?? Color.white;
            var alignment = so.FindProperty("m_textAlignment")?.intValue ?? 0x101;
            var wrappingMode = so.FindProperty("m_TextWrappingMode")?.intValue ?? 1;
            var enableAutoSizing = so.FindProperty("m_enableAutoSizing")?.boolValue ?? false;
            var fontSizeMin = so.FindProperty("m_fontSizeMin")?.floatValue ?? 10f;
            var fontSizeMax = so.FindProperty("m_fontSizeMax")?.floatValue ?? 72f;
            var fontStyle = so.FindProperty("m_fontStyle")?.intValue ?? 0;
            var isRtl = so.FindProperty("m_isRightToLeft")?.boolValue ?? false;
            var charSpacing = so.FindProperty("m_characterSpacing")?.floatValue ?? 0f;
            var lineSpacing = so.FindProperty("m_lineSpacing")?.floatValue ?? 0f;
            var overflowMode = so.FindProperty("m_overflowMode")?.intValue ?? 0;

            
            var fontAssetProp = so.FindProperty("m_fontAsset");
            string tmpFontGuid = null;
            if (fontAssetProp?.objectReferenceValue != null)
            {
                var fontPath = AssetDatabase.GetAssetPath(fontAssetProp.objectReferenceValue);
                tmpFontGuid = AssetDatabase.AssetPathToGUID(fontPath);
            }

            var richTextResult = RichTextConverter.Convert(text);
            if (richTextResult.warnings != null)
            {
                foreach (var w in richTextResult.warnings)
                    Log(LogSeverity.Warning, $"  Rich text: {w}");
            }

            var (hAlign, vAlign, alignWarning) = MigrationMapping.DecomposeAlignment(alignment);
            if (alignWarning != null)
                Log(LogSeverity.Warning, $"  {alignWarning}");

            Undo.RegisterCompleteObjectUndo(go, "Migrate TMP → UniText");

            Component newComponent;
            if (targetGuid == MigrationMapping.UniTextGuid)
                newComponent = Undo.AddComponent<UniText>(go);
            else if (targetGuid == MigrationMapping.UniTextWorldGuid)
                newComponent = Undo.AddComponent<UniTextWorld>(go);
            else
                return false;

            var newSo = new SerializedObject(newComponent);

            SetString(newSo, "text", richTextResult.text);
            SetFloat(newSo, "fontSize", effectiveFontSize);
            SetColor(newSo, "m_Color", fontColor);
            SetInt(newSo, "horizontalAlignment", hAlign);
            SetInt(newSo, "verticalAlignment", vAlign);
            SetBool(newSo, "wordWrap", MigrationMapping.ConvertWordWrap(wrappingMode));
            SetBool(newSo, "autoSize", enableAutoSizing);
            SetFloat(newSo, "minFontSize", fontSizeMin);
            SetFloat(newSo, "maxFontSize", fontSizeMax);
            SetInt(newSo, "baseDirection", isRtl ? 1 : 2);

            if (tmpFontGuid != null)
            {
                var fontStack = FindMappedFontStack(tmpFontGuid);
                if (fontStack != null)
                    SetObjectRef(newSo, "fontStack", fontStack);
                else
                    Log(LogSeverity.Warning, $"  No font mapping for TMP font GUID {tmpFontGuid}");
            }

            newSo.ApplyModifiedProperties();

            var uniTextBase = (UniTextBase)newComponent;

            if (richTextResult.requiredStyles != null)
            {
                foreach (var rs in richTextResult.requiredStyles)
                {
                    var modifier = CreateModifier(rs.modifierTypeName);
                    if (modifier != null)
                    {
                        var rule = new TagRule(rs.tagName);
                        if (rs.defaultParameter != null)
                            rule.defaultParameter = rs.defaultParameter;
                        uniTextBase.AddStyle(new Style { Modifier = modifier, Rule = rule });
                        Log(LogSeverity.Info, $"  Added Style: {rs.modifierTypeName} + TagRule(\"{rs.tagName}\")");
                    }
                }
            }

            if (fontStyle != 0)
            {
                foreach (var mapping in MigrationMapping.FontStyleMappings)
                {
                    if ((fontStyle & mapping.flag) != 0)
                    {
                        var modifier = CreateModifier(mapping.modifierTypeName);
                        if (modifier != null)
                        {
                            uniTextBase.AddStyle(new Style { Modifier = modifier, Rule = new TagRule(mapping.tagName) });
                            Log(LogSeverity.Info, $"  Added Style: {mapping.modifierTypeName} + TagRule(\"{mapping.tagName}\")");
                        }
                    }
                }
            }

            if (charSpacing != 0)
            {
                var modifier = new LetterSpacingModifier();
                var rule = new TagRule("cspace") { defaultParameter = charSpacing.ToString() };
                uniTextBase.AddStyle(new Style { Modifier = modifier, Rule = rule });
                Log(LogSeverity.Info, $"  Added Style: LetterSpacingModifier + TagRule(\"cspace\") default=\"{charSpacing}\"");
            }

            if (lineSpacing != 0)
            {
                var modifier = new LineHeightModifier();
                var rule = new TagRule("line-spacing") { defaultParameter = lineSpacing.ToString() };
                uniTextBase.AddStyle(new Style { Modifier = modifier, Rule = rule });
                Log(LogSeverity.Info, $"  Added Style: LineHeightModifier + TagRule(\"line-spacing\") default=\"{lineSpacing}\"");
                Log(LogSeverity.Warning, $"  Note: TMP line-spacing is additive %, UniText line-height may differ — verify visually");
            }

            if (overflowMode == 1)
            {
                uniTextBase.AddStyle(new Style { Modifier = new EllipsisModifier(), Rule = new TagRule("ellipsis") });
                Log(LogSeverity.Info, $"  Added Style: EllipsisModifier + TagRule(\"ellipsis\")");
            }

            var goName = go.name;
            Log(LogSeverity.Info, $"  Migrated '{goName}': text=\"{Truncate(text, 40)}\", fontSize={effectiveFontSize}, " +
                                  $"align=({hAlign},{vAlign}), wrap={MigrationMapping.ConvertWordWrap(wrappingMode)}, autoSize={enableAutoSizing}");

            Undo.DestroyObjectImmediate(tmpComponent);

            UpdateFindingStatus(filePath, go.name, findings);

            return true;
        }

        void CleanUpSubMeshes(GameObject root)
        {
            if (tmpSubMeshUiType == null && tmpSubMeshType == null) return;

            var toDestroy = new List<GameObject>();
            FindSubMeshObjects(root, toDestroy);

            foreach (var obj in toDestroy)
            {
                Log(LogSeverity.Info, $"  Removed TMP sub-mesh: '{obj.name}'");
                Undo.DestroyObjectImmediate(obj);
            }
        }

        void FindSubMeshObjects(GameObject go, List<GameObject> result)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i).gameObject;

                bool isSubMesh = false;
                if (tmpSubMeshUiType != null && child.GetComponent(tmpSubMeshUiType) != null)
                    isSubMesh = true;
                if (tmpSubMeshType != null && child.GetComponent(tmpSubMeshType) != null)
                    isSubMesh = true;

                if (isSubMesh)
                    result.Add(child);
                else
                    FindSubMeshObjects(child, result);
            }
        }

        UnityEngine.Object FindMappedFontStack(string tmpFontGuid)
        {
            foreach (var entry in fontMappings.fontMappings)
            {
                if (entry.tmpFontGuid != tmpFontGuid) continue;
                if (entry.skipped || string.IsNullOrEmpty(entry.uniTextFontStackGuid)) return null;

                var path = AssetDatabase.GUIDToAssetPath(entry.uniTextFontStackGuid);
                if (string.IsNullOrEmpty(path)) return null;
                return AssetDatabase.LoadAssetAtPath<UniTextFontStack>(path);
            }
            return null;
        }

        void UpdateFindingStatus(string filePath, string objectName, List<MigrationFinding> findings)
        {
            foreach (var f in findings)
            {
                if (f.filePath == filePath && f.type == FindingType.Component &&
                    f.objectPath == objectName && f.status == MigrationStatus.NotStarted)
                {
                    f.status = MigrationStatus.Completed;
                    migrationState.SetStatus(f.id, MigrationStatus.Completed);
                    break;
                }
            }
        }

        static readonly Dictionary<string, Func<BaseModifier>> modifierFactories = new()
        {
            { "BoldModifier",          () => new BoldModifier() },
            { "ItalicModifier",        () => new ItalicModifier() },
            { "UnderlineModifier",     () => new UnderlineModifier() },
            { "StrikethroughModifier", () => new StrikethroughModifier() },
            { "UppercaseModifier",     () => new UppercaseModifier() },
            { "LowercaseModifier",     () => new LowercaseModifier() },
            { "SmallCapsModifier",     () => new SmallCapsModifier() },
            { "LetterSpacingModifier", () => new LetterSpacingModifier() },
            { "LineHeightModifier",    () => new LineHeightModifier() },
            { "EllipsisModifier",      () => new EllipsisModifier() },
            { "ObjModifier",           () => new ObjModifier() },
            { "SizeModifier",          () => new SizeModifier() },
            { "ColorModifier",         () => new ColorModifier() },
            { "OutlineModifier",       () => new OutlineModifier() },
            { "ShadowModifier",        () => new ShadowModifier() },
        };

        static BaseModifier CreateModifier(string typeName)
        {
            if (modifierFactories.TryGetValue(typeName, out var factory))
                return factory();

            Debug.LogWarning($"[Migration] Unknown modifier type: {typeName}");
            return null;
        }

        static void SetString(SerializedObject so, string prop, string value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.stringValue = value;
        }

        static void SetFloat(SerializedObject so, string prop, float value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.floatValue = value;
        }

        static void SetInt(SerializedObject so, string prop, int value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = value;
        }

        static void SetBool(SerializedObject so, string prop, bool value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = value;
        }

        static void SetColor(SerializedObject so, string prop, Color value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.colorValue = value;
        }

        static void SetObjectRef(SerializedObject so, string prop, UnityEngine.Object value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.objectReferenceValue = value;
        }

        void Log(LogSeverity severity, string message)
        {
            log.Add(new LogEntry(severity, message));
        }

        static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
