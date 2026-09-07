using Dragoneye.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Clears the arena of the one component the playback made redundant.
    ///
    /// The attack lean moved onto the token, so <c>AttackFlourish</c> is gone from the project
    /// and the Arena scene holds a component with no script behind it. This strips it. The
    /// playback itself needs no wiring: the arena context makes it when it wakes.
    ///
    /// Disposable: run it once, then delete this file.
    /// </summary>
    static class PlaybackTidySetup
    {
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

        [MenuItem("ClaudeCode/Tidy The Arena For The Playback")]
        static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);
            var context = Object.FindAnyObjectByType<ArenaContext>();

            if (context == null)
            {
                Debug.LogError("No ArenaContext in the arena; nothing to tidy.");
                return;
            }

            var stripped = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(context.gameObject);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"Arena tidied: {stripped} dead component(s) removed. "
                + "Delete Assets/Editor/PlaybackTidySetup.cs now.");
        }
    }
}
