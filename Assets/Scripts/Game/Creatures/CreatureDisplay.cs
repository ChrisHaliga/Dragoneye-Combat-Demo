using Dragoneye.Data;
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

        /// <summary>
        /// Draws a creature's face into an element, or its initial when there is no face to draw.
        ///
        /// One implementation, because the turn bar, the party column and the inspect card all show
        /// the same creature and a second copy would eventually disagree about what it looks like.
        ///
        /// A premade carries its own sprite. A character a player built carries a portrait that
        /// lives in that player's save folder and is never sent over the wire, so only the machine
        /// that owns it can draw it -- everybody else gets the lettered tile. Putting image bytes on
        /// the network is a decision worth taking deliberately rather than as a side effect of
        /// wanting a face in the turn bar.
        /// </summary>
        public static void DrawPortrait(VisualElement into, CreatureState creature,
            string initialClass = "portrait__initial")
        {
            var definition = creature.Definition;

            if (definition != null && definition.Portrait != null)
            {
                into.style.backgroundImage = new StyleBackground(definition.Portrait);
                return;
            }

            var own = OwnPortrait(creature);

            if (own != null)
            {
                into.style.backgroundImage = new StyleBackground(own);
                return;
            }

            var initial = new Label(Initial(creature.DisplayName));
            initial.AddToClassList(initialClass);
            into.Add(initial);
        }

        /// <summary>
        /// The portrait of the character this player is playing as, when this creature is it.
        ///
        /// Matched on the build slot rather than on control: a player who has also claimed a premade
        /// controls two creatures, and only one of them is theirs in the sense that matters here.
        /// </summary>
        static Texture2D OwnPortrait(CreatureState creature)
        {
            if (creature.BuildSlot == PartyInfo.Unclaimed
                || !LocalPlayer.Controls(creature)
                || SelectedCharacter.Current == null)
            {
                return null;
            }

            return SelectedCharacter.Current.Portrait;
        }

        /// <summary>Stand-in for a missing portrait: the creature's initial on a plain tile.</summary>
        public static string Initial(string name) =>
            string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();

        /// <summary>
        /// Stops a full-screen HUD document swallowing board clicks.
        ///
        /// A UIDocument's root fills the screen, and so does the template root underneath it, and so
        /// does any layout wrapper the markup grows later. Every one of them takes clicks by default
        /// and the board never sees them. So this walks the whole subtree rather than one level:
        /// the previous version stopped at direct children and was correct only for the markup that
        /// existed when it was written -- adding a wrapper would have silently killed board input
        /// again, which is precisely the bug it was added to fix.
        ///
        /// Controls are left alone, along with everything inside them. A button that cannot be
        /// clicked and a scroll view that cannot be dragged are the opposite of the problem being
        /// solved here. Elements built at runtime, like the party portraits, opt back in themselves.
        /// </summary>
        public static void MakeClickThrough(VisualElement documentRoot)
        {
            if (documentRoot == null || IsControl(documentRoot))
            {
                return;
            }

            documentRoot.pickingMode = PickingMode.Ignore;

            foreach (var child in documentRoot.Children())
            {
                MakeClickThrough(child);
            }
        }

        /// <summary>
        /// Whether an element handles pointer input in its own right, and so must keep picking.
        ///
        /// A type test rather than a marker class: these are the framework's interactive primitives,
        /// and a list of them here cannot get out of step with markup the way a hand-applied USS
        /// class would.
        /// </summary>
        static bool IsControl(VisualElement element) =>
            element is Button
            || element is Toggle
            || element is TextField
            || element is ScrollView
            || element is Slider
            || element is DropdownField;
    }
}
