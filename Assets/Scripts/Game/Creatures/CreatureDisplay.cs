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
        /// A premade carries its own sprite. A character a player built carries the id of one of
        /// the game's own portraits, which every machine can resolve -- so everybody sees the same
        /// face, which is the whole reason the pictures ship with the game rather than being loaded
        /// off the player who made the character.
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
        /// The same picture, as a texture and the part of it to use.
        ///
        /// For the board token, which paints a mesh rather than an element. A sprite may be one
        /// region of a larger page, so the rect comes back with it -- handing the whole page to a
        /// disc would show a creature its neighbours.
        /// </summary>
        /// <returns>False when this creature has no picture available on this machine.</returns>
        public static bool TryPortraitTexture(CreatureState creature, out Texture texture,
            out Vector4 scaleOffset)
        {
            scaleOffset = new Vector4(1f, 1f, 0f, 0f);
            texture = null;

            var definition = creature.Definition;
            var sprite = definition != null && definition.Portrait != null
                ? definition.Portrait
                : OwnPortrait(creature);

            if (sprite == null || sprite.texture == null)
            {
                return false;
            }

            // A sprite may be one region of a larger page, so the rect comes with it -- handing the
            // whole page to a disc would show a creature its neighbours.
            var rect = sprite.textureRect;
            var page = sprite.texture;

            // The middle square of it, because the token is round and stretching a tall picture
            // across a circle squashes the face. Cropping loses the edges of a portrait, which is
            // the part nobody was looking at.
            var side = Mathf.Min(rect.width, rect.height);
            var left = rect.x + ((rect.width - side) * 0.5f);
            var bottom = rect.y + ((rect.height - side) * 0.5f);

            texture = page;
            scaleOffset = new Vector4(
                side / page.width, side / page.height,
                left / page.width, bottom / page.height);

            return true;
        }

        /// <summary>
        /// The portrait of the character this player is playing as, when this creature is it.
        ///
        /// Matched on the build slot rather than on control: a player who has also claimed a premade
        /// controls two creatures, and only one of them is theirs in the sense that matters here.
        /// </summary>
        static Sprite OwnPortrait(CreatureState creature)
        {
            var characters = PlayerCharacters.Current;

            if (creature.BuildSlot == PartyInfo.Unclaimed || characters == null)
            {
                return null;
            }

            var build = characters.BuildFor(creature.BuildSlot);
            return build != null ? Portraits.Get(build.PortraitId) : null;
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
