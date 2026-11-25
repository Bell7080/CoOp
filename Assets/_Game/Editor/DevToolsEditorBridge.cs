using System;
using System.Reflection;
using CoOpHorror.Testing;
using UnityEditor;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Editor side of the in-game debug tools. The "Player 2" button stops Play, makes the DeveloperHostClient
    // Play Mode Scenario active and starts it (main Editor + Player 2 clone); when that session ends the
    // default configuration (main Editor only) is restored, so a normal Play never launches the clone.
    // Unity 6.3 has no public API for this; it goes through PlayModeManager like the Unity CLI pipeline does.
    [InitializeOnLoad]
    public static class DevToolsEditorBridge
    {
        public const string ScenarioPath = "Assets/Settings/PlayMode/DeveloperHostClient.asset";
        private const string PendingKey = "CoOp.Dev.Player2Pending";
        private const string SessionKey = "CoOp.Dev.Player2Session";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        static DevToolsEditorBridge()
        {
            DevTools.RequestPlayer2 = RequestPlayer2;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void RequestPlayer2()
        {
            SessionState.SetBool(PendingKey, true);
            EditorApplication.ExitPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            if (SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                SessionState.SetBool(SessionKey, true);
                EditorApplication.delayCall += StartWithPlayer2;
            }
            else if (SessionState.GetBool(SessionKey, false))
            {
                SessionState.SetBool(SessionKey, false);
                UseMainEditorOnly();
            }
        }

        private static void StartWithPlayer2()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ScenarioPath);
            var manager = Manager(out var type);
            if (scenario == null || manager == null) { Debug.LogWarning("[DevTools] Player 2 scenario or play mode manager not found."); return; }
            type.GetProperty("ActivePlayModeConfig", Flags)?.SetValue(manager, scenario);
            type.GetMethod("Start", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(manager, null);
        }

        // Normal Play: the main Editor alone.
        [MenuItem("CoOp/Dev/Play with main Editor only")]
        public static void UseMainEditorOnly()
        {
            var manager = Manager(out var type);
            if (manager == null) return;
            var fallback = type.GetProperty("DefaultConfig", Flags)?.GetValue(manager);
            if (fallback != null) type.GetProperty("ActivePlayModeConfig", Flags)?.SetValue(manager, fallback);
        }

        private static object Manager(out Type type)
        {
            type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType("Unity.PlayMode.Editor.PlayModeManager", false);
                if (type != null) break;
            }
            return type?.GetProperty("instance", Flags)?.GetValue(null);
        }
    }
}
