using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.UI
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
        /// The four numbers that decide a fight, big enough to read at a glance, each carrying
        /// where it came from and what it does.
        ///
        /// Not level. Level is in the subtitle above every one of these, and a number already on
        /// screen does not earn a fifth of the row.
        /// </summary>
        public static void Stats(VisualElement into, Loadout loadout)
        {
            var vitals = loadout.Vitals;

            into.Clear();
            into.Add(Stat("HP", vitals.MaxHealth.ToString(), StatLore.Health(loadout)));
            into.Add(Stat("AP", vitals.MaxAp.ToString(), StatLore.ActionPoints(loadout)));
            into.Add(Stat("SPD", vitals.Speed.ToString(), StatLore.Speed(loadout)));
            into.Add(Stat("ARM", loadout.ArmourPoints.ToString(), StatLore.Armour(loadout)));
        }

        public static VisualElement Stat(string label, string value, string tooltip = null)
        {
            var stat = new VisualElement();
            stat.AddToClassList("stat");
            stat.tooltip = tooltip;

            var name = new Label(label);
            name.AddToClassList("stat__label");
            stat.Add(name);

            var number = new Label(value);
            number.AddToClassList("stat__value");
            stat.Add(number);

            return stat;
        }

        /// <summary>
        /// How far this character is towards its next level.
        ///
        /// A bar rather than a number, because the question a player asks is "am I close", and two
        /// numbers separated by a slash makes them do the division. What it takes doubles every
        /// level, so the same eight experience is a full bar at level three and a quarter of one at
        /// level five -- which is exactly what the bar shows and the pair of numbers hides.
        /// </summary>
        public static void Experience(VisualElement into, int level, int xp)
        {
            into.Clear();

            var needed = Progression.XpToLeave(level);
            var held = xp < 0 ? 0 : xp;
            var ready = held >= needed;

            var track = new VisualElement();
            track.AddToClassList("xp-track");

            var fill = new VisualElement();
            fill.AddToClassList("xp-fill");
            fill.EnableInClassList("xp-fill--ready", ready);
            fill.style.width = Length.Percent(ready ? 100f : 100f * held / needed);
            track.Add(fill);

            var label = new Label(ready ? "READY TO LEVEL UP" : $"{held} / {needed} XP");
            label.AddToClassList("xp-label");
            label.EnableInClassList("xp-label--ready", ready);

            into.Add(track);
            into.Add(label);
        }

        /// <summary>
        /// The same progress, on one line rather than two.
        ///
        /// For a card where the bar has to share a row with something else. Split from
        /// <see cref="Experience"/> in layout only -- both ask <see cref="Progression"/> the same
        /// question, so a card and a sheet can never disagree about how close a character is.
        /// </summary>
        public static void ExperienceInline(VisualElement into, int level, int xp)
        {
            into.Clear();

            var needed = Progression.XpToLeave(level);
            var held = xp < 0 ? 0 : xp;
            var ready = held >= needed;

            var track = new VisualElement();
            track.AddToClassList("xp-track");
            track.AddToClassList("xp-track--inline");

            var fill = new VisualElement();
            fill.AddToClassList("xp-fill");
            fill.EnableInClassList("xp-fill--ready", ready);
            fill.style.width = Length.Percent(ready ? 100f : 100f * held / needed);
            track.Add(fill);

            // Shorter than the sheet's wording, which does not fit a card. The bar beside it is
            // already full and gold, so the word only has to name what that means.
            var label = new Label(ready ? "LEVEL UP" : $"{held} / {needed} XP");
            label.AddToClassList("xp-label");
            label.AddToClassList("xp-label--inline");
            label.EnableInClassList("xp-label--ready", ready);

            into.Add(track);
            into.Add(label);
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

                // The stepper rows in the creator carry the same text. These tiles are the only
                // attributes on every other screen, so without it the description existed on one
                // screen out of four.
                tile.tooltip = AttributeInfo.DescribeEffect(attribute);

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
        /// Paints an element onto a mark: its rune, or the coloured gem the stylesheet already
        /// carries when the art is missing.
        ///
        /// One implementation for the creator, the level-up screen, the roster and the arena card,
        /// because "what does Nyx look like" has to have one answer -- four screens each reaching
        /// for their own is how a player ends up learning two legends.
        ///
        /// The fallback is not defensive padding. The runes are built from a folder by the setup
        /// step, so a project that has not run it, or an element somebody has not drawn yet, should
        /// draw the thing it used to rather than a hole.
        /// </summary>
        public static void PaintElement(VisualElement mark, Element element)
        {
            var icon = ElementIcons.Get(element);

            if (icon == null)
            {
                mark.style.unityBackgroundImageTintColor = ElementPalette.ForElement(element);
                return;
            }

            mark.style.backgroundImage = new StyleBackground(icon);
            mark.style.unityBackgroundImageTintColor = Color.white;
        }

        /// <summary>
        /// One element and how much of it is held: the rune, then the number beside it.
        ///
        /// Beside rather than on top, which is where the count sat when the mark was a plain
        /// coloured disc. A number printed over a rune is a number over a picture, and neither
        /// survives it.
        /// </summary>
        public static VisualElement ElementChip(Element element, int count, bool dim = false)
        {
            var chip = new VisualElement();
            chip.AddToClassList("element-chip");
            chip.EnableInClassList("element-chip--none", dim);
            chip.tooltip = ElementLore.Describe(element);

            var mark = new VisualElement();
            mark.AddToClassList("element-chip__mark");
            PaintElement(mark, element);
            chip.Add(mark);

            var value = new Label(count.ToString());
            value.AddToClassList("element-chip__count");
            chip.Add(value);

            return chip;
        }

        /// <summary>
        /// Elements somebody is holding that nobody has put a name to.
        ///
        /// The eighth mark in a row of seven, and the only honest way to show an opponent's hand:
        /// what they have spent is theirs to have shown you, and what is left is a number and not a
        /// list. It shrinks as they spend, and each one that leaves it turns up under its own rune.
        /// </summary>
        public static VisualElement UnknownChip(int count)
        {
            var chip = new VisualElement();
            chip.AddToClassList("element-chip");
            chip.EnableInClassList("element-chip--none", count <= 0);
            chip.tooltip = count > 0
                ? $"{count} element{(count == 1 ? string.Empty : "s")} you have not seen"
                : "Everything this creature holds has been seen";

            var mark = new VisualElement();
            mark.AddToClassList("element-chip__mark");
            mark.AddToClassList("element-chip__mark--unknown");

            var glyph = new Label("?");
            glyph.AddToClassList("element-chip__unknown");
            mark.Add(glyph);
            chip.Add(mark);

            var value = new Label(count.ToString());
            value.AddToClassList("element-chip__count");
            chip.Add(value);

            return chip;
        }

        /// <summary>
        /// The starting pool as one rune per element held, each carrying its count.
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

                var chip = ElementChip(element, held);
                chip.tooltip = $"{ElementPricing.CostOf(element)} point"
                    + (ElementPricing.CostOf(element) == 1 ? string.Empty : "s") + " each"
                    + "\n\n" + ElementLore.Describe(element);

                into.Add(chip);
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
                into.Add(Line(skill));
            }
        }

        /// <summary>
        /// One skill: its name and price on a line, and what it is and does under them.
        ///
        /// Written out rather than left to a tooltip. A tooltip is a promise that the reader will
        /// hover, on a screen where the whole decision is which of these to take -- and it says
        /// nothing at all until they do.
        /// </summary>
        static VisualElement Line(SkillSpec skill)
        {
            var line = new VisualElement();
            line.AddToClassList("skill-line");

            var head = new VisualElement();
            head.AddToClassList("skill-line__head");

            var name = new Label(skill.Name);
            name.AddToClassList("skill-line__name");

            // The points in a grey no element uses, the element in its own colour. Both halves in
            // the element's colour put two gold numbers side by side with nothing to tell them apart.
            var cost = new Label(Cost(skill));
            cost.AddToClassList("skill-line__cost");

            head.Add(name);
            head.Add(cost);
            line.Add(head);

            // What it does, in numbers, after what it is in words. The list already resolves the
            // attributes in, so "6 damage" here is the 6 this character will do.
            line.Add(Detail(skill));

            return line;
        }

        /// <summary>The grey the points are written in, which no element uses.</summary>
        public const string PointsColour = "#8B93A5";

        /// <summary>"1 AP · 1 PYR", points grey and element in its colour, as rich text.</summary>
        public static string Cost(SkillSpec skill)
        {
            var points = $"<color={PointsColour}>{skill.ApCost} AP</color>";

            if (skill.ElementCost <= 0)
            {
                return points;
            }

            var tint = ColorUtility.ToHtmlStringRGB(ElementPalette.ForElement(skill.Element));

            return points + $"<color={PointsColour}> · </color>"
                + $"<color=#{tint}>{skill.ElementCost} {ElementInfo.ShortNameOf(skill.Element)}</color>";
        }

        /// <summary>Reach, effect and description, in one wrapped line.</summary>
        static Label Detail(SkillSpec skill)
        {
            var text = new Label(Describe(skill));
            text.AddToClassList("skill-line__text");
            return text;
        }

        /// <summary>
        /// A skill in a sentence: how far it reaches, what it does, and what it is.
        ///
        /// The one wording, so the creator, the level-up screen, the creature card and the bar in
        /// a match all say the same thing about the same skill.
        /// </summary>
        public static string Describe(SkillSpec skill)
        {
            var reach = skill.Target == SkillTarget.Self
                ? "Yourself"
                : skill.Range <= 1 ? "Adjacent" : $"Reach {skill.Range}";

            var text = $"{reach} · {SkillEffectInfo.Describe(skill.Effect)}";

            if (skill.RollsToHit)
            {
                text += $" · {SkillRules.HitChance(skill, 1)}% at one tile";
            }

            return string.IsNullOrWhiteSpace(skill.Description)
                ? text
                : text + ". " + skill.Description;
        }

        /// <summary>"Level 4 · Human · Guardian", or whichever parts of it resolved.</summary>
        public static string Describe(Loadout loadout) =>
            Describe(loadout.Vitals.Level,
                loadout.Species != null ? loadout.Species.Name : "No species",
                loadout.Class != null ? loadout.Class.Name : "No class");

        /// <summary>
        /// What a creature is, in one line: level first, then species, then class.
        ///
        /// Level leads because it is the part that changes. Species and class are settled when a
        /// character is made and never move again, so putting the one number that grows at the end
        /// of the line is putting it where nobody looks.
        ///
        /// One implementation for every screen that shows it -- the roster, the hero card, the
        /// draft board and the arena inspector -- because four copies of a format string is four
        /// chances to reorder three of them.
        /// </summary>
        /// <param name="compact">
        /// "LVL 4" rather than "LEVEL 4", for the draft cards, which are four to a row.
        /// </param>
        public static string Describe(int level, string species, string className,
            bool compact = false)
        {
            var word = compact ? "LVL" : "LEVEL";
            var gap = compact ? " " : "  ";

            return ($"{word} {level}{gap}{DotSeparator}{gap}{species}{gap}{DotSeparator}{gap}"
                + className).ToUpperInvariant();
        }
    }
}
