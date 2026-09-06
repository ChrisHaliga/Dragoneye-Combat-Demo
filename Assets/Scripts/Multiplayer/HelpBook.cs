using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The rules, in one place, built into whatever container is handed to it.
    ///
    /// A game whose central mechanic is a matchup table has to be able to state its own rules.
    /// Until this existed the only way to learn how a clash resolved was to lose one, and the only
    /// way to learn what flanking did was to be flanked.
    ///
    /// Built rather than written in markup because two screens show it -- the main menu and the
    /// pause menu, in different scenes -- and rules that exist twice are rules that disagree. The
    /// element chart inside it goes further and is generated from the shipped table itself, so the
    /// one part of this page that could contradict the game cannot.
    ///
    /// The prose is still prose, and it can go stale. Everything here that is a number is a number
    /// somebody could otherwise only find by reading the source, so it is worth the risk; anything
    /// that could be derived is derived.
    /// </summary>
    public static class HelpBook
    {
        /// <summary>Fills a container with the whole rules page, replacing whatever was in it.</summary>
        public static void Build(VisualElement into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();

            // Declared here as well as in the stylesheet, the way the combat log's is: a scroll
            // view whose mode was left to the markup has, more than once, ended up not scrolling.
            if (into is ScrollView scroll)
            {
                scroll.mode = ScrollViewMode.Vertical;
                scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scroll.mouseWheelScrollSize = 40;
            }

            into.Add(Section("THE FIGHT"));
            into.Add(Text(
                "Two sides take turns, one creature at a time. Turn order runs on Speed, highest "
                + "first, and the bar across the top of the arena shows it. When everyone has had "
                + "a turn the round ends and the order begins again. A creature also heals its "
                + "Toughness at the start of each of its turns."));
            into.Add(Text(
                "A creature refills its action points at the start of its own turn, and keeps "
                + "whatever it does not spend until then. A turn ends when you say so -- never "
                + "automatically -- because holding something back is a decision the game should "
                + "not take away from you."));
            into.Add(Text(
                "The side left standing wins. There is no clock anywhere in this game: nothing you "
                + "are asked is timed, and thinking for an hour costs you nothing."));

            into.Add(Section("ARMOUR"));
            into.Add(Text(
                "Armour is a pool above health. Every blow wears it down first, and only what it "
                + "cannot hold reaches the creature underneath. It does not come back: health can "
                + "be healed, armour cannot, and once it is gone it is gone for the match. The "
                + "silver bar over the health bar is the armour, in the turn order and on the "
                + "card, and the number beside it is what is left."));
            into.Add(Text(
                "Heavier armour holds more and costs more speed. Light armour is +4 ARM for -2 "
                + "SPD; medium is +8 for -4; heavy is +16 for -8. A shield adds +4 ARM and costs no "
                + "speed -- what it costs is the hand, which a second weapon or a two-handed one "
                + "would want. Nothing you wear changes an attribute."));

            into.Add(Section("ACTION POINTS"));
            into.Add(Text(
                "Everything costs action points. A skill costs whatever it says on it. Walking "
                + "costs by your Speed: a point of movement carries you Speed-over-four tiles, so "
                + "at the base speed of eight a tile costs half a point, at four it costs a whole "
                + "one, and in plate with nothing to make up for it you crawl. Half points are "
                + "real -- a light attack can cost half a point where a heavy one costs two."));
            into.Add(Text(
                "Moving to reach a target is part of using a skill, not a separate order. If you "
                + "aim something at a creature out of reach, your creature walks the cheapest way "
                + "into range and then uses it -- and it is refused outright if you cannot afford "
                + "both halves, so you never spend a turn walking towards something you could not "
                + "have done."));

            into.Add(Section("FACING"));
            into.Add(Text(
                "Every creature faces one of the six directions, marked by the triangle on its "
                + "token. Moving sets your facing: click a tile, turn the ghost that appears to "
                + "the direction you want, and click again to commit. Attacking also turns you --"
                + " you end up facing whoever you swung at, which opens your back to everybody you "
                + "did not."));
            into.Add(Text(
                "A creature struck from the tile directly behind it is flanked. Only directly "
                + "behind: the two tiles either side of that are still the front. A creature that "
                + "is flanked turns to face whoever did it once the blow has landed, so the "
                + "position is worth one attack and has to be earned again."));

            into.Add(Section("OPPORTUNITY ATTACKS"));
            into.Add(Text(
                "The three tiles a creature is looking at are the ones it is watching. Walk out of "
                + "an enemy's front three and they get a swing at you: their own weapon attack, for "
                + "no action points, and you answer it exactly as you would answer an attack on "
                + "their own turn. Stepping from one watched tile to another is safe -- it is "
                + "leaving their sight that costs. The warning on the cursor is the rule; whether "
                + "they take the swing is up to them, and the log says when they let you go."));
            into.Add(Text(
                "The swing is whatever they are carrying -- there is no choosing an element for it, "
                + "so what a creature threatens you with is something you can read off their card "
                + "before you decide to walk. It is optional and it costs them the element, so it "
                + "is not free for either of you."));
            into.Add(Text(
                "The cursor warns you before you commit, and you carry on moving afterwards whether "
                + "the swing landed or not."));

            into.Add(Section("SHOOTING"));
            into.Add(Text(
                "A ranged skill rolls to hit before anybody answers it. Its chance starts at what "
                + "the skill says and falls with every tile past the first; each point of Skill "
                + "adds five. Anybody standing on a tile the shot passes over -- yours or theirs "
                + "-- is cover, and each of them takes twenty off. The cursor shows the chance "
                + "and names the cover before you commit, and the arc it draws is the line the "
                + "shot will fly."));
            into.Add(Text(
                "A miss still costs the element and the action points: the arrow left the bow. "
                + "The element you spent is shown, because it was seen leaving. A shot that lands "
                + "is answered like any other attack."));

            into.Add(Section("GETTING PAST PEOPLE"));
            into.Add(Text(
                "Nobody walks through anybody. A route goes around whoever is standing in it, "
                + "friend or enemy, and the extra tiles are extra action points -- so a line of "
                + "allies is a wall, and one creature in a corridor is a toll. Hovering a tile "
                + "draws the exact route the walk would take."));

            into.Add(Section("ELEMENTS"));
            into.Add(Text(
                "Every creature holds a pool of elements, bought when it is built. They are spent, "
                + "not refreshed -- an element you use is gone until something gives it back, and "
                + "the ones that come back come back in the order they were spent, oldest first."));
            into.Add(Text(
                "What you have spent is public. Anybody can see which runes have left your hand, "
                + "which is how they work out what you might still be holding -- and how you work "
                + "out what they are."));

            into.Add(Section("FIGHTING WITH NOTHING"));
            into.Add(Text(
                "A character carrying no weapon is not unarmed in the useless sense. Every species "
                + "has its own strike -- fists, claws, a slam, a shiv -- and it appears the moment "
                + "the weapon slot is empty and goes away again the moment it is not."));
            into.Add(Text(
                "It hits for less than a real weapon, and in exchange it is the only attack in the "
                + "game you choose the element of: any of the four common ones, picked each time "
                + "you throw it. A sword asks its question in one element because a sword is a "
                + "particular thing. A fist is not."));

            into.Add(Section("WHICH ELEMENT ANSWERS WHICH"));
            into.Add(Text(
                "The four common elements answer Arcana. Arcana answers Lux and Nyx. Lux and Nyx "
                + "answer the common four. Nothing is simply best: what an element buys is reach, "
                + "not power."));

            var chart = new VisualElement();
            chart.AddToClassList("element-chart");
            chart.AddToClassList("help__chart");
            ElementChart.Build(chart);
            into.Add(chart);

            into.Add(Section("CLASHES"));
            into.Add(Text(
                "An attack on an enemy is contested. The attacker commits the element their skill "
                + "is made of; the defender is asked to put one up in answer, without being told "
                + "what is coming. The two are compared on the table above."));
            into.Add(Text(
                "Neither commitment is announced until both are made. An attack aimed at yourself "
                + "or at an ally is not contested and simply happens."));

            into.Add(Section("WHAT A CLASH IS WORTH"));
            into.Add(Bullet("As the defender, a win means no damage and you keep the element."));
            into.Add(Bullet("A tie means no damage, but the element is gone."));
            into.Add(Bullet("A loss means you take the hit and the element is gone."));
            into.Add(Bullet(
                "As the attacker, only a win lands the blow. A tie stops it as completely as a "
                + "loss does, and your element is spent whichever way it goes."));

            into.Add(Section("ADVANTAGE AND FLANKING"));
            into.Add(Text(
                "A creature with advantage -- granted by some equipment -- commits two elements to "
                + "a clash and the better of them answers. A creature caught from behind also "
                + "commits two, and the worse of them answers."));
            into.Add(Text(
                "Both cost two elements, and that symmetry is deliberate: being better placed is "
                + "not free, it makes you spend twice as fast. Advantage and a flank on the same "
                + "side cancel out, leaving an ordinary single commitment."));

            into.Add(Section("BUILDING A CHARACTER"));
            into.Add(Bullet("Vitality gives one more health per point."));
            into.Add(Bullet("Willpower gives one more action point per point."));
            into.Add(Bullet(
                "Endurance gives one more speed per point, on a base of eight. Armour takes "
                + "speed away, and speed prices every step."));
            into.Add(Bullet("Toughness is healed back at the start of every one of your turns."));
            into.Add(Bullet(
                "Strength and Dexterity are added to weapon damage. Each weapon skill says which "
                + "one it uses: a sword hits for 4 + STR, a bow for 3 + DEX."));
            into.Add(Bullet(
                "Skill will raise the chance to hit on skills that roll for it. None of the "
                + "shipped skills roll yet."));
            into.Add(Bullet(
                "Weapons grant skills. Armour grants armour and costs speed. A shield grants "
                + "armour and costs the hand. Nothing you equip touches an attribute."));
            into.Add(Text(
                "Attributes get more expensive the higher you push them, so a spread and a spike "
                + "cost differently for the same total. Elements are bought out of a separate "
                + "budget that grows by one each level."));

            into.Add(Section("READING THE ARENA"));
            into.Add(Bullet(
                "The bar along the top is turn order. The creature acting is the large one, and it "
                + "shows the action points it has left."));
            into.Add(Bullet(
                "Above your skills is your own hand, and beside it what you have spent, in the "
                + "order it went. The leftmost spent rune is the next one you would get back."));
            into.Add(Bullet(
                "Clicking any creature inspects it: what it is holding, what it has spent, and "
                + "which of its skills you have watched it use."));
            into.Add(Bullet(
                "The log in the bottom left is everything that has happened. Lines you are part of "
                + "are the bright ones."));
            into.Add(Bullet(
                "Hovering any rune, anywhere, says what it beats and what beats it."));
        }

        static Label Section(string title)
        {
            var label = new Label(title);
            label.AddToClassList("help__section");
            return label;
        }

        static Label Text(string body)
        {
            var label = new Label(body);
            label.AddToClassList("help__text");
            return label;
        }

        static VisualElement Bullet(string body)
        {
            var row = new VisualElement();
            row.AddToClassList("help__bullet");

            var dot = new Label("\u00b7");
            dot.AddToClassList("help__dot");
            row.Add(dot);

            var label = new Label(body);
            label.AddToClassList("help__text");
            row.Add(label);

            return row;
        }
    }
}
