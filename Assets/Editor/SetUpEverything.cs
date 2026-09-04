using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Runs every outstanding setup step, in the order they depend on each other.
    ///
    /// The steps exist separately because each was written for one change and each is safe to
    /// re-run; this is the one command that applies all of them to a clone that has had none. Order
    /// is not incidental -- the turn system reads references the arena rewire assigns, and the
    /// content step needs the menu component the menu rewire adds.
    ///
    /// Every step is idempotent, so running this on an already-configured project is a no-op that
    /// re-saves the same scenes.
    /// </summary>
    static class SetUpEverything
    {
        const string k_BootScene = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("ClaudeCode/Set Up Everything", priority = -100)]
        static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            // Ordered by dependency, not by age:
            //   1. the arena gains its registry and HUD views
            //   2. the turn system wires the director and bar onto those
            //   3. the menu gains MainMenuUI
            //   4. content is authored and handed to it
            Step("Arena rewire", AuditRewireSetup.Run);
            Step("Turn system", TurnSystemSetup.Run);
            Step("Main menu", MainMenuSetup.Run);
            Step("Character content", CharacterContentSetup.Run);

            AssetDatabase.SaveAssets();

            // Land on the boot scene: playing from any other one skips Bootstrap, so the persistent
            // objects never exist and no match can start.
            EditorSceneManager.OpenScene(k_BootScene, OpenSceneMode.Single);

            Debug.Log("Set Up Everything finished. Press Play from Bootstrap.");
        }

        /// <summary>
        /// Runs one step, and keeps going if it throws.
        ///
        /// A step that fails on a project missing something earlier should not hide the three that
        /// would have worked -- the log is more useful listing what succeeded and what did not than
        /// stopping at the first exception with no summary.
        /// </summary>
        static void Step(string name, System.Action action)
        {
            try
            {
                action();
                Debug.Log($"[Set Up Everything] {name}: done.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Set Up Everything] {name} failed: {e}");
            }
        }
    }
}
