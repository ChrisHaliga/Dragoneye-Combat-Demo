using Dragoneye.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-off scaffolding for the turn system: puts the replicated turn state on the match prefab
    /// and the director, input and HUD views into the arena. Disposable once it has run.
    ///
    /// Safe to re-run.
    /// </summary>
    static class TurnSystemSetup
    {
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";
        const string k_BootScene = "Assets/Scenes/Bootstrap.unity";
        const string k_MatchPrefab = "Assets/NGO_Minimal_Setup/DraftState.prefab";
        const string k_UnitPrefab = "Assets/NGO_Minimal_Setup/Unit.prefab";

        [MenuItem("ClaudeCode/Set Up Turn System")]
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

            if (!SetUpMatchPrefab() || !SetUpUnitPrefab())
            {
                return;
            }

            SetUpArena();

            // Leave the editor on the boot scene: playing from the arena skips Bootstrap, so the
            // persistent objects never exist and no match can start.
            EditorSceneManager.OpenScene(k_BootScene, OpenSceneMode.Single);
        }

        /// <summary>
        /// Adds the turn state to the object that already carries the draft and the roster.
        ///
        /// That prefab is the match-wide networked object: spawned when the server starts and
        /// carried into the arena, which is exactly the lifetime a turn order needs. A second prefab
        /// would need its own NetworkPrefabsList entry and its own spawn call for no gain.
        /// </summary>
        static bool SetUpMatchPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_MatchPrefab);
            if (prefab == null)
            {
                Debug.LogError($"No prefab at {k_MatchPrefab}; cannot install the turn state.");
                return false;
            }

            var added = false;

            if (prefab.GetComponent<TurnState>() == null)
            {
                prefab.AddComponent<TurnState>();
                added = true;
            }

            if (prefab.GetComponent<TurnCommands>() == null)
            {
                prefab.AddComponent<TurnCommands>();
                added = true;
            }

            if (added)
            {
                PrefabUtility.SavePrefabAsset(prefab);
            }

            Debug.Log(added
                ? "Turn state added to the match prefab."
                : "Match prefab already carries the turn state.");

            return true;
        }

        /// <summary>
        /// Gives every unit the element pool DE-001 asks for.
        ///
        /// On the prefab rather than added at spawn, because it is a NetworkBehaviour: netcode
        /// matches behaviours between host and client by their order on the prefab, so one added at
        /// runtime would not exist on the other side.
        /// </summary>
        static bool SetUpUnitPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_UnitPrefab);

            if (prefab == null)
            {
                Debug.LogError($"No prefab at {k_UnitPrefab}; cannot install the element pool.");
                return false;
            }

            var added = false;

            if (prefab.GetComponent<CreaturePool>() == null)
            {
                prefab.AddComponent<CreaturePool>();
                added = true;
            }

            if (prefab.GetComponent<SkillCommands>() == null)
            {
                prefab.AddComponent<SkillCommands>();
                added = true;
            }

            if (added)
            {
                PrefabUtility.SavePrefabAsset(prefab);
                Debug.Log("Element pool and skill commands added to the unit prefab.");
            }

            return true;
        }

        static void SetUpArena()
        {
            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);

            var context = Object.FindAnyObjectByType<ArenaContext>();
            if (context == null)
            {
                Debug.LogError("No ArenaContext in the arena; run the earlier setups first.");
                return;
            }

            var host = context.gameObject;

            // BoardCommandInput was replaced by BoardActionInput, so the scene is holding a
            // component with no script behind it.
            var stripped = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(host);

            var selection = host.GetComponent<CreatureSelection>();
            var pointer = host.GetComponent<HexPointer>();

            if (selection == null || pointer == null)
            {
                Debug.LogError("The arena context is missing its selection or pointer; "
                    + "run the earlier setups first.");
                return;
            }

            // Pulled off ArenaContext rather than searched for, so this cannot wire a different
            // registry or map than the rest of the arena uses.
            var creatures = context.Creatures;
            var units = context.Units;
            var map = context.Map;

            if (creatures == null || units == null || map == null)
            {
                Debug.LogError("ArenaContext has unassigned references; run "
                    + "'Rewire Arena After Audit' first.");
                return;
            }

            var director = host.GetComponent<CombatDirector>() ?? host.AddComponent<CombatDirector>();
            Assign(director, ("m_Creatures", creatures), ("m_Units", units), ("m_Map", map));

            var input = host.GetComponent<BoardActionInput>() ?? host.AddComponent<BoardActionInput>();
            Assign(input,
                ("m_Pointer", pointer), ("m_Units", units), ("m_Selection", selection),
                ("m_Map", map), ("m_Creatures", creatures));

            // Closing the match is lifecycle, not presentation, so it is its own component rather
            // than a few lines inside the HUD.
            if (host.GetComponent<MatchConclusion>() == null)
            {
                host.AddComponent<MatchConclusion>();
            }

            SetUpHud(creatures, units, map, input, selection);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"Arena turn system wired: {stripped} dead component(s) removed. "
                + "Delete Assets/Editor/TurnSystemSetup.cs once you have verified play mode.");
        }

        static void SetUpHud(CreatureRegistry creatures, UnitIndex units,
            Dragoneye.Hex.Systems.ArenaMap map, BoardActionInput input, CreatureSelection selection)
        {
            var hud = GameObject.Find("Arena HUD");
            if (hud == null || hud.GetComponent<UIDocument>() == null)
            {
                Debug.LogError("No 'Arena HUD' object with a UIDocument; run the earlier setups first.");
                return;
            }

            var bar = hud.GetComponent<TurnBarView>() ?? hud.AddComponent<TurnBarView>();
            Assign(bar, ("m_Creatures", creatures), ("m_Selection", selection));

            var skills = hud.GetComponent<SkillBarView>() ?? hud.AddComponent<SkillBarView>();
            Assign(skills, ("m_Input", input));

            // The board input asks the bar what is armed, so it needs the reference back.
            Assign(input, ("m_SkillBar", skills));

            var controls = hud.GetComponent<TurnControlsView>() ?? hud.AddComponent<TurnControlsView>();
            Assign(controls, ("m_Input", input), ("m_Map", map), ("m_Units", units));
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
