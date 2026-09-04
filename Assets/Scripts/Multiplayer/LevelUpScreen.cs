using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// What a character became, and the one decision that comes with it.
    ///
    /// Shown when experience earned in a match has bought at least one level. Every level at once
    /// rather than one screen per level: a character that came out of a long fight three levels up
    /// is asked what it becomes once, and the heading says where it started and where it ended
    /// rather than counting the player through three identical screens.
    ///
    /// A level is worth one element point, so spending them is the whole of the decision. What the
    /// levels unlocked is shown but not chosen -- a skill a character has reached is theirs, and
    /// asking them to pick it would make it a reward rather than a consequence.
    ///
    /// The arithmetic is <see cref="Progression"/>'s and the check is
    /// <see cref="BuildValidator"/>'s. This screen only asks them, on every step, so a Confirm that
    /// is offered is a save that will be accepted.
    /// </summary>
    public sealed class LevelUpScreen
    {
        readonly ContentCatalog m_Content;
        readonly Action m_OnDone;

        readonly Label m_Name;
        readonly Label m_Arc;
        readonly Label m_Xp;
        readonly Label m_Fault;
        readonly Label m_ElementsTitle;
        readonly VisualElement m_Elements;
        readonly VisualElement m_Unlocked;
        readonly Label m_SummaryName;
        readonly Label m_SummaryClass;
        readonly VisualElement m_Stats;
        readonly VisualElement m_Attrs;
        readonly VisualElement m_Pool;
        readonly VisualElement m_Skills;
        readonly Button m_Confirm;
        readonly Button m_Later;

        readonly List<BuildFault> m_FaultBuffer = new List<BuildFault>();

        ElementPicker m_Picker;
        SavedCharacter m_Character;
        CharacterBuild m_Build;
        LevelGain m_Gain;

        // What the character already held when the screen opened. The new points are spent on top
        // of it, and nothing bought in an earlier level may be taken back here to pay for this one.
        ElementCounts m_Floor;

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        public LevelUpScreen(VisualElement root, ContentCatalog content, Action onDone)
        {
            m_Content = content;
            m_OnDone = onDone;

            m_Name = root.Q<Label>("levelup-name");
            m_Arc = root.Q<Label>("levelup-arc");
            m_Xp = root.Q<Label>("levelup-xp");
            m_Fault = root.Q<Label>("levelup-fault");
            m_ElementsTitle = root.Q<Label>("levelup-elements-title");
            m_Elements = root.Q<VisualElement>("levelup-elements");
            m_Unlocked = root.Q<VisualElement>("levelup-unlocked");
            m_SummaryName = root.Q<Label>("levelup-summary-name");
            m_SummaryClass = root.Q<Label>("levelup-summary-class");
            m_Stats = root.Q<VisualElement>("levelup-stats");
            m_Attrs = root.Q<VisualElement>("levelup-attrs");
            m_Pool = root.Q<VisualElement>("levelup-pool");
            m_Skills = root.Q<VisualElement>("levelup-skills");
            m_Confirm = root.Q<Button>("levelup-confirm-button");
            m_Later = root.Q<Button>("levelup-later-button");

            IsBound = m_Name != null && m_Arc != null && m_Xp != null && m_Fault != null
                && m_ElementsTitle != null && m_Elements != null && m_Unlocked != null
                && m_SummaryName != null && m_SummaryClass != null && m_Stats != null
                && m_Attrs != null && m_Pool != null && m_Skills != null && m_Confirm != null
                && m_Later != null;

            if (!IsBound)
            {
                return;
            }

            m_Confirm.clicked += OnConfirmClicked;
            m_Later.clicked += () => m_OnDone?.Invoke();
        }

        /// <summary>
        /// Whether this character has earned at least one level.
        ///
        /// Static, and asked before the screen is opened rather than by it, so the router does not
        /// have to build a screen to find out there is nothing to show.
        /// </summary>
        public static bool HasLevelsWaiting(SavedCharacter character) =>
            character != null && Progression.Resolve(character.Build.Level, character.Build.Xp).Any;

        /// <summary>Opens on a character that has levels waiting.</summary>
        public void Open(SavedCharacter character)
        {
            if (!IsBound || m_Content == null || !HasLevelsWaiting(character))
            {
                return;
            }

            m_Character = character;
            m_Build = new CharacterBuild(character.Build);
            m_Gain = Progression.Resolve(m_Build.Level, m_Build.Xp);
            m_Floor = m_Build.StartingPool;

            // The level is applied to the working copy up front, so everything below -- the budget,
            // the skills, the health -- is what the character is about to be rather than what it
            // was. Nothing is written until Confirm.
            m_Build.Level = m_Gain.ToLevel;
            m_Build.Xp = m_Gain.RemainingXp;

            m_Name.text = string.IsNullOrWhiteSpace(m_Build.Name) ? "Unnamed" : m_Build.Name;
            m_Arc.text = $"LEVEL {m_Gain.FromLevel}  →  LEVEL {m_Gain.ToLevel}";
            m_Xp.text = m_Gain.RemainingXp > 0
                ? $"{m_Gain.RemainingXp} XP towards level {m_Gain.ToLevel + 1}"
                : "All experience spent";

            m_Picker = new ElementPicker(m_Elements, Adjust);

            BuildUnlocked(character.Build.Level);
            Refresh();
        }

        void Adjust(Element element, int delta)
        {
            m_Build.StartingPool =
                m_Build.StartingPool.With(element, m_Build.StartingPool[element] + delta);
            Refresh();
        }

        /// <summary>
        /// What these levels have made available.
        ///
        /// Resolved twice -- once at the old level and once at the new -- and shown as the
        /// difference, because a skill list is the same list either way and "what is new" is the
        /// only part of it worth a section of its own.
        /// </summary>
        void BuildUnlocked(int fromLevel)
        {
            m_Unlocked.Clear();

            var before = new HashSet<int>();
            var was = new CharacterBuild(m_Build) { Level = fromLevel };

            foreach (var skill in LoadoutResolver.Resolve(was, m_Content).Skills)
            {
                before.Add(skill.Id);
            }

            var any = false;

            foreach (var skill in LoadoutResolver.Resolve(m_Build, m_Content).Skills)
            {
                if (before.Contains(skill.Id))
                {
                    continue;
                }

                var line = new Label(skill.Name);
                line.AddToClassList("skill-line");
                line.tooltip = string.IsNullOrWhiteSpace(skill.Description)
                    ? skill.Name
                    : skill.Description;

                m_Unlocked.Add(line);
                any = true;
            }

            if (any)
            {
                return;
            }

            var none = new Label("Nothing new this time. The points are the reward.");
            none.AddToClassList("skill-line--none");
            m_Unlocked.Add(none);
        }

        void Refresh()
        {
            if (m_Build == null)
            {
                return;
            }

            var budget = m_Build.PoolBudget();
            var left = ElementPricing.Remaining(m_Build.StartingPool, budget);

            m_ElementsTitle.text = left == 0
                ? $"SPEND YOUR POINTS  ·  ALL {budget} SPENT"
                : $"SPEND YOUR POINTS  ·  {left} OF {budget} LEFT";

            m_Picker.Refresh(m_Build.StartingPool, budget, m_Floor);

            var loadout = LoadoutResolver.Resolve(m_Build, m_Content);

            m_SummaryName.text = m_Name.text;
            m_SummaryClass.text = CharacterSheet.Describe(loadout);

            CharacterSheet.Stats(m_Stats, loadout.Vitals);
            CharacterSheet.Attributes(m_Attrs, loadout.Attributes, m_Build.Attributes);
            CharacterSheet.Pool(m_Pool, m_Build.StartingPool, budget);
            CharacterSheet.Skills(m_Skills, loadout);

            // The same validator the host runs, so a Confirm that is offered is a save that will be
            // accepted -- and the unspent points are reported as the fault they are.
            BuildValidator.Validate(m_Build, m_Content, m_FaultBuffer);

            m_Fault.text = BuildFaultText.Summarise(m_FaultBuffer);
            m_Confirm.SetEnabled(m_FaultBuffer.Count == 0);
        }

        void OnConfirmClicked()
        {
            BuildValidator.Validate(m_Build, m_Content, m_FaultBuffer);

            if (m_FaultBuffer.Count > 0)
            {
                m_Fault.text = BuildFaultText.Summarise(m_FaultBuffer);
                return;
            }

            var saved = new SavedCharacter(m_Character.Id, m_Build, m_Character.Portrait);

            if (!CharacterStore.Save(saved))
            {
                m_Fault.text = "Could not save. See the console.";
                return;
            }

            // The character being played as is the one that just levelled, so the selection has to
            // follow it -- otherwise the next match is fought by the version before the level-up.
            if (SelectedCharacter.Current != null && SelectedCharacter.Current.Id == saved.Id)
            {
                SelectedCharacter.Current = saved;
            }

            m_OnDone?.Invoke();
        }
    }
}
