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
    /// to be told, not merely informed, that it is now their go.
    ///
    /// Driven by the record as it is shown, so the banner goes up when the turn is shown to begin
    /// and not when the server got to it. Built into the HUD document from code rather than
    /// authored in the markup, because it is transient.
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
        float m_Shown;
        CombatPlayback m_Playback;

        void Start()
        {
            var document = GetComponent<UIDocument>().rootVisualElement;

            // The template root, not the document root: the stylesheet is attached inside it.
            m_Root = document.Q<VisualElement>("root") ?? document;
        }

        void OnDestroy()
        {
            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }
        }

        void Update()
        {
            if (m_Playback != CombatPlayback.Current)
            {
                if (m_Playback != null)
                {
                    m_Playback.Presenting -= OnPresenting;
                }

                m_Playback = CombatPlayback.Current;

                if (m_Playback != null)
                {
                    m_Playback.Presenting += OnPresenting;
                }
            }

            Advance();
        }

        void OnPresenting(CombatEvent e)
        {
            if (e.Kind != CombatEventKind.TurnBegan || m_Creatures == null)
            {
                return;
            }

            var active = m_Creatures.ByTurnId(e.Actor);

            if (active != null)
            {
                Show(active, e.Round);
            }
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
