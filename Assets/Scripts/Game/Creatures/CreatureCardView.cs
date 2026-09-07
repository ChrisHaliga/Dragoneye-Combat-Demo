using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Dragoneye.UI;
using Dragoneye.Game;
using Dragoneye.Game.Combat;

namespace Dragoneye.Game.Creatures
{
    /// <summary>
    /// The summary card for whatever creature has been asked about.
    ///
    /// Driven entirely by <see cref="CreatureSelection"/>, so every way of asking produces the same
    /// card without any of them knowing the others exist. Asking is a right-click and an Inspect:
    /// the card used to appear on any click and on being attacked, which meant it was usually up,
    /// usually about somebody the player had stopped caring about, and always over the board.
    ///
    /// It closes from the cross in its corner, and by itself the moment attention goes elsewhere.
    /// The pin beside the cross stops that, for reading two creatures against each other.
    ///
    /// AP is shown as discrete pips rather than a bar: players count remaining actions, they do not
    /// estimate them. It appears here and not on the portraits because until turn order exists it
    /// would always render full, which is a readout that teaches the player to ignore it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class CreatureCardView : MonoBehaviour
    {
        [SerializeField]
        CreatureSelection m_Selection;

        VisualElement m_Card;
        Button m_Pin;
        bool m_Pinned;

        // The frame the card was last asked for. The click that asks is a click outside the card,
        // and without this it would be the same click that dismissed it.
        int m_AskedOn = -1;
        VisualElement m_ApPips;
        Label m_Name;
        VisualElement m_Portrait;
        VisualElement m_Xp;
        Label m_Subtitle;
        Label m_Party;
        Label m_Controller;
        Label m_Hp;
        Label m_Armour;
        VisualElement m_ArmourRow;
        Label m_Ap;
        Label m_Speed;
        Label m_Description;
        Label m_ElementsTitle;
        VisualElement m_Elements;
        Label m_SkillsTitle;
        VisualElement m_Skills;

        /// <summary>The interpunct the rest of the HUD separates fields with.</summary>
        const string Bullet = "\u00b7";

        CreatureState m_Observed;
        CreaturePool m_ObservedPool;
        SkillCommands m_ObservedSkills;
        CombatPlayback m_Playback;

        void Start()
        {
            if (m_Selection == null)
            {
                Debug.LogError("CreatureCardView has no selection.", this);
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;
            CreatureDisplay.MakeClickThrough(root);

            m_Card = root.Q<VisualElement>("summary-card");
            m_ApPips = root.Q<VisualElement>("card-ap-pips");
            m_Name = root.Q<Label>("card-name");
            m_Portrait = root.Q<VisualElement>("card-portrait");
            m_Xp = root.Q<VisualElement>("card-xp");
            m_Subtitle = root.Q<Label>("card-subtitle");
            m_Party = root.Q<Label>("card-party");
            m_Controller = root.Q<Label>("card-controller");
            m_Hp = root.Q<Label>("card-hp");
            m_Armour = root.Q<Label>("card-armour");
            m_ArmourRow = root.Q<VisualElement>("card-armour-row");
            m_Ap = root.Q<Label>("card-ap");
            m_Speed = root.Q<Label>("card-speed");
            m_Description = root.Q<Label>("card-description");
            m_ElementsTitle = root.Q<Label>("card-elements-title");
            m_Elements = root.Q<VisualElement>("card-elements");
            m_SkillsTitle = root.Q<Label>("card-skills-title");
            m_Skills = root.Q<VisualElement>("card-skills");

            if (m_Card == null || m_ApPips == null || m_Name == null)
            {
                Debug.LogError("CreatureCardView could not find its elements; check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }

            BindCorner(root);

            m_Selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged(m_Selection.Selected);
        }

        /// <summary>
        /// The two buttons over the top right: pin, then close.
        ///
        /// Both are drawn rather than lettered. A cross in a typeface is the letter x sitting where
        /// a button should be, and there is no character for "pinned" at all.
        /// </summary>
        void BindCorner(VisualElement root)
        {
            var close = root.Q<Button>("card-close");

            if (close != null)
            {
                HudIcons.DrawClose(close);
                close.tooltip = "Close.";
                close.clicked += () => m_Selection.Clear();
            }

            m_Pin = root.Q<Button>("card-pin");

            if (m_Pin != null)
            {
                HudIcons.DrawPin(m_Pin);
                m_Pin.clicked += TogglePin;
                RefreshPin();
            }
        }

        void TogglePin()
        {
            m_Pinned = !m_Pinned;
            RefreshPin();
        }

        void RefreshPin()
        {
            if (m_Pin == null)
            {
                return;
            }

            m_Pin.EnableInClassList("icon-button--pinned", m_Pinned);
            m_Pin.tooltip = m_Pinned
                ? "Pinned. It stays until you close it."
                : "Pin it open. Otherwise it closes when you look elsewhere.";
        }

        void OnDestroy()
        {
            if (m_Selection != null)
            {
                m_Selection.SelectionChanged -= OnSelectionChanged;
            }

            if (m_Playback != null)
            {
                m_Playback.Changed -= Redraw;
            }

            Observe(null);
        }

        // The vitals on the card are the shown ones, so the card redraws as the fight is shown.
        void Update()
        {
            CloseIfLookedAway();

            if (m_Playback == CombatPlayback.Current)
            {
                return;
            }

            if (m_Playback != null)
            {
                m_Playback.Changed -= Redraw;
            }

            m_Playback = CombatPlayback.Current;

            if (m_Playback != null)
            {
                m_Playback.Changed += Redraw;
            }
        }

        void OnSelectionChanged(CreatureState creature)
        {
            m_AskedOn = Time.frameCount;
            Observe(creature);
            Redraw();
        }

        /// <summary>
        /// Shuts an unpinned card as soon as the player presses anywhere that is not it.
        ///
        /// Watched from the mouse rather than from a callback on the panel, because the click that
        /// should close it is as often on the board as on the HUD, and those arrive by different
        /// routes. One test, in the one place, against the card's own rectangle.
        /// </summary>
        void CloseIfLookedAway()
        {
            // Not on the frame it was asked for. Inspect is on a menu, and that menu is outside the
            // card -- so the click that opens it is a click that would close it, and whether the
            // two land in the same frame is a matter of when input is pumped rather than of intent.
            if (m_Pinned || m_Card == null || !m_Selection.HasSelection
                || Time.frameCount <= m_AskedOn + 1)
            {
                return;
            }

            var mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || m_Card.panel == null)
            {
                return;
            }

            var screen = mouse.position.ReadValue();
            var point = RuntimePanelUtils.ScreenToPanel(m_Card.panel,
                new Vector2(screen.x, Screen.height - screen.y));

            if (!m_Card.worldBound.Contains(point))
            {
                m_Selection.Clear();
            }
        }

        /// <summary>
        /// Follows only the selected creature. Watching every creature would repaint the card on
        /// damage taken across the board, which is work for a card that is not showing them.
        /// </summary>
        void Observe(CreatureState creature)
        {
            if (m_Observed == creature)
            {
                return;
            }

            if (m_Observed != null)
            {
                m_Observed.Changed -= Redraw;
            }

            if (m_ObservedPool != null)
            {
                m_ObservedPool.Changed -= Redraw;
            }

            if (m_ObservedSkills != null)
            {
                m_ObservedSkills.SeenChanged -= Redraw;
            }

            m_Observed = creature;
            m_ObservedPool = creature != null ? creature.Pool : null;
            m_ObservedSkills = creature != null ? creature.SkillCommands : null;

            if (m_Observed != null)
            {
                m_Observed.Changed += Redraw;
            }

            if (m_ObservedPool != null)
            {
                m_ObservedPool.Changed += Redraw;
            }

            if (m_ObservedSkills != null)
            {
                m_ObservedSkills.SeenChanged += Redraw;
            }
        }

        void Redraw()
        {
            var creature = m_Selection.Selected;
            var visible = creature != null;

            m_Card.EnableInClassList("is-hidden", !visible);
            m_Card.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;

            if (!visible)
            {
                return;
            }

            var definition = creature.Definition;

            if (m_Portrait != null)
            {
                m_Portrait.Clear();
                m_Portrait.style.backgroundImage = new StyleBackground();
                CreatureDisplay.DrawPortrait(m_Portrait, creature);

                var tint = PartyPalette.ForParty(creature.Party);

                m_Portrait.style.borderTopColor = m_Portrait.style.borderBottomColor =
                    m_Portrait.style.borderLeftColor = m_Portrait.style.borderRightColor = tint;
            }

            m_Name.text = creature.DisplayName;

            // Level, species, class -- the same line every other screen shows. Which side it is on
            // moved to the controller line below, where the rest of "who is running this" lives.
            m_Subtitle.text = definition != null
                ? CharacterSheet.Describe(creature.Level, definition.SpeciesName,
                    definition.ClassName)
                : string.Empty;

            // The side is the band at the top now, in its own colour, so this line is left with
            // the one thing it was always for: the person, when there is one. Telling somebody
            // that the goblin is run by the computer is telling them what a goblin is.
            if (m_Party != null)
            {
                m_Party.text = "TEAM " + PartyPalette.NameOf(creature.Party).ToUpperInvariant();
                m_Party.style.color = PartyPalette.ForParty(creature.Party);
            }

            m_Controller.text = creature.IsComputerControlled
                ? string.Empty
                : CreatureDisplay.ControllerName(creature);

            m_Controller.EnableInClassList("is-hidden", creature.IsComputerControlled);

            // The shown numbers, not the live ones: the card is read, and what is read should be
            // what has been watched happen.
            m_Hp.text = $"{Shown.Hp(creature)} / {creature.MaxHp}";

            // Always shown, even at nothing. Armour is one of the four numbers a creature is, and
            // a row that comes and goes is a row the player cannot learn the position of.
            if (m_Armour != null && m_ArmourRow != null)
            {
                m_Armour.text = $"{Shown.Armour(creature)} / {creature.MaxArmour}";
                m_ArmourRow.tooltip = StatLore.Armour(creature.MaxArmour);
            }

            m_Ap.text = $"{Shown.Ap(creature)} / {creature.MaxAp}";
            m_Speed.text = creature.Speed.ToString();

            // What each number does, on the row. The card is where a player goes to understand a
            // creature, and a stat with no explanation is a number they are being asked to guess.
            if (m_Hp.parent != null)
            {
                m_Hp.parent.tooltip = StatLore.Health(creature.Regen);
            }

            if (m_Ap.parent != null)
            {
                m_Ap.parent.tooltip = StatLore.ActionPoints();
            }

            if (m_Speed.parent != null)
            {
                m_Speed.parent.tooltip = StatLore.Speed(creature.Speed);
            }
            m_Description.text = definition != null ? definition.Description : string.Empty;

            BuildPips(Shown.Ap(creature), creature.MaxAp);
            BuildExperience(creature);
            BuildElements();
            BuildSkills();
        }

        /// <summary>
        /// What this creature can do, or -- for anybody else's -- what it has been caught doing.
        ///
        /// Your own creature lists everything, because you are entitled to it and the bar only
        /// shows what is affordable this instant. Anybody else lists only the skills they have used
        /// in front of you, which is public by the same reasoning that makes a spent element
        /// public: it happened where everyone could see.
        ///
        /// It earns its place now that the log names skills. "Ogre used Cleave" is only useful to
        /// somebody who can find out what Cleave is.
        /// </summary>
        void BuildSkills()
        {
            if (m_Skills == null || m_SkillsTitle == null)
            {
                return;
            }

            m_Skills.Clear();

            if (m_ObservedSkills == null || m_ObservedPool == null)
            {
                m_SkillsTitle.text = string.Empty;
                return;
            }

            var mine = m_ObservedPool.CanSee;

            if (mine)
            {
                m_SkillsTitle.text = "SKILLS";

                foreach (var skill in m_ObservedSkills.Skills)
                {
                    m_Skills.Add(SkillRow(skill));
                }

                return;
            }

            // What has been *shown* used. The replicated list is a turn ahead of the screen.
            var shown = Shown.Of(m_Observed);
            var seen = shown != null ? (IReadOnlyList<int>)shown.Seen : m_ObservedSkills.SeenSkillIds;
            m_SkillsTitle.text = "SEEN USING " + Bullet + " " + seen.Count;

            foreach (var id in seen)
            {
                if (SkillCatalog.Current != null && SkillCatalog.Current.TryGetSkill(id, out var spec))
                {
                    m_Skills.Add(SkillRow(spec));
                }
            }

            if (m_Skills.childCount == 0)
            {
                m_Skills.Add(Note("Nothing yet."));
            }
        }

        /// <summary>One skill: what it is called, and what it costs to throw.</summary>
        static VisualElement SkillRow(SkillSpec skill)
        {
            var row = new VisualElement();
            row.AddToClassList("card-skill");

            var head = new VisualElement();
            head.AddToClassList("card-skill__head");

            var name = new Label(skill.Name);
            name.AddToClassList("card-skill__name");
            head.Add(name);

            // The element in its own colour and the points in a grey that no element uses, with a
            // dot between them. Both halves used to be the element's colour, which for a Geo or an
            // Aero skill put two gold-ish numbers side by side and nothing to tell them apart.
            var cost = new Label(CombatLogLines.Cost(skill).Replace(", ",
                CombatLogLines.Tint("#8B93A5", " \u00b7 ")));

            cost.AddToClassList("card-skill__cost");
            head.Add(cost);
            row.Add(head);

            // Written out, not hidden behind a hover. Reading an enemy's card is how a player
            // learns what is about to be thrown at them, and a name alone tells them nothing.
            var text = new Label(CharacterSheet.Describe(skill));
            text.AddToClassList("card-skill__text");
            row.Add(text);

            row.tooltip = ElementLore.Describe(skill.Element);

            return row;
        }

        /// <summary>
        /// How far this character is towards its next level, including what it has earned today.
        ///
        /// Only for a character somebody brought. A premade does not level and has nowhere to put
        /// experience, so it gets no bar rather than an empty one.
        ///
        /// The build carries what the character walked in with and the match tally carries what it
        /// has earned since, because banking writes to the owner's save file rather than back into
        /// the replicated build -- so the two have to be added here to read as one number.
        /// </summary>
        void BuildExperience(CreatureState creature)
        {
            if (m_Xp == null)
            {
                return;
            }

            var characters = PlayerCharacters.Current;
            var build = characters != null && creature.IsPlayerCharacter
                ? characters.BuildFor(creature.BuildSlot)
                : null;

            m_Xp.EnableInClassList("is-hidden", build == null);

            if (build == null)
            {
                return;
            }

            CharacterSheet.Experience(m_Xp, build.Level,
                build.Xp + characters.XpFor(creature.BuildSlot));
        }

        /// <summary>
        /// The element counters, which DE-001 asks to be primary rather than behind a menu.
        ///
        /// Which of the two is shown depends on who is looking. Your own creature shows what it can
        /// still spend, because that is the decision in front of you.
        ///
        /// Anyone else shows what has been *proven* about them, plus a count of what has not --
        /// which is what you actually know, and is the same for every player watching. Proven is
        /// not the same as spent: an element spent, taken back and spent again was seen twice and
        /// only ever existed once, and a tally that counted both would have opponents holding more
        /// than they own.
        /// </summary>
        void BuildElements()
        {
            if (m_Elements == null)
            {
                return;
            }

            m_Elements.Clear();

            if (m_ObservedPool == null)
            {
                m_ElementsTitle.text = string.Empty;
                return;
            }

            var mine = m_ObservedPool.CanSee;
            var spent = SpentCounts();

            // Two rows, always: what can still be spent, and what has been. The second is the
            // graveyard, and it is the half a player was having to reconstruct from memory --
            // knowing an ogre has burned both its Pyro is most of knowing what to throw next.
            //
            // Both halves are public for an opponent. What is spent was spent in front of
            // everybody; what is left is only ever counted, never named.
            if (mine)
            {
                m_ElementsTitle.text = $"YOUR POOL  ·  {m_ObservedPool.Pool.Total} "
                    + $"OF {m_ObservedPool.Total}";

                foreach (var element in ElementInfo.All)
                {
                    var held = m_ObservedPool.Pool[element];

                    if (held > 0)
                    {
                        m_Elements.Add(CharacterSheet.ElementChip(element, held));
                    }
                }

                if (m_ObservedPool.Pool.Total == 0)
                {
                    m_Elements.Add(Note("Nothing left to spend"));
                }

                AddSpentRow(spent);
                return;
            }

            var guess = PossibleElements.Seen(ShownLedger());

            m_ElementsTitle.text = $"THEIR HAND  ·  {ShownInHand()} "
                + $"OF {m_ObservedPool.Total}";

            foreach (var element in ElementInfo.All)
            {
                var available = guess.Known[element];

                if (available > 0)
                {
                    var chip = CharacterSheet.ElementChip(element, available);
                    chip.tooltip = "Seen, and back in hand.\n\n"
                        + ElementLore.Describe(element);
                    m_Elements.Add(chip);
                }
            }

            var unknown = CharacterSheet.UnknownChip(guess.Unknown);
            unknown.tooltip = guess.Unknown > 0
                ? $"{guess.Unknown} element{(guess.Unknown == 1 ? string.Empty : "s")} in hand that "
                    + "nobody has seen yet"
                : "Everything in this hand has been seen";
            m_Elements.Add(unknown);

            AddSpentRow(spent);
        }

        /// <summary>
        /// The graveyard: what has been spent and not taken back.
        ///
        /// Public for everybody, because every one of them was spent in the open. Drawn even when
        /// it is empty, so the row does not appear and disappear as a fight goes on and move the
        /// rest of the card around under the cursor.
        /// </summary>
        void AddSpentRow(ElementCounts spent)
        {
            var heading = new Label($"SPENT  ·  {spent.Total}");
            heading.AddToClassList("card__spent-title");
            m_Elements.Add(heading);

            if (spent.Total == 0)
            {
                m_Elements.Add(Note("Nothing spent yet"));
                return;
            }

            foreach (var element in ElementInfo.All)
            {
                if (spent[element] > 0)
                {
                    var chip = CharacterSheet.ElementChip(element, spent[element]);
                    chip.AddToClassList("element-chip--spent");
                    chip.tooltip = "Spent, and not taken back.\n\n"
                        + ElementLore.Describe(element);
                    m_Elements.Add(chip);
                }
            }
        }

        /// <summary>What is currently spent, per element, as it has been shown spent.</summary>
        ElementCounts SpentCounts()
        {
            var spent = ElementCounts.Empty;

            foreach (var element in ShownOutstanding())
            {
                spent = spent.Plus(element, 1);
            }

            return spent;
        }

        /// <summary>
        /// The public half of this creature's elements, as the watcher has been shown it.
        ///
        /// Before the fight opens there is nothing to have shown, and the replicated record --
        /// which is empty then -- is the answer. After it, the record is a turn ahead of the
        /// screen and the shown fight is what the card reads.
        /// </summary>
        ElementLedger ShownLedger()
        {
            var shown = Shown.Of(m_Observed);

            if (shown == null)
            {
                return m_ObservedPool.Ledger;
            }

            return new ElementLedger(ElementCounts.Empty, shown.Revealed, shown.Outstanding,
                m_ObservedPool.Total, shown.Identified);
        }

        IReadOnlyList<Element> ShownOutstanding()
        {
            var shown = Shown.Of(m_Observed);
            return shown != null ? shown.Outstanding : m_ObservedPool.Outstanding;
        }

        int ShownInHand()
        {
            var left = m_ObservedPool.Total - ShownOutstanding().Count;
            return left < 0 ? 0 : left;
        }

        static Label Note(string text)
        {
            var note = new Label(text);
            note.AddToClassList("card__elements-note");
            return note;
        }

        /// <summary>
        /// One pip per half-unit, since that is the smallest amount that can actually be spent.
        ///
        /// Pips at whole-point granularity would have to round, and rounding an action budget is the
        /// one place a player will notice: a creature showing "1 AP" that cannot afford a one-point
        /// skill reads as a bug rather than as a half spent on movement. Every second pip is marked
        /// so a whole point is still countable at a glance.
        /// </summary>
        void BuildPips(Ap filled, Ap total) => ApPips.Fill(m_ApPips, filled, total);
    }
}
