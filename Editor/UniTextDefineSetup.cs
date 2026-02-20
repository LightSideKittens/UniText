#if !UNITEXT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace LightSide
{
    [InitializeOnLoad]
    internal static class UniTextDefineSetup
    {
        private const string Define = "UNITEXT";

        static UniTextDefineSetup()
        {
            var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (targetGroup == BuildTargetGroup.Unknown)
                return;

#if UNITY_2023_1_OR_NEWER
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
            var defines = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
#else
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
#endif

            var set = new HashSet<string>(defines.Split(
                new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            if (!set.Add(Define))
                return;

#if UNITY_2023_1_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, string.Join(";", set));
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, string.Join(";", set));
#endif
        }
    }
}
#endif
