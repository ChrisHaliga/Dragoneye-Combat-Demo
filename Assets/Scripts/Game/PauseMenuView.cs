using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.UI;
using Dragoneye.Game.Combat;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game
{
    /// <summary>
    /// What Escape does in a match: resume, the rules, settings, or leave.
    ///
    /// It replaces what Escape used to do, which was quit the match on the spot. That is a
    /// destructive action on a single keypress with nothing between the press and the consequence,
    /// and it was one fumbled key away from ending somebody else's game too.
    ///
    /// Nothing is actually paused. There is no clock in this game and nothing resolves on its own,
    /// so there is nothing to stop -- what this does is put a screen in front of the board, which
    /// also stops the board taking clicks, since a full-window panel is picked before any tile.
    ///
    /// The rules and the settings on it are not its own. <see cref="HelpBook"/> and
    /// <see cref="SettingsPanel"/> build the same two pages the main menu shows, and
    /// <see cref="SettingsScreen"/> drives them with the same code -- so a settings control added
    /// once turns up in both places, and the rules cannot say different things in different scenes.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class PauseMenuView : MonoBehaviour
    {
        /// <summary>The one in the scene, for whatever is handling the key.</summary>
        public static PauseMenuView Current { get; private set; }

        VisualElement m_Root;
        VisualElement m_Menu;
        VisualElement m_Help;
        VisualElement m_Settings;

        SettingsScreen m_SettingsScreen;

        /// <summary>Whether anything is on screen.</summary>
        public bool IsOpen { get; private set; }

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        void Start()
        {
            var document = GetComponent<UIDocument>().rootVisualElement;
            UiTypeface.Apply(document);

            m_Root = document.Q<VisualElement>("pause-root");
            m_Menu = document.Q<VisualElement>("pause-menu");
            m_Help = document.Q<VisualElement>("pause-help");
            m_Settings = document.Q<VisualElement>("pause-settings");

            var resume = document.Q<Button>("pause-resume-button");
            var help = document.Q<Button>("pause-help-button");
            var settings = document.Q<Button>("pause-settings-button");
            var quit = document.Q<Button>("pause-quit-button");
            var helpBack = document.Q<Button>("help-back-button");

            if (m_Root == null || m_Menu == null || m_Help == null || m_Settings == null
                || resume == null || help == null || settings == null || quit == null
                || helpBack == null)
            {
                Debug.LogError($"{nameof(PauseMenuView)} could not find its elements; "
                    + "check PauseMenu.uxml.", this);
                enabled = false;
                return;
            }

            // Built before the screen binds, because it finds its controls by name and they do not
            // exist until now.
            HelpBook.Build(document.Q<ScrollView>("help-body"));
            SettingsPanel.Build(document.Q<VisualElement>("settings-body"));

            m_SettingsScreen = new SettingsScreen(document, ShowMenu);

            if (!m_SettingsScreen.IsBound)
            {
                Debug.LogError($"{nameof(PauseMenuView)} could not bind the settings controls; "
                    + "check PauseMenu.uxml.", this);
                enabled = false;
                return;
            }

            resume.clicked += Close;
            help.clicked += ShowHelp;
            settings.clicked += ShowSettings;
            quit.clicked += Quit;
            helpBack.clicked += ShowMenu;

            Close();
        }

        /// <summary>Puts the menu up, at its top level.</summary>
        public void Open()
        {
            IsOpen = true;
            m_Root.EnableInClassList("is-hidden", false);

            var root = m_Root;
            root.schedule.Execute(() => root.AddToClassList("pause-root--in"));

            ShowMenu();
        }

        public void Close()
        {
            IsOpen = false;
            m_Root.RemoveFromClassList("pause-root--in");
            m_Root.EnableInClassList("is-hidden", true);

            // Picking off as well as hidden. A hidden element is not picked, but this is the one
            // panel in the arena that covers the whole window, and a bug that left it pickable
            // would make the board look dead with nothing on screen to explain why.
            m_Root.pickingMode = PickingMode.Ignore;
        }

        /// <summary>
        /// Escape, once the menu is up: a page goes back to the menu, the menu closes.
        ///
        /// One key that always means "less", rather than a key that means different things
        /// depending on how deep you are and never closes what you are looking at.
        /// </summary>
        public void Back()
        {
            if (m_Help.ClassListContains("is-hidden") && m_Settings.ClassListContains("is-hidden"))
            {
                Close();
                return;
            }

            ShowMenu();
        }

        void ShowMenu() => Show(m_Menu);

        void ShowHelp() => Show(m_Help);

        void ShowSettings()
        {
            // Refreshed on open rather than once, because a monitor can be plugged in mid-match.
            m_SettingsScreen.Refresh();
            Show(m_Settings);
        }

        void Show(VisualElement page)
        {
            m_Root.pickingMode = PickingMode.Position;

            m_Menu.EnableInClassList("is-hidden", page != m_Menu);
            m_Help.EnableInClassList("is-hidden", page != m_Help);
            m_Settings.EnableInClassList("is-hidden", page != m_Settings);
        }

        /// <summary>
        /// Leaves the match.
        ///
        /// Through <see cref="MatchFlow"/> rather than the session runner, because leaving is the
        /// same gesture whether this is a hosted session or a solo match, and a menu button should
        /// not have to know which one it is in.
        /// </summary>
        void Quit()
        {
            Close();

            var flow = MatchFlow.Instance;

            if (flow != null && flow.InMatch)
            {
                flow.LeaveMatch();
            }
        }
    }
}
