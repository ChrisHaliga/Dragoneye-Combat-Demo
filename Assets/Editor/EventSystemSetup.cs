using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Puts an event system in the scenes that draw UI.
    ///
    /// Runtime UI Toolkit needs one. Without it a panel falls back to an input path that reads the
    /// legacy Input class, which returns nothing in a project set to the Input System package --
    /// so a click still lands, by another route, but the wheel never arrives and neither does a
    /// hover. That is why no scroll view in this game has ever scrolled and why no tooltip has
    /// ever appeared, both of which were reported as separate bugs for months.
    ///
    /// Safe to re-run: a scene that already has one is left alone. Delete this file once both
    /// scenes have been seen to have it.
    /// </summary>
    static class EventSystemSetup
    {
        const string k_Arena = "Assets/Scenes/Arena.unity";
        const string k_Menu = "Assets/Scenes/MainMenu.unity";

        [MenuItem("ClaudeCode/Add Input Event System")]
        internal static void Run()
        {
            Add(k_Menu);
            Add(k_Arena);

            // Left on the menu scene rather than wherever it finished, so the editor is not sitting
            // in the arena with no Bootstrap behind it.
            EditorSceneManager.OpenScene(k_Menu, OpenSceneMode.Single);
        }

        static void Add(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var existing = Object.FindAnyObjectByType<EventSystem>();

            if (existing != null)
            {
                Debug.Log($"{path} already has an event system.");
                return;
            }

            var host = new GameObject("Event System");
            host.AddComponent<EventSystem>();

            var module = host.AddComponent<InputSystemUIInputModule>();

            // The module ships with a set of UI actions; added from a script it has none until it
            // is asked for them, and a module with no actions is the same as no module at all.
            module.AssignDefaultActions();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"{path}: event system added. Scroll views and tooltips will now get their events.");
        }
    }
}
