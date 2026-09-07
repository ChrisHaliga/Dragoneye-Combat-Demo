using Dragoneye.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// What the map choices need on disk, and one piece of tidying.
    ///
    /// The islands stand in water, which is a terrain the project did not have. This writes the
    /// three shipped terrains -- grass, stone, water -- from their specs and binds all three into
    /// the arena's authored map, which is where the arena reads its palette from. It also strips
    /// the one dead component the playback left on the arena.
    ///
    /// Disposable: run it once, then delete this file. The arena map step does the same
    /// authoring on a fresh clone.
    /// </summary>
    static class MapChoicesSetup
    {
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

        [MenuItem("ClaudeCode/Author The Map Choices")]
        static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ArenaMapSetup.AuthorTerrainAndMap();

            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);
            var context = Object.FindAnyObjectByType<ArenaContext>();

            if (context != null)
            {
                var stripped = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(context.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"Arena tidied: {stripped} dead component(s) removed.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Map choices authored. Delete Assets/Editor/MapChoicesSetup.cs now.");
        }
    }
}
