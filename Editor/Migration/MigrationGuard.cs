using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Optional AssetPostprocessor that warns when new TMP components are added
    /// to scenes/prefabs during the migration period.
    /// Activated via Settings tab in UniTextMigrationWindow.
    /// </summary>
    internal class MigrationGuard : AssetPostprocessor
    {
        static bool IsEnabled
        {
            get
            {
                var state = MigrationStateData.Load();
                return state.migrationGuardEnabled;
            }
        }

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!IsEnabled) return;

            foreach (var path in importedAssets)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".unity" && ext != ".prefab") continue;

                string content;
                try { content = File.ReadAllText(path); }
                catch { continue; }

                bool hasTmp = content.Contains(MigrationMapping.TmpTextUiGuid)
                              || content.Contains(MigrationMapping.TmpText3dGuid)
                              || content.Contains(MigrationMapping.TmpInputFieldGuid);

                if (!hasTmp) continue;

                var choice = EditorUtility.DisplayDialogComplex(
                    "TMP Component Detected",
                    $"'{Path.GetFileName(path)}' contains TextMesh Pro components.\n\n" +
                    "This project is being migrated to UniText. Consider using UniText instead.",
                    "OK",
                    "Don't warn again",
                    "Open Migration Tool"
                );

                switch (choice)
                {
                    case 1:
                        var state = MigrationStateData.Load();
                        state.migrationGuardEnabled = false;
                        state.Save();
                        break;
                    case 2:
                        EditorApplication.ExecuteMenuItem("Tools/UniText Migration");
                        break;
                }

                break;
            }
        }
    }
}
