using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LightSide
{
    /// <summary>
    /// Transforms C# source files to replace TMP references with UniText equivalents.
    /// Pure text transformation using regex patterns from <see cref="MigrationMapping"/>.
    /// </summary>
    internal static class ScriptMigrator
    {
        /// <summary>
        /// Analyze a C# file and return all proposed replacements.
        /// </summary>
        public static List<ScriptReplacement> AnalyzeFile(string filePath)
        {
            string content;
            try { content = File.ReadAllText(filePath); }
            catch { return new List<ScriptReplacement>(); }

            var lines = content.Split('\n');
            var replacements = new List<ScriptReplacement>();
            bool alreadyHasLightSide = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("using LightSide;"))
                {
                    alreadyHasLightSide = true;
                    break;
                }
            }

            bool inPreprocessorBlock = false;
            int preprocessorDepth = 0;

            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                var line = lines[lineIdx];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#if") && (trimmed.Contains("TEXTMESHPRO") || trimmed.Contains("TMP")))
                {
                    inPreprocessorBlock = true;
                    preprocessorDepth = 1;
                    replacements.Add(new ScriptReplacement
                    {
                        lineNumber = lineIdx + 1,
                        original = line.TrimEnd('\r'),
                        isWarningOnly = true,
                        warningMessage = "#if block with TMP reference — review manually. Consider replacing with #if UNITEXT_PRESENT or removing the #if.",
                    });
                    continue;
                }

                if (inPreprocessorBlock)
                {
                    if (trimmed.StartsWith("#if")) preprocessorDepth++;
                    if (trimmed.StartsWith("#endif"))
                    {
                        preprocessorDepth--;
                        if (preprocessorDepth <= 0)
                            inPreprocessorBlock = false;
                    }

                    continue;
                }

                foreach (var pattern in MigrationMapping.ScriptPatterns)
                {
                    var matches = pattern.regex.Matches(line);
                    if (matches.Count == 0) continue;

                    foreach (Match match in matches)
                    {
                        if (!pattern.warningOnly && pattern.replacement == "using LightSide;" && alreadyHasLightSide)
                            continue;

                        replacements.Add(new ScriptReplacement
                        {
                            lineNumber = lineIdx + 1,
                            columnStart = match.Index,
                            columnEnd = match.Index + match.Length,
                            original = match.Value,
                            replacement = pattern.warningOnly ? null : pattern.regex.Replace(match.Value, pattern.replacement),
                            isWarningOnly = pattern.warningOnly,
                            warningMessage = pattern.warningMessage,
                            isSelected = !pattern.warningOnly,
                        });
                    }
                }
            }

            if (alreadyHasLightSide)
            {
                replacements.RemoveAll(r =>
                    !r.isWarningOnly &&
                    r.replacement != null &&
                    r.replacement.Contains("using LightSide;"));
            }

            return replacements;
        }

        /// <summary>
        /// Generate a unified diff string for display, with context lines.
        /// Uses IMGUI rich text for coloring (red = removed, green = added).
        /// </summary>
        public static string GenerateDiff(string filePath, List<ScriptReplacement> replacements)
        {
            string content;
            try { content = File.ReadAllText(filePath); }
            catch { return "Error: cannot read file"; }

            var lines = content.Split('\n');
            var sb = new StringBuilder();
            const int contextLines = 3;

            var changedLines = new HashSet<int>();
            foreach (var r in replacements)
                changedLines.Add(r.lineNumber);

            var sortedLines = new List<int>(changedLines);
            sortedLines.Sort();

            int lastPrintedLine = -1;

            foreach (var lineNum in sortedLines)
            {
                int idx = lineNum - 1;

                int contextStart = Math.Max(0, idx - contextLines);
                if (lastPrintedLine >= 0 && contextStart <= lastPrintedLine + 1)
                    contextStart = lastPrintedLine + 1;
                else if (lastPrintedLine >= 0)
                    sb.AppendLine("  ...");

                for (int i = contextStart; i < idx; i++)
                {
                    sb.AppendLine($"  {i + 1,4}  {EscapeRichText(lines[i].TrimEnd('\r'))}");
                    lastPrintedLine = i;
                }

                var lineReplacements = new List<ScriptReplacement>();
                foreach (var r in replacements)
                {
                    if (r.lineNumber == lineNum)
                        lineReplacements.Add(r);
                }

                var originalLine = idx < lines.Length ? lines[idx].TrimEnd('\r') : "";

                foreach (var r in lineReplacements)
                {
                    if (r.isWarningOnly)
                    {
                        sb.AppendLine($"  {lineNum,4}  {EscapeRichText(originalLine)}");
                        sb.AppendLine($"  <color=#FFD700>     ^ {EscapeRichText(r.warningMessage)}</color>");
                    }
                    else
                    {
                        sb.AppendLine($"  <color=#FF6B6B>- {lineNum,4}  {EscapeRichText(r.original)}</color>");
                        sb.AppendLine($"  <color=#69DB7C>+ {lineNum,4}  {EscapeRichText(r.replacement)}</color>");
                    }
                }

                lastPrintedLine = idx;

                int contextEnd = Math.Min(lines.Length - 1, idx + contextLines);
                for (int i = idx + 1; i <= contextEnd; i++)
                {
                    sb.AppendLine($"  {i + 1,4}  {EscapeRichText(lines[i].TrimEnd('\r'))}");
                    lastPrintedLine = i;
                }
            }

            return sb.ToString();
        }

        static string EscapeRichText(string text)
        {
            return text.Replace("<", "\\<");
        }

        /// <summary>
        /// Apply selected replacements to the file. Returns true on success.
        /// Creates .bak backup if <paramref name="createBackup"/> is true.
        /// </summary>
        public static (bool success, string backupPath, string error) ApplyReplacements(
            string filePath,
            List<ScriptReplacement> replacements,
            bool createBackup)
        {
            string content;
            try { content = File.ReadAllText(filePath); }
            catch (Exception ex) { return (false, null, $"Cannot read {filePath}: {ex.Message}"); }

            var selected = new List<ScriptReplacement>();
            foreach (var r in replacements)
            {
                if (r.isSelected && !r.isWarningOnly && r.replacement != null)
                    selected.Add(r);
            }

            if (selected.Count == 0)
                return (true, null, null);

            string backupPath = null;
            if (createBackup)
            {
                backupPath = filePath + ".bak";
                try { File.Copy(filePath, backupPath, true); }
                catch (Exception ex) { return (false, null, $"Cannot create backup: {ex.Message}"); }
            }

            var lines = content.Split('\n');

            var byLine = new Dictionary<int, List<ScriptReplacement>>();
            foreach (var r in selected)
            {
                int idx = r.lineNumber - 1;
                if (!byLine.ContainsKey(idx))
                    byLine[idx] = new List<ScriptReplacement>();
                byLine[idx].Add(r);
            }

            foreach (var kvp in byLine)
            {
                int idx = kvp.Key;
                if (idx >= lines.Length) continue;

                var line = lines[idx];
                kvp.Value.Sort((a, b) => b.columnStart.CompareTo(a.columnStart));

                foreach (var r in kvp.Value)
                {
                    if (r.columnStart == 0 && r.columnEnd == 0)
                    {
                        line = MigrationMapping.ScriptPatterns[0].regex.Replace(line, r.replacement);
                        foreach (var p in MigrationMapping.ScriptPatterns)
                        {
                            if (!p.warningOnly && p.regex.IsMatch(r.original))
                            {
                                line = p.regex.Replace(line, p.replacement);
                                break;
                            }
                        }
                    }
                    else if (r.columnStart >= 0 && r.columnEnd <= line.Length)
                    {
                        line = line.Substring(0, r.columnStart) + r.replacement + line.Substring(r.columnEnd);
                    }
                }

                lines[idx] = line;
            }

            try
            {
                File.WriteAllText(filePath, string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                return (false, backupPath, $"Cannot write {filePath}: {ex.Message}");
            }

            return (true, backupPath, null);
        }

        /// <summary>
        /// Restore a file from its .bak backup.
        /// </summary>
        public static bool RestoreFromBackup(string backupPath)
        {
            if (!File.Exists(backupPath)) return false;

            var originalPath = backupPath.Substring(0, backupPath.Length - 4);
            try
            {
                File.Copy(backupPath, originalPath, true);
                File.Delete(backupPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
