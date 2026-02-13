using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using LightSide;


namespace LightSide
{
    [InitializeOnLoad]
    internal class UniTextBuildProcessor : IPreprocessBuildWithReport, IActiveBuildTargetChanged
    {
        public int callbackOrder => -100;

        static UniTextBuildProcessor()
        {
            Cat.Meow($"[UniText] InitializeOnLoad, target: {EditorUserBuildSettings.activeBuildTarget}");

            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                ValidateWebGLSettings();
            }
        }

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            Cat.Meow($"[UniText] Build target changed: {previousTarget} → {newTarget}");
            if (newTarget == BuildTarget.WebGL)
            {
                ValidateWebGLSettings();
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            Cat.Meow($"[UniText] OnPreprocessBuild, platform: {report.summary.platformGroup}");

            if (report.summary.platformGroup == BuildTargetGroup.WebGL)
            {
                ValidateWebGLSettings();
            }
        }

        private static void ValidateWebGLSettings()
        {
            var colorSpace = PlayerSettings.colorSpace;
            var isAutoAPI = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL);
            var graphicsAPIs = isAutoAPI ? System.Array.Empty<GraphicsDeviceType>() : PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);

            Cat.Meow($"[UniText] ValidateWebGLSettings: colorSpace={colorSpace}, autoAPI={isAutoAPI}, APIs=[{string.Join(", ", graphicsAPIs)}]");

            if (colorSpace != ColorSpace.Linear)
                return;

            if (isAutoAPI)
            {
                Cat.Meow("[UniText] Disabling Auto Graphics API for WebGL");
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
                graphicsAPIs = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);
                Cat.Meow($"[UniText] After disabling auto: APIs=[{string.Join(", ", graphicsAPIs)}]");
            }

            if (!graphicsAPIs.Contains(GraphicsDeviceType.OpenGLES2))
            {
                Cat.Meow("[UniText] No WebGL 1.0 found, settings OK");
                return;
            }

            var newAPIs = graphicsAPIs
                .Where(api => api != GraphicsDeviceType.OpenGLES2)
                .DefaultIfEmpty(GraphicsDeviceType.OpenGLES3)
                .Distinct()
                .ToArray();

            Cat.Meow($"[UniText] Switching to WebGL 2.0: [{string.Join(", ", newAPIs)}]");
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, newAPIs);
        }
    }

}
