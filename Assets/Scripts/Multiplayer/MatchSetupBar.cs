using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The session controls on the prepare-for-battle screen: the join code, who is here, ready,
    /// leave, and start.
    ///
    /// They belong on that screen because it is the only pre-match screen there is. The draft board
    /// fills the window and sorts above the menu, so the lobby dialog that used to carry these was
    /// a dialog nobody could see or reach -- which is how a host ended up with no way to start the
    /// match and no code to hand anybody.
    ///
    /// Bound here rather than in the draft view because sessions are this assembly's business and
    /// creatures are not. The draft owns the document and passes in its root; what a session is
    /// stays on this side of the boundary.
    ///
    /// Solo and multiplayer are the same screen. The only difference is which controls mean
    /// anything: with nobody to wait for there is no code to share, no readiness to declare, and
    /// nothing between the host and the arena.
    /// </summary>
    public sealed class MatchSetupBar
    {
        // Held rather than looked up each time: the runner outlives this bar, and unsubscribing
        // through a static that has already been torn down would leave the handler attached.
        readonly SessionRunner m_Runner;

        readonly VisualElement m_CodeCard;
        readonly Label m_Code;
        readonly Button m_Copy;
        readonly Label m_Players;
        readonly Toggle m_Ready;
        readonly Button m_Leave;
        readonly Button m_Start;

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        public MatchSetupBar(VisualElement root)
        {
            m_Runner = SessionRunner.Instance;

            m_CodeCard = root.Q<VisualElement>("setup-code");
            m_Code = root.Q<Label>("setup-code-label");
            m_Copy = root.Q<Button>("setup-copy-button");
            m_Players = root.Q<Label>("setup-players");
            m_Ready = root.Q<Toggle>("setup-ready-toggle");
            m_Leave = root.Q<Button>("setup-leave-button");
            m_Start = root.Q<Button>("setup-start-button");

            IsBound = m_CodeCard != null && m_Code != null && m_Copy != null && m_Players != null
                && m_Ready != null && m_Leave != null && m_Start != null;

            if (!IsBound)
            {
                return;
            }

            m_Copy.clicked += OnCopyClicked;
            m_Leave.clicked += OnLeaveClicked;
            m_Start.clicked += OnStartClicked;
            m_Ready.RegisterValueChangedCallback(evt => TaskUtil.Forget(ReadyAsync(evt.newValue)));

            if (m_Runner != null)
            {
                m_Runner.Changed += Refresh;
            }

            Refresh();
        }

        public void Dispose()
        {
            if (m_Runner != null)
            {
                m_Runner.Changed -= Refresh;
            }
        }

        /// <summary>Repaints from the session, or from the absence of one.</summary>
        public void Refresh()
        {
            if (!IsBound)
            {
                return;
            }

            var lobby = m_Runner != null ? m_Runner.CurrentLobby : null;
            var busy = m_Runner != null && m_Runner.IsBusy;

            // Netcode rather than the lobby decides who may start, because a solo host has no
            // lobby to be the host of.
            var manager = NetworkManager.Singleton;
            var isHost = manager != null && manager.IsServer;

            SetVisible(m_CodeCard, lobby.HasValue);
            SetVisible(m_Ready, lobby.HasValue);
            SetVisible(m_Start, isHost);

            m_Leave.SetEnabled(!busy);

            if (!lobby.HasValue)
            {
                m_Players.text = "SOLO";
                m_Start.text = "Start match";
                m_Start.SetEnabled(isHost);
                return;
            }

            var view = lobby.Value;

            m_Code.text = view.Code;
            m_Players.text = DescribePlayers(view);

            m_Ready.SetValueWithoutNotify(view.SelfIsReady);
            m_Ready.SetEnabled(!busy);

            m_Start.text = view.EveryoneReady ? "Start match" : "Waiting for players";
            m_Start.SetEnabled(isHost && view.EveryoneReady && !busy);
        }

        /// <summary>Who is here and how many of them have readied up, in one line.</summary>
        static string DescribePlayers(LobbyView view)
        {
            var ready = 0;
            var names = string.Empty;

            foreach (var player in view.Players)
            {
                if (player.IsReady)
                {
                    ready++;
                }

                names += names.Length == 0 ? player.Name : ", " + player.Name;
            }

            return $"{ready} OF {view.PlayerCount} READY   {names.ToUpperInvariant()}";
        }

        void OnStartClicked()
        {
            // A session has a lobby to lock before the arena loads. A solo host has nothing to wait
            // for and no lobby to lock, so it goes straight there.
            if (m_Runner != null && m_Runner.IsInSession)
            {
                TaskUtil.Forget(m_Runner.StartMatchAsync());
                return;
            }

            var flow = MatchFlow.Instance;

            if (flow == null || !flow.InMatch)
            {
                Debug.LogError("Start pressed with no host running; the board should not be up.");
                return;
            }

            flow.BeginArena();
        }

        /// <summary>
        /// Backing out, whichever kind of match this is.
        ///
        /// One call for both, because the difference -- whether there is a session to hand back --
        /// is <see cref="MatchFlow"/>'s to know. This used to fork here, because leaving a solo
        /// setup through the normal path left the host running under a board the player thought
        /// they had closed. The path no longer does that, so the fork has nothing left to be.
        /// </summary>
        void OnLeaveClicked()
        {
            if (m_Runner != null && m_Runner.IsBusy)
            {
                return;
            }

            MatchFlow.Instance?.LeaveMatch();
        }

        /// <summary>
        /// Lobby player-data writes are rate limited. Lock the toggle for the write and snap it back
        /// to the server value if it is refused, so the local checkbox can never disagree with what
        /// the other players see.
        /// </summary>
        async Task ReadyAsync(bool ready)
        {
            if (m_Runner == null)
            {
                return;
            }

            m_Ready.SetEnabled(false);

            try
            {
                if (!await m_Runner.SetReadyAsync(ready))
                {
                    var lobby = m_Runner.CurrentLobby;

                    if (lobby.HasValue)
                    {
                        m_Ready.SetValueWithoutNotify(lobby.Value.SelfIsReady);
                    }
                }
            }
            finally
            {
                Refresh();
            }
        }

        void OnCopyClicked()
        {
            var lobby = m_Runner != null ? m_Runner.CurrentLobby : null;

            if (!lobby.HasValue)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = lobby.Value.Code;
            m_Copy.text = "Copied";
            m_Copy.schedule.Execute(() => m_Copy.text = "Copy").StartingIn(1200);
        }

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
