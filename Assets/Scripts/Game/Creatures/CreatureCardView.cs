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
        Label m_Subtitle;
        Label m_Controller;
        Label m_Hp;
        Label m_Ap;
        Label m_Speed;
        Label m_Description;

        CreatureState m_Observed;

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
            m_Subtitle = root.Q<Label>("card-subtitle");
            m_Controller = root.Q<Label>("card-controller");
            m_Hp = root.Q<Label>("card-hp");
            m_Ap = root.Q<Label>("card-ap");
            m_Speed = root.Q<Label>("card-speed");
            m_Description = root.Q<Label>("card-description");

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

            m_Observed = creature;

            if (m_Observed != null)
            {
                m_Observed.Changed += Redraw;
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
        }

        void BuildPips(int filled, int total)
        {
            m_ApPips.Clear();

            for (var i = 0; i < total; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");

                if (i < filled)
                {
                    pip.AddToClassList("pip--filled");
                }

                m_ApPips.Add(pip);
            }
        }
    }
}
