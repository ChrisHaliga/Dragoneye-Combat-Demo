using Dragoneye.Game;
using Dragoneye.Game.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Everything the feedback pass needs in a scene: input events, the attack lean, and the
    /// camera that follows whoever is acting.
    ///
    /// **The event system is the important one.** Runtime UI Toolkit needs one, and no scene had
    /// it. Without it a panel falls back to an input path that reads the legacy Input class, which
    /// returns nothing in a project set to the Input System package -- so a click still landed, by
    /// another route, but the wheel never arrived and neither did a hover. That is why no scroll
    /// view in this game has ever scrolled and why no tooltip has ever appeared, both of which
    /// were reported as bugs for months.
    ///
    /// Safe to re-run: anything already there is left alone. Delete this file once the arena and
    /// the menu have both been seen to have it.
    /// </summary>
    static class FeedbackPassSetup
    {
        const string k_Arena = "Assets/Scenes/Arena.unity";
        const string k_Menu = "Assets/Scenes/MainMenu.unity";

        [MenuItem("ClaudeCode/Wire The Feedback Pass")]
        internal static void Run()
        {
            AddEventSystem(k_Menu);
            AddArena();

            // Left on the menu scene rather than wherever it finished, so the editor is not sitting
            // in the arena with no Bootstrap behind it.
            EditorSceneManager.OpenScene(k_Menu, OpenSceneMode.Single);
        }

        static void AddArena()
        {
            var scene = EditorSceneManager.OpenScene(k_Arena, OpenSceneMode.Single);

            EnsureEventSystem(k_Arena);

            var context = Object.FindAnyObjectByType<ArenaContext>();

            if (context == null)
            {
                Debug.LogError("No ArenaContext in the arena; the feedback pass cannot be wired.");
                return;
            }

            var host = context.gameObject;
            var creatures = context.Creatures;

            // The lean an attacker throws at whoever it swung at, and the camera that brings each
            // turn's creature into view. Both read announcements every peer already gets.
            var flourish = Ensure<AttackFlourish>(host);
            var follow = Ensure<TurnCameraFocus>(host);

            Assign(flourish, ("m_Creatures", creatures));
            Assign(follow, ("m_Creatures", creatures));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("Arena: event system, attack lean and turn camera wired.");
        }

        static void AddEventSystem(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            if (!EnsureEventSystem(path))
            {
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Adds one if the open scene has none. True when it added something.</summary>
        static bool EnsureEventSystem(string path)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
            {
                Debug.Log($"{path} already has an event system.");
                return false;
            }

            var host = new GameObject("Event System");
            host.AddComponent<EventSystem>();

            var module = host.AddComponent<InputSystemUIInputModule>();

            // The module ships with a set of UI actions; added from a script it has none until it
            // is asked for them, and a module with no actions is the same as no module at all.
            module.AssignDefaultActions();

            Debug.Log($"{path}: event system added. Scroll views and tooltips will get their events.");
            return true;
        }

        static T Ensure<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing == null ? target.AddComponent<T>() : existing;
        }

        static void Assign(Object target, params (string Path, Object Value)[] fields)
        {
            var serialized = new SerializedObject(target);

            foreach (var (path, value) in fields)
            {
                var property = serialized.FindProperty(path);

                if (property == null)
                {
                    Debug.LogError($"{target.GetType().Name} has no field '{path}'.", target);
                    continue;
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
