using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Scans the project for TMP usage. All scanning is file-based (YAML/text search)
    /// — no scene loading, no TMP assembly dependency. Safe and fast.
    /// </summary>
    internal class ProjectAnalyzer
    {
        public List<MigrationFinding> Findings { get; } = new();
        public List<FontMappingEntry> DiscoveredFonts { get; } = new();
        public bool IsScanning { get; private set; }
        public bool WasCancelled { get; private set; }
        public float Progress { get; private set; }
        public string CurrentFile { get; private set; }

        public Dictionary<string, List<string>> PrefabDependencies { get; } = new();

        readonly List<string> excludedPaths;
        Action onComplete;
        List<string> allFiles;
        int fileIndex;
        int totalFiles;

        static readonly Regex ScriptGuidRegex = new(@"m_Script:\s*\{[^}]*guid:\s*([a-f0-9]{32})", RegexOptions.Compiled);
        static readonly Regex FileIdRegex = new(@"---\s*!u!\d+\s*&(\d+)", RegexOptions.Compiled);
        static readonly Regex GameObjectNameRegex = new(@"m_Name:\s*(.+)", RegexOptions.Compiled);
        static readonly Regex NestedPrefabRegex = new(@"m_SourcePrefab:\s*\{[^}]*guid:\s*([a-f0-9]{32})", RegexOptions.Compiled);
        static readonly Regex ShaderNameRegex = new(@"m_Shader:\s*\{[^}]*\}\s*m_Name:\s*(.+)|Shader\s*""([^""]+)""", RegexOptions.Compiled);

        static readonly Regex TmpScriptRegex = new(@"\bTMPro\b|\bTextMeshPro\b|\bTMP_\w+", RegexOptions.Compiled);
        static readonly Regex PreprocessorTmpRegex = new(@"#if\b.*\bTEXTMESHPRO", RegexOptions.Compiled);

        static readonly Regex TmpAsmRefRegex = new(@"""Unity\.TextMeshPro""", RegexOptions.Compiled);

        public ProjectAnalyzer(List<string> excludedPaths)
        {
            this.excludedPaths = excludedPaths ?? new List<string>();
        }

        public void StartScan(Action onComplete)
        {
            this.onComplete = onComplete;
            Findings.Clear();
            DiscoveredFonts.Clear();
            PrefabDependencies.Clear();
            IsScanning = true;
            WasCancelled = false;
            Progress = 0f;

            allFiles = new List<string>();
            CollectFiles("Assets", allFiles);

            totalFiles = allFiles.Count;
            fileIndex = 0;

            EditorApplication.delayCall += ProcessBatch;
        }

        public void Cancel()
        {
            WasCancelled = true;
        }

        const int BatchSize = 50;

        void ProcessBatch()
        {
            if (WasCancelled || fileIndex >= totalFiles)
            {
                FinishScan();
                return;
            }

            int end = Math.Min(fileIndex + BatchSize, totalFiles);
            for (int i = fileIndex; i < end; i++)
            {
                var path = allFiles[i];
                CurrentFile = path;
                ScanFile(path);
            }

            fileIndex = end;
            Progress = (float)fileIndex / totalFiles;

            EditorApplication.delayCall += ProcessBatch;
        }

        void FinishScan()
        {
            ScanCompiledAssemblies();

            IsScanning = false;
            Progress = WasCancelled ? Progress : 1f;
            CurrentFile = null;

            onComplete?.Invoke();
        }

        void CollectFiles(string root, List<string> result)
        {
            if (!Directory.Exists(root)) return;

            try
            {
                foreach (var file in Directory.GetFiles(root))
                {
                    var relative = file.Replace('\\', '/');
                    if (IsExcluded(relative)) continue;

                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".unity" || ext == ".prefab" || ext == ".cs" ||
                        ext == ".asset" || ext == ".mat" || ext == ".anim" ||
                        ext == ".asmdef" || ext == ".csv" || ext == ".json")
                    {
                        result.Add(relative);
                    }
                }

                foreach (var dir in Directory.GetDirectories(root))
                {
                    var relative = dir.Replace('\\', '/');
                    if (IsExcluded(relative)) continue;
                    var name = Path.GetFileName(dir);
                    if (name is "Library" or "Temp" or "Logs" or "obj" or "Packages" || name.StartsWith("."))
                        continue;
                    CollectFiles(dir, result);
                }
            }
            catch (UnauthorizedAccessException) { }
        }

        bool IsExcluded(string path)
        {
            for (int i = 0; i < excludedPaths.Count; i++)
            {
                if (path.StartsWith(excludedPaths[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void ScanFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".unity":
                case ".prefab":
                    ScanSceneOrPrefab(path);
                    break;
                case ".cs":
                    ScanScript(path);
                    break;
                case ".asset":
                    ScanAsset(path);
                    break;
                case ".mat":
                    ScanMaterial(path);
                    break;
                case ".anim":
                    ScanAnimation(path);
                    break;
                case ".asmdef":
                    ScanAssemblyDef(path);
                    break;
                case ".csv":
                case ".json":
                    ScanTextContentForRichText(path);
                    break;
            }
        }

        void ScanSceneOrPrefab(string path)
        {
            string content;
            try { content = File.ReadAllText(path); }
            catch { return; }

            var matches = ScriptGuidRegex.Matches(content);
            foreach (Match match in matches)
            {
                var guid = match.Groups[1].Value;

                if (MigrationMapping.SubMeshGuids.Contains(guid))
                {
                    continue;
                }

                if (!MigrationMapping.AllTmpComponentGuids.Contains(guid))
                    continue;

                var fileID = ExtractFileID(content, match.Index);
                var objectName = ExtractObjectName(content, match.Index);

                var tmpName = MigrationMapping.GetTmpName(guid);
                var targetName = MigrationMapping.GetTargetName(guid);
                var details = targetName != "(none)"
                    ? $"{tmpName} → {targetName} on '{objectName}'"
                    : $"{tmpName} on '{objectName}' (no equivalent)";

                var complexity = DetermineComponentComplexity(guid, content, match.Index);
                var warnings = CollectComponentWarnings(content, match.Index);

                var finding = new MigrationFinding
                {
                    id = MigrationFinding.ComputeId(path, guid, fileID),
                    filePath = path,
                    type = FindingType.Component,
                    complexity = complexity,
                    details = details,
                    objectPath = objectName,
                    scriptGuid = guid,
                    fileID = fileID,
                    warnings = warnings,
                };

                Findings.Add(finding);
            }

            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var nestedPrefabs = NestedPrefabRegex.Matches(content);
                if (nestedPrefabs.Count > 0)
                {
                    var deps = new List<string>();
                    foreach (Match m in nestedPrefabs)
                    {
                        var nestedGuid = m.Groups[1].Value;
                        var nestedPath = AssetDatabase.GUIDToAssetPath(nestedGuid);
                        if (!string.IsNullOrEmpty(nestedPath) && nestedPath.EndsWith(".prefab"))
                            deps.Add(nestedPath);
                    }
                    if (deps.Count > 0)
                        PrefabDependencies[path] = deps;
                }
            }
        }

        string ExtractFileID(string content, int matchIndex)
        {
            int searchFrom = Math.Max(0, matchIndex - 500);
            var block = content.Substring(searchFrom, matchIndex - searchFrom);
            var m = FileIdRegex.Match(block);
            Match last = null;
            while (m.Success)
            {
                last = m;
                m = m.NextMatch();
            }
            return last?.Groups[1].Value ?? "0";
        }

        string ExtractObjectName(string content, int matchIndex)
        {
            int searchFrom = Math.Max(0, matchIndex - 2000);
            var block = content.Substring(searchFrom, matchIndex - searchFrom);
            var m = GameObjectNameRegex.Match(block);
            Match last = null;
            while (m.Success)
            {
                last = m;
                m = m.NextMatch();
            }
            return last?.Groups[1].Value.Trim() ?? "(unknown)";
        }

        MigrationComplexity DetermineComponentComplexity(string guid, string content, int matchIndex)
        {
            if (guid == MigrationMapping.TmpDropdownGuid)
                return MigrationComplexity.Manual;

            if (guid == MigrationMapping.TmpInputFieldGuid)
                return MigrationComplexity.Complex;

            if (guid == MigrationMapping.TmpText3dGuid)
                return MigrationComplexity.Complex;

            int blockEnd = content.IndexOf("---", matchIndex + 1);
            if (blockEnd < 0) blockEnd = content.Length;
            var block = content.Substring(matchIndex, Math.Min(blockEnd - matchIndex, 3000));

            bool hasComplexFeature =
                block.Contains("m_textAlignment") && !block.Contains("m_textAlignment: 257") || block.Contains("m_fontStyle") && !block.Contains("m_fontStyle: 0") ||
                block.Contains("m_characterSpacing") && !block.Contains("m_characterSpacing: 0") ||
                block.Contains("m_lineSpacing") && !block.Contains("m_lineSpacing: 0") ||
                block.Contains("m_overflowMode") && !block.Contains("m_overflowMode: 0") ||
                block.Contains("m_enableVertexGradient: 1");

            return hasComplexFeature ? MigrationComplexity.Moderate : MigrationComplexity.Simple;
        }

        List<string> CollectComponentWarnings(string content, int matchIndex)
        {
            int blockEnd = content.IndexOf("---", matchIndex + 1);
            if (blockEnd < 0) blockEnd = content.Length;
            var block = content.Substring(matchIndex, Math.Min(blockEnd - matchIndex, 3000));
            var warnings = new List<string>();

            var alignMatch = Regex.Match(block, @"m_textAlignment:\s*(\d+)");
            if (alignMatch.Success)
            {
                int val = int.Parse(alignMatch.Groups[1].Value);
                var (_, _, warning) = MigrationMapping.DecomposeAlignment(val);
                if (warning != null) warnings.Add(warning);
            }

            var cspaceMatch = Regex.Match(block, @"m_characterSpacing:\s*([-\d.]+)");
            if (cspaceMatch.Success && cspaceMatch.Groups[1].Value != "0")
                warnings.Add($"characterSpacing={cspaceMatch.Groups[1].Value} → will add LetterSpacingModifier");

            var lspaceMatch = Regex.Match(block, @"m_lineSpacing:\s*([-\d.]+)");
            if (lspaceMatch.Success && lspaceMatch.Groups[1].Value != "0")
                warnings.Add($"lineSpacing={lspaceMatch.Groups[1].Value}% → will add LineHeightModifier");

            var overflowMatch = Regex.Match(block, @"m_overflowMode:\s*(\d+)");
            if (overflowMatch.Success)
            {
                int mode = int.Parse(overflowMatch.Groups[1].Value);
                if (mode == 1) warnings.Add("Overflow=Ellipsis → will add EllipsisModifier");
                else if (mode >= 2) warnings.Add($"Overflow mode {mode} has no UniText equivalent");
            }

            if (block.Contains("m_enableVertexGradient: 1"))
                warnings.Add("Vertex gradient enabled — UniText uses GradientModifier (different model)");

            var paraMatch = Regex.Match(block, @"m_paragraphSpacing:\s*([-\d.]+)");
            if (paraMatch.Success && paraMatch.Groups[1].Value != "0")
                warnings.Add($"paragraphSpacing={paraMatch.Groups[1].Value} — no UniText equivalent");

            return warnings.Count > 0 ? warnings : null;
        }

        void ScanScript(string path)
        {
            string content;
            try { content = File.ReadAllText(path); }
            catch { return; }

            if (!TmpScriptRegex.IsMatch(content))
                return;

            var lines = content.Split('\n');
            int tmpRefCount = 0;
            bool hasPreprocessor = false;
            var warnings = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (TmpScriptRegex.IsMatch(line))
                    tmpRefCount++;
                if (PreprocessorTmpRegex.IsMatch(line))
                    hasPreprocessor = true;
            }

            if (tmpRefCount == 0) return;

            var complexity = MigrationComplexity.Simple;
            if (content.Contains("TextAlignmentOptions"))
            {
                complexity = MigrationComplexity.Moderate;
                warnings.Add("Uses TextAlignmentOptions — needs decomposition into HorizontalAlignment + VerticalAlignment");
            }
            if (content.Contains("TMP_SpriteAsset") || content.Contains("TMP_Dropdown") || content.Contains("textInfo"))
            {
                complexity = MigrationComplexity.Manual;
            }
            if (hasPreprocessor)
                warnings.Add("Contains #if TEXTMESHPRO blocks — review manually");

            var details = $"{tmpRefCount} TMP reference{(tmpRefCount > 1 ? "s" : "")}";

            Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.ScriptReference),
                filePath = path,
                type = FindingType.ScriptReference,
                complexity = complexity,
                details = details,
                warnings = warnings.Count > 0 ? warnings : null,
            });
        }

        void ScanAsset(string path)
        {
            string content;
            try { content = File.ReadAllText(path); }
            catch { return; }

            foreach (var assetGuid in MigrationMapping.TmpAssetGuids)
            {
                if (!content.Contains(assetGuid)) continue;

                var assetName = MigrationMapping.GetTmpName(assetGuid);

                if (assetGuid == MigrationMapping.TmpFontAssetGuid)
                {
                    var nameMatch = Regex.Match(content, @"m_Name:\s*(.+)");
                    var familyMatch = Regex.Match(content, @"m_FamilyName:\s*(.+)");
                    var fontName = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(path);
                    var familyName = familyMatch.Success ? familyMatch.Groups[1].Value.Trim() : fontName;

                    var fontGuid = AssetDatabase.AssetPathToGUID(path);

                    var sourcePath = TryFindTtfSource(familyName);

                    DiscoveredFonts.Add(new FontMappingEntry
                    {
                        tmpFontGuid = fontGuid,
                        tmpFontName = fontName,
                        tmpFamilyName = familyName,
                        sourceTtfPath = sourcePath,
                    });

                    Findings.Add(new MigrationFinding
                    {
                        id = MigrationFinding.ComputeIdForAsset(path, FindingType.FontAsset),
                        filePath = path,
                        type = FindingType.FontAsset,
                        complexity = MigrationComplexity.Moderate,
                        details = $"TMP_FontAsset '{fontName}' (family: {familyName})"
                                  + (sourcePath != null ? $" — source: {sourcePath}" : " — no source TTF/OTF found"),
                    });
                }
                else
                {
                    Findings.Add(new MigrationFinding
                    {
                        id = MigrationFinding.ComputeIdForAsset(path, FindingType.FontAsset),
                        filePath = path,
                        type = FindingType.FontAsset,
                        complexity = MigrationComplexity.Manual,
                        details = $"{assetName} — no direct UniText equivalent",
                    });
                }

                break;
            }

            ScanTextContentForRichText(path, content);
        }

        string TryFindTtfSource(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;

            var guids = AssetDatabase.FindAssets(familyName);
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var ext = Path.GetExtension(assetPath).ToLowerInvariant();
                if (ext == ".ttf" || ext == ".otf")
                    return assetPath;
            }

            var simplified = familyName.Replace(" ", "").Replace("-", "");
            if (simplified != familyName)
            {
                guids = AssetDatabase.FindAssets(simplified);
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var ext = Path.GetExtension(assetPath).ToLowerInvariant();
                    if (ext == ".ttf" || ext == ".otf")
                        return assetPath;
                }
            }

            return null;
        }

        void ScanMaterial(string path)
        {
            string content;
            try { content = File.ReadAllText(path); }
            catch { return; }

            foreach (var prefix in MigrationMapping.TmpShaderPrefixes)
            {
                if (!content.Contains(prefix)) continue;

                var warnings = new List<string>();

                foreach (var recipe in MigrationMapping.MaterialRecipes)
                {
                    if (content.Contains(recipe.shaderProperty))
                    {
                        var valMatch = Regex.Match(content, recipe.shaderProperty + @":\s*(.+)");
                        var valStr = valMatch.Success ? valMatch.Groups[1].Value.Trim() : "?";
                        warnings.Add($"{recipe.description} ({recipe.shaderProperty}={valStr}) → {recipe.uniTextEquivalent}");
                    }
                }

                Findings.Add(new MigrationFinding
                {
                    id = MigrationFinding.ComputeIdForAsset(path, FindingType.Material),
                    filePath = path,
                    type = FindingType.Material,
                    complexity = MigrationComplexity.Moderate,
                    details = $"TMP material — will be unused after migration",
                    warnings = warnings.Count > 0 ? warnings : null,
                });
                break;
            }
        }

        void ScanAnimation(string path)
        {
            string content;
            try { content = File.ReadAllText(path); }
            catch { return; }

            bool hasTmpBinding = false;
            var remappableProps = new List<string>();

            foreach (var guid in MigrationMapping.AllTmpComponentGuids)
            {
                if (content.Contains(guid))
                {
                    hasTmpBinding = true;
                    break;
                }
            }

            if (!hasTmpBinding) return;

            foreach (var kvp in MigrationMapping.AnimationPropertyMap)
            {
                if (content.Contains(kvp.Key))
                    remappableProps.Add($"{kvp.Key} → {kvp.Value}");
            }

            var details = remappableProps.Count > 0
                ? $"Targets TMP properties: {string.Join(", ", remappableProps)}"
                : "References TMP component type";

            Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.Animation),
                filePath = path,
                type = FindingType.Animation,
                complexity = MigrationComplexity.Complex,
                details = details,
            });
        }

        void ScanAssemblyDef(string path)
        {
            string content;
            try { content = File.ReadAllText(path); }
            catch { return; }

            if (!TmpAsmRefRegex.IsMatch(content)) return;

            Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.AssemblyDef),
                filePath = path,
                type = FindingType.AssemblyDef,
                complexity = MigrationComplexity.Simple,
                details = "References Unity.TextMeshPro assembly",
            });
        }

        void ScanTextContentForRichText(string path, string content = null)
        {
            if (content == null)
            {
                try { content = File.ReadAllText(path); }
                catch { return; }
            }

            var unsupportedTags = new List<string>();
            var styleNeededTags = new List<string>();

            foreach (var tag in MigrationMapping.UnsupportedTagPatterns)
            {
                if (content.Contains(tag))
                    unsupportedTags.Add(tag);
            }

            foreach (var kvp in MigrationMapping.TagsNeedingStyleEntry)
            {
                if (content.Contains($"<{kvp.Key}"))
                    styleNeededTags.Add($"<{kvp.Key}> → needs {kvp.Value} + TagRule(\"{kvp.Key}\")");
            }

            if (unsupportedTags.Count == 0 && styleNeededTags.Count == 0) return;

            var details = new List<string>();
            if (unsupportedTags.Count > 0)
                details.Add($"Unsupported tags: {string.Join(", ", unsupportedTags)}");
            if (styleNeededTags.Count > 0)
                details.Add($"Tags needing Style entries: {styleNeededTags.Count}");

            Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.RichTextContent),
                filePath = path,
                type = FindingType.RichTextContent,
                complexity = unsupportedTags.Count > 0 ? MigrationComplexity.Moderate : MigrationComplexity.Simple,
                details = string.Join("; ", details),
                warnings = styleNeededTags.Count > 0 ? styleNeededTags : null,
            });
        }

        void ScanCompiledAssemblies()
        {
            var tmpTypes = new HashSet<string>
            {
                "TMPro.TMP_Text", "TMPro.TextMeshPro", "TMPro.TextMeshProUGUI",
                "TMPro.TMP_InputField", "TMPro.TMP_FontAsset", "TMPro.TMP_Dropdown",
                "TMPro.TMP_SpriteAsset",
            };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name;
                if (asmName.StartsWith("Unity") || asmName.StartsWith("System") ||
                    asmName.StartsWith("mscorlib") || asmName.StartsWith("netstandard") ||
                    asmName.StartsWith("Mono") || asmName.Contains("TextMeshPro") ||
                    asmName.StartsWith("LightSide"))
                    continue;

                if (asm.IsDynamic) continue;

                string loc;
                try { loc = asm.Location; }
                catch { continue; }
                if (string.IsNullOrEmpty(loc)) continue;
                loc = loc.Replace('\\', '/');
                if (!loc.Contains("/Assets/")) continue;

                try
                {
                    bool hasTmpRef = false;
                    var tmpMembers = new List<string>();

                    foreach (var type in asm.GetExportedTypes())
                    {
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
                        {
                            if (tmpTypes.Contains(method.ReturnType.FullName ?? ""))
                            {
                                hasTmpRef = true;
                                tmpMembers.Add($"{type.Name}.{method.Name}() → {method.ReturnType.Name}");
                            }
                            foreach (var param in method.GetParameters())
                            {
                                if (tmpTypes.Contains(param.ParameterType.FullName ?? ""))
                                {
                                    hasTmpRef = true;
                                    tmpMembers.Add($"{type.Name}.{method.Name}({param.ParameterType.Name} {param.Name})");
                                }
                            }
                        }
                    }

                    if (hasTmpRef)
                    {
                        Findings.Add(new MigrationFinding
                        {
                            id = MigrationFinding.ComputeIdForAsset(asmName, FindingType.CompiledDependency),
                            filePath = loc,
                            type = FindingType.CompiledDependency,
                            complexity = MigrationComplexity.Manual,
                            details = $"Compiled assembly '{asmName}' has public API referencing TMP types",
                            warnings = tmpMembers.Count > 0 ? tmpMembers : null,
                        });
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Returns prefabs in bottom-up migration order (leaves first, parents last).
        /// </summary>
        public List<string> GetPrefabMigrationOrder()
        {
            var visited = new HashSet<string>();
            var order = new List<string>();

            void Visit(string prefab)
            {
                if (!visited.Add(prefab)) return;
                if (PrefabDependencies.TryGetValue(prefab, out var deps))
                {
                    foreach (var dep in deps)
                        Visit(dep);
                }
                order.Add(prefab);
            }

            foreach (var prefab in PrefabDependencies.Keys)
                Visit(prefab);

            foreach (var f in Findings)
            {
                if (f.type == FindingType.Component && f.filePath.EndsWith(".prefab") && visited.Add(f.filePath))
                    order.Add(f.filePath);
            }

            return order;
        }
    }
}
