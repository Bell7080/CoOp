using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CoOpHorror.Editor
{
    [InitializeOnLoad]
    public static class ProjectVersionSync
    {
        static ProjectVersionSync() => EditorApplication.delayCall += Apply;

        public static string Current
        {
            get
            {
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "VERSION"));
                var version = File.ReadAllText(path).Trim();
                if (!Regex.IsMatch(version, @"^0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"))
                    throw new InvalidOperationException("VERSION must be 0.minor.patch until the team explicitly approves the 1.0.0 release.");
                return version;
            }
        }

        [MenuItem("CoOp/Version/Apply VERSION to Player Settings")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // MPPM clone players run from Library/VP without a VERSION file; only the main project applies it.
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "VERSION")))) return;
            if (PlayerSettings.bundleVersion != Current) PlayerSettings.bundleVersion = Current;
        }

        [MenuItem("CoOp/Version/Open changelog")]
        public static void OpenChangelog() => EditorUtility.OpenWithDefaultApp(Path.Combine(Application.dataPath, "..", "CHANGELOG.md"));
    }

    public sealed class VersionFileWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.delayCall += ProjectVersionSync.Apply;
        }
    }
}
