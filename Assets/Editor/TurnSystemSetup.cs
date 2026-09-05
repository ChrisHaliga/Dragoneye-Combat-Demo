using Dragoneye.Game;
using Unity.Netcode;
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
        const string k_TurnObject = "Turn State";

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
        /// Puts on the match prefab what belongs to a match, and takes off it what belongs to a
        /// fight.
        ///
        /// That prefab is spawned when the server starts and carried into the arena and back, which
        /// is the right lifetime for the draft, the roster and the characters players bring -- all
        /// of those outlive any one fight.
        ///
        /// The turn order does not, and it used to live here anyway. An initiative order, a round
        /// number and a winner are facts about one fight, and keeping them on an object that
        /// survives into the lobby meant a freshly loaded arena spent its first frames showing the
        /// previous match's result to everyone in it. They live in the arena scene now, where they
        /// are created and destroyed with the board they describe.
        /// </summary>
        static bool SetUpMatchPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_MatchPrefab);
            if (prefab == null)
            {
                Debug.LogError($"No prefab at {k_MatchPrefab}; cannot install the match objects.");
                return false;
            }

            var changed = false;

            // The characters players bring. On the match object because it has to exist from the
            // lobby, where they are submitted, through to the arena, where they are spawned.
            if (prefab.GetComponent<PlayerCharacters>() == null)
            {
                prefab.AddComponent<PlayerCharacters>();
                changed = true;
            }

            // Moved to the arena. TurnCommands travels with it -- it requires TurnState and is
            // fetched off the same object.
            changed |= Strip<TurnCommands>(prefab);
            changed |= Strip<TurnState>(prefab);

            if (changed)
            {
                PrefabUtility.SavePrefabAsset(prefab);
            }

            Debug.Log(changed
                ? "Match prefab updated: turn state moved out, player characters in."
                : "Match prefab already correct.");

            return true;
        }

        /// <summary>Removes a component from a prefab, if it is still on it.</summary>
        static bool Strip<T>(GameObject prefab) where T : Component
        {
            var existing = prefab.GetComponent<T>();

            if (existing == null)
            {
                return false;
            }

            Object.DestroyImmediate(existing, allowDestroyingAssets: true);
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

            SetUpTurnState();

            var director = Ensure<CombatDirector>(host);
            Assign(director, ("m_Creatures", creatures), ("m_Units", units), ("m_Map", map));

            var input = Ensure<BoardActionInput>(host);
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

        /// <summary>
        /// The turn order, as an object in the arena scene.
        ///
        /// An in-scene networked object rather than a spawned one, because that is precisely the
        /// lifetime wanted: NGO spawns it when the server loads this scene and destroys it when the
        /// scene goes, so a fight's order cannot outlive the fight. Nothing has to remember to
        /// clear it, which is the whole reason for moving it off the match prefab.
        ///
        /// Its own object rather than a component on the arena context, so that what is replicated
        /// and what is local stay visibly separate in the hierarchy.
        /// </summary>
        static void SetUpTurnState()
        {
            var existing = Object.FindAnyObjectByType<TurnState>();
            var host = existing != null ? existing.gameObject : GameObject.Find(k_TurnObject);

            if (host == null)
            {
                host = new GameObject(k_TurnObject);
            }

            // The NetworkObject first: adding it is what earns the scene id that lets clients match
            // this object to the host's.
            Ensure<NetworkObject>(host);
            Ensure<TurnState>(host);
            Ensure<TurnCommands>(host);

            // The clash postbox. Match-scoped like the turn order, and for the same reason: a
            // question about one attack has no business outliving the fight it was asked in.
            Ensure<ClashCommands>(host);

            EditorUtility.SetDirty(host);
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

            var bar = Ensure<TurnBarView>(hud);
            Assign(bar, ("m_Creatures", creatures), ("m_Selection", selection));

            var skills = Ensure<SkillBarView>(hud);
            Assign(skills, ("m_Input", input));

            // The board input asks the bar what is armed, so it needs the reference back.
            Assign(input, ("m_SkillBar", skills));

            var controls = Ensure<TurnControlsView>(hud);
            Assign(controls, ("m_Input", input), ("m_Map", map), ("m_Units", units));

            // Right-click asks what can be done here. It takes only the board input, because that
            // already holds the map, the units and the routes -- a second set of references
            // pointing at the same things is a second set that can be pointed somewhere else.
            var context = Ensure<ContextMenuView>(hud);
            Assign(context, ("m_Input", input));

            // Where a defender answers. On the HUD because it is the one panel that appears when
            // it is not your turn, which is exactly the moment nothing else on screen is for you.
            var prompt = Ensure<ClashPromptView>(hud);
            Assign(prompt, ("m_Input", input));
        }

        /// <summary>
        /// The component, adding it if it is not there.
        ///
        /// Written out rather than done with <c>??</c>: a Unity object that is missing is not
        /// reference-null even though <c>== null</c> says it is, and the null-coalescing operator
        /// does not go through that operator. It hands back an object that throws when touched.
        /// </summary>
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
