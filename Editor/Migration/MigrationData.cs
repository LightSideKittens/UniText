using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LightSide
{
    internal enum FindingType : byte
    {
        Component,
        ScriptReference,
        FontAsset,
        Material,
        Animation,
        AssemblyDef,
        RichTextContent,
        MissingScript,
        CompiledDependency,
    }

    internal enum MigrationStatus : byte
    {
        NotStarted,
        Completed,
        Skipped,
        Failed,
    }

    internal enum MigrationComplexity : byte
    {
        Simple,
        Moderate,
        Complex,
        Manual,
    }

    internal enum LogSeverity : byte
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// One discovered TMP usage in the project.
    /// Identity is based on <see cref="id"/> which is stable across renames.
    /// </summary>
    [Serializable]
    internal class MigrationFinding
    {
        /// <summary>Stable identity: hash of filePath + scriptGUID + fileID (or filePath + lineContent for scripts).</summary>
        public string id;

        /// <summary>Relative path from project root (e.g. "Assets/Scenes/Main.unity").</summary>
        public string filePath;

        public FindingType type;
        public MigrationComplexity complexity;

        /// <summary>Human-readable description (e.g. "TextMeshProUGUI on 'Title'").</summary>
        public string details;

        /// <summary>Transform path inside the scene/prefab (for component findings).</summary>
        public string objectPath;

        /// <summary>The specific TMP script GUID found (for component findings).</summary>
        public string scriptGuid;

        /// <summary>Unity fileID for the object in the YAML file (for stable identity).</summary>
        public string fileID;

        /// <summary>Line number (for script findings).</summary>
        public int lineNumber;

        /// <summary>Specific warnings for this finding.</summary>
        public List<string> warnings;

        /// <summary>Dependency info: IDs of findings this one depends on.</summary>
        public List<string> dependsOn;

        [NonSerialized] public MigrationStatus status;
        [NonSerialized] public string errorMessage;
        [NonSerialized] public bool isSelected;

        public static string ComputeId(string filePath, string scriptGuid, string fileID)
        {
            return ComputeHash($"{filePath}|{scriptGuid}|{fileID}");
        }

        public static string ComputeIdForScript(string filePath, int lineNumber, string lineContent)
        {
            return ComputeHash($"{filePath}|{lineNumber}|{lineContent}");
        }

        public static string ComputeIdForAsset(string filePath, FindingType type)
        {
            return ComputeHash($"{filePath}|{type}");
        }

        static string ComputeHash(string input)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;
                for (int i = 0; i < input.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ input[i];
                    if (i + 1 < input.Length)
                        hash2 = ((hash2 << 5) + hash2) ^ input[i + 1];
                }
                long combined = ((long)(uint)hash1 << 32) | (uint)(hash1 + hash2 * 1566083941);
                return combined.ToString("x16");
            }
        }
    }

    /// <summary>
    /// Maps a TMP_FontAsset to a UniTextFont + UniTextFontStack.
    /// Stored by asset GUID (stable across moves/renames).
    /// </summary>
    [Serializable]
    internal class FontMappingEntry
    {
        /// <summary>GUID of the TMP_FontAsset .asset file.</summary>
        public string tmpFontGuid;
        /// <summary>Display name from the TMP_FontAsset (for UI).</summary>
        public string tmpFontName;
        /// <summary>Font family name extracted from TMP font metadata.</summary>
        public string tmpFamilyName;
        /// <summary>Auto-detected TTF/OTF source path (may be empty).</summary>
        public string sourceTtfPath;
        /// <summary>GUID of the assigned UniTextFont asset (empty = unmapped).</summary>
        public string uniTextFontGuid;
        /// <summary>GUID of the assigned UniTextFontStack asset (empty = unmapped).</summary>
        public string uniTextFontStackGuid;
        /// <summary>True if user explicitly skipped this font.</summary>
        public bool skipped;

        public bool IsMapped => !string.IsNullOrEmpty(uniTextFontStackGuid) || skipped;
        public bool HasSource => !string.IsNullOrEmpty(sourceTtfPath);
    }

    /// <summary>One text substitution proposed for a .cs file.</summary>
    [Serializable]
    internal class ScriptReplacement
    {
        public int lineNumber;
        public int columnStart;
        public int columnEnd;
        public string original;
        public string replacement;
        public bool isWarningOnly;
        public string warningMessage;
        public bool isSelected = true;
    }

    [Serializable]
    internal class LogEntry
    {
        public string timestamp;
        public LogSeverity severity;
        public string message;
        /// <summary>If this is a script backup, the path to the .bak file for restore.</summary>
        public string backupPath;

        public LogEntry() { }

        public LogEntry(LogSeverity severity, string message)
        {
            timestamp = DateTime.Now.ToString("HH:mm:ss");
            this.severity = severity;
            this.message = message;
        }
    }

    [Serializable]
    internal class FontMappingsData
    {
        public int schemaVersion = 1;
        public List<FontMappingEntry> fontMappings = new();

        const string Path = "ProjectSettings/UniText/FontMappings.json";

        public static FontMappingsData Load()
        {
            if (!File.Exists(Path))
                return new FontMappingsData();
            try
            {
                return JsonUtility.FromJson<FontMappingsData>(File.ReadAllText(Path))
                       ?? new FontMappingsData();
            }
            catch
            {
                return new FontMappingsData();
            }
        }

        public void Save()
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonUtility.ToJson(this, true));
        }
    }

    [Serializable]
    internal class FindingStatusEntry
    {
        public string id;
        public MigrationStatus status;
    }

    [Serializable]
    internal class MigrationStateData
    {
        public int schemaVersion = 1;
        public string lastScanTime;
        public List<FindingStatusEntry> statuses = new();
        public List<string> excludedPaths = new();
        public bool migrationGuardEnabled;

        const string Path = "ProjectSettings/UniText/MigrationState.json";

        public static MigrationStateData Load()
        {
            if (!File.Exists(Path))
                return new MigrationStateData();
            try
            {
                return JsonUtility.FromJson<MigrationStateData>(File.ReadAllText(Path))
                       ?? new MigrationStateData();
            }
            catch
            {
                return new MigrationStateData();
            }
        }

        public void Save()
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonUtility.ToJson(this, true));
        }

        public MigrationStatus GetStatus(string findingId)
        {
            for (int i = 0; i < statuses.Count; i++)
                if (statuses[i].id == findingId)
                    return statuses[i].status;
            return MigrationStatus.NotStarted;
        }

        public void SetStatus(string findingId, MigrationStatus status)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].id == findingId)
                {
                    statuses[i].status = status;
                    Save();
                    return;
                }
            }
            statuses.Add(new FindingStatusEntry { id = findingId, status = status });
            Save();
        }
    }

    internal struct MigrationSummary
    {
        public int totalFindings;
        public int completed;
        public int pending;
        public int skipped;
        public int failed;

        public int simpleCount;
        public int moderateCount;
        public int complexCount;
        public int manualCount;

        public int componentCount;
        public int scriptCount;
        public int fontCount;
        public int materialCount;
        public int animationCount;
        public int asmdefCount;
        public int richTextContentCount;
        public int missingScriptCount;

        public float ProgressPercent =>
            totalFindings == 0 ? 0f : (float)(completed + skipped) / totalFindings;

        public static MigrationSummary Compute(List<MigrationFinding> findings)
        {
            var s = new MigrationSummary { totalFindings = findings.Count };
            for (int i = 0; i < findings.Count; i++)
            {
                var f = findings[i];

                switch (f.status)
                {
                    case MigrationStatus.Completed: s.completed++; break;
                    case MigrationStatus.Skipped: s.skipped++; break;
                    case MigrationStatus.Failed: s.failed++; break;
                    default: s.pending++; break;
                }

                switch (f.complexity)
                {
                    case MigrationComplexity.Simple: s.simpleCount++; break;
                    case MigrationComplexity.Moderate: s.moderateCount++; break;
                    case MigrationComplexity.Complex: s.complexCount++; break;
                    case MigrationComplexity.Manual: s.manualCount++; break;
                }

                switch (f.type)
                {
                    case FindingType.Component: s.componentCount++; break;
                    case FindingType.ScriptReference: s.scriptCount++; break;
                    case FindingType.FontAsset: s.fontCount++; break;
                    case FindingType.Material: s.materialCount++; break;
                    case FindingType.Animation: s.animationCount++; break;
                    case FindingType.AssemblyDef: s.asmdefCount++; break;
                    case FindingType.RichTextContent: s.richTextContentCount++; break;
                    case FindingType.MissingScript: s.missingScriptCount++; break;
                }
            }
            return s;
        }
    }
}
