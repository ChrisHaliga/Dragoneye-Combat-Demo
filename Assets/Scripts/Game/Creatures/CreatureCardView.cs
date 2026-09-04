using Dragoneye.Data;
using Dragoneye.Combat;
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
            m_Subtitle.text = definition != null
                ? $"{definition.SpeciesName} · {definition.ClassName} · {PartyPalette.NameOf(creature.Party)}"
                : PartyPalette.NameOf(creature.Party);

            m_Controller.text = $"Controlled by {CreatureDisplay.ControllerName(creature)}";
            m_Hp.text = $"{creature.CurrentHp} / {creature.MaxHp}";
            m_Ap.text = $"{creature.CurrentAp} / {creature.MaxAp}";
            m_Speed.text = creature.Speed.ToString();
            m_Description.text = definition != null ? definition.Description : string.Empty;

            BuildPips(creature.CurrentAp, creature.MaxAp);
            BuildElements();
        }

        /// <summary>
        /// The element counters, which DE-001 asks to be primary rather than behind a menu.
        ///
        /// Which of the two is shown depends on who is looking. Your own creature shows what it can
        /// still spend, because that is the decision in front of you. Anyone else shows what it has
        /// been seen to spend, because that is all you are entitled to know -- and it is the same
        /// information every other player has, so nobody has to track it by hand.
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
            var counts = mine ? m_ObservedPool.Pool : m_ObservedPool.Revealed;

            m_ElementsTitle.text = mine ? "POOL" : "SPENT";

            foreach (var element in ElementInfo.All)
            {
                m_Elements.Add(BuildElementCount(element, counts[element]));
            }
        }

        /// <summary>
        /// One element, drawn as a lit gem carrying its count.
        ///
        /// The same shape the creator and the roster use for a pool, so a player learns to read it
        /// once. Elements a creature does not hold stay drawn but nearly dark, which keeps the row a
        /// fixed width as a fight drains it.
        /// </summary>
        static VisualElement BuildElementCount(Element element, int amount)
        {
            var gem = new VisualElement();
            gem.AddToClassList("element-count");
            gem.EnableInClassList("element-count--none", amount == 0);
            gem.style.unityBackgroundImageTintColor = ElementPalette.ForElement(element);
            gem.tooltip = ElementInfo.NameOf(element);

            var value = new Label(amount.ToString());
            value.AddToClassList("element-count__value");
            gem.Add(value);

            return gem;
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
