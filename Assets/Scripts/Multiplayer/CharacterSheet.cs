using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// How a resolved character is drawn: four stats, seven attributes, a pool of gems and a skill
    /// list.
    ///
    /// One implementation, because the creator and the roster show the same character and a second
    /// copy would eventually disagree about what one looks like. Each method fills a container the
    /// caller owns, so the two screens keep their own layouts and share only the pieces.
    ///
    /// Nothing here decides anything. It takes a <see cref="Loadout"/> -- already resolved by the
    /// same resolver the host will use -- and renders it.
    /// </summary>
    public static class CharacterSheet
    {
        /// <summary>The separator these lines are built from, written once.</summary>
        const string DotSeparator = "\u00b7";

        /// <summary>
        /// The three numbers that decide a fight, big enough to read at a glance.
        ///
        /// Not level. Level is in the subtitle above every one of these, and a number already on
        /// screen does not earn a quarter of the row.
        /// </summary>
        public static void Stats(VisualElement into, Vitals vitals)
        {
            into.Clear();
            into.Add(Stat("HP", vitals.MaxHealth.ToString()));
            into.Add(Stat("AP", vitals.MaxAp.ToString()));
            into.Add(Stat("SPD", vitals.Speed.ToString()));
        }

        public static VisualElement Stat(string label, string value)
        {
            var stat = new VisualElement();
            stat.AddToClassList("stat");

            var name = new Label(label);
            name.AddToClassList("stat__label");
            stat.Add(name);

            var number = new Label(value);
            number.AddToClassList("stat__value");
            stat.Add(number);

            return stat;
        }

        /// <summary>
        /// The seven resolved attributes.
        ///
        /// <paramref name="bought"/> is what the player actually paid for; where the resolved value
        /// differs the tile is coloured, so the species baseline and the equipment modifiers are
        /// visible without a second panel explaining them. Pass null to skip the comparison.
        /// </summary>
        public static void Attributes(VisualElement into, AttributeBlock resolved,
            AttributeBlock? bought = null)
        {
            into.Clear();

            foreach (var attribute in AttributeInfo.All)
            {
                var value = resolved[attribute];

                var tile = new VisualElement();
                tile.AddToClassList("attr");

                if (bought.HasValue)
                {
                    var paid = bought.Value[attribute];
                    tile.EnableInClassList("attr--boosted", value > paid);
                    tile.EnableInClassList("attr--reduced", value < paid);
                }

                var label = new Label(AttributeInfo.ShortNameOf(attribute));
                label.AddToClassList("attr__label");
                tile.Add(label);

                var number = new Label(value.ToString());
                number.AddToClassList("attr__value");
                tile.Add(number);

                into.Add(tile);
            }
        }

        /// <summary>
        /// The starting pool as one gem per element held, each carrying its count.
        ///
        /// The shape is free and the size is not, so what needs saying is the total against the
        /// level. Drawing empty slots would imply a fixed number of picks, which is exactly what a
        /// pool is not.
        /// </summary>
        public static void Pool(VisualElement into, ElementCounts pool, int budget)
        {
            into.Clear();

            foreach (var element in ElementInfo.All)
            {
                var held = pool[element];

                if (held <= 0)
                {
                    continue;
                }

                var gem = new VisualElement();
                gem.AddToClassList("gem");
                gem.style.unityBackgroundImageTintColor = ElementPalette.ForElement(element);
                gem.tooltip = $"{ElementInfo.NameOf(element)} " + DotSeparator
                    + $" {ElementPricing.CostOf(element)} point"
                    + (ElementPricing.CostOf(element) == 1 ? string.Empty : "s") + " each";

                var count = new Label(held.ToString());
                count.AddToClassList("gem__count");
                gem.Add(count);

                into.Add(gem);
            }

            // Points, not gems. The elements are not all the same price, so a count would say
            // nothing about how much of the budget is left.
            var spent = ElementPricing.CostOf(pool);

            if (spent == budget)
            {
                return;
            }

            var note = new Label($"{spent} of {budget} points spent");
            note.AddToClassList("gem-row__empty");
            into.Add(note);
        }

        /// <summary>
        /// Everything the character can do, and any passives it holds.
        ///
        /// The resolved list rather than the class list, which is what makes "no sword, no sword
        /// skills" visible: swap the weapon and the list changes under the player's hand.
        /// </summary>
        public static void Skills(VisualElement into, Loadout loadout)
        {
            into.Clear();

            if (loadout.Skills.Count == 0)
            {
                var none = new Label("Nothing this character carries grants a skill.");
                none.AddToClassList("skill-line--none");
                into.Add(none);
            }

            foreach (var skill in loadout.Skills)
            {
                var line = new VisualElement();
                line.AddToClassList("skill-line");
                line.tooltip = string.IsNullOrWhiteSpace(skill.Description)
                    ? skill.Name
                    : skill.Description;

                var name = new Label(skill.Name);
                name.AddToClassList("skill-line__name");

                var cost = new Label(skill.ElementCost > 0
                    ? $"{skill.ApCost} AP · {skill.ElementCost} {ElementInfo.ShortNameOf(skill.Element)}"
                    : $"{skill.ApCost} AP");
                cost.AddToClassList("skill-line__cost");
                cost.style.color = ElementPalette.ForElement(skill.Element);

                line.Add(name);
                line.Add(cost);
                into.Add(line);
            }

            // Armour reads here rather than in a passive line, because it is now a number rather
            // than a flag: what a suit stops is the whole of what wearing it does in a fight.
            if (loadout.DamageReduction > 0)
            {
                var armour = new Label($"Stops {loadout.DamageReduction} damage a blow");
                armour.AddToClassList("passive-line");
                into.Add(armour);
            }
        }

        /// <summary>"Human · Guardian · Level 4", or whichever halves of it resolved.</summary>
        public static string Describe(Loadout loadout)
        {
            var species = loadout.Species != null ? loadout.Species.Name : "No species";
            var className = loadout.Class != null ? loadout.Class.Name : "No class";

            return $"{species}  ·  {className}  ·  Level {loadout.Vitals.Level}".ToUpperInvariant();
        }
    }
}
