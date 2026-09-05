using Dragoneye.Combat;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The summary card for whatever creature is selected.
    ///
    /// Driven entirely by <see cref="CreatureSelection"/>, so a board click and a portrait click
    /// produce the same card without either producer knowing the other exists.
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
        VisualElement m_ApPips;
        Label m_Name;
        VisualElement m_Portrait;
        VisualElement m_Xp;
        Label m_Subtitle;
        Label m_Controller;
        Label m_Hp;
        Label m_Ap;
        Label m_Speed;
        Label m_Description;
        Label m_ElementsTitle;
        VisualElement m_Elements;

        CreatureState m_Observed;
        CreaturePool m_ObservedPool;

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
            m_Controller = root.Q<Label>("card-controller");
            m_Hp = root.Q<Label>("card-hp");
            m_Ap = root.Q<Label>("card-ap");
            m_Speed = root.Q<Label>("card-speed");
            m_Description = root.Q<Label>("card-description");
            m_ElementsTitle = root.Q<Label>("card-elements-title");
            m_Elements = root.Q<VisualElement>("card-elements");

            if (m_Card == null || m_ApPips == null || m_Name == null)
            {
                Debug.LogError("CreatureCardView could not find its elements; check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }

            m_Selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged(m_Selection.Selected);
        }

        void OnDestroy()
        {
            if (m_Selection != null)
            {
                m_Selection.SelectionChanged -= OnSelectionChanged;
            }

            Observe(null);
        }

        void OnSelectionChanged(CreatureState creature)
        {
            Observe(creature);
            Redraw();
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

            m_Observed = creature;
            m_ObservedPool = creature != null ? creature.GetComponent<CreaturePool>() : null;

            if (m_Observed != null)
            {
                m_Observed.Changed += Redraw;
            }

            if (m_ObservedPool != null)
            {
                m_ObservedPool.Changed += Redraw;
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
            }

            m_Name.text = creature.DisplayName;

            // Level, species, class -- the same line every other screen shows. Which side it is on
            // moved to the controller line below, where the rest of "who is running this" lives.
            m_Subtitle.text = definition != null
                ? CharacterSheet.Describe(creature.Level, definition.SpeciesName,
                    definition.ClassName)
                : string.Empty;

            // The side always, the player only when there is one. Telling somebody that the
            // goblin is run by the computer is telling them what a goblin is.
            m_Controller.text = creature.IsComputerControlled
                ? PartyPalette.NameOf(creature.Party)
                : $"{PartyPalette.NameOf(creature.Party)}  ·  "
                    + CreatureDisplay.ControllerName(creature);
            m_Hp.text = $"{creature.CurrentHp} / {creature.MaxHp}";
            m_Ap.text = $"{creature.CurrentAp} / {creature.MaxAp}";
            m_Speed.text = creature.Speed.ToString();
            m_Description.text = definition != null ? definition.Description : string.Empty;

            BuildPips(creature.CurrentAp, creature.MaxAp);
            BuildExperience(creature);
            BuildElements();
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

            if (mine)
            {
                m_ElementsTitle.text = "POOL";

                foreach (var element in ElementInfo.All)
                {
                    var held = m_ObservedPool.Pool[element];
                    m_Elements.Add(CharacterSheet.ElementChip(element, held, held == 0));
                }

                return;
            }

            // Somebody else's hand, so what is drawn is what has been worked out about it: how big
            // it is, which of it has a name, and how much of it does not. The size is exact --
            // everybody watched the spends -- and it is the number that decides whether attacking
            // is worth it, so it goes in the heading rather than being left to be counted.
            var guess = PossibleElements.Seen(m_ObservedPool.Ledger);

            m_ElementsTitle.text = $"HAND  ·  {m_ObservedPool.InHand} OF {m_ObservedPool.Total}";

            foreach (var element in ElementInfo.All)
            {
                var available = guess.Known[element];
                var chip = CharacterSheet.ElementChip(element, available, available == 0);

                // Proven to exist but currently spent is a different thing from never seen, and a
                // player tracking a fight wants both.
                var spent = m_ObservedPool.Identified[element] - available;

                chip.tooltip = spent > 0
                    ? $"{ElementInfo.NameOf(element)} · {available} in hand, {spent} spent"
                    : $"{ElementInfo.NameOf(element)} · {available} in hand";

                m_Elements.Add(chip);
            }

            m_Elements.Add(CharacterSheet.UnknownChip(guess.Unknown));
        }

        /// <summary>
        /// One pip per half-unit, since that is the smallest amount that can actually be spent.
        ///
        /// Pips at whole-point granularity would have to round, and rounding an action budget is the
        /// one place a player will notice: a creature showing "1 AP" that cannot afford a one-point
        /// skill reads as a bug rather than as a half spent on movement. Every second pip is marked
        /// so a whole point is still countable at a glance.
        /// </summary>
        void BuildPips(Ap filled, Ap total)
        {
            m_ApPips.Clear();

            for (var i = 0; i < total.Units; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");
                pip.EnableInClassList("pip--filled", i < filled.Units);
                pip.EnableInClassList("pip--whole", (i + 1) % Ap.UnitsPerPoint == 0);

                m_ApPips.Add(pip);
            }
        }
    }
}
