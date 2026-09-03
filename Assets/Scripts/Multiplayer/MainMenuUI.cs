using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The main menu: which screen is showing, and what the top-level choices do.
    ///
    /// One MonoBehaviour on the document, delegating to a screen controller per area
    /// (<see cref="SessionScreens"/>, <see cref="SettingsScreen"/>). Routing lives here so there is
    /// a single owner of "which panel is visible" -- the previous arrangement inferred it from
    /// whether a lobby existed, which had no room for screens that are not about sessions at all.
    ///
    /// Nothing on the Singleplayer, Settings or Quit paths touches Unity services, so they work
    /// with the network unplugged.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class MainMenuUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Leave empty to use the persistent runner.")]
        SessionRunner m_Runner;

        readonly Dictionary<MenuScreen, VisualElement> m_Panels =
            new Dictionary<MenuScreen, VisualElement>();

        SessionScreens m_Session;
        SettingsScreen m_Settings;
        Label m_Status;
        Button m_SingleplayerButton;
        Button m_MultiplayerButton;
        MenuScreen m_Screen = MenuScreen.Home;

        // Routing and refreshing call each other: showing a screen repaints it, and repainting the
        // session screens can ask to be routed to the lobby. Both guards make that loop terminate
        // without either side having to know it is in one.
        bool m_Shown;
        bool m_Refreshing;

        void Start()
        {
            if (m_Runner == null)
            {
                m_Runner = SessionRunner.Instance;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;

            if (!BindPanels(root) || !BindHome(root))
            {
                enabled = false;
                return;
            }

            m_Status = root.Q<Label>("status-label");

            m_Settings = new SettingsScreen(root, () => Show(MenuScreen.Home));
            if (!m_Settings.IsBound)
            {
                Debug.LogError("Main menu markup is missing controls; check SessionMenu.uxml.", this);
                enabled = false;
                return;
            }

            // No runner is a survivable state, not a fatal one: it means the persistent objects
            // never booted, which is what pressing Play in this scene instead of Bootstrap does.
            // Settings and Quit have no session in them, so they keep working and the menu says
            // plainly why the other two are dead -- more useful than a blank screen and a console
            // error, and it is the same separation that lets Singleplayer run without services.
            if (m_Runner != null)
            {
                m_Session = new SessionScreens(root, m_Runner, Show);

                if (!m_Session.IsBound)
                {
                    Debug.LogError("Main menu markup is missing controls; check SessionMenu.uxml.", this);
                    enabled = false;
                    return;
                }

                m_Runner.Changed += Refresh;
            }

            ApplyAvailability();

            // A lobby can already exist: returning from a match leaves the session open, and the
            // player should land back in the lobby rather than at the top of the menu.
            Show(m_Session != null && m_Session.InLobby ? MenuScreen.Lobby : MenuScreen.Home);
        }

        /// <summary>
        /// Greys out what cannot work in this process, and says why on the button itself.
        ///
        /// Both play buttons need the persistent Bootstrap objects. Rather than let them fail on
        /// click, they are disabled up front with the reason attached.
        /// </summary>
        void ApplyAvailability()
        {
            var booted = MatchFlow.Instance != null;

            m_SingleplayerButton.SetEnabled(booted);
            m_MultiplayerButton.SetEnabled(booted && m_Session != null);

            if (booted && m_Session != null)
            {
                return;
            }

            const string reason = "Play from Assets/Scenes/Bootstrap.unity -- "
                + "the network manager and session runner live there.";

            m_SingleplayerButton.tooltip = reason;
            m_MultiplayerButton.tooltip = reason;

            Debug.LogWarning($"{nameof(MainMenuUI)}: Bootstrap has not run, so no match can start. "
                + reason, this);
        }

        void OnDestroy()
        {
            if (m_Runner != null)
            {
                m_Runner.Changed -= Refresh;
            }
        }

        bool BindPanels(VisualElement root)
        {
            m_Panels[MenuScreen.Home] = root.Q<VisualElement>("home-panel");
            m_Panels[MenuScreen.SoloSetup] = root.Q<VisualElement>("solo-panel");
            m_Panels[MenuScreen.Multiplayer] = root.Q<VisualElement>("multiplayer-panel");
            m_Panels[MenuScreen.Host] = root.Q<VisualElement>("host-panel");
            m_Panels[MenuScreen.Join] = root.Q<VisualElement>("join-panel");
            m_Panels[MenuScreen.Lobby] = root.Q<VisualElement>("lobby-panel");
            m_Panels[MenuScreen.Settings] = root.Q<VisualElement>("settings-panel");

            foreach (var pair in m_Panels)
            {
                if (pair.Value == null)
                {
                    Debug.LogError($"Main menu markup has no '{pair.Key}' panel.", this);
                    return false;
                }
            }

            return true;
        }

        bool BindHome(VisualElement root)
        {
            var singleplayer = root.Q<Button>("singleplayer-button");
            var multiplayer = root.Q<Button>("multiplayer-button");
            var testMode = root.Q<Button>("test-mode-button");
            var settings = root.Q<Button>("settings-button");
            var quit = root.Q<Button>("quit-button");

            var soloStart = root.Q<Button>("solo-start-button");
            var soloBack = root.Q<Button>("solo-back-button");

            var hostMenu = root.Q<Button>("host-menu-button");
            var joinMenu = root.Q<Button>("join-menu-button");
            var multiplayerBack = root.Q<Button>("multiplayer-back-button");
            var hostBack = root.Q<Button>("host-back-button");
            var joinBack = root.Q<Button>("join-back-button");

            if (singleplayer == null || multiplayer == null || testMode == null || settings == null
                || quit == null || soloStart == null || soloBack == null || hostMenu == null || joinMenu == null || multiplayerBack == null
                || hostBack == null || joinBack == null)
            {
                Debug.LogError("Main menu markup is missing a button; check SessionMenu.uxml.", this);
                return false;
            }

            m_SingleplayerButton = singleplayer;
            m_MultiplayerButton = multiplayer;

            singleplayer.clicked += OnSingleplayerClicked;
            multiplayer.clicked += () => Show(MenuScreen.Multiplayer);
            settings.clicked += () => Show(MenuScreen.Settings);
            quit.clicked += Quit;

            soloStart.clicked += OnSoloStartClicked;
            soloBack.clicked += OnSoloBackClicked;

            hostMenu.clicked += () => Show(MenuScreen.Host);
            joinMenu.clicked += () => Show(MenuScreen.Join);
            multiplayerBack.clicked += () => Show(MenuScreen.Home);
            hostBack.clicked += () => Show(MenuScreen.Multiplayer);
            joinBack.clicked += () => Show(MenuScreen.Multiplayer);

            testMode.SetEnabled(false);
            testMode.tooltip = "Not implemented yet.";

            return true;
        }

        /// <summary>
        /// Starts a solo match. No sign-in, no lobby, no join code -- the button is live even when
        /// Unity services are unreachable, which is the whole point of having it.
        /// </summary>
        void OnSingleplayerClicked()
        {
            var flow = MatchFlow.Instance;
            if (flow == null)
            {
                Debug.LogError("No MatchFlow; the Bootstrap scene has not run.", this);
                SetStatus("Cannot start: the game did not boot correctly. See the console.");
                return;
            }

            if (!flow.StartSoloMatch())
            {
                SetStatus("Could not start a solo match. See the console.");
                return;
            }

            // The draft is its own document and appears on its own once the host spawns it. This
            // screen only carries the Start and Back that the multiplayer lobby would have provided.
            Show(MenuScreen.SoloSetup);
        }

        void OnSoloStartClicked()
        {
            var flow = MatchFlow.Instance;
            if (flow == null || !flow.InMatch)
            {
                SetStatus("The solo host is not running. Go back and start again.");
                return;
            }

            flow.BeginArena();
        }

        void OnSoloBackClicked()
        {
            MatchFlow.Instance?.CancelSoloSetup();
            Show(MenuScreen.Home);
        }

        void Show(MenuScreen screen)
        {
            if (m_Shown && m_Screen == screen)
            {
                return;
            }

            m_Screen = screen;
            m_Shown = true;

            foreach (var pair in m_Panels)
            {
                pair.Value.EnableInClassList("is-hidden", pair.Key != screen);
            }

            if (screen == MenuScreen.Settings)
            {
                m_Settings.Refresh();
            }

            Refresh();
        }

        void Refresh()
        {
            if (m_Refreshing)
            {
                return;
            }

            m_Refreshing = true;
            try
            {
                // Null without a runner, which the Multiplayer button is disabled for. Nothing that
                // reaches the session screens is clickable in that state.
                m_Session?.Refresh();

                // A session that ends while its lobby is on screen has nowhere to go but back up.
                if (m_Screen == MenuScreen.Lobby && (m_Session == null || !m_Session.InLobby))
                {
                    m_Screen = MenuScreen.Multiplayer;
                    m_Shown = false;
                }

                SetStatus(m_Session != null && ShowsSessionStatus(m_Screen)
                    ? SessionScreens.Describe(m_Runner.Phase, m_Runner.Fault, m_Runner.PlayerName)
                    : string.Empty);
            }
            finally
            {
                m_Refreshing = false;
            }

            // Outside the guard: routing away from a dead lobby is a real navigation, and it has to
            // be able to repaint the screen it lands on.
            if (!m_Shown)
            {
                Show(m_Screen);
            }
        }

        /// <summary>
        /// Session status is only meaningful on the screens that use a session. Showing
        /// "Connecting to Unity services..." under the Singleplayer button would suggest solo play
        /// is waiting on something, which it never is.
        /// </summary>
        static bool ShowsSessionStatus(MenuScreen screen) =>
            screen == MenuScreen.Host || screen == MenuScreen.Join || screen == MenuScreen.Lobby;

        void SetStatus(string text)
        {
            if (m_Status != null)
            {
                m_Status.text = text;
            }
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
