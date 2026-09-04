using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
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

        [SerializeField, Tooltip("Classes, equipment and the rules a character is built against.")]
        ContentCatalog m_Content;

        readonly Dictionary<MenuScreen, VisualElement> m_Panels =
            new Dictionary<MenuScreen, VisualElement>();

        SessionScreens m_Session;
        SettingsScreen m_Settings;
        CharacterListScreen m_Characters;
        CharacterCreatorScreen m_Creator;
        LevelUpScreen m_LevelUp;
        Button m_LevelUpButton;
        Label m_Status;
        Label m_PlayingAs;
        VisualElement m_HeroBody;
        Button m_SingleplayerButton;
        Button m_MultiplayerButton;
        MenuScreen m_Screen = MenuScreen.Start;

        // Routing and refreshing call each other: showing a screen repaints it, and repainting can
        // route away from a screen a session has moved past. Both guards make that loop terminate
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
                m_Session = new SessionScreens(root, m_Runner);

                if (!m_Session.IsBound)
                {
                    Debug.LogError("Main menu markup is missing controls; check SessionMenu.uxml.", this);
                    enabled = false;
                    return;
                }

                m_Runner.Changed += Refresh;
            }

            if (!BindCharacterScreens(root))
            {
                enabled = false;
                return;
            }

            ApplyAvailability();

            // A character that came back from a match with levels waiting takes priority over
            // everything: it is the first thing that happened to the player and the only screen
            // with a decision on it. Otherwise a character already chosen skips the roster, and a
            // fresh launch starts at the title. A session that survived a match needs no case of
            // its own -- the draft board covers whichever screen is showing.
            if (ShowLevelUpIfWaiting())
            {
                return;
            }

            Show(SelectedCharacter.HasSelection ? MenuScreen.Home : MenuScreen.Start);
        }

        bool BindCharacterScreens(VisualElement root)
        {
            if (m_Content == null)
            {
                Debug.LogError($"{nameof(MainMenuUI)} has no {nameof(ContentCatalog)}; "
                    + "characters cannot be built or listed.", this);
                return false;
            }

            m_Characters = new CharacterListScreen(root, m_Content, OnEditCharacter,
                () => Show(MenuScreen.Home));

            m_Creator = new CharacterCreatorScreen(root, m_Content, ShowCharacters);
            m_LevelUp = new LevelUpScreen(root, m_Content, () => Show(MenuScreen.Home));

            if (!m_Characters.IsBound || !m_Creator.IsBound || !m_LevelUp.IsBound)
            {
                Debug.LogError("Character markup is missing controls; check SessionMenu.uxml.", this);
                return false;
            }

            m_PlayingAs = root.Q<Label>("playing-as-label");
            m_HeroBody = root.Q<VisualElement>("home-hero-body");
            m_LevelUpButton = root.Q<Button>("levelup-open-button");

            if (m_LevelUpButton != null)
            {
                m_LevelUpButton.clicked += OpenLevelUp;
            }

            var change = root.Q<Button>("change-character-button");

            if (change != null)
            {
                change.clicked += ShowCharacters;
            }

            return true;
        }

        /// <summary>
        /// Opens the level-up screen when a match has just handed the player one.
        ///
        /// Only unprompted at boot, which is where a player lands after a fight. Everywhere else it
        /// is a button on the hero card, because arriving at a screen you asked for and being shown
        /// a different one is how a player loses track of where they are.
        /// </summary>
        bool ShowLevelUpIfWaiting()
        {
            if (!LevelUpScreen.ShouldPrompt(SelectedCharacter.Current))
            {
                return false;
            }

            OpenLevelUp();
            return true;
        }

        void OpenLevelUp()
        {
            if (!LevelUpScreen.HasLevelsWaiting(SelectedCharacter.Current))
            {
                return;
            }

            m_LevelUp.Open(SelectedCharacter.Current);
            Show(MenuScreen.LevelUp);
        }

        void OnEditCharacter(SavedCharacter existing)
        {
            m_Creator.Open(existing);
            Show(MenuScreen.CreateCharacter);
        }

        /// <summary>
        /// The title card advances on any key.
        ///
        /// Polled rather than driven by an input action: this is the one screen where the specific
        /// key does not matter, and adding a binding to the input asset for "any" would be a
        /// mapping nobody should ever need to rebind.
        /// </summary>
        void Update()
        {
            if (m_Screen != MenuScreen.Start)
            {
                return;
            }

            // Keyboard only. A click also arrives when the window takes focus, so accepting the
            // mouse here means alt-tabbing back into the game skips the title card.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;

            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                ShowCharacters();
            }
        }

        /// <summary>
        /// Opens the roster, or the creator when there is nothing on it yet.
        ///
        /// A first launch has no characters, and an empty list with a New button is a worse first
        /// screen than the thing that button would have opened.
        ///
        /// The only way in to the roster, which is why <see cref="Show"/> does not also refresh it:
        /// deciding which of the two screens to open needs the list read first, and doing it in both
        /// places would read the folder twice per open.
        /// </summary>
        void ShowCharacters()
        {
            m_Characters.Refresh();

            if (m_Characters.HasAny)
            {
                Show(MenuScreen.Characters);
            }
            else
            {
                OnEditCharacter(null);
            }
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

            // The menu scene is going away, so nothing is showing a level-up any more. A static
            // left true here would hide the draft board for the rest of the session.
            LevelUpScreen.IsShowing = false;
        }

        bool BindPanels(VisualElement root)
        {
            m_Panels[MenuScreen.Start] = root.Q<VisualElement>("start-panel");
            m_Panels[MenuScreen.Characters] = root.Q<VisualElement>("characters-panel");
            m_Panels[MenuScreen.CreateCharacter] = root.Q<VisualElement>("create-panel");
            m_Panels[MenuScreen.LevelUp] = root.Q<VisualElement>("levelup-panel");
            m_Panels[MenuScreen.Home] = root.Q<VisualElement>("home-panel");
            m_Panels[MenuScreen.Multiplayer] = root.Q<VisualElement>("multiplayer-panel");
            m_Panels[MenuScreen.Host] = root.Q<VisualElement>("host-panel");
            m_Panels[MenuScreen.Join] = root.Q<VisualElement>("join-panel");
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

            var hostMenu = root.Q<Button>("host-menu-button");
            var joinMenu = root.Q<Button>("join-menu-button");
            var multiplayerBack = root.Q<Button>("multiplayer-back-button");
            var hostBack = root.Q<Button>("host-back-button");
            var joinBack = root.Q<Button>("join-back-button");

            if (singleplayer == null || multiplayer == null || testMode == null || settings == null
                || quit == null || hostMenu == null || joinMenu == null || multiplayerBack == null
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

            // Nothing to route to. The draft board is its own document, it appears as soon as
            // netcode is up, and it carries the Start and Leave that go with a match being set up.
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

            // The draft board sorts above this document and would cover the level-up screen, so it
            // is told what is on screen by the one place that knows.
            LevelUpScreen.IsShowing = screen == MenuScreen.LevelUp;

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

                // A lobby means the board has taken the screen. Route out of the form that
                // opened it, so leaving the session later lands on the menu rather than back on a
                // half-filled join code.
                if (m_Session != null && m_Session.InLobby
                    && (m_Screen == MenuScreen.Host || m_Screen == MenuScreen.Join))
                {
                    m_Screen = MenuScreen.Home;
                    m_Shown = false;
                }

                    RefreshPlayingAs();

                SetStatus(m_Session != null && ShowsSessionStatus(m_Screen)
                    ? SessionScreens.Describe(m_Runner.Phase, m_Runner.Fault, m_Runner.PlayerName)
                    : string.Empty);
            }
            finally
            {
                m_Refreshing = false;
            }

            // Outside the guard: routing away from a screen the session has moved past is a real
            // navigation, and it has to be able to repaint the screen it lands on.
            if (!m_Shown)
            {
                Show(m_Screen);
            }
        }

        /// <summary>
        /// Who the player is currently playing as, drawn as a card rather than announced in a line.
        ///
        /// The main menu's one piece of content: everything else on this screen is a button, and a
        /// menu with nothing on it but buttons is a menu the player has no reason to look at.
        /// </summary>
        void RefreshPlayingAs()
        {
            if (m_PlayingAs == null)
            {
                return;
            }

            var current = SelectedCharacter.Current;

            m_PlayingAs.text = current != null
                ? DisplayName(current)
                : "Nobody yet";

            // Offered rather than forced. The bar above it already says the levels are there; this
            // is where a player goes and spends them when they are ready to.
            if (m_LevelUpButton != null)
            {
                m_LevelUpButton.EnableInClassList("is-hidden",
                    !LevelUpScreen.HasLevelsWaiting(current));
            }

            if (m_HeroBody == null)
            {
                return;
            }

            m_HeroBody.Clear();

            if (current == null || m_Content == null)
            {
                m_HeroBody.Add(Note("Choose a character before you play."));
                return;
            }

            m_HeroBody.Add(HeroPortrait(current));

            var loadout = LoadoutResolver.Resolve(current.Build, m_Content);

            var subtitle = new Label(CharacterSheet.Describe(loadout));
            subtitle.AddToClassList("hero__class");
            m_HeroBody.Add(subtitle);

            // Under the level it is counting towards, and above the stats it will change. This is
            // the last screen before a match and the first one after, so it is where a player finds
            // out whether one more fight is worth it.
            var xp = new VisualElement();
            xp.AddToClassList("xp");
            CharacterSheet.Experience(xp, current.Build.Level, current.Build.Xp);
            m_HeroBody.Add(xp);

            var stats = new VisualElement();
            stats.AddToClassList("statline");
            CharacterSheet.Stats(stats, loadout.Vitals);
            m_HeroBody.Add(stats);
        }

        static string DisplayName(SavedCharacter character) =>
            string.IsNullOrWhiteSpace(character.Build.Name) ? "Unnamed" : character.Build.Name;

        static Label Note(string text)
        {
            var label = new Label(text);
            label.AddToClassList("setting-note");
            return label;
        }

        static VisualElement HeroPortrait(SavedCharacter character)
        {
            var portrait = new VisualElement();
            portrait.AddToClassList("portrait");
            portrait.AddToClassList("hero__portrait");

            if (character.Portrait != null)
            {
                portrait.style.backgroundImage = new StyleBackground(character.Portrait);
                return portrait;
            }

            var initial = new Label(MenuControls.Initial(character.Build.Name));
            initial.AddToClassList("portrait__initial");
            portrait.Add(initial);
            return portrait;
        }

        /// <summary>
        /// Session status is only meaningful on the screens that use a session. Showing
        /// "Connecting to Unity services..." under the Singleplayer button would suggest solo play
        /// is waiting on something, which it never is.
        /// </summary>
        static bool ShowsSessionStatus(MenuScreen screen) =>
            screen == MenuScreen.Host || screen == MenuScreen.Join;

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
