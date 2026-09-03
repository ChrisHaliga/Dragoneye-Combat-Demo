using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// Shared presentation helpers for creatures.
    ///
    /// Both HUD views need the same answers to "what colour is this owner" and "what is this player
    /// called". Keeping them here stops the two drifting into showing different things for the same
    /// creature.
    /// </summary>
    public static class CreatureDisplay
    {
        /// <summary>Neutral border for a creature nobody controls.</summary>
        public static readonly Color ComputerColor = new Color(0.35f, 0.37f, 0.44f);

        /// <summary>The controlling player's colour, or a neutral grey for the computer.</summary>
        public static Color OwnerColor(CreatureState creature) =>
            creature.IsComputerControlled
                ? ComputerColor
                : PlayerPalette.ForSlot(creature.ControllerSlot);

        /// <summary>Who runs this creature, by name where the roster knows one.</summary>
        public static string ControllerName(CreatureState creature)
        {
            if (creature.IsComputerControlled)
            {
                return "Computer";
            }

            var roster = PlayerRoster.Current;
            if (roster != null && roster.TryGetBySlot(creature.ControllerSlot, out var entry))
            {
                var name = entry.Name.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return $"Player {creature.ControllerSlot + 1}";
        }

        public static float HealthFraction(CreatureState creature) =>
            creature.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)creature.CurrentHp / creature.MaxHp);

        /// <summary>Stand-in for a missing portrait: the creature's initial on a plain tile.</summary>
        public static string Initial(string name) =>
            string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();

        /// <summary>
        /// Stops a full-screen HUD document swallowing board clicks.
        ///
        /// Both the document root and the template root underneath it have to opt out -- setting
        /// only the document root leaves a child covering the screen with the default picking mode.
        /// Interactive elements opt themselves back in.
        /// </summary>
        public static void MakeClickThrough(VisualElement documentRoot)
        {
            documentRoot.pickingMode = PickingMode.Ignore;

            foreach (var child in documentRoot.Children())
            {
                child.pickingMode = PickingMode.Ignore;
            }
        }
    }
}
