using Dragoneye.Combat;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Two words across the middle of the screen when a turn changes hands, gone again in a moment.
    ///
    /// The turn bar records whose turn it is, permanently and quietly, at the top of the screen. It
    /// does not *announce* it -- and a player who has been watching an ogre for twenty seconds needs
    /// to be told, not merely informed, that it is now their go. Every tactics game with a
    /// reputation for feel does exactly this: a banner, a beat, and then out of the way.
    ///
    /// Built into the HUD document from code rather than authored in the markup, because it is
    /// transient. It exists for a second and a half and is torn down; a permanent element that is
    /// hidden ninety-eight per cent of the time is a permanent thing to lay out around.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class TurnBannerView : MonoBehaviour
    {
        [SerializeField, Tooltip("Every creature on the board, to name the active one.")]
        CreatureRegistry m_Creatures;

        [SerializeField, Min(0.3f), Tooltip("Seconds the banner holds before it leaves.")]
        float m_Hold = 1.25f;

        VisualElement m_Root;
        VisualElement m_Banner;
        uint m_Announced;
        int m_AnnouncedRound;
        float m_Shown;

        void Start()
        {
            var document = GetComponent<UIDocument>().rootVisualElement;

            // The template root, not the document root: the stylesheet is attached inside it.
            m_Root = document.Q<VisualElement>("root") ?? document;
        }

        void Update()
        {
            var turns = TurnState.Current;

            if (turns == null || turns.IsOver || m_Creatures == null)
            {
                m_Announced = 0;
                return;
            }

            var active = m_Creatures.ByTurnId(turns.ActiveId);

            // The same creature acting twice in a row -- a solo match with one fighter -- is still a
            // new turn if the round moved on.
            if (active != null && (active.TurnId != m_Announced || turns.Round != m_AnnouncedRound))
            {
                m_Announced = active.TurnId;
                m_AnnouncedRound = turns.Round;
                Show(active, turns.Round);
            }

            Advance();
        }

        void Show(CreatureState active, int round)
        {
            Clear();

            var mine = LocalPlayer.Controls(active);

            m_Banner = new VisualElement();
            m_Banner.AddToClassList("turn-banner");
            m_Banner.EnableInClassList("turn-banner--mine", mine);
            m_Banner.pickingMode = PickingMode.Ignore;

            var title = new Label(mine ? "YOUR TURN" : $"{active.DisplayName.ToUpperInvariant()}'S TURN");
            title.AddToClassList("turn-banner__title");
            title.pickingMode = PickingMode.Ignore;

            if (!mine)
            {
                title.style.color = PartyPalette.ForParty(active.Party);
            }

            m_Banner.Add(title);

            var sub = new Label($"ROUND {round}");
            sub.AddToClassList("turn-banner__round");
            sub.pickingMode = PickingMode.Ignore;
            m_Banner.Add(sub);

            m_Root.Add(m_Banner);
            m_Shown = 0f;

            // The frame after it exists, so it eases in from where the stylesheet starts it.
            var banner = m_Banner;
            banner.schedule.Execute(() => banner.AddToClassList("turn-banner--in"));
        }

        void Advance()
        {
            if (m_Banner == null)
            {
                return;
            }

            m_Shown += Time.unscaledDeltaTime;

            if (m_Shown > m_Hold)
            {
                m_Banner.RemoveFromClassList("turn-banner--in");
                m_Banner.AddToClassList("turn-banner--out");
            }

            if (m_Shown > m_Hold + 0.5f)
            {
                Clear();
            }
        }

        void Clear()
        {
            m_Banner?.RemoveFromHierarchy();
            m_Banner = null;
        }
    }
}
