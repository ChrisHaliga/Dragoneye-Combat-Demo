using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, and System.Attribute would otherwise win.
    using Attribute = Dragoneye.Combat.Attribute;

    /// <summary>
    /// Building a character: who they are, what they look like, and what they carry.
    ///
    /// Three sections down the left, a live summary down the right. The summary is the point of the
    /// screen -- DE-004 asks that a player choose between outcomes rather than inputs, so every edit
    /// re-resolves the whole loadout and the resolved health, AP, initiative and damage are on
    /// screen the whole time. The starting pool is shown as chips being filled rather than a count,
    /// because it is the least obvious and least reversible choice here.
    ///
    /// Rules live in <see cref="BuildValidator"/> and <see cref="LoadoutResolver"/>. This screen
    /// asks them on every keystroke and never decides anything itself: the Save button is enabled by
    /// the same call the host will make when the build arrives, so a character that saves is a
    /// character that will be accepted.
    /// </summary>
    public sealed class CharacterCreatorScreen
    {
        readonly ContentCatalog m_Content;
        readonly Action m_OnDone;

        readonly VisualElement m_Identity;
        readonly VisualElement m_Attributes;
        readonly VisualElement m_Elements;
        readonly VisualElement m_Equipment;
        readonly VisualElement m_PortraitControls;
        readonly Label m_Budget;
        readonly Label m_ElementsTitle;
        readonly VisualElement m_Attrs;
        readonly Label m_Title;
        readonly Label m_Faults;
        readonly Label m_SummaryName;
        readonly Label m_SummaryClass;
        readonly Label m_PortraitInitial;
        readonly VisualElement m_Portrait;
        readonly VisualElement m_Stats;
        readonly VisualElement m_Xp;
        readonly VisualElement m_Pool;
        readonly VisualElement m_Skills;
        readonly Button m_Save;
        readonly Button m_Cancel;

        readonly List<BuildFault> m_FaultBuffer = new List<BuildFault>();

        readonly Dictionary<Attribute, Label> m_AttributeValues = new Dictionary<Attribute, Label>();
        readonly Dictionary<Attribute, Button> m_AttributeMinus = new Dictionary<Attribute, Button>();
        readonly Dictionary<Attribute, Button> m_AttributePlus = new Dictionary<Attribute, Button>();

        ElementPicker m_Picker;

        readonly Dictionary<int, VisualElement> m_PortraitChoices =
            new Dictionary<int, VisualElement>();

        CharacterBuild m_Build;
        string m_EditingId;

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        public CharacterCreatorScreen(VisualElement root, ContentCatalog content, Action onDone)
        {
            m_Content = content;
            m_OnDone = onDone;

            m_Identity = root.Q<VisualElement>("create-identity");
            m_Attributes = root.Q<VisualElement>("create-attributes");
            m_Elements = root.Q<VisualElement>("create-elements");
            m_Equipment = root.Q<VisualElement>("create-equipment");
            m_PortraitControls = root.Q<VisualElement>("create-portrait-controls");
            m_Budget = root.Q<Label>("create-budget");
            m_ElementsTitle = root.Q<Label>("create-elements-title");
            m_Attrs = root.Q<VisualElement>("create-attrs");
            m_Title = root.Q<Label>("create-title");
            m_Faults = root.Q<Label>("create-faults");
            m_SummaryName = root.Q<Label>("create-summary-name");
            m_SummaryClass = root.Q<Label>("create-summary-class");
            m_PortraitInitial = root.Q<Label>("create-portrait-initial");
            m_Portrait = root.Q<VisualElement>("create-portrait");
            m_Stats = root.Q<VisualElement>("create-stats");
            m_Xp = root.Q<VisualElement>("create-xp");
            m_Pool = root.Q<VisualElement>("create-pool");
            m_Skills = root.Q<VisualElement>("create-skills");
            m_Save = root.Q<Button>("create-save-button");
            m_Cancel = root.Q<Button>("create-cancel-button");

            IsBound = m_Identity != null && m_Attributes != null && m_Elements != null
                && m_Equipment != null && m_PortraitControls != null && m_Budget != null
                && m_Attrs != null && m_Title != null && m_Faults != null && m_SummaryName != null
                && m_SummaryClass != null && m_PortraitInitial != null && m_Portrait != null
                && m_Stats != null && m_Pool != null && m_Save != null && m_Cancel != null;

            if (!IsBound)
            {
                return;
            }

            m_Save.clicked += OnSaveClicked;
            m_Cancel.clicked += () => m_OnDone?.Invoke();
        }

        /// <summary>
        /// Opens the screen on a character, or on a fresh one when <paramref name="existing"/> is null.
        /// </summary>
        public void Open(SavedCharacter existing)
        {
            if (!IsBound || m_Content == null)
            {
                return;
            }

            var classes = m_Content.Classes;

            if (classes.Count == 0)
            {
                m_Title.text = "NO CLASSES ARE AUTHORED";
                m_Identity.Clear();
                m_Save.SetEnabled(false);
                return;
            }

            m_EditingId = existing != null ? existing.Id : null;
            m_Title.text = existing != null ? "EDIT CHARACTER" : "NEW CHARACTER";

            m_Build = existing != null
                ? new CharacterBuild(existing.Build)
                : CharacterBuild.StartingFrom(FirstSpecies(), classes[0],
                    m_Content.Rules.StartingLevel);

            // A character with no face yet gets the first one its species has, so nobody has to
            // pick before they can see what they are making.
            if (m_Build.PortraitId == 0 && Portraits.Current != null)
            {
                m_Build.PortraitId = Portraits.Current.DefaultFor(m_Build.SpeciesId);
            }

            BuildForm();
            Refresh();
        }

        // ---------- form ----------

        /// <summary>
        /// Fills the four columns.
        ///
        /// Called when the class changes as well as on open, because the weapon list is class
        /// specific and a dropdown holding another class's weapons is worse than a rebuild that
        /// costs nothing on a screen this size.
        ///
        /// Everything is placed into a column rather than appended to one scrolling form. A screen
        /// with a scrollbar down the middle of it is a web page; a character sheet fits.
        /// </summary>
        void BuildForm()
        {
            m_Identity.Clear();
            m_Attributes.Clear();
            m_Elements.Clear();
            m_Equipment.Clear();
            m_PortraitControls.Clear();

            m_Identity.Add(NameField());
            m_Identity.Add(SpeciesField());
            m_Identity.Add(ClassField());

            foreach (var stat in AttributeInfo.All)
            {
                m_Attributes.Add(AttributeRow(stat));
            }

            m_Picker = new ElementPicker(m_Elements, AdjustPool);

            m_Equipment.Add(EquipmentField("Weapon", EquipmentSlot.Weapon));
            m_Equipment.Add(EquipmentField("Armour", EquipmentSlot.Armor));
            m_Equipment.Add(EquipmentField("Offhand", EquipmentSlot.Offhand));

            BuildPortraitControls();
        }

        VisualElement NameField()
        {
            var group = new VisualElement();
            group.AddToClassList("field-group");
            group.Add(MenuControls.FieldLabel("Name"));

            var field = new TextField { maxLength = CharacterBuild.MaxNameLength };
            field.AddToClassList("text-input");
            field.SetValueWithoutNotify(m_Build.Name);
            field.RegisterValueChangedCallback(evt =>
            {
                m_Build.Name = evt.newValue;
                Refresh();
            });

            group.Add(field);
            return group;
        }

        /// <summary>
        /// The species picker.
        ///
        /// Separate from the class picker because they answer different questions: species is what
        /// the character is and class is what it trained at. Changing it cannot invalidate the kit,
        /// so unlike ClassField this does not rebuild the form.
        /// </summary>
        VisualElement SpeciesField()
        {
            var group = new VisualElement();
            group.AddToClassList("field-group");
            group.Add(MenuControls.FieldLabel("Species"));

            var species = m_Content.Species;
            var names = new List<string>();
            var index = 0;

            for (var i = 0; i < species.Count; i++)
            {
                names.Add(species[i].Name);

                if (species[i].Id == m_Build.SpeciesId)
                {
                    index = i;
                }
            }

            var dropdown = new DropdownField { choices = names, index = index };
            dropdown.AddToClassList("dropdown");
            dropdown.RegisterValueChangedCallback(_ =>
            {
                if (species.Count == 0)
                {
                    return;
                }

                m_Build.SpeciesId = species[Mathf.Clamp(dropdown.index, 0, species.Count - 1)].Id;

                // A face belongs to a species, so becoming something else means wearing one of its
                // faces instead. Rebuilt rather than kept, because the old one may not be on offer.
                if (Portraits.Current != null)
                {
                    m_Build.PortraitId = Portraits.Current.DefaultFor(m_Build.SpeciesId);
                }

                BuildPortraitControls();
                Refresh();
            });

            group.Add(dropdown);
            return group;
        }

        /// <summary>The species a new character starts as. Null when none are authored.</summary>
        SpeciesSpec FirstSpecies() =>
            m_Content.Species.Count > 0 ? m_Content.Species[0] : null;


        VisualElement ClassField()
        {
            var group = new VisualElement();
            group.AddToClassList("field-group");
            group.Add(MenuControls.FieldLabel("Class"));

            var classes = m_Content.Classes;
            var names = new List<string>();
            var index = 0;

            for (var i = 0; i < classes.Count; i++)
            {
                names.Add(classes[i].Name);

                if (classes[i].Id == m_Build.ClassId)
                {
                    index = i;
                }
            }

            var dropdown = new DropdownField { choices = names, index = index };
            dropdown.AddToClassList("dropdown");
            dropdown.RegisterValueChangedCallback(_ =>
            {
                var picked = classes[Mathf.Clamp(dropdown.index, 0, classes.Count - 1)];

                if (picked.Id == m_Build.ClassId)
                {
                    return;
                }

                m_Build.ClassId = picked.Id;

                // A weapon the new class cannot carry would fail validation the moment the class
                // changed, which reads as the screen breaking rather than as a choice being made.
                if (!picked.AllowsWeapon(m_Build.WeaponId))
                {
                    m_Build.WeaponId = picked.WeaponIds.Count > 0
                        ? picked.WeaponIds[0]
                        : CharacterBuild.NoEquipment;
                }

                BuildForm();
                Refresh();
            });

            group.Add(dropdown);
            return group;
        }

        /// <summary>
        /// One attribute, its name and its two steppers.
        ///
        /// What the attribute does is a tooltip rather than a second label on the row. Seven rows
        /// each carrying a name and an explanation is more text than the column is wide, and the
        /// explanation is read once when a player is learning the game -- not on every edit.
        /// </summary>
        VisualElement AttributeRow(Attribute stat)
        {
            var row = new VisualElement();
            row.AddToClassList("alloc-row");
            row.tooltip = AttributeInfo.DescribeEffect(stat);

            var label = new Label(AttributeInfo.NameOf(stat));
            label.AddToClassList("alloc-row__label");
            row.Add(label);

            var minus = MenuControls.StepButton("-", () => Adjust(stat, -1));
            var value = new Label();
            value.AddToClassList("alloc-row__value");
            var plus = MenuControls.StepButton("+", () => Adjust(stat, +1));

            row.Add(minus);
            row.Add(value);
            row.Add(plus);

            m_AttributeValues[stat] = value;
            m_AttributeMinus[stat] = minus;
            m_AttributePlus[stat] = plus;

            return row;
        }

        void Adjust(Attribute attribute, int delta)
        {
            m_Build.Attributes =
                m_Build.Attributes.With(attribute, m_Build.Attributes[attribute] + delta);
            Refresh();
        }

        void AdjustPool(Element element, int delta)
        {
            m_Build.StartingPool =
                m_Build.StartingPool.With(element, m_Build.StartingPool[element] + delta);
            Refresh();
        }

        /// <summary>
        /// The faces this species can wear, as a row of thumbnails under the big one.
        ///
        /// Chosen from what the game ships rather than loaded off the player's disk. A picture
        /// nobody else has is a picture nobody else can see, and a lobby is not the place to be
        /// transferring images -- so the pictures come with the game and a character carries the
        /// number of the one it picked.
        ///
        /// Rebuilt when the species changes, because a face belongs to a species and the ones that
        /// do not fit should not be on offer.
        /// </summary>
        void BuildPortraitControls()
        {
            // Cleared first, because this is called again whenever the choice or the species
            // changes. Without it, picking a face appended a second row of the same faces.
            m_PortraitControls.Clear();
            m_PortraitChoices.Clear();

            var library = Portraits.Current;
            var choices = library != null ? library.ForSpecies(m_Build.SpeciesId) : null;

            if (choices == null || choices.Count == 0)
            {
                // Named precisely, because "none for this species" sent somebody looking at their
                // art folder when the library had simply never been built from it.
                m_PortraitControls.Add(MenuControls.Note(library == null
                    ? "No portrait library. Run ClaudeCode > Set Up Everything."
                    : "No portraits for this species. Add images to Assets/Art/Portraits."));
                return;
            }

            var row = new VisualElement();
            row.AddToClassList("portrait-picker");

            foreach (var entry in choices)
            {
                row.Add(PortraitChoice(entry));
            }

            m_PortraitControls.Add(row);
        }

        /// <summary>Moves the gold edge to whichever face is being worn.</summary>
        void MarkChosen()
        {
            foreach (var pair in m_PortraitChoices)
            {
                pair.Value.EnableInClassList("portrait-choice--chosen", pair.Key == m_Build.PortraitId);
            }
        }

        VisualElement PortraitChoice(PortraitEntry entry)
        {
            var choice = new VisualElement();
            choice.AddToClassList("portrait-choice");
            choice.EnableInClassList("portrait-choice--chosen", entry.Id == m_Build.PortraitId);
            choice.style.backgroundImage = new StyleBackground(entry.Image);
            choice.tooltip = entry.Name;

            // Only the highlight moves. Rebuilding the row would throw away the element that was
            // just clicked, which is the same mistake that once left the skill bar unclickable.
            choice.RegisterCallback<ClickEvent>(_ =>
            {
                m_Build.PortraitId = entry.Id;
                MarkChosen();
                Refresh();
            });

            m_PortraitChoices[entry.Id] = choice;
            return choice;
        }

        VisualElement EquipmentField(string label, EquipmentSlot slot)
        {
            var group = new VisualElement();
            group.AddToClassList("field-group");
            group.Add(MenuControls.FieldLabel(label));

            var options = Options(slot);
            var names = new List<string>();
            var current = Equipped(slot);
            var index = 0;

            for (var i = 0; i < options.Count; i++)
            {
                names.Add(Describe(options[i]));

                if (Id(options[i]) == current)
                {
                    index = i;
                }
            }

            var dropdown = new DropdownField { choices = names, index = index };
            dropdown.AddToClassList("dropdown");

            // What an item is stays on the control rather than in the option list: a dropdown row
            // cannot carry a tooltip, and the one the player has chosen is the one they are
            // deciding about.
            dropdown.tooltip = Explain(options[Mathf.Clamp(index, 0, options.Count - 1)]);

            dropdown.RegisterValueChangedCallback(_ =>
            {
                var picked = options[Mathf.Clamp(dropdown.index, 0, options.Count - 1)];
                dropdown.tooltip = Explain(picked);
                Equip(slot, Id(picked));
                Refresh();
            });

            group.Add(dropdown);
            return group;
        }

        /// <summary>
        /// What may go in a slot, with "None" first.
        ///
        /// Weapons are filtered to the class, because offering one that validation will then refuse
        /// is the exact failure the shared validator exists to prevent.
        /// </summary>
        List<EquipmentSpec> Options(EquipmentSlot slot)
        {
            var options = new List<EquipmentSpec> { null };

            m_Content.TryGetClass(m_Build.ClassId, out var classSpec);

            foreach (var spec in m_Content.InSlot(slot))
            {
                if (slot == EquipmentSlot.Weapon
                    && (classSpec == null || !classSpec.AllowsWeapon(spec.Id)))
                {
                    continue;
                }

                options.Add(spec);
            }

            return options;
        }

        int Equipped(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon: return m_Build.WeaponId;
                case EquipmentSlot.Armor: return m_Build.ArmorId;
                default: return m_Build.OffhandId;
            }
        }

        void Equip(EquipmentSlot slot, int id)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon: m_Build.WeaponId = id; break;
                case EquipmentSlot.Armor: m_Build.ArmorId = id; break;
                default: m_Build.OffhandId = id; break;
            }
        }

        static int Id(EquipmentSpec spec) => spec == null ? CharacterBuild.NoEquipment : spec.Id;

        /// <summary>
        /// What an item is, for a tooltip: what it was written to be, then what it does.
        ///
        /// Pulled off the spec rather than written here. Equipment is authored, so its description
        /// is authored too -- a table of item text in the creation screen would be a second place
        /// for a designer to have to remember to edit.
        /// </summary>
        static string Explain(EquipmentSpec spec)
        {
            if (spec == null)
            {
                return "Nothing in this slot.";
            }

            var text = string.IsNullOrWhiteSpace(spec.Description) ? spec.Name : spec.Description;
            var modifiers = Modifiers(spec.Modifiers);

            if (modifiers.Length > 0)
            {
                text += $"\n\n{modifiers}";
            }

            var stops = ArmourRules.ReductionFor(spec.Armour) + spec.DamageReduction;

            if (stops > 0)
            {
                text += $"\nStops {stops} damage a blow";
            }

            var speed = ArmourRules.SpeedCostOf(spec.Armour);

            if (speed > 0)
            {
                text += $"\n-{speed} Speed";
            }

            return text;
        }

        static string Describe(EquipmentSpec spec)
        {
            if (spec == null)
            {
                return "None";
            }

            var modifiers = Modifiers(spec.Modifiers);
            return modifiers.Length == 0 ? spec.Name : $"{spec.Name}   {modifiers}";
        }

        /// <summary>"+2 Power  -1 Speed", or empty when an item changes nothing.</summary>
        static string Modifiers(AttributeBlock block)
        {
            var text = string.Empty;

            foreach (var stat in AttributeInfo.All)
            {
                var value = block[stat];

                if (value == 0)
                {
                    continue;
                }

                text += $"{(value > 0 ? "+" : "")}{value} {AttributeInfo.NameOf(stat)}  ";
            }

            return text.TrimEnd();
        }

        // ---------- live summary ----------

        /// <summary>
        /// Re-resolves everything and repaints.
        ///
        /// One method rather than targeted updates: an edit to any field can change the resolved
        /// stats, the budget, which stats can still be raised, and whether Save is allowed. Working
        /// out which of those a given edit touched would be a second model of the rules.
        /// </summary>
        void Refresh()
        {
            if (m_Build == null)
            {
                return;
            }

            var rules = m_Content.Rules;
            var loadout = LoadoutResolver.Resolve(m_Build, m_Content);

            BuildValidator.Validate(m_Build, m_Content, m_FaultBuffer);

            RefreshAttributes(rules);
            RefreshElements(rules);
            RefreshSummary(loadout, rules);

            m_Faults.text = BuildFaultText.Summarise(m_FaultBuffer);
            m_Save.SetEnabled(m_FaultBuffer.Count == 0);
        }

        void RefreshAttributes(CharacterRules rules)
        {
            var remaining = m_Build.PointsRemaining(rules);

            if (m_Budget != null)
            {
                m_Budget.text = remaining == 0
                    ? $"ALL {rules.PointBudget} POINTS SPENT"
                    : remaining > 0
                        ? $"{remaining} OF {rules.PointBudget} POINTS LEFT"
                        : $"{-remaining} POINTS OVER BUDGET";

                m_Budget.EnableInClassList("budget--over", remaining < 0);
                m_Budget.EnableInClassList("budget--spent", remaining == 0);
            }

            foreach (var stat in AttributeInfo.All)
            {
                var value = m_Build.Attributes[stat];

                if (m_AttributeValues.TryGetValue(stat, out var label))
                {
                    label.text = value.ToString();
                }

                // Disabled rather than clamped on click, so the limit is visible before it is hit --
                // and the plus knows the price of the next step rather than assuming it is one.
                if (m_AttributeMinus.TryGetValue(stat, out var minus))
                {
                    minus.SetEnabled(value > PointBuy.Floor);
                }

                if (m_AttributePlus.TryGetValue(stat, out var plus))
                {
                    plus.SetEnabled(PointBuy.CanRaise(m_Build.Attributes, stat,
                        rules.PointBudget, rules.MaxPerAttribute));
                }
            }
        }

        /// <summary>
        /// Repaints the element rows: the value, whether it can still be stepped, and whether the
        /// row is lit at all.
        ///
        /// Dimming an element the character does not hold is what makes the pool read as a hand
        /// rather than as seven fields that all happen to be zero.
        /// </summary>
        /// <summary>
        /// Repaints the element rows against the budget a character of this level has.
        ///
        /// The heading carries the budget rather than a line of its own: this is the tightest
        /// column on the screen, and a budget nobody has overspent is worth no vertical space.
        /// </summary>
        void RefreshElements(CharacterRules rules)
        {
            var budget = m_Build.PoolBudget();

            if (m_ElementsTitle != null)
            {
                var left = ElementPricing.Remaining(m_Build.StartingPool, budget);

                m_ElementsTitle.text = left == 0
                    ? $"STARTING ELEMENTS  \u00b7  {budget} PT SPENT"
                    : $"STARTING ELEMENTS  \u00b7  {left} OF {budget} LEFT";
            }

            m_Picker.Refresh(m_Build.StartingPool, budget);
        }

        void RefreshSummary(Loadout loadout, CharacterRules rules)
        {
            m_SummaryName.text = string.IsNullOrWhiteSpace(m_Build.Name) ? "Unnamed" : m_Build.Name;
            m_SummaryClass.text = CharacterSheet.Describe(loadout);

            RefreshPortrait();

            CharacterSheet.Stats(m_Stats, loadout.Vitals);

            if (m_Xp != null)
            {
                CharacterSheet.Experience(m_Xp, m_Build.Level, m_Build.Xp);
            }

            CharacterSheet.Attributes(m_Attrs, loadout.Attributes, m_Build.Attributes);
            CharacterSheet.Pool(m_Pool, m_Build.StartingPool, m_Build.PoolBudget());

            if (m_Skills != null)
            {
                CharacterSheet.Skills(m_Skills, loadout);
            }
        }

        void RefreshPortrait()
        {
            var face = Portraits.Get(m_Build.PortraitId);

            m_Portrait.style.backgroundImage = face != null
                ? new StyleBackground(face)
                : new StyleBackground();

            m_PortraitInitial.text = face != null ? string.Empty : MenuControls.Initial(m_Build.Name);
        }

        // ---------- saving ----------

        void OnSaveClicked()
        {
            // Checked again rather than trusting the button state: the button is a courtesy, and a
            // build that reached here illegal would be written to disk and refused at the lobby.
            BuildValidator.Validate(m_Build, m_Content, m_FaultBuffer);

            if (m_FaultBuffer.Count > 0)
            {
                m_Faults.text = BuildFaultText.Summarise(m_FaultBuffer);
                return;
            }

            m_Build.Name = m_Build.Name.Trim();

            var character = new SavedCharacter(m_EditingId, m_Build);

            if (!CharacterStore.Save(character))
            {
                m_Faults.text = "Could not save. See the console.";
                return;
            }

            // Keep the live selection pointing at what is now on disk, so editing the character you
            // are playing as does not leave the old build selected.
            if (SelectedCharacter.Current != null && SelectedCharacter.Current.Id == character.Id)
            {
                SelectedCharacter.Current = character;
            }

            m_OnDone?.Invoke();
        }

    }
}
