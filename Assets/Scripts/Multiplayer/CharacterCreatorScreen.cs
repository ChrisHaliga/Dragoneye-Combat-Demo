using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.UI;

namespace Dragoneye.Multiplayer
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, and System.Attribute would otherwise win.
    using Attribute = Dragoneye.Combat.Attribute;

    /// <summary>
    /// Building a character, in four pages: who they are, what they trained at, what they are made
    /// of and carry, and the whole of it read back before it is saved.
    ///
    /// One decision to a page. The old screen put every choice on one sheet, which was honest and
    /// unreadable: a new player was asked to spend points before they knew what a class was. Now
    /// each page explains the choice it asks for -- a species card says what a species does, a
    /// class page lists every skill the class will ever grant and at what level, an attribute
    /// says on its own row what a point in it buys -- and the last page is the character sheet as
    /// the roster will show it.
    ///
    /// Rules live in <see cref="BuildValidator"/> and <see cref="LoadoutResolver"/>. This screen
    /// asks them on every change and never decides anything itself: Save is enabled by the same
    /// call the host will make when the build arrives, so a character that saves is a character
    /// that will be accepted.
    /// </summary>
    public sealed class CharacterCreatorScreen
    {
        /// <summary>The pages, in order. The number is the number on the screen.</summary>
        enum Page
        {
            Who = 0,
            Class = 1,
            Body = 2,
            Overview = 3
        }

        const int PageCount = 4;

        static readonly string[] k_PageNames = { "WHO", "CLASS", "BODY AND KIT", "OVERVIEW" };

        readonly ContentCatalog m_Content;
        readonly Action m_OnDone;

        readonly Label m_Title;
        readonly Label m_Faults;
        readonly Label m_Steps;
        readonly VisualElement[] m_Pages = new VisualElement[PageCount];
        readonly Button m_Back;
        readonly Button m_Next;
        readonly Button m_Save;
        readonly Button m_Cancel;

        // Page one.
        readonly VisualElement m_Identity;
        readonly VisualElement m_Portrait;
        readonly Label m_PortraitInitial;
        readonly VisualElement m_PortraitControls;
        readonly VisualElement m_SpeciesCards;

        // Page two.
        readonly ScrollView m_ClassList;
        readonly ScrollView m_ClassDetail;

        // Page three.
        readonly Label m_Budget;
        readonly VisualElement m_Attributes;
        readonly Label m_ElementsTitle;
        readonly VisualElement m_Elements;
        readonly VisualElement m_Equipment;

        // Page four.
        readonly VisualElement m_OverviewPortrait;
        readonly Label m_SummaryName;
        readonly Label m_SummaryClass;
        readonly VisualElement m_Stats;
        readonly VisualElement m_Xp;
        readonly VisualElement m_Attrs;
        readonly VisualElement m_Pool;
        readonly VisualElement m_Kit;
        readonly VisualElement m_Skills;

        readonly List<BuildFault> m_FaultBuffer = new List<BuildFault>();

        readonly Dictionary<Attribute, Label> m_AttributeValues = new Dictionary<Attribute, Label>();
        readonly Dictionary<Attribute, Label> m_AttributeCost = new Dictionary<Attribute, Label>();
        readonly Dictionary<Attribute, Button> m_AttributeMinus = new Dictionary<Attribute, Button>();
        readonly Dictionary<Attribute, Button> m_AttributePlus = new Dictionary<Attribute, Button>();
        readonly Dictionary<int, VisualElement> m_PortraitChoices = new Dictionary<int, VisualElement>();
        readonly Dictionary<int, VisualElement> m_SpeciesChoices = new Dictionary<int, VisualElement>();
        readonly Dictionary<int, VisualElement> m_ClassChoices = new Dictionary<int, VisualElement>();

        // The offhand field, kept because whether it may be used at all is decided by the weapon
        // in the field above it, and answered on every refresh.
        DropdownField m_OffhandField;
        Label m_OffhandNote;

        ElementPicker m_Picker;
        CharacterBuild m_Build;
        string m_EditingId;
        Page m_Page;

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        public CharacterCreatorScreen(VisualElement root, ContentCatalog content, Action onDone)
        {
            m_Content = content;
            m_OnDone = onDone;

            m_Title = root.Q<Label>("create-title");
            m_Faults = root.Q<Label>("create-faults");
            m_Steps = root.Q<Label>("create-steps");
            m_Back = root.Q<Button>("create-back-button");
            m_Next = root.Q<Button>("create-next-button");
            m_Save = root.Q<Button>("create-save-button");
            m_Cancel = root.Q<Button>("create-cancel-button");

            for (var i = 0; i < PageCount; i++)
            {
                m_Pages[i] = root.Q<VisualElement>($"create-page-{i + 1}");
            }

            m_Identity = root.Q<VisualElement>("create-identity");
            m_Portrait = root.Q<VisualElement>("create-portrait");
            m_PortraitInitial = root.Q<Label>("create-portrait-initial");
            m_PortraitControls = root.Q<VisualElement>("create-portrait-controls");
            m_SpeciesCards = root.Q<VisualElement>("create-species");

            m_ClassList = root.Q<ScrollView>("create-class-list");
            m_ClassDetail = root.Q<ScrollView>("create-class-detail");

            m_Budget = root.Q<Label>("create-budget");
            m_Attributes = root.Q<VisualElement>("create-attributes");
            m_ElementsTitle = root.Q<Label>("create-elements-title");
            m_Elements = root.Q<VisualElement>("create-elements");
            m_Equipment = root.Q<VisualElement>("create-equipment");

            m_OverviewPortrait = root.Q<VisualElement>("create-overview-portrait");
            m_SummaryName = root.Q<Label>("create-summary-name");
            m_SummaryClass = root.Q<Label>("create-summary-class");
            m_Stats = root.Q<VisualElement>("create-stats");
            m_Xp = root.Q<VisualElement>("create-xp");
            m_Attrs = root.Q<VisualElement>("create-attrs");
            m_Pool = root.Q<VisualElement>("create-pool");
            m_Kit = root.Q<VisualElement>("create-kit");
            m_Skills = root.Q<VisualElement>("create-skills");

            IsBound = m_Title != null && m_Faults != null && m_Steps != null && m_Back != null
                && m_Next != null && m_Save != null && m_Cancel != null
                && Array.TrueForAll(m_Pages, page => page != null)
                && m_Identity != null && m_Portrait != null && m_PortraitInitial != null
                && m_PortraitControls != null && m_SpeciesCards != null
                && m_ClassList != null && m_ClassDetail != null
                && m_Budget != null && m_Attributes != null && m_ElementsTitle != null
                && m_Elements != null && m_Equipment != null
                && m_OverviewPortrait != null && m_SummaryName != null && m_SummaryClass != null
                && m_Stats != null && m_Xp != null && m_Attrs != null && m_Pool != null
                && m_Kit != null && m_Skills != null;

            if (!IsBound)
            {
                return;
            }

            WheelScroll.Attach(m_ClassList);
            WheelScroll.Attach(m_ClassDetail);
            WheelScroll.Attach(root.Q<ScrollView>("create-sheet-scroll"));

            m_Back.clicked += () => Show(m_Page - 1);
            m_Next.clicked += () => Show(m_Page + 1);
            m_Save.clicked += OnSaveClicked;
            m_Cancel.clicked += () => m_OnDone?.Invoke();
        }

        /// <summary>
        /// Opens the screen on a character, or on a fresh one when <paramref name="existing"/> is null.
        /// Always on the first page: a character is read from the top even when it is being edited.
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

            BuildPages();
            Show(Page.Who);
        }

        // ---------- the pages ----------

        void Show(Page page)
        {
            if (page < Page.Who || (int)page >= PageCount)
            {
                return;
            }

            m_Page = page;

            for (var i = 0; i < PageCount; i++)
            {
                m_Pages[i].EnableInClassList("is-hidden", i != (int)page);
            }

            m_Steps.text = $"STEP {(int)page + 1} OF {PageCount}  ·  {k_PageNames[(int)page]}";
            m_Back.EnableInClassList("is-hidden", page == Page.Who);
            m_Next.EnableInClassList("is-hidden", page == Page.Overview);
            m_Save.EnableInClassList("is-hidden", page != Page.Overview);

            Refresh();
        }

        /// <summary>
        /// Fills every page.
        ///
        /// Called on open, and again when the class changes, because the weapon list is class
        /// specific and a dropdown holding another class's weapons is worse than a rebuild that
        /// costs nothing on a screen this size.
        /// </summary>
        void BuildPages()
        {
            m_Identity.Clear();
            m_Identity.Add(NameField());
            BuildPortraitControls();
            BuildSpeciesCards();

            BuildClassList();

            m_Attributes.Clear();

            foreach (var stat in AttributeInfo.All)
            {
                m_Attributes.Add(AttributeRow(stat));
            }

            m_Picker = new ElementPicker(m_Elements, AdjustPool);

            m_Equipment.Clear();
            m_Equipment.Add(EquipmentField("Weapon", EquipmentSlot.Weapon));
            m_Equipment.Add(EquipmentField("Armour", EquipmentSlot.Armor));
            m_Equipment.Add(EquipmentField("Offhand", EquipmentSlot.Offhand));
        }

        // ---------- page one: who ----------

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
        /// The faces this species can wear, as a row of thumbnails under the big one.
        ///
        /// Chosen from what the game ships rather than loaded off the player's disk: a picture
        /// nobody else has is a picture nobody else can see. Rebuilt when the species changes,
        /// because a face belongs to a species.
        /// </summary>
        void BuildPortraitControls()
        {
            m_PortraitControls.Clear();
            m_PortraitChoices.Clear();

            var library = Portraits.Current;
            var choices = library != null ? library.ForSpecies(m_Build.SpeciesId) : null;

            if (choices == null || choices.Count == 0)
            {
                m_PortraitControls.Add(MenuControls.Note(library == null
                    ? "No portrait library. Run the character content step."
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

        VisualElement PortraitChoice(PortraitEntry entry)
        {
            var choice = new VisualElement();
            choice.AddToClassList("portrait-choice");
            choice.EnableInClassList("portrait-choice--chosen", entry.Id == m_Build.PortraitId);
            choice.style.backgroundImage = new StyleBackground(entry.Image);
            choice.tooltip = entry.Name;

            // Only the highlight moves. Rebuilding the row would throw away the element that was
            // just clicked.
            choice.RegisterCallback<ClickEvent>(_ =>
            {
                m_Build.PortraitId = entry.Id;
                MarkChosen(m_PortraitChoices, m_Build.PortraitId, "portrait-choice--chosen");
                Refresh();
            });

            m_PortraitChoices[entry.Id] = choice;
            return choice;
        }

        /// <summary>
        /// One card per species: what it is, and what being it gives before anything is chosen.
        ///
        /// Cards rather than a dropdown, because a species is the first thing a player decides and
        /// a list of four names says nothing about which to pick.
        /// </summary>
        void BuildSpeciesCards()
        {
            m_SpeciesCards.Clear();
            m_SpeciesChoices.Clear();

            foreach (var species in m_Content.Species)
            {
                var card = new VisualElement();
                card.AddToClassList("choice-card");
                card.EnableInClassList("choice-card--chosen", species.Id == m_Build.SpeciesId);

                var name = new Label(species.Name);
                name.AddToClassList("choice-card__name");
                card.Add(name);

                var text = new Label(species.Description);
                text.AddToClassList("choice-card__text");
                card.Add(text);

                var line = new Label(BaselineLine(species.Baseline, species.BaseAp));
                line.AddToClassList("choice-card__line");
                card.Add(line);

                var picked = species;
                card.RegisterCallback<ClickEvent>(_ => PickSpecies(picked));

                m_SpeciesChoices[species.Id] = card;
                m_SpeciesCards.Add(card);
            }
        }

        void PickSpecies(SpeciesSpec species)
        {
            if (species.Id == m_Build.SpeciesId)
            {
                return;
            }

            m_Build.SpeciesId = species.Id;

            // A face belongs to a species, so becoming something else means wearing one of its
            // faces instead.
            if (Portraits.Current != null)
            {
                m_Build.PortraitId = Portraits.Current.DefaultFor(m_Build.SpeciesId);
            }

            MarkChosen(m_SpeciesChoices, m_Build.SpeciesId, "choice-card--chosen");
            BuildPortraitControls();
            Refresh();
        }

        /// <summary>"+1 STR  +1 TGH  -1 DEX  ·  4 AP", or "No bonuses" for a baseline of nothing.</summary>
        static string BaselineLine(AttributeBlock baseline, int baseAp)
        {
            var text = string.Empty;

            foreach (var attribute in AttributeInfo.All)
            {
                var value = baseline[attribute];

                if (value != 0)
                {
                    text += $"{(value > 0 ? "+" : string.Empty)}{value} {AttributeInfo.ShortNameOf(attribute)}  ";
                }
            }

            return (text.Length == 0 ? "No bonuses" : text.TrimEnd()) + $"  ·  {baseAp} AP a turn";
        }

        /// <summary>The species a new character starts as. Null when none are authored.</summary>
        SpeciesSpec FirstSpecies() =>
            m_Content.Species.Count > 0 ? m_Content.Species[0] : null;

        // ---------- page two: class ----------

        /// <summary>The classes down the left; the chosen one, in full, on the right.</summary>
        void BuildClassList()
        {
            m_ClassList.Clear();
            m_ClassChoices.Clear();

            foreach (var classSpec in m_Content.Classes)
            {
                var row = new Button { text = string.Empty };
                row.AddToClassList("class-row");
                row.EnableInClassList("class-row--chosen", classSpec.Id == m_Build.ClassId);

                var name = new Label(classSpec.Name);
                name.AddToClassList("class-row__name");
                row.Add(name);

                var blurb = new Label(classSpec.Description);
                blurb.AddToClassList("class-row__blurb");
                row.Add(blurb);

                var picked = classSpec;
                row.clicked += () => PickClass(picked);

                m_ClassChoices[classSpec.Id] = row;
                m_ClassList.Add(row);
            }

            BuildClassDetail();
        }

        void PickClass(ClassSpec picked)
        {
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

            MarkChosen(m_ClassChoices, m_Build.ClassId, "class-row--chosen");
            BuildClassDetail();

            // The kit page offers the class's weapons, so it is rebuilt with the class.
            m_Equipment.Clear();
            m_Equipment.Add(EquipmentField("Weapon", EquipmentSlot.Weapon));
            m_Equipment.Add(EquipmentField("Armour", EquipmentSlot.Armor));
            m_Equipment.Add(EquipmentField("Offhand", EquipmentSlot.Offhand));

            Refresh();
        }

        /// <summary>
        /// The chosen class in full: what it is, what it may carry, and every skill it will ever
        /// grant, by the level it grants it at, each with what it does.
        ///
        /// Every skill, not the ones this character has reached. This is the page for deciding
        /// what to become, and the skills at level six are most of the difference between two
        /// classes that look alike at level one.
        /// </summary>
        void BuildClassDetail()
        {
            m_ClassDetail.Clear();

            if (!m_Content.TryGetClass(m_Build.ClassId, out var classSpec))
            {
                return;
            }

            var name = new Label(classSpec.Name.ToUpperInvariant());
            name.AddToClassList("class-detail__name");
            m_ClassDetail.Add(name);

            var text = new Label(classSpec.Description);
            text.AddToClassList("class-detail__text");
            m_ClassDetail.Add(text);

            var baseline = new Label("Baseline  ·  " + BaselineOnly(classSpec.Baseline));
            baseline.AddToClassList("class-detail__line");
            m_ClassDetail.Add(baseline);

            var weapons = new Label("Carries  ·  " + Names(classSpec.WeaponIds));
            weapons.AddToClassList("class-detail__line");
            m_ClassDetail.Add(weapons);

            var byLevel = new SortedDictionary<int, List<SkillSpec>>();

            foreach (var id in classSpec.SkillIds)
            {
                if (!m_Content.TryGetSkill(id, out var skill))
                {
                    continue;
                }

                if (!byLevel.TryGetValue(skill.LevelRequired, out var list))
                {
                    list = new List<SkillSpec>();
                    byLevel[skill.LevelRequired] = list;
                }

                list.Add(skill);
            }

            if (byLevel.Count == 0)
            {
                var none = new Label("This class grants no skills of its own; its weapons do.");
                none.AddToClassList("skill-line--none");
                m_ClassDetail.Add(none);
            }

            foreach (var pair in byLevel)
            {
                var heading = new Label(pair.Key <= Progression.FirstLevel
                    ? "FROM THE START"
                    : $"AT LEVEL {pair.Key}");
                heading.AddToClassList("col__title");
                heading.AddToClassList("col__title--spaced");
                m_ClassDetail.Add(heading);

                foreach (var skill in pair.Value)
                {
                    m_ClassDetail.Add(SkillLine(skill));
                }
            }
        }

        /// <summary>A skill as the sheet writes it: name and price, then what it does.</summary>
        static VisualElement SkillLine(SkillSpec skill)
        {
            var line = new VisualElement();
            line.AddToClassList("skill-line");

            var head = new VisualElement();
            head.AddToClassList("skill-line__head");

            var name = new Label(skill.Name);
            name.AddToClassList("skill-line__name");
            head.Add(name);

            var cost = new Label(CharacterSheet.Cost(skill));
            cost.AddToClassList("skill-line__cost");
            head.Add(cost);

            line.Add(head);

            var text = new Label(CharacterSheet.Describe(skill));
            text.AddToClassList("skill-line__text");
            line.Add(text);

            return line;
        }

        string Names(IReadOnlyList<int> equipmentIds)
        {
            var names = new List<string>();

            foreach (var id in equipmentIds)
            {
                if (m_Content.TryGetEquipment(id, out var spec))
                {
                    names.Add(spec.Name);
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : "nothing";
        }

        static string BaselineOnly(AttributeBlock baseline)
        {
            var text = string.Empty;

            foreach (var attribute in AttributeInfo.All)
            {
                var value = baseline[attribute];

                if (value != 0)
                {
                    text += $"{(value > 0 ? "+" : string.Empty)}{value} {AttributeInfo.ShortNameOf(attribute)}  ";
                }
            }

            return text.Length == 0 ? "nothing" : text.TrimEnd();
        }

        // ---------- page three: body and kit ----------

        /// <summary>
        /// One attribute: its name and its two steppers on the first line, what a point in it
        /// buys on the second.
        ///
        /// Written on the row, not only in a tooltip. This is the page where the points are
        /// spent, and a player deciding between seven things should not have to hover seven
        /// times to learn what they are.
        /// </summary>
        VisualElement AttributeRow(Attribute stat)
        {
            var row = new VisualElement();
            row.AddToClassList("alloc-row");
            row.AddToClassList("alloc-row--tall");
            row.tooltip = AttributeInfo.DescribeEffect(stat);

            var line = new VisualElement();
            line.AddToClassList("alloc-row__line");

            var label = new Label(AttributeInfo.NameOf(stat));
            label.AddToClassList("alloc-row__label");
            line.Add(label);

            // What the next point costs, on the row rather than under the pointer. Each step
            // costs the value it leaves, so the price changes as it is spent and a player working
            // from the one number in the header has to do the arithmetic seven times.
            var cost = new Label();
            cost.AddToClassList("alloc-row__cost");

            var minus = MenuControls.StepButton("-", () => Adjust(stat, -1));
            var value = new Label();
            value.AddToClassList("alloc-row__value");
            var plus = MenuControls.StepButton("+", () => Adjust(stat, +1));

            line.Add(cost);
            line.Add(minus);
            line.Add(value);
            line.Add(plus);
            row.Add(line);

            m_AttributeCost[stat] = cost;

            var text = new Label(AttributeInfo.Summarise(stat));
            text.AddToClassList("alloc-row__text");
            row.Add(text);

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

            // What the chosen item is, written under the field rather than hidden in a hover: a
            // weapon is its skills, and which skills is the whole decision.
            var explain = new Label(Explain(options[Mathf.Clamp(index, 0, options.Count - 1)]));
            explain.AddToClassList("field-note");

            dropdown.RegisterValueChangedCallback(_ =>
            {
                var picked = options[Mathf.Clamp(dropdown.index, 0, options.Count - 1)];
                explain.text = Explain(picked);
                Equip(slot, Id(picked));

                // Picking up something that needs both hands puts down what was in the other one,
                // rather than leaving a build that Save will refuse for a reason on another line.
                if (slot == EquipmentSlot.Weapon && picked != null && picked.TwoHanded)
                {
                    m_Build.OffhandId = CharacterBuild.NoEquipment;
                }

                Refresh();
            });

            if (slot == EquipmentSlot.Offhand)
            {
                m_OffhandField = dropdown;
                m_OffhandNote = explain;
            }

            group.Add(dropdown);
            group.Add(explain);
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
        /// What an item is: what it was written to be, then what it does.
        ///
        /// A weapon does its skills, so it lists them, each with its price and what it hits for --
        /// the formula, because the attributes are still being decided on the same page. Armour
        /// and shields do numbers, in the same shorthand as the stats they change.
        /// </summary>
        string Explain(EquipmentSpec spec)
        {
            if (spec == null)
            {
                return "Nothing in this slot.";
            }

            var text = string.IsNullOrWhiteSpace(spec.Description) ? spec.Name : spec.Description;
            var effects = Shorthand(spec);

            if (effects.Length > 0)
            {
                text += $"  {effects}";
            }

            foreach (var id in spec.SkillIds)
            {
                if (m_Content.TryGetSkill(id, out var skill))
                {
                    text += $"\n{skill.Name}  ·  {CharacterSheet.Cost(skill)}  ·  {SkillEffectInfo.Describe(skill.Effect)}";
                }
            }

            return text;
        }

        /// <summary>The dropdown text: the name, and for anything worn, what it does to the stats.</summary>
        static string Describe(EquipmentSpec spec)
        {
            if (spec == null)
            {
                return "None";
            }

            var effects = Shorthand(spec);
            return effects.Length == 0 ? spec.Name : $"{spec.Name}   {effects}";
        }

        /// <summary>"+4 ARM  -2 SPD", in the same three letters the stats beside it use.</summary>
        static string Shorthand(EquipmentSpec spec)
        {
            var text = string.Empty;

            if (spec.TotalArmour > 0)
            {
                text += $"+{spec.TotalArmour} ARM  ";
            }

            if (spec.SpeedCost > 0)
            {
                text += $"-{spec.SpeedCost} SPD  ";
            }

            if (spec.TwoHanded)
            {
                text += "Both hands  ";
            }

            foreach (var attribute in AttributeInfo.All)
            {
                var moved = spec.Modifiers[attribute];

                if (moved != 0)
                {
                    text += $"{(moved > 0 ? "+" : string.Empty)}{moved} "
                        + $"{AttributeInfo.ShortNameOf(attribute)}  ";
                }
            }

            if (spec.GrantsAdvantage)
            {
                text += "Advantage  ";
            }

            return text.TrimEnd();
        }

        // ---------- everything that repaints ----------

        /// <summary>
        /// Re-resolves everything and repaints every page.
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

            RefreshPortrait();
            RefreshAttributes(rules);
            RefreshOffhand();
            RefreshElements();
            RefreshOverview(loadout);

            m_Faults.text = BuildFaultText.Summarise(m_FaultBuffer);
            m_Save.SetEnabled(m_FaultBuffer.Count == 0);
        }

        /// <summary>
        /// The offhand slot, which the weapon decides the use of.
        ///
        /// Shut rather than hidden: a slot that vanishes reads as a bug, and a slot that is there
        /// and greyed out with the reason under it says what the weapon costs you.
        /// </summary>
        void RefreshOffhand()
        {
            if (m_OffhandField == null)
            {
                return;
            }

            var bothHands = m_Content.TryGetEquipment(m_Build.WeaponId, out var weapon)
                && weapon.TwoHanded;

            m_OffhandField.SetEnabled(!bothHands);

            if (!bothHands)
            {
                return;
            }

            m_OffhandField.SetValueWithoutNotify(Describe(null));

            if (m_OffhandNote != null)
            {
                m_OffhandNote.text = $"{weapon.Name} takes both hands.";
            }
        }

        void RefreshAttributes(CharacterRules rules)
        {
            var remaining = m_Build.PointsRemaining(rules);

            m_Budget.text = remaining == 0
                ? $"ALL {rules.PointBudget} POINTS SPENT"
                : remaining > 0
                    ? $"{remaining} OF {rules.PointBudget} POINTS LEFT  ·  each step costs the value it leaves"
                    : $"{-remaining} POINTS OVER BUDGET";

            m_Budget.EnableInClassList("budget--over", remaining < 0);
            m_Budget.EnableInClassList("budget--spent", remaining == 0);

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

                if (m_AttributeCost.TryGetValue(stat, out var cost))
                {
                    var next = PointBuy.CostToRaise(value);
                    var capped = value >= rules.MaxPerAttribute;

                    cost.text = capped ? "MAX" : $"COST {next}";
                    cost.EnableInClassList("alloc-row__cost--dear", !capped && next > remaining);
                    cost.tooltip = capped
                        ? $"{AttributeInfo.NameOf(stat)} cannot go above {rules.MaxPerAttribute}."
                        : $"The next point in {AttributeInfo.NameOf(stat)} costs {next}.";
                }
            }
        }

        void RefreshElements()
        {
            var budget = m_Build.PoolBudget();
            var left = ElementPricing.Remaining(m_Build.StartingPool, budget);

            m_ElementsTitle.text = left == 0
                ? $"STARTING ELEMENTS  ·  {budget} PT SPENT"
                : $"STARTING ELEMENTS  ·  {left} OF {budget} LEFT";

            m_Picker.Refresh(m_Build.StartingPool, budget);
        }

        void RefreshOverview(Loadout loadout)
        {
            m_SummaryName.text = string.IsNullOrWhiteSpace(m_Build.Name) ? "Unnamed" : m_Build.Name;
            m_SummaryClass.text = CharacterSheet.Describe(loadout);

            CharacterSheet.Stats(m_Stats, loadout);
            CharacterSheet.Experience(m_Xp, m_Build.Level, m_Build.Xp);
            CharacterSheet.Attributes(m_Attrs, loadout.Attributes, m_Build.Attributes);
            CharacterSheet.Pool(m_Pool, m_Build.StartingPool, m_Build.PoolBudget());
            CharacterSheet.Kit(m_Kit, loadout);
            CharacterSheet.Skills(m_Skills, loadout);
        }

        void RefreshPortrait()
        {
            var face = Portraits.Get(m_Build.PortraitId);
            var image = face != null ? new StyleBackground(face) : new StyleBackground();

            m_Portrait.style.backgroundImage = image;
            m_OverviewPortrait.style.backgroundImage = image;
            m_PortraitInitial.text = face != null ? string.Empty : MenuControls.Initial(m_Build.Name);
        }

        static void MarkChosen(Dictionary<int, VisualElement> choices, int chosen, string className)
        {
            foreach (var pair in choices)
            {
                pair.Value.EnableInClassList(className, pair.Key == chosen);
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
