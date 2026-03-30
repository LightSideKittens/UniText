using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal sealed class UniTextDefaultsGuard : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (deletedAssets.Length == 0) return;
            EditorApplication.delayCall += () => UniTextSettingsProvider.EnsureDefaults();
        }
    }

    internal sealed class UniTextSettingsMoveGuard : AssetModificationProcessor
    {
        private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
        {
            if (sourcePath == UniTextSettingsProvider.AssetPath && destinationPath != sourcePath)
            {
                Debug.LogWarning("[UniText] UniTextSettings must stay in Resources/ for runtime loading.");
                return AssetMoveResult.FailedMove;
            }
            return AssetMoveResult.DidNotMove;
        }
    }
}
