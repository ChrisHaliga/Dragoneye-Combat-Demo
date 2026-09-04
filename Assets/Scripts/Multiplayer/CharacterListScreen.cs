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
        readonly VisualElement m_Sheet;
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
            m_Sheet = root.Q<VisualElement>("character-sheet");
            m_Note = root.Q<Label>("characters-note");
            m_New = root.Q<Button>("character-new-button");
            m_Play = root.Q<Button>("character-play-button");
            m_Delete = root.Q<Button>("character-delete-button");
            m_Edit = root.Q<Button>("character-edit-button");

            IsBound = m_List != null && m_Sheet != null && m_Note != null && m_New != null
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
                ? "NO CHARACTERS YET"
                : $"{m_Characters.Count} SAVED";

            foreach (var character in m_Characters)
            {
                m_List.Add(BuildRow(character));
            }

            var selected = Selected();

            m_Play.SetEnabled(selected != null);
            m_Delete.SetEnabled(selected != null);
            m_Edit.SetEnabled(selected != null);

            RebuildSheet(selected);
        }

        /// <summary>
        /// The right half of the screen: whoever is selected, drawn full size.
        ///
        /// The list is a list of names; this is the character. Picking who to play as means
        /// comparing what they can do, and a row three lines tall cannot show that -- which is why
        /// the roster used to be a scrolling box of rows with nothing to read.
        /// </summary>
        void RebuildSheet(SavedCharacter character)
        {
            m_Sheet.Clear();

            if (character == null || m_Content == null)
            {
                var empty = new Label(m_Characters.Count == 0
                    ? "Make a character to begin."
                    : "Pick a character.");
                empty.AddToClassList("sheet__empty");
                m_Sheet.Add(empty);
                return;
            }

            var loadout = LoadoutResolver.Resolve(character.Build, m_Content);

            var head = new VisualElement();
            head.AddToClassList("sheet__head");
            head.Add(SheetPortrait(character));

            var titles = new VisualElement();
            titles.style.flexGrow = 1;

            var name = new Label(string.IsNullOrWhiteSpace(character.Build.Name)
                ? "Unnamed" : character.Build.Name);
            name.AddToClassList("sheet__name");
            titles.Add(name);

            var subtitle = new Label(CharacterSheet.Describe(loadout));
            subtitle.AddToClassList("sheet__class");
            titles.Add(subtitle);

            var stats = new VisualElement();
            stats.AddToClassList("statline");
            CharacterSheet.Stats(stats, loadout.Vitals);
            titles.Add(stats);

            head.Add(titles);
            m_Sheet.Add(head);

            var columns = new VisualElement();
            columns.AddToClassList("sheet__columns");
            columns.Add(SheetColumn("ATTRIBUTES", attrs =>
                CharacterSheet.Attributes(attrs, loadout.Attributes), "attr-grid"));
            columns.Add(SheetColumn("POOL", pool =>
                CharacterSheet.Pool(pool, character.Build.StartingPool,
                    character.Build.PoolBudget()),
                "gem-row"));
            columns.Add(SheetColumn("SKILLS", skills =>
                CharacterSheet.Skills(skills, loadout), "group"));
            m_Sheet.Add(columns);

            if (!BuildValidator.IsValid(character.Build, m_Content))
            {
                var warning = new Label("This character is not playable as it stands. Edit it.");
                warning.AddToClassList("character-row__invalid");
                m_Sheet.Add(warning);
            }
        }

        /// <summary>A titled block in the sheet, filled by whoever knows how to draw it.</summary>
        static VisualElement SheetColumn(string title, System.Action<VisualElement> fill,
            string bodyClass)
        {
            var column = new VisualElement();
            column.AddToClassList("sheet__column");

            var heading = new Label(title);
            heading.AddToClassList("col__title");
            column.Add(heading);

            var body = new VisualElement();
            body.AddToClassList(bodyClass);
            fill(body);
            column.Add(body);

            return column;
        }

        static VisualElement SheetPortrait(SavedCharacter character)
        {
            var portrait = new VisualElement();
            portrait.AddToClassList("portrait");
            portrait.AddToClassList("portrait--sheet");

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
            var level = loadout.Vitals.Level;

            var species = loadout.Species != null ? loadout.Species.Name : "?";

            return $"{species}  ·  {className}  ·  LVL {level}".ToUpperInvariant();
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
