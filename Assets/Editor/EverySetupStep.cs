using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Runs every setup step, in the order they depend on each other, and then checks the result.
    ///
    /// There used to be a step like this called Set Up Everything, and it was rightly thrown out.
    /// Not because running the steps together is wrong -- it is the only sensible way to bring a
    /// fresh clone up, or to put a project back together after a scene has been mangled -- but
    /// because the content step it ran <b>rewrote every asset in the project from code</b>. So the
    /// only way to import a portrait was to also throw away every number anybody had tuned, and
    /// the honest advice was to never run it.
    ///
    /// That is fixed at the source: the content step seeds what is missing and does not touch what
    /// is there. With that true, running the steps in order costs nothing, and having to click
    /// eleven menu items in the right sequence to get a working project is its own kind of trap.
    ///
    /// Order is not incidental. The turn system reads references the arena rewire assigns; the
    /// content step needs the menu component the menu rewire adds; the premades are given faces by
    /// path, and a face is only a sprite once the importer has been told so. The audit runs last,
    /// because the useful thing to know after rebuilding a project is whether it is actually
    /// wired.
    ///
    /// Every step is still its own menu item. When you know which one you want, run that one.
    /// </summary>
    static class EverySetupStep
    {
        const string k_BootScene = "Assets/Scenes/Bootstrap.unity";

        /// <summary>
        /// Whether the steps are being run together rather than one at a time.
        ///
        /// A step that opens a scene has to be sure the last one's work is on disk first. Alone,
        /// it asks. In a run of eleven that would be eleven prompts, so the run saves for them.
        /// </summary>
        static bool s_Batch;

        /// <summary>
        /// Called by any step that is about to open a scene: true when it may go ahead.
        ///
        /// Alone, this is the prompt that stops a step discarding unsaved work. In a batch it
        /// saves what is open without asking, because the answer was given once at the start.
        /// </summary>
        internal static bool ReadyToTouchScenes()
        {
            if (!s_Batch)
            {
                return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            }

            EditorSceneManager.SaveOpenScenes();
            return true;
        }

        [MenuItem("ClaudeCode/Run Every Setup Step In Order", priority = -100)]
        static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            s_Batch = true;
            var failed = 0;

            try
            {
                //   1. the interface art is sliced and imported
                //   2. the arena gains its registry and HUD views
                //   3. the turn system wires the director and bar onto those
                //   4. the menu gains MainMenuUI
                //   5. portraits become sprites, then content can point at them
                //   6. content is seeded and handed to the menu
                failed += Step("UI art", UiArtSetup.Run);
                failed += Step("Arena rewire", AuditRewireSetup.Run);
                failed += Step("Arena visuals", ArenaVisualsSetup.Run);
                failed += Step("Turn system", TurnSystemSetup.Run);
                failed += Step("Arena map", ArenaMapSetup.Run);
                failed += Step("Main menu", MainMenuSetup.Run);
                failed += Step("Portraits", PortraitSetup.Run);
                failed += Step("Character content", CharacterContentSetup.Run);
                failed += Step("Element icons", ElementIconSetup.Run);
                failed += Step("Element matchups", ElementMatchupSetup.Run);

                AssetDatabase.SaveAssets();

                // Last, and on purpose: what you want to know after rebuilding a project is
                // whether the thing you rebuilt is wired, not that eleven steps returned.
                failed += Step("Content audit", ContentAudit.Run);
            }
            finally
            {
                s_Batch = false;
            }

            AssetDatabase.SaveAssets();

            // Land on the boot scene: playing from any other one skips Bootstrap, so the
            // persistent objects never exist and no match can start.
            EditorSceneManager.OpenScene(k_BootScene, OpenSceneMode.Single);

            if (failed == 0)
            {
                Debug.Log("Every setup step finished. Press Play from Bootstrap.");
            }
            else
            {
                Debug.LogError($"{failed} setup steps failed; see the errors above. The rest ran.");
            }
        }

        /// <summary>
        /// Runs one step, and keeps going if it throws.
        ///
        /// A step that fails on a project missing something earlier should not hide the three that
        /// would have worked. The log is more useful listing what succeeded and what did not than
        /// stopping at the first exception with no summary.
        /// </summary>
        static int Step(string name, System.Action action)
        {
            try
            {
                action();
                Debug.Log($"[Setup] {name}: done.");
                return 0;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Setup] {name} failed: {e}");
                return 1;
            }
        }
    }
}
