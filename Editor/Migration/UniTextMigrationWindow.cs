using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal class UniTextMigrationWindow : EditorWindow
    {
        enum Tab { Dashboard, Analysis, FontMapping, ScriptPreview, Settings, Log }

        static readonly string[] TabLabels =
            { "Dashboard", "Analysis", "Font Mapping", "Script Preview", "Settings", "Log" };

        Tab currentTab;

        ProjectAnalyzer analyzer;
        ComponentMigrator componentMigrator;

        MigrationStateData stateData;
        FontMappingsData fontMappingsData;

        List<MigrationFinding> findings = new();
        List<LogEntry> logEntries = new();
        MigrationSummary summary;

        FindingType? filterType;
        MigrationStatus? filterStatus;
        string searchText = "";
        Vector2 analysisScroll;

        int selectedScriptIndex = -1;
        List<string> scriptFiles = new();
        List<ScriptReplacement> currentReplacements = new();
        string currentDiff = "";
        Vector2 scriptListScroll;
        Vector2 scriptDiffScroll;

        Vector2 fontMappingScroll;

        LogSeverity? logFilter;
        Vector2 logScroll;

        string newExclusionPath = "";

        GUIStyle boxStyle;
        GUIStyle headerStyle;
        GUIStyle richLabelStyle;
        GUIStyle statusGreen, statusYellow, statusRed, statusGray;

        [MenuItem("Tools/UniText Migration")]
        public static void ShowWindow()
        {
            var window = GetWindow<UniTextMigrationWindow>("UniText Migration");
            window.minSize = new Vector2(600, 500);
        }

        void OnEnable()
        {
            stateData = MigrationStateData.Load();
            fontMappingsData = FontMappingsData.Load();
            logEntries = new List<LogEntry>();

            foreach (var f in findings)
                f.status = stateData.GetStatus(f.id);
        }

        void OnDisable()
        {
            if (analyzer != null && analyzer.IsScanning)
                analyzer.Cancel();
        }

        void OnGUI()
        {
            InitStyles();

            EditorGUILayout.Space(8);
            currentTab = (Tab)GUILayout.Toolbar((int)currentTab, TabLabels, GUILayout.Height(25));
            EditorGUILayout.Space(8);

            switch (currentTab)
            {
                case Tab.Dashboard:     DrawDashboard();     break;
                case Tab.Analysis:      DrawAnalysis();      break;
                case Tab.FontMapping:   DrawFontMapping();   break;
                case Tab.ScriptPreview: DrawScriptPreview(); break;
                case Tab.Settings:      DrawSettings();      break;
                case Tab.Log:           DrawLog();           break;
            }
        }

        void InitStyles()
        {
            boxStyle ??= new GUIStyle("helpBox") { padding = new RectOffset(10, 10, 8, 8) };
            headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            richLabelStyle ??= new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };

            if (statusGreen == null)
            {
                statusGreen = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.4f, 0.85f, 0.4f) } };
                statusYellow = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.9f, 0.8f, 0.2f) } };
                statusRed = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.95f, 0.4f, 0.4f) } };
                statusGray = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.gray } };
            }
        }

        void DrawDashboard()
        {
            if (analyzer != null && analyzer.IsScanning)
            {
                var rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, analyzer.Progress,
                    $"Scanning... {(int)(analyzer.Progress * 100)}% — {analyzer.CurrentFile}");

                if (GUILayout.Button("Cancel", GUILayout.Width(80)))
                    analyzer.Cancel();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Scan Project", GUILayout.Height(32)))
                    StartScan();
                if (findings.Count > 0 && GUILayout.Button("Verify Migrations", GUILayout.Height(32), GUILayout.Width(140)))
                    VerifyMigrations();
                EditorGUILayout.EndHorizontal();
            }

            if (findings.Count == 0 && (analyzer == null || !analyzer.IsScanning))
            {
                EditorGUILayout.HelpBox("Click 'Scan Project' to detect all TextMesh Pro usage in your project.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(8);

            summary = MigrationSummary.Compute(findings);

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Migration Order", headerStyle);
            DrawOrderStep(1, "Font Mapping", summary.fontCount, IsFontMappingComplete());
            DrawOrderStep(2, "Components (prefabs bottom-up, then scenes)", summary.componentCount, summary.componentCount > 0 && CountByTypeAndStatus(FindingType.Component, MigrationStatus.NotStarted) == 0);
            DrawOrderStep(3, "Scripts", summary.scriptCount, summary.scriptCount > 0 && CountByTypeAndStatus(FindingType.ScriptReference, MigrationStatus.NotStarted) == 0);
            DrawOrderStep(4, "Cleanup (materials, animations)", summary.materialCount + summary.animationCount, false);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            var progressRect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(progressRect, summary.ProgressPercent,
                $"{(int)(summary.ProgressPercent * 100)}% — {summary.completed} completed, {summary.pending} pending, {summary.skipped} skipped");

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(boxStyle);
            DrawSummaryRow("Components", summary.componentCount);
            DrawSummaryRow("Scripts", summary.scriptCount);
            DrawSummaryRow("Fonts", summary.fontCount);
            DrawSummaryRow("Materials", summary.materialCount);
            DrawSummaryRow("Animations", summary.animationCount);
            DrawSummaryRow("Assembly Defs", summary.asmdefCount);
            if (summary.richTextContentCount > 0) DrawSummaryRow("Rich Text Content", summary.richTextContentCount);
            if (summary.missingScriptCount > 0) DrawSummaryRow("Missing Scripts", summary.missingScriptCount);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Complexity Breakdown", headerStyle);
            EditorGUILayout.LabelField($"Simple (auto):     {summary.simpleCount}  ({Pct(summary.simpleCount)})", richLabelStyle);
            EditorGUILayout.LabelField($"Moderate (review):  {summary.moderateCount}  ({Pct(summary.moderateCount)})", richLabelStyle);
            EditorGUILayout.LabelField($"Complex (semi):     {summary.complexCount}  ({Pct(summary.complexCount)})", richLabelStyle);
            EditorGUILayout.LabelField($"Manual:             {summary.manualCount}  ({Pct(summary.manualCount)})", richLabelStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            DrawWarnings();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            int simpleRemaining = CountSimplePending();
            GUI.enabled = simpleRemaining > 0 && IsFontMappingComplete();
            if (GUILayout.Button($"Migrate All Simple ({simpleRemaining})", GUILayout.Height(28)))
                MigrateAllSimple();
            GUI.enabled = true;

            if (GUILayout.Button("Export Report", GUILayout.Height(28), GUILayout.Width(120)))
                ExportReport();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(stateData.lastScanTime))
                EditorGUILayout.LabelField($"Last scan: {stateData.lastScanTime}", EditorStyles.miniLabel);
        }

        void DrawAnalysis()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Type:", GUILayout.Width(35));
            var typeOptions = new[] { "All", "Component", "Script", "Font", "Material", "Animation", "AssemblyDef", "RichText", "MissingScript", "Compiled" };
            int typeIdx = filterType.HasValue ? (int)filterType.Value + 1 : 0;
            typeIdx = EditorGUILayout.Popup(typeIdx, typeOptions, GUILayout.Width(100));
            filterType = typeIdx == 0 ? null : (FindingType?)(typeIdx - 1);

            EditorGUILayout.LabelField("Status:", GUILayout.Width(45));
            var statusOptions = new[] { "All", "Pending", "Completed", "Skipped", "Failed" };
            int statusIdx = filterStatus.HasValue ? (int)filterStatus.Value + 1 : 0;
            statusIdx = EditorGUILayout.Popup(statusIdx, statusOptions, GUILayout.Width(90));
            filterStatus = statusIdx == 0 ? null : (MigrationStatus?)(statusIdx - 1);

            searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            analysisScroll = EditorGUILayout.BeginScrollView(analysisScroll);

            string lastFile = null;
            int selectedCount = 0;

            foreach (var f in findings)
            {
                if (!MatchesFilter(f)) continue;

                if (f.filePath != lastFile)
                {
                    lastFile = f.filePath;
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(f.filePath, EditorStyles.boldLabel);
                }

                EditorGUILayout.BeginHorizontal(boxStyle);

                f.isSelected = EditorGUILayout.Toggle(f.isSelected, GUILayout.Width(18));
                if (f.isSelected) selectedCount++;

                var statusStyle = f.status switch
                {
                    MigrationStatus.Completed => statusGreen,
                    MigrationStatus.Skipped => statusGray,
                    MigrationStatus.Failed => statusRed,
                    _ => statusYellow,
                };
                EditorGUILayout.LabelField(f.status.ToString(), statusStyle, GUILayout.Width(70));

                var complexityStr = f.complexity switch
                {
                    MigrationComplexity.Simple => "[Simple]",
                    MigrationComplexity.Moderate => "[Moderate]",
                    MigrationComplexity.Complex => "[Complex]",
                    MigrationComplexity.Manual => "[Manual]",
                    _ => ""
                };
                EditorGUILayout.LabelField(complexityStr, GUILayout.Width(70));

                EditorGUILayout.LabelField(f.details, richLabelStyle);

                if (f.status == MigrationStatus.NotStarted && f.type == FindingType.Component)
                {
                    if (GUILayout.Button("Migrate", GUILayout.Width(60)))
                        MigrateSingleFinding(f);
                }
                if (f.status == MigrationStatus.NotStarted)
                {
                    if (GUILayout.Button("Skip", GUILayout.Width(40)))
                    {
                        f.status = MigrationStatus.Skipped;
                        stateData.SetStatus(f.id, MigrationStatus.Skipped);
                    }
                }
                if (GUILayout.Button("Open", GUILayout.Width(40)))
                    PingAsset(f.filePath);

                EditorGUILayout.EndHorizontal();

                if (f.warnings != null)
                {
                    foreach (var w in f.warnings)
                        EditorGUILayout.LabelField($"  {w}", statusYellow);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = selectedCount > 0;
            if (GUILayout.Button($"Migrate Selected ({selectedCount})"))
                MigrateSelected();
            if (GUILayout.Button($"Skip Selected ({selectedCount})"))
                SkipSelected();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawFontMapping()
        {
            EditorGUILayout.LabelField("TMP Font → UniText Font Mapping", headerStyle);
            EditorGUILayout.Space(4);

            if (fontMappingsData.fontMappings.Count == 0)
            {
                EditorGUILayout.HelpBox("No TMP fonts discovered. Run a project scan first.", MessageType.Info);
                return;
            }

            fontMappingScroll = EditorGUILayout.BeginScrollView(fontMappingScroll);

            foreach (var entry in fontMappingsData.fontMappings)
            {
                EditorGUILayout.BeginVertical(boxStyle);

                EditorGUILayout.LabelField($"TMP Font: {entry.tmpFontName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Family: {entry.tmpFamilyName}", EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Source:", GUILayout.Width(50));
                if (entry.HasSource)
                    EditorGUILayout.LabelField(entry.sourceTtfPath, statusGreen);
                else
                    EditorGUILayout.LabelField("Not found", statusRed);

                if (GUILayout.Button("Browse...", GUILayout.Width(70)))
                {
                    var path = EditorUtility.OpenFilePanel("Select TTF/OTF", "Assets", "ttf,otf");
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (!path.Replace('\\', '/').Contains("/Assets/"))
                        {
                            var dest = "Assets/" + Path.GetFileName(path);
                            File.Copy(path, dest, true);
                            AssetDatabase.ImportAsset(dest);
                            entry.sourceTtfPath = dest;
                        }
                        else
                        {
                            entry.sourceTtfPath = "Assets" + path.Substring(path.IndexOf("/Assets/") + 7);
                        }
                        fontMappingsData.Save();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("UniTextFont:", GUILayout.Width(80));

                var currentFont = !string.IsNullOrEmpty(entry.uniTextFontGuid)
                    ? AssetDatabase.LoadAssetAtPath<UniTextFont>(AssetDatabase.GUIDToAssetPath(entry.uniTextFontGuid))
                    : null;
                var newFont = (UniTextFont)EditorGUILayout.ObjectField(currentFont, typeof(UniTextFont), false);
                if (newFont != currentFont)
                {
                    entry.uniTextFontGuid = newFont != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(newFont)) : "";
                    fontMappingsData.Save();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("FontStack:", GUILayout.Width(80));

                var currentStack = !string.IsNullOrEmpty(entry.uniTextFontStackGuid)
                    ? AssetDatabase.LoadAssetAtPath<UniTextFontStack>(AssetDatabase.GUIDToAssetPath(entry.uniTextFontStackGuid))
                    : null;
                var newStack = (UniTextFontStack)EditorGUILayout.ObjectField(currentStack, typeof(UniTextFontStack), false);
                if (newStack != currentStack)
                {
                    entry.uniTextFontStackGuid = newStack != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(newStack)) : "";
                    fontMappingsData.Save();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (entry.IsMapped)
                    EditorGUILayout.LabelField("Mapped", statusGreen);
                else if (entry.HasSource)
                    EditorGUILayout.LabelField("Source found, needs font creation", statusYellow);
                else
                    EditorGUILayout.LabelField("Unmapped", statusRed);

                if (entry.HasSource && string.IsNullOrEmpty(entry.uniTextFontGuid))
                {
                    if (GUILayout.Button("Create Font", GUILayout.Width(90)))
                        CreateFontFromSource(entry);
                }

                if (!entry.skipped && GUILayout.Button("Skip", GUILayout.Width(50)))
                {
                    entry.skipped = true;
                    fontMappingsData.Save();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawScriptPreview()
        {
            scriptFiles.Clear();
            foreach (var f in findings)
            {
                if (f.type == FindingType.ScriptReference && f.status == MigrationStatus.NotStarted)
                    scriptFiles.Add(f.filePath);
            }

            if (scriptFiles.Count == 0)
            {
                EditorGUILayout.HelpBox("No pending script migrations. Scan first or check Analysis tab.", MessageType.Info);
                return;
            }

            int pendingComponents = CountByTypeAndStatus(FindingType.Component, MigrationStatus.NotStarted);
            if (pendingComponents > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{pendingComponents} components are not yet migrated. Migrating scripts before components will break serialized references. Migrate components first.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            EditorGUILayout.LabelField("Script Files", headerStyle);
            scriptListScroll = EditorGUILayout.BeginScrollView(scriptListScroll, GUILayout.Width(200));

            for (int i = 0; i < scriptFiles.Count; i++)
            {
                var fileName = Path.GetFileName(scriptFiles[i]);
                bool isSelected = i == selectedScriptIndex;
                var style = isSelected ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(fileName, style))
                {
                    selectedScriptIndex = i;
                    currentReplacements = ScriptMigrator.AnalyzeFile(scriptFiles[i]);
                    currentDiff = ScriptMigrator.GenerateDiff(scriptFiles[i], currentReplacements);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();

            if (selectedScriptIndex >= 0 && selectedScriptIndex < scriptFiles.Count)
            {
                EditorGUILayout.LabelField(scriptFiles[selectedScriptIndex], EditorStyles.boldLabel);
                EditorGUILayout.Space(4);

                scriptDiffScroll = EditorGUILayout.BeginScrollView(scriptDiffScroll);
                EditorGUILayout.LabelField(currentDiff, richLabelStyle);
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply Selected"))
                {
                    var (ok, bakPath, err) = ScriptMigrator.ApplyReplacements(
                        scriptFiles[selectedScriptIndex], currentReplacements, stateData.excludedPaths.Count > 0);

                    if (ok)
                    {
                        logEntries.Add(new LogEntry(LogSeverity.Info, $"Migrated script: {scriptFiles[selectedScriptIndex]}")
                            { backupPath = bakPath });
                        AssetDatabase.ImportAsset(scriptFiles[selectedScriptIndex]);
                        currentReplacements = ScriptMigrator.AnalyzeFile(scriptFiles[selectedScriptIndex]);
                        currentDiff = ScriptMigrator.GenerateDiff(scriptFiles[selectedScriptIndex], currentReplacements);
                    }
                    else
                    {
                        logEntries.Add(new LogEntry(LogSeverity.Error, $"Script migration failed: {err}"));
                    }
                }
                if (GUILayout.Button("Apply All Files"))
                    ApplyAllScripts();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Select a script file from the list to preview changes.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        void DrawSettings()
        {
            EditorGUILayout.LabelField("Migration Settings", headerStyle);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Exclusion Paths", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Files in these folders will be skipped during scanning.", EditorStyles.miniLabel);

            for (int i = stateData.excludedPaths.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(stateData.excludedPaths[i]);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    stateData.excludedPaths.RemoveAt(i);
                    stateData.Save();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Folder"))
            {
                var path = EditorUtility.OpenFolderPanel("Select folder to exclude", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    var projectPath = Application.dataPath.Replace("/Assets", "");
                    if (path.StartsWith(projectPath))
                        path = path.Substring(projectPath.Length + 1);
                    path = path.Replace('\\', '/');

                    if (!stateData.excludedPaths.Contains(path))
                    {
                        stateData.excludedPaths.Add(path);
                        stateData.Save();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Migration Guard", EditorStyles.boldLabel);
            var guardEnabled = EditorGUILayout.Toggle("Warn on new TMP components", stateData.migrationGuardEnabled);
            if (guardEnabled != stateData.migrationGuardEnabled)
            {
                stateData.migrationGuardEnabled = guardEnabled;
                stateData.Save();
            }
            EditorGUILayout.LabelField("Shows a dialog when someone adds TMP components to scenes/prefabs.", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Best Practices", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("- Commit to version control before batch migration", richLabelStyle);
            EditorGUILayout.LabelField("- Migrate in short-lived branches (1-2 weeks)", richLabelStyle);
            EditorGUILayout.LabelField("- Migrate leaf prefabs before parent prefabs", richLabelStyle);
            EditorGUILayout.LabelField("- Migrate components before scripts", richLabelStyle);
            EditorGUILayout.LabelField("- Configure font mappings before migrating components", richLabelStyle);
            EditorGUILayout.EndVertical();
        }

        void DrawLog()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Migration Log", headerStyle);

            var filterOptions = new[] { "All", "Info", "Warning", "Error" };
            int filterIdx = logFilter.HasValue ? (int)logFilter.Value + 1 : 0;
            filterIdx = EditorGUILayout.Popup(filterIdx, filterOptions, GUILayout.Width(80));
            logFilter = filterIdx == 0 ? null : (LogSeverity?)(filterIdx - 1);

            if (GUILayout.Button("Export", GUILayout.Width(60)))
                ExportLog();
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
                logEntries.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            logScroll = EditorGUILayout.BeginScrollView(logScroll);

            foreach (var entry in logEntries)
            {
                if (logFilter.HasValue && entry.severity != logFilter.Value) continue;

                var style = entry.severity switch
                {
                    LogSeverity.Error => statusRed,
                    LogSeverity.Warning => statusYellow,
                    _ => EditorStyles.miniLabel,
                };

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{entry.timestamp}] {entry.message}", style);

                if (!string.IsNullOrEmpty(entry.backupPath))
                {
                    if (GUILayout.Button("Restore", GUILayout.Width(60)))
                    {
                        if (ScriptMigrator.RestoreFromBackup(entry.backupPath))
                        {
                            logEntries.Add(new LogEntry(LogSeverity.Info, $"Restored from backup: {entry.backupPath}"));
                            AssetDatabase.Refresh();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        void StartScan()
        {
            analyzer = new ProjectAnalyzer(stateData.excludedPaths);
            analyzer.StartScan(() =>
            {
                findings = analyzer.Findings;

                foreach (var f in findings)
                    f.status = stateData.GetStatus(f.id);

                foreach (var discovered in analyzer.DiscoveredFonts)
                {
                    bool exists = fontMappingsData.fontMappings.Exists(e => e.tmpFontGuid == discovered.tmpFontGuid);
                    if (!exists)
                        fontMappingsData.fontMappings.Add(discovered);
                }
                fontMappingsData.Save();

                stateData.lastScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                stateData.Save();

                logEntries.Add(new LogEntry(LogSeverity.Info,
                    $"Scan complete: {findings.Count} findings ({(analyzer.WasCancelled ? "partial" : "full")})"));

                Repaint();
            });
        }

        void VerifyMigrations()
        {
            int inconsistencies = 0;
            foreach (var f in findings)
            {
                if (f.status != MigrationStatus.Completed) continue;
                if (f.type != FindingType.Component) continue;

                try
                {
                    var content = File.ReadAllText(f.filePath);
                    if (content.Contains(f.scriptGuid))
                    {
                        f.status = MigrationStatus.NotStarted;
                        stateData.SetStatus(f.id, MigrationStatus.NotStarted);
                        inconsistencies++;
                    }
                }
                catch { }
            }

            if (inconsistencies > 0)
                logEntries.Add(new LogEntry(LogSeverity.Warning, $"Verification: {inconsistencies} item(s) marked Completed but still contain TMP. Status reset to Pending."));
            else
                logEntries.Add(new LogEntry(LogSeverity.Info, "Verification: all completed migrations are valid."));

            Repaint();
        }

        void MigrateSingleFinding(MigrationFinding finding)
        {
            EnsureComponentMigrator();
            if (!componentMigrator.IsTmpAvailable)
            {
                EditorUtility.DisplayDialog("TMP Not Found", "TextMesh Pro assemblies not detected. Install TMP package for component migration.", "OK");
                return;
            }

            if (finding.filePath.EndsWith(".prefab"))
                componentMigrator.MigratePrefab(finding.filePath, findings);
            else if (finding.filePath.EndsWith(".unity"))
                componentMigrator.MigrateScene(finding.filePath, findings);

            Repaint();
        }

        void MigrateSelected()
        {
            EnsureComponentMigrator();

            var byFile = new Dictionary<string, List<MigrationFinding>>();
            foreach (var f in findings)
            {
                if (!f.isSelected || f.status != MigrationStatus.NotStarted || f.type != FindingType.Component) continue;
                if (!byFile.ContainsKey(f.filePath))
                    byFile[f.filePath] = new List<MigrationFinding>();
                byFile[f.filePath].Add(f);
            }

            foreach (var kvp in byFile)
            {
                if (kvp.Key.EndsWith(".prefab"))
                    componentMigrator.MigratePrefab(kvp.Key, findings);
                else if (kvp.Key.EndsWith(".unity"))
                    componentMigrator.MigrateScene(kvp.Key, findings);
            }

            Repaint();
        }

        void SkipSelected()
        {
            foreach (var f in findings)
            {
                if (f.isSelected && f.status == MigrationStatus.NotStarted)
                {
                    f.status = MigrationStatus.Skipped;
                    stateData.SetStatus(f.id, MigrationStatus.Skipped);
                }
            }
            Repaint();
        }

        void MigrateAllSimple()
        {
            EnsureComponentMigrator();

            var simpleByFile = new Dictionary<string, List<MigrationFinding>>();
            foreach (var f in findings)
            {
                if (f.type != FindingType.Component || f.status != MigrationStatus.NotStarted || f.complexity != MigrationComplexity.Simple)
                    continue;
                if (!simpleByFile.ContainsKey(f.filePath))
                    simpleByFile[f.filePath] = new List<MigrationFinding>();
                simpleByFile[f.filePath].Add(f);
            }

            int total = simpleByFile.Count;
            int done = 0;
            foreach (var kvp in simpleByFile)
            {
                EditorUtility.DisplayProgressBar("Migrating...", kvp.Key, (float)done / total);

                if (kvp.Key.EndsWith(".prefab"))
                    componentMigrator.MigratePrefab(kvp.Key, findings);
                else if (kvp.Key.EndsWith(".unity"))
                    componentMigrator.MigrateScene(kvp.Key, findings);

                done++;
            }

            EditorUtility.ClearProgressBar();
            Repaint();
        }

        void ApplyAllScripts()
        {
            bool backup = true;
            foreach (var file in scriptFiles.ToList())
            {
                var replacements = ScriptMigrator.AnalyzeFile(file);
                var (ok, bakPath, err) = ScriptMigrator.ApplyReplacements(file, replacements, backup);
                if (ok)
                    logEntries.Add(new LogEntry(LogSeverity.Info, $"Migrated: {file}") { backupPath = bakPath });
                else
                    logEntries.Add(new LogEntry(LogSeverity.Error, $"Failed: {file} — {err}"));
            }
            AssetDatabase.Refresh();
            Repaint();
        }

        void CreateFontFromSource(FontMappingEntry entry)
        {
            byte[] fontBytes;
            try { fontBytes = File.ReadAllBytes(entry.sourceTtfPath); }
            catch (Exception ex)
            {
                logEntries.Add(new LogEntry(LogSeverity.Error, $"Cannot read font file: {entry.sourceTtfPath} — {ex.Message}"));
                return;
            }

            var dir = Path.GetDirectoryName(entry.sourceTtfPath);
            var name = Path.GetFileNameWithoutExtension(entry.sourceTtfPath);
            var fontAssetPath = $"{dir}/{name}.asset";
            var fontStackPath = $"{dir}/{name} Stack.asset";

            var existingFont = AssetDatabase.LoadAssetAtPath<UniTextFont>(fontAssetPath);
            if (existingFont != null)
            {
                entry.uniTextFontGuid = AssetDatabase.AssetPathToGUID(fontAssetPath);
                var existingStack = AssetDatabase.LoadAssetAtPath<UniTextFontStack>(fontStackPath);
                if (existingStack != null)
                    entry.uniTextFontStackGuid = AssetDatabase.AssetPathToGUID(fontStackPath);
                fontMappingsData.Save();
                logEntries.Add(new LogEntry(LogSeverity.Info, $"Font already exists: {fontAssetPath}"));
                return;
            }

            var fontAsset = UniTextFont.CreateFontAsset(fontBytes);
            if (fontAsset == null)
            {
                logEntries.Add(new LogEntry(LogSeverity.Error, $"Failed to create font asset from {entry.sourceTtfPath}"));
                return;
            }

            AssetDatabase.CreateAsset(fontAsset, fontAssetPath);
            entry.uniTextFontGuid = AssetDatabase.AssetPathToGUID(fontAssetPath);

            var fontStack = CreateInstance<UniTextFontStack>();
            AssetDatabase.CreateAsset(fontStack, fontStackPath);

            var stackSo = new SerializedObject(fontStack);
            var familiesProp = stackSo.FindProperty("families");
            familiesProp.arraySize = 1;
            var primaryProp = familiesProp.GetArrayElementAtIndex(0).FindPropertyRelative("primary");
            primaryProp.objectReferenceValue = fontAsset;
            stackSo.ApplyModifiedPropertiesWithoutUndo();

            entry.uniTextFontStackGuid = AssetDatabase.AssetPathToGUID(fontStackPath);
            fontMappingsData.Save();
            AssetDatabase.SaveAssets();

            logEntries.Add(new LogEntry(LogSeverity.Info, $"Created font: {fontAssetPath} + stack: {fontStackPath}"));
        }

        void ExportReport()
        {
            var path = EditorUtility.SaveFilePanel("Export Migration Report", "", "migration-report.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# TMP → UniText Migration Report");
            sb.AppendLine($"# Generated: {DateTime.Now}");
            sb.AppendLine($"# Total findings: {findings.Count}");
            sb.AppendLine();

            summary = MigrationSummary.Compute(findings);
            sb.AppendLine($"Simple: {summary.simpleCount} ({Pct(summary.simpleCount)})");
            sb.AppendLine($"Moderate: {summary.moderateCount} ({Pct(summary.moderateCount)})");
            sb.AppendLine($"Complex: {summary.complexCount} ({Pct(summary.complexCount)})");
            sb.AppendLine($"Manual: {summary.manualCount} ({Pct(summary.manualCount)})");
            sb.AppendLine();

            foreach (var f in findings)
            {
                sb.AppendLine($"[{f.status}] [{f.complexity}] {f.filePath} — {f.details}");
                if (f.warnings != null)
                    foreach (var w in f.warnings)
                        sb.AppendLine($"    {w}");
            }

            File.WriteAllText(path, sb.ToString());
            logEntries.Add(new LogEntry(LogSeverity.Info, $"Report exported to {path}"));
        }

        void ExportLog()
        {
            var path = EditorUtility.SaveFilePanel("Export Log", "", "migration-log.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new System.Text.StringBuilder();
            foreach (var e in logEntries)
                sb.AppendLine($"[{e.timestamp}] [{e.severity}] {e.message}");
            File.WriteAllText(path, sb.ToString());
        }

        void EnsureComponentMigrator()
        {
            if (componentMigrator == null)
            {
                componentMigrator = new ComponentMigrator(logEntries, fontMappingsData, stateData);
                componentMigrator.Initialize();
            }
        }

        bool MatchesFilter(MigrationFinding f)
        {
            if (filterType.HasValue && f.type != filterType.Value) return false;
            if (filterStatus.HasValue)
            {
                if (filterStatus.Value == MigrationStatus.NotStarted && f.status != MigrationStatus.NotStarted) return false;
                if (filterStatus.Value != MigrationStatus.NotStarted && f.status != filterStatus.Value) return false;
            }
            if (!string.IsNullOrEmpty(searchText) &&
                !f.filePath.Contains(searchText, StringComparison.OrdinalIgnoreCase) &&
                !f.details.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        bool IsFontMappingComplete()
        {
            foreach (var entry in fontMappingsData.fontMappings)
                if (!entry.IsMapped) return false;
            return true;
        }

        int CountSimplePending()
        {
            int count = 0;
            foreach (var f in findings)
                if (f.type == FindingType.Component && f.status == MigrationStatus.NotStarted && f.complexity == MigrationComplexity.Simple)
                    count++;
            return count;
        }

        int CountByTypeAndStatus(FindingType type, MigrationStatus status)
        {
            int count = 0;
            foreach (var f in findings)
                if (f.type == type && f.status == status)
                    count++;
            return count;
        }

        string Pct(int count)
        {
            return summary.totalFindings == 0 ? "0%" : $"{count * 100 / summary.totalFindings}%";
        }

        void DrawOrderStep(int step, string label, int count, bool done)
        {
            var prefix = done ? " " : (count > 0 ? " " : " ");
            EditorGUILayout.LabelField($"  {prefix} {step}. {label} ({count})", done ? statusGreen : (count > 0 ? richLabelStyle : statusGray));
        }

        void DrawSummaryRow(string label, int count)
        {
            if (count == 0) return;
            EditorGUILayout.LabelField($"  {label}: {count}", richLabelStyle);
        }

        void DrawWarnings()
        {
            if (!IsFontMappingComplete())
            {
                int unmapped = fontMappingsData.fontMappings.Count(e => !e.IsMapped);
                EditorGUILayout.HelpBox($"{unmapped} TMP font(s) have no UniText mapping. Configure in Font Mapping tab.", MessageType.Warning);
            }

            int manualScripts = 0;
            foreach (var f in findings)
                if (f.type == FindingType.ScriptReference && f.complexity == MigrationComplexity.Manual)
                    manualScripts++;
            if (manualScripts > 0)
                EditorGUILayout.HelpBox($"{manualScripts} script(s) use TMP APIs with no UniText equivalent (manual migration needed).", MessageType.Warning);

            int compiledDeps = 0;
            foreach (var f in findings)
                if (f.type == FindingType.CompiledDependency)
                    compiledDeps++;
            if (compiledDeps > 0)
                EditorGUILayout.HelpBox($"{compiledDeps} compiled assembly/assemblies reference TMP types.", MessageType.Warning);
        }

        static void PingAsset(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }
    }
}
