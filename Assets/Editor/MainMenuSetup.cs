using Dragoneye.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-off scaffolding: swaps the old single-purpose session menu for the routed main menu.
    /// Disposable once it has run.
    ///
    /// Safe to re-run: it moves a misplaced component rather than assuming a clean scene.
    /// </summary>
    static class MainMenuSetup
    {
        const string k_MenuScene = "Assets/Scenes/MainMenu.unity";
        const string k_BootScene = "Assets/Scenes/Bootstrap.unity";

        /// <summary>The document MainMenuUI drives. The menu scene has more than one.</summary>
        const string k_MenuDocument = "SessionMenu";

        [MenuItem("ClaudeCode/Rewire Main Menu")]
        static void SetUp()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Run();
            }
        }

        /// <summary>Runs the whole step. Called directly by the master setup.</summary>
        internal static void Run()
        {

            var scene = EditorSceneManager.OpenScene(k_MenuScene, OpenSceneMode.Single);

            // Select by the asset the document sources, not by "any UIDocument". The menu scene also
            // holds the draft panel's document, and FindAnyObjectByType picks between them
            // arbitrarily -- which is how MainMenuUI ended up on the draft panel, looking for
            // home-panel in markup that has never contained it.
            var documents = Object.FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            UIDocument menu = null;
            foreach (var document in documents)
            {
                if (document.visualTreeAsset != null && document.visualTreeAsset.name == k_MenuDocument)
                {
                    menu = document;
                    break;
                }
            }

            if (menu == null)
            {
                Debug.LogError($"No UIDocument in the menu scene sources {k_MenuDocument}.uxml. "
                    + "Assign it on the 'Session UI' object and run this again.");
                return;
            }

            // A previous run may have put the component on the wrong object.
            var stray = 0;
            foreach (var existing in Object.FindObjectsByType<MainMenuUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (existing.gameObject != menu.gameObject)
                {
                    Object.DestroyImmediate(existing);
                    stray++;
                }
            }

            // SessionMenuUI no longer exists, so its object is holding a component with no script.
            var stripped = 0;
            foreach (var document in documents)
            {
                stripped += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(document.gameObject);
            }

            if (menu.GetComponent<MainMenuUI>() == null)
            {
                // m_Runner is left empty on purpose: it falls back to the persistent
                // SessionRunner.Instance, which does not exist in this scene to be wired to.
                menu.gameObject.AddComponent<MainMenuUI>();
            }

            // Offers the chosen character to the host once there is one. In the menu scene because
            // that is where a lobby is joined, and it stops when the scene does.
            if (menu.GetComponent<Dragoneye.Game.CharacterSubmitter>() == null)
            {
                menu.gameObject.AddComponent<Dragoneye.Game.CharacterSubmitter>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"Main menu rewired: MainMenuUI on '{menu.gameObject.name}', "
                + $"{stripped} dead component(s) removed, {stray} misplaced component(s) cleaned up. "
                + "Delete Assets/Editor/MainMenuSetup.cs once you have verified play mode.");

            // Leave the boot scene open, not the one that was edited. Play from MainMenu and the
            // NetworkManager and SessionRunner never exist, so no match can start -- landing the
            // editor there after a rewire invites exactly that.
            EditorSceneManager.OpenScene(k_BootScene, OpenSceneMode.Single);
        }
    }
}
