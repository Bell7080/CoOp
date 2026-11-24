using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace CoOpHorror.Editor
{
    public static class WindowsBuild
    {
        [MenuItem("CoOp/Build/Windows development player")]
        public static void Development()
        {
            ProjectVersionSync.Apply();
            var root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var output = Path.Combine(root, "Builds", "WindowsDev", "CoOpHorror.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { DevelopmentProjectSetup.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
        }
    }
}
