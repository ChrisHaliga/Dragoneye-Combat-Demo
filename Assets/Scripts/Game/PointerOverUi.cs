using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// Whether the HUD is under the cursor.
    ///
    /// The board reads the mouse straight off the Input System, which knows nothing about UI
    /// Toolkit and is not asked to. So a click on a skill button was also a click on whatever tile
    /// happened to be drawn behind it, and the turn was spent walking there.
    ///
    /// Asked of the panels rather than tracked by them. <c>Pick</c> already knows the answer --
    /// it walks the same hierarchy the pointer events use and honours every element that has opted
    /// out of picking -- so a hand-maintained list of screen rectangles would be a second answer to
    /// a question the framework has already answered, and one that goes stale the first time a
    /// panel moves.
    /// </summary>
    public static class PointerOverUi
    {
        /// <summary>
        /// True when a live panel has something pickable at this screen point.
        ///
        /// The documents are looked up per call rather than cached. This runs on a click and a
        /// context menu, not per frame, and a cache would have to be told about every document that
        /// appears or goes -- which across three scenes is more bookkeeping than the search costs.
        /// </summary>
        public static bool AtScreenPoint(Vector2 screenPosition)
        {
            var documents = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);

            foreach (var document in documents)
            {
                if (document == null || !document.isActiveAndEnabled)
                {
                    continue;
                }

                var root = document.rootVisualElement;
                var panel = root?.panel;

                if (panel == null)
                {
                    continue;
                }

                // Panel space runs from the top left and screen space from the bottom left, so the
                // y has to be turned over before the panel is asked about it.
                var point = RuntimePanelUtils.ScreenToPanel(panel,
                    new Vector2(screenPosition.x, Screen.height - screenPosition.y));

                if (panel.Pick(point) != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
