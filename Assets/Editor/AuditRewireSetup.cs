using Dragoneye.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// The arena's wiring: clears the components whose scripts were removed, adds the scene-scoped
    /// creature registry, and puts the two HUD views where the single one was.
    ///
    /// Written for one change but kept, because it is idempotent and it is what configures the arena
    /// scene in a fresh clone. <see cref="SetUpEverything"/> runs it; it has no menu item of its own.
    /// </summary>
    static class AuditRewireSetup
    {
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

        /// <summary>Runs the whole step. Called directly by the master setup.</summary>
        internal static void Run()
        {

            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);

            var context = Object.FindAnyObjectByType<ArenaContext>();
            if (context == null)
            {
                Debug.LogError("No ArenaContext in the arena; run the earlier setups first.");
                return;
            }

            var hud = GameObject.Find("Arena HUD");
            if (hud == null)
            {
                Debug.LogError("No 'Arena HUD' object in the arena; run the earlier setups first.");
                return;
            }

            // BoardSelectionInput, UnitOrderInput and ArenaHudView no longer exist, so the scene is
            // holding three components with no script behind them.
            var stripped = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(context.gameObject)
                + GameObjectUtility.RemoveMonoBehavioursWithMissingScript(hud);

            var host = context.gameObject;

            // The registry is a component now rather than a static: a static list outlives the arena
            // and would carry dead creatures into the next match.
            var creatures = host.GetComponent<CreatureRegistry>() ?? host.AddComponent<CreatureRegistry>();
            var selection = host.GetComponent<CreatureSelection>() ?? host.AddComponent<CreatureSelection>();

            Assign(context, ("m_Creatures", creatures));
            Assign(selection, ("m_Creatures", creatures));

            // Both views read the same document; they simply own different parts of its tree.
            var panel = hud.GetComponent<PartyPanelView>() ?? hud.AddComponent<PartyPanelView>();
            var card = hud.GetComponent<CreatureCardView>() ?? hud.AddComponent<CreatureCardView>();

            // A third reader of the same document: the numbers that rise off a creature when it
            // earns something. It needs the registry to find whose head to sit over.
            var floating = hud.GetComponent<FloatingTextView>() ?? hud.AddComponent<FloatingTextView>();

            Assign(panel, ("m_Selection", selection), ("m_Creatures", creatures));
            Assign(card, ("m_Selection", selection));
            Assign(floating, ("m_Creatures", creatures));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"Arena rewired: {stripped} dead component(s) removed, "
                + "registry and HUD views wired.");
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
