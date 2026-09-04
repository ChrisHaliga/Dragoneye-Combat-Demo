using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Takes the in-world cursor's visuals off the focus prefab.
    ///
    /// The focus point stays: it is what the camera follows and where a player is looking is
    /// replicated. What goes is the disc drawn under it and the username floating above it. A
    /// coloured puck on the ground told a player nothing they could act on, and a name hovering over
    /// it answered a question nobody was asking -- whose turn it is now reads under the turn order,
    /// where it belongs.
    ///
    /// Housekeeping, not the rule. <see cref="FocusPoint"/> is what guarantees a focus point has
    /// no appearance, because that guarantee cannot depend on somebody having run a menu item -- and
    /// it did not, which is how a disc labelled "Player" survived being deleted twice. This deletes
    /// the objects from the asset so they are not sitting there being switched off forever.
    /// </summary>
    static class ArenaVisualsSetup
    {
        const string k_FocusPrefab = "Assets/NGO_Minimal_Setup/PlayerFocus.prefab";

        /// <summary>Runs the whole step. Called by <see cref="SetUpEverything"/>.</summary>
        internal static void Run()
        {
            var contents = PrefabUtility.LoadPrefabContents(k_FocusPrefab);

            if (contents == null)
            {
                Debug.LogWarning($"No prefab at {k_FocusPrefab}; the world cursor was not stripped.");
                return;
            }

            try
            {
                var removed = StripVisuals(contents);

                // FocusView is gone from the project, so the prefab is carrying a component whose
                // script no longer resolves. Left in place it logs on every load of the prefab.
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(contents);

                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, k_FocusPrefab);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Removes every child that exists only to be looked at.
        ///
        /// By what it draws rather than by name: the two are called Marker and Label today, and a
        /// step that matched on those strings would quietly do nothing the day somebody renamed one.
        /// The root is left alone -- it carries the focus point itself.
        /// </summary>
        static int StripVisuals(GameObject root)
        {
            var removed = 0;

            for (var i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i).gameObject;

                if (child.GetComponentInChildren<Renderer>(true) == null)
                {
                    continue;
                }

                Object.DestroyImmediate(child);
                removed++;
            }

            return removed;
        }
    }
}
