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

        readonly ScrollView m_Form;
        readonly Label m_Title;
        readonly Label m_Faults;
        readonly Label m_SummaryName;
        readonly Label m_SummaryClass;
        readonly Label m_PortraitInitial;
        readonly VisualElement m_Portrait;
        readonly VisualElement m_Stats;
        readonly VisualElement m_Pool;
        readonly VisualElement m_Skills;
        readonly Button m_Save;
        readonly Button m_Cancel;

        readonly List<BuildFault> m_FaultBuffer = new List<BuildFault>();

        readonly Dictionary<Attribute, Label> m_AttributeValues = new Dictionary<Attribute, Label>();
        readonly Dictionary<Attribute, Button> m_AttributeMinus = new Dictionary<Attribute, Button>();
        readonly Dictionary<Attribute, Button> m_AttributePlus = new Dictionary<Attribute, Button>();

        CharacterBuild m_Build;
        Label m_Budget;
        string m_EditingId;

        // What the summary draws and what gets saved. May be the store's texture (when editing) or
        // one this screen decoded.
        Texture2D m_PortraitTexture;

        // Only ever a texture this screen created. The store owns everything else, so destroying a
        // portrait we merely borrowed would blank it on the roster behind us.
        Texture2D m_DecodedPortrait;

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        public CharacterCreatorScreen(VisualElement root, ContentCatalog content, Action onDone)
        {
            m_Content = content;
            m_OnDone = onDone;

            m_Form = root.Q<ScrollView>("create-form");
            m_Title = root.Q<Label>("create-title");
            m_Faults = root.Q<Label>("create-faults");
            m_SummaryName = root.Q<Label>("create-summary-name");
            m_SummaryClass = root.Q<Label>("create-summary-class");
            m_PortraitInitial = root.Q<Label>("create-portrait-initial");
            m_Portrait = root.Q<VisualElement>("create-portrait");
            m_Stats = root.Q<VisualElement>("create-stats");
            m_Pool = root.Q<VisualElement>("create-pool");
            m_Skills = root.Q<VisualElement>("create-skills");
            m_Save = root.Q<Button>("create-save-button");
            m_Cancel = root.Q<Button>("create-cancel-button");

            IsBound = m_Form != null && m_Title != null && m_Faults != null && m_SummaryName != null
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
                m_Title.text = "No classes are authored";
                m_Form.Clear();
                m_Save.SetEnabled(false);
                return;
            }

            m_EditingId = existing != null ? existing.Id : null;
            m_Title.text = existing != null ? "Edit character" : "New character";

            m_Build = existing != null
                ? new CharacterBuild(existing.Build)
                : CharacterBuild.StartingFrom(FirstSpecies(), classes[0]);

            m_PortraitTexture = existing != null ? existing.Portrait : null;
            m_DecodedPortrait = null;

            BuildForm();
            Refresh();
        }

        // ---------- form ----------

        /// <summary>
        /// Rebuilds the whole form.
        ///
        /// Called when the class changes as well as on open, because the weapon list is class
        /// specific and a dropdown holding another class's weapons is worse than a rebuild that
        /// costs nothing on a screen this size.
        /// </summary>
        void BuildForm()
        {
            m_Form.Clear();

            m_Form.Add(MenuControls.Heading("1 · Identity"));
            m_Form.Add(NameField());
            m_Form.Add(SpeciesField());
            m_Form.Add(ClassField());
            m_Form.Add(MenuControls.Heading("Attributes"));
            m_Form.Add(BudgetLine());

            foreach (var stat in AttributeInfo.All)
            {
                m_Form.Add(AttributeRow(stat));
            }

            m_Form.Add(MenuControls.Heading("Elements"));
            m_Form.Add(MenuControls.Note("Any spread totalling your level. This is what you can "
                + "answer with, and Take a Breath is the only way to get one back mid-fight."));
            m_Form.Add(ElementPicker());

            m_Form.Add(MenuControls.Heading("2 · Portrait"));
            m_Form.Add(PortraitRow());

            m_Form.Add(MenuControls.Heading("3 · Equipment"));
            m_Form.Add(EquipmentField("Weapon", EquipmentSlot.Weapon));
            m_Form.Add(EquipmentField("Armour", EquipmentSlot.Armor));
            m_Form.Add(EquipmentField("Offhand", EquipmentSlot.Offhand));
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

        VisualElement BudgetLine()
        {
            m_Budget = new Label();
            m_Budget.AddToClassList("budget-line");
            return m_Budget;
        }

        VisualElement AttributeRow(Attribute stat)
        {
            var row = new VisualElement();
            row.AddToClassList("alloc-row");

            var label = new Label(AttributeInfo.NameOf(stat));
            label.AddToClassList("alloc-row__label");
            row.Add(label);

            var effect = new Label(AttributeInfo.DescribeEffect(stat));
            effect.AddToClassList("alloc-row__effect");
            row.Add(effect);

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

        /// <summary>
        /// The starting pool as a spread rather than a list of picks.
        ///
        /// Any combination that totals the level is legal, so four of one element and one each of
        /// four others are both valid at level four. Stepping each element up and down is the only
        /// control that makes that obvious -- a pick list would imply an order that does not exist.
        /// </summary>
        VisualElement ElementPicker()
        {
            var group = new VisualElement();

            foreach (var element in ElementInfo.All)
            {
                group.Add(ElementRow(element));
            }

            return group;
        }

        readonly Dictionary<Element, Label> m_ElementValues = new Dictionary<Element, Label>();
        readonly Dictionary<Element, Button> m_ElementMinus = new Dictionary<Element, Button>();
        readonly Dictionary<Element, Button> m_ElementPlus = new Dictionary<Element, Button>();

        VisualElement ElementRow(Element element)
        {
            var row = new VisualElement();
            row.AddToClassList("alloc-row");

            var label = new Label(ElementInfo.NameOf(element));
            label.AddToClassList("alloc-row__label");
            label.style.color = ElementPalette.ForElement(element);
            row.Add(label);

            var spacer = new VisualElement();
            spacer.AddToClassList("alloc-row__effect");
            row.Add(spacer);

            var minus = MenuControls.StepButton("-", () => AdjustPool(element, -1));
            var value = new Label();
            value.AddToClassList("alloc-row__value");
            var plus = MenuControls.StepButton("+", () => AdjustPool(element, +1));

            row.Add(minus);
            row.Add(value);
            row.Add(plus);

            m_ElementValues[element] = value;
            m_ElementMinus[element] = minus;
            m_ElementPlus[element] = plus;

            return row;
        }

        void AdjustPool(Element element, int delta)
        {
            m_Build.StartingPool =
                m_Build.StartingPool.With(element, m_Build.StartingPool[element] + delta);
            Refresh();
        }

        VisualElement PortraitRow()
        {
            var group = new VisualElement();

            var row = new VisualElement();
            row.AddToClassList("portrait-row");

            var path = new TextField();
            path.AddToClassList("text-input");
            path.RegisterValueChangedCallback(evt => LoadPortrait(evt.newValue));
            row.Add(path);

            if (PortraitBrowser.IsAvailable)
            {
                row.Add(MenuControls.TextButton("Browse", "button button--compact", () =>
                {
                    if (PortraitBrowser.TryPick(out var picked))
                    {
                        path.SetValueWithoutNotify(picked);
                        LoadPortrait(picked);
                    }
                }));
            }

            group.Add(row);

            group.Add(MenuControls.Note(PortraitBrowser.IsAvailable
                ? "PNG or JPG. Stored with your character on this machine; other players see your "
                    + "initial instead."
                : "Paste the full path to a PNG or JPG. Stored with your character on this machine."));

            group.Add(MenuControls.TextButton("Remove portrait", "button button--ghost button--compact",
                () =>
                {
                    ReplaceDecoded(null);
                    path.SetValueWithoutNotify(string.Empty);
                    Refresh();
                }));

            return group;
        }

        void LoadPortrait(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var loaded = PortraitLoader.FromFile(path.Trim().Trim('\"'));

            if (loaded == null)
            {
                m_Faults.text = "That file could not be read as a PNG or JPG.";
                return;
            }

            ReplaceDecoded(loaded);
            Refresh();
        }

        /// <summary>
        /// Swaps in a portrait this screen decoded, destroying the previous one it decoded.
        ///
        /// Only textures this screen created are destroyed. The one an edited character arrived
        /// with belongs to the store, and freeing it here would blank that row on the roster.
        /// </summary>
        void ReplaceDecoded(Texture2D decoded)
        {
            if (m_DecodedPortrait != null && m_DecodedPortrait != decoded)
            {
                UnityEngine.Object.Destroy(m_DecodedPortrait);
            }

            m_DecodedPortrait = decoded;
            m_PortraitTexture = decoded;
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
            dropdown.RegisterValueChangedCallback(_ =>
            {
                Equip(slot, Id(options[Mathf.Clamp(dropdown.index, 0, options.Count - 1)]));
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
                    ? $"All {rules.PointBudget} points spent."
                    : remaining > 0
                        ? $"{remaining} of {rules.PointBudget} points left."
                        : $"{-remaining} points over budget.";

                m_Budget.EnableInClassList("budget-line--over", remaining < 0);
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

        void RefreshSummary(Loadout loadout, CharacterRules rules)
        {
            m_SummaryName.text = string.IsNullOrWhiteSpace(m_Build.Name) ? "Unnamed" : m_Build.Name;
            m_SummaryClass.text = loadout.Class != null
                ? $"{loadout.Class.Name} · Level {rules.Level}"
                : "No class";

            RefreshPortrait();

            m_Stats.Clear();
            m_Stats.Add(MenuControls.ReadoutRow("LVL", loadout.Vitals.Level.ToString()));
            m_Stats.Add(MenuControls.ReadoutRow("HP", loadout.Vitals.MaxHealth.ToString()));
            m_Stats.Add(MenuControls.ReadoutRow("AP", loadout.Vitals.MaxAp.ToString()));
            m_Stats.Add(MenuControls.ReadoutRow("SPD", loadout.Vitals.Speed.ToString()));

            foreach (var attribute in AttributeInfo.All)
            {
                m_Stats.Add(MenuControls.ReadoutRow(AttributeInfo.ShortNameOf(attribute),
                    loadout.Attributes[attribute].ToString()));
            }

            RefreshPool(rules);
            RefreshSkills(loadout);
        }

        void RefreshPortrait()
        {
            var has = m_PortraitTexture != null;

            m_Portrait.style.backgroundImage = has
                ? new StyleBackground(m_PortraitTexture)
                : new StyleBackground();

            m_PortraitInitial.text = has ? string.Empty : MenuControls.Initial(m_Build.Name);
        }

        /// <summary>
        /// The pool as one chip per element held, with the running total against the level.
        ///
        /// The shape is free and the size is not, so the total is what needs saying. Showing empty
        /// slots would imply a fixed number of picks, which is exactly what this is not.
        /// </summary>
        void RefreshPool(CharacterRules rules)
        {
            m_Pool.Clear();

            var pool = m_Build.StartingPool;

            foreach (var element in ElementInfo.All)
            {
                var held = pool[element];

                if (held <= 0)
                {
                    continue;
                }

                var chip = new Label(ElementInfo.ShortNameOf(element) + " " + held);
                chip.AddToClassList("pool-chip");

                var color = ElementPalette.ForElement(element);
                chip.style.color = color;
                chip.style.borderTopColor = chip.style.borderBottomColor =
                    chip.style.borderLeftColor = chip.style.borderRightColor = color;

                m_Pool.Add(chip);
            }

            if (pool.Total == rules.Level)
            {
                return;
            }

            var total = new Label(pool.Total + " of " + rules.Level);
            total.AddToClassList("pool-chip");
            total.AddToClassList("pool-chip--empty");
            m_Pool.Add(total);
        }

        /// <summary>
        /// The skills this build resolves to, and any passives it holds.
        ///
        /// DE-003 asks for a creature's usable skills to be visibly the sum of class and
        /// equipment. Showing the resolved list rather than the class list is what makes that
        /// visible: swap the weapon and the list changes under the player's hand.
        /// </summary>
        void RefreshSkills(Loadout loadout)
        {
            if (m_Skills == null)
            {
                return;
            }

            m_Skills.Clear();

            if (loadout.Skills.Count == 0)
            {
                var none = new Label("Nothing equipped grants a skill.");
                none.AddToClassList("skill-line--none");
                m_Skills.Add(none);
            }

            foreach (var skill in loadout.Skills)
            {
                var line = new VisualElement();
                line.AddToClassList("skill-line");

                var name = new Label(skill.Name);
                name.AddToClassList("skill-line__name");

                var cost = new Label(skill.ElementCost > 0
                    ? $"{skill.ApCost} AP · {skill.ElementCost} {ElementInfo.NameOf(skill.Element)}"
                    : $"{skill.ApCost} AP");
                cost.AddToClassList("skill-line__cost");
                cost.style.color = ElementPalette.ForElement(skill.Element);

                line.Add(name);
                line.Add(cost);
                m_Skills.Add(line);
            }

            if (loadout.Passives.Has(Passive.DefendAdvantage))
            {
                var passive = new Label("Advantage when defending");
                passive.AddToClassList("passive-line");
                m_Skills.Add(passive);
            }
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

            var character = new SavedCharacter(m_EditingId, m_Build, m_PortraitTexture);

            // The store takes ownership on save, so this screen must stop treating it as its own.
            m_DecodedPortrait = null;

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
