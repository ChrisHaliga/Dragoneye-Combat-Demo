using Dragoneye.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-off scaffolding for the audit refactor: clears the components whose scripts were removed,
    /// adds the scene-scoped creature registry, and puts the two HUD views where the single one was.
    /// Delete this file once it has run.
    /// </summary>
    static class AuditRewireSetup
    {
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

        [MenuItem("ClaudeCode/Rewire Arena After Audit")]
        static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

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

            Assign(panel, ("m_Selection", selection), ("m_Creatures", creatures));
            Assign(card, ("m_Selection", selection));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"Arena rewired: {stripped} dead component(s) removed, registry and HUD views wired. "
                + "Delete Assets/Editor/AuditRewireSetup.cs once you have verified play mode.");
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
