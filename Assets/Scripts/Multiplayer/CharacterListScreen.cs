using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The roster of characters saved on this machine: pick one to play as, make another, or delete.
    ///
    /// A plain class owning a subtree of the menu document, the same shape as
    /// <see cref="SessionScreens"/> and <see cref="SettingsScreen"/>. It reads the store and writes
    /// the selection; it does not decide whether a build is legal, which is
    /// <see cref="BuildValidator"/>'s answer and is only shown here.
    /// </summary>
    public sealed class CharacterListScreen
    {
        readonly ContentCatalog m_Content;
        readonly Action<SavedCharacter> m_OnEdit;
        readonly Action m_OnPlay;

        readonly ScrollView m_List;
        readonly Label m_Note;
        readonly Button m_New;
        readonly Button m_Edit;
        readonly Button m_Play;
        readonly Button m_Delete;

        readonly List<SavedCharacter> m_Characters = new List<SavedCharacter>();

        string m_SelectedId;

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        /// <summary>Whether anything is saved, so the menu knows to open straight into the creator.</summary>
        public bool HasAny => m_Characters.Count > 0;

        public CharacterListScreen(VisualElement root, ContentCatalog content,
            Action<SavedCharacter> onEdit, Action onPlay)
        {
            m_Content = content;
            m_OnEdit = onEdit;
            m_OnPlay = onPlay;

            m_List = root.Q<ScrollView>("character-list");
            m_Note = root.Q<Label>("characters-note");
            m_New = root.Q<Button>("character-new-button");
            m_Play = root.Q<Button>("character-play-button");
            m_Delete = root.Q<Button>("character-delete-button");
            m_Edit = root.Q<Button>("character-edit-button");

            IsBound = m_List != null && m_Note != null && m_New != null
                && m_Play != null && m_Delete != null && m_Edit != null;

            if (!IsBound)
            {
                return;
            }

            m_New.clicked += () => m_OnEdit?.Invoke(null);
            m_Edit.clicked += () => { if (Selected() != null) m_OnEdit?.Invoke(Selected()); };
            m_Play.clicked += OnPlayClicked;
            m_Delete.clicked += OnDeleteClicked;
        }

        /// <summary>
        /// Re-reads the store and repaints.
        ///
        /// Called every time the screen opens rather than cached, because the creator writes to the
        /// same folder and a stale list would offer a character that no longer exists.
        /// </summary>
        public void Refresh()
        {
            if (!IsBound)
            {
                return;
            }

            m_Characters.Clear();
            m_Characters.AddRange(CharacterStore.LoadAll());

            // Keep the selection if it survived, otherwise fall back to the first row so Play is
            // never pointing at nothing.
            if (!Contains(m_SelectedId))
            {
                m_SelectedId = m_Characters.Count > 0 ? m_Characters[0].Id : null;
            }

            Rebuild();
        }

        void Rebuild()
        {
            m_List.Clear();

            m_Note.text = m_Characters.Count == 0
                ? "You have not made a character yet."
                : $"{m_Characters.Count} saved. Pick who you are playing as.";

            foreach (var character in m_Characters)
            {
                m_List.Add(BuildRow(character));
            }

            var selected = Selected();

            m_Play.SetEnabled(selected != null);
            m_Delete.SetEnabled(selected != null);
            m_Edit.SetEnabled(selected != null);
        }

        VisualElement BuildRow(SavedCharacter character)
        {
            var row = new VisualElement();
            row.AddToClassList("character-row");
            row.EnableInClassList("character-row--selected", character.Id == m_SelectedId);

            row.Add(BuildPortrait(character));
            row.Add(BuildBody(character));

            row.RegisterCallback<ClickEvent>(_ =>
            {
                m_SelectedId = character.Id;
                Rebuild();
            });

            return row;
        }

        static VisualElement BuildPortrait(SavedCharacter character)
        {
            var portrait = new VisualElement();
            portrait.AddToClassList("character-row__portrait");

            if (character.Portrait != null)
            {
                portrait.style.backgroundImage = new StyleBackground(character.Portrait);
                return portrait;
            }

            var initial = new Label(MenuControls.Initial(character.Build.Name));
            initial.AddToClassList("character-row__initial");
            portrait.Add(initial);
            return portrait;
        }

        VisualElement BuildBody(SavedCharacter character)
        {
            var body = new VisualElement();
            body.AddToClassList("character-row__body");

            var name = new Label(string.IsNullOrWhiteSpace(character.Build.Name)
                ? "Unnamed" : character.Build.Name);
            name.AddToClassList("character-row__name");
            body.Add(name);

            var detail = new Label(Describe(character));
            detail.AddToClassList("character-row__detail");
            body.Add(detail);

            // A character can become illegal without being touched -- the budget moved, or the class
            // it used was deleted. Saying so here is kinder than refusing it at the lobby.
            if (m_Content != null && !BuildValidator.IsValid(character.Build, m_Content))
            {
                var warning = new Label("Needs editing to be playable");
                warning.AddToClassList("character-row__invalid");
                body.Add(warning);
            }

            return body;
        }

        /// <summary>The line under the name: class, level and the headline stats.</summary>
        string Describe(SavedCharacter character)
        {
            if (m_Content == null)
            {
                return string.Empty;
            }

            var loadout = LoadoutResolver.Resolve(character.Build, m_Content);
            var className = loadout.Class != null ? loadout.Class.Name : "No class";
            var level = m_Content.Rules.Level;

            return $"{className}  ·  Level {level}  ·  "
                + $"{loadout.Vitals.MaxHealth} HP  ·  {loadout.Vitals.MaxAp} AP";
        }

        SavedCharacter Selected()
        {
            foreach (var character in m_Characters)
            {
                if (character.Id == m_SelectedId)
                {
                    return character;
                }
            }

            return null;
        }

        bool Contains(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            foreach (var character in m_Characters)
            {
                if (character.Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        void OnPlayClicked()
        {
            var selected = Selected();

            if (selected == null)
            {
                return;
            }

            // Refused here as well as shown on the row: an illegal build reaching a match would be
            // rejected by the host, which is a worse place to find out.
            if (m_Content != null && !BuildValidator.IsValid(selected.Build, m_Content))
            {
                m_Note.text = "That character is not playable. Edit it, or make another.";
                return;
            }

            SelectedCharacter.Current = selected;
            m_OnPlay?.Invoke();
        }

        void OnDeleteClicked()
        {
            var selected = Selected();

            if (selected == null)
            {
                return;
            }

            CharacterStore.Delete(selected.Id);

            if (SelectedCharacter.Current != null && SelectedCharacter.Current.Id == selected.Id)
            {
                SelectedCharacter.Current = null;
            }

            m_SelectedId = null;
            Refresh();
        }

    }
}
