using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.Game;
using Dragoneye.Game.Combat;

namespace Dragoneye.Game.Creatures
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

        /// <summary>How full a bar is: nothing at all when there is no maximum to be full of.</summary>
        public static float Fraction(int current, int max) =>
            max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

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
        /// <summary>
        /// A face from a sprite, or a lettered tile from a name. For anywhere that knows the
        /// sprite already -- a draft card reading a definition -- rather than a live creature.
        /// </summary>
        public static void DrawPortrait(VisualElement into, Sprite portrait, string name,
            string initialClass = "portrait__initial")
        {
            if (portrait != null)
            {
                into.style.backgroundImage = new StyleBackground(portrait);
                return;
            }

            var initial = new Label(Initial(name));
            initial.AddToClassList(initialClass);
            into.Add(initial);
        }

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
        /// What is left of a creature, along the bottom of its card: armour over health.
        ///
        /// One drawing for every place a creature is shown small, so the party column and the turn
        /// order cannot end up disagreeing about what a half-empty bar looks like. Armour is only
        /// drawn where there is armour to lose, and the numbers are optional -- a row of faces
        /// answers "who is next", and a number on every one of them answers a question nobody
        /// asked.
        /// </summary>
        public static void DrawVitals(VisualElement into, CreatureState creature, bool numbers)
        {
            var bars = new VisualElement();
            bars.AddToClassList("portrait__bars");
            bars.pickingMode = PickingMode.Ignore;

            if (creature.MaxArmour > 0)
            {
                bars.Add(Bar("armour-track", "armour-fill",
                    Shown.Armour(creature), creature.MaxArmour, numbers,
                    "Armour. Takes every blow first, and does not come back."));
            }

            bars.Add(Bar("hp-track", "hp-fill", Shown.Hp(creature), creature.MaxHp, numbers,
                "Health."));

            into.Add(bars);
        }

        static VisualElement Bar(string trackClass, string fillClass, int current, int max,
            bool numbers, string tip)
        {
            var track = new VisualElement();
            track.AddToClassList(trackClass);
            track.tooltip = tip;

            var fill = new VisualElement();
            fill.AddToClassList(fillClass);
            fill.style.width = Length.Percent(Fraction(current, max) * 100f);
            track.Add(fill);

            if (numbers)
            {
                var text = new Label($"{current} / {max}");
                text.AddToClassList("bar-text");
                text.pickingMode = PickingMode.Ignore;
                track.Add(text);
            }

            return track;
        }

        /// <summary>
        /// What a creature is holding, in a column beside its card.
        ///
        /// Beside it rather than on it: runes over a face are two pictures in one square, and the
        /// face is the half a player recognises a creature by.
        ///
        /// Its own pool where the local player is entitled to it, and only what has been proven
        /// otherwise. The rule is the card's rule, kept here so a portrait cannot become the one
        /// place an opponent's hand leaks.
        /// </summary>
        public static void DrawElements(VisualElement into, CreatureState creature)
        {
            var pool = creature.Pool;

            if (pool == null)
            {
                return;
            }

            var held = LocalPlayer.Controls(creature) ? pool.Pool : PossibleElements.Seen(pool.Ledger).Known;
            var column = new VisualElement();
            column.AddToClassList("portrait__elements");
            column.pickingMode = PickingMode.Ignore;

            foreach (var element in ElementInfo.All)
            {
                var count = held[element];

                if (count <= 0)
                {
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList("portrait__element");

                var rune = new VisualElement();
                rune.AddToClassList("portrait__rune");
                CharacterSheet.PaintElement(rune, element);
                row.Add(rune);

                var label = new Label(count.ToString());
                label.AddToClassList("portrait__count");
                row.Add(label);

                column.Add(row);
            }

            if (column.childCount > 0)
            {
                into.Add(column);
            }
        }

        /// <summary>
        /// Everything known about a creature's elements, laid out in a grid, for a hover.
        ///
        /// **On hover rather than always.** Seven elements will not fit beside a portrait, and the
        /// four or five that did fit made the column a wall of runes that told you nothing at a
        /// glance. Asked for, they are worth the room; unasked, the face is the thing.
        ///
        /// What is shown is what this viewer is entitled to know. Their own creature shows its
        /// hand exactly. Somebody else's shows only what has been proven, plus one last cell
        /// holding a question mark and the number of elements nobody has put a name to yet -- so
        /// the unknown is a number on the screen rather than something to be inferred.
        /// </summary>
        /// <param name="columns">How many runes to a row. The last row takes whatever is left.</param>
        public static VisualElement ElementGrid(CreatureState creature, int columns)
        {
            var grid = new VisualElement();
            grid.AddToClassList("rune-grid");
            grid.pickingMode = PickingMode.Ignore;

            var pool = creature.Pool;

            if (pool == null)
            {
                return grid;
            }

            var mine = LocalPlayer.Controls(creature);
            var held = mine ? pool.Pool : PossibleElements.Seen(pool.Ledger).Known;
            var unknown = mine ? 0 : pool.Unidentified;

            var cells = new List<VisualElement>();

            foreach (var element in ElementInfo.All)
            {
                var count = held[element];

                if (count > 0)
                {
                    cells.Add(RuneCell(element, count.ToString()));
                }
            }

            if (unknown > 0)
            {
                cells.Add(UnknownCell(unknown));
            }

            for (var i = 0; i < cells.Count; i += columns)
            {
                var row = new VisualElement();
                row.AddToClassList("rune-grid__row");

                for (var j = i; j < i + columns && j < cells.Count; j++)
                {
                    row.Add(cells[j]);
                }

                grid.Add(row);
            }

            return grid;
        }

        static VisualElement RuneCell(Element element, string count)
        {
            var cell = new VisualElement();
            cell.AddToClassList("rune-grid__cell");

            var rune = new VisualElement();
            rune.AddToClassList("portrait__rune");
            CharacterSheet.PaintElement(rune, element);
            cell.Add(rune);

            var label = new Label(count);
            label.AddToClassList("portrait__count");
            cell.Add(label);

            return cell;
        }

        /// <summary>The elements nobody has put a name to, as one cell rather than seven guesses.</summary>
        static VisualElement UnknownCell(int count)
        {
            var cell = new VisualElement();
            cell.AddToClassList("rune-grid__cell");

            var mark = new Label("?");
            mark.AddToClassList("rune-grid__unknown");
            cell.Add(mark);

            var label = new Label(count.ToString());
            label.AddToClassList("portrait__count");
            cell.Add(label);

            return cell;
        }

        /// <summary>
        /// Shows the grid beside a portrait while the pointer is on it, and takes it away after.
        ///
        /// Built on entry rather than kept and hidden, because what it says changes every time an
        /// element is spent and a panel that is only correct when it was built is worse than none.
        /// </summary>
        public static void ShowElementsOnHover(VisualElement portrait, CreatureState creature,
            int columns, string placement)
        {
            VisualElement shown = null;

            portrait.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (shown != null)
                {
                    return;
                }

                shown = ElementGrid(creature, columns);
                shown.AddToClassList(placement);
                portrait.Add(shown);
            });

            portrait.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                shown?.RemoveFromHierarchy();
                shown = null;
            });
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
