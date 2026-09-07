using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Hex;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.UI;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// A running account of the fight, bottom-left, newest at the top, in two sizes.
    ///
    /// Shut, it is one line: the last thing that happened, floated above the bar, taking a strip of
    /// screen and no attention. Open, it is the whole record and scrolls. A fight is watched, not
    /// read -- so the size that is up almost all the time is the one that says the least, and the
    /// player asks for the rest when they have looked away and want to know what they missed.
    ///
    /// Everything else on the HUD shows a state: how much health, whose turn, what is in a hand
    /// right now. None of it shows a *change*, and a fight is made of changes -- so a creature that
    /// caught its breath and got an element back did something invisible, and a player who looked
    /// away for a second had no way to find out what had happened.
    ///
    /// It reads the record as it is shown, one event at a time, so a line appears at the moment
    /// the thing it describes is on the board and never a turn early. Names are read live and
    /// kept as they are read, because the most interesting line in the log is about a creature
    /// that has just fallen.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class CombatLogView : MonoBehaviour
    {
        [SerializeField, Tooltip("Every creature on the board, for names and colours.")]
        CreatureRegistry m_Creatures;

        [SerializeField, Min(4), Tooltip("Lines kept before the oldest are dropped.")]
        int m_MaxLines = 60;

        /// <summary>A creature as the log refers to it, kept past its death.</summary>
        readonly struct Known
        {
            public readonly string Label;
            public readonly bool Mine;

            public Known(string label, bool mine)
            {
                Label = label;
                Mine = mine;
            }
        }

        readonly Dictionary<uint, Known> m_Known = new Dictionary<uint, Known>();

        ScrollView m_List;
        VisualElement m_Panel;
        Button m_Sliver;
        CombatPlayback m_Playback;

        // Whether the record is open, and whether there is anything to say. The sliver is up only
        // when both answers are no and yes: shut, with something to show.
        bool m_Open;
        bool m_Anything;

        void Start()
        {
            var document = GetComponent<UIDocument>().rootVisualElement;

            m_List = document.Q<ScrollView>("combat-log-list");
            m_Panel = document.Q<VisualElement>("combat-log");

            if (m_List == null)
            {
                Debug.LogError($"{nameof(CombatLogView)} could not find its list; "
                    + "check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }

            // Said here rather than trusted to the markup. The list is a fixed-height frame
            // with more in it than fits, and a scroller that only appears when the layout
            // agrees it is needed has, in practice, not appeared.
            m_List.mode = ScrollViewMode.Vertical;
            m_List.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            m_List.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            // The wheel, handled here by one path. See WheelScroll for the history.
            WheelScroll.Attach(m_List);

            m_Sliver = document.Q<Button>("combat-log-sliver");
            var minimise = document.Q<Button>("combat-log-minimize");

            if (minimise != null)
            {
                HudIcons.DrawMinimise(minimise);
                minimise.tooltip = "Shut the log. The last line stays.";
                minimise.clicked += () => SetOpen(false);
            }

            if (m_Sliver != null)
            {
                m_Sliver.text = string.Empty;
                m_Sliver.tooltip = "Open the log.";
                m_Sliver.clicked += () => SetOpen(true);
            }

            SetOpen(false);

            if (m_Creatures != null)
            {
                m_Creatures.Changed += Remember;
                Remember();
            }

            Listen();
        }

        void OnDestroy()
        {
            if (m_Creatures != null)
            {
                m_Creatures.Changed -= Remember;
            }

            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }
        }

        void Update() => Listen();

        void Listen()
        {
            var playback = CombatPlayback.Current;

            if (playback == null || playback == m_Playback)
            {
                return;
            }

            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }

            m_Playback = playback;
            m_Playback.Presenting += OnPresenting;
        }

        /// <summary>Notes down how every creature on the board should be referred to.</summary>
        void Remember()
        {
            if (m_Creatures == null)
            {
                return;
            }

            foreach (var creature in m_Creatures.All)
            {
                if (creature != null)
                {
                    m_Known[creature.TurnId] = Describe(creature);
                }
            }
        }

        /// <summary>What to call a creature, in its party colour.</summary>
        static Known Describe(CreatureState creature) =>
            new Known(
                CombatLogLines.Tint(PartyPalette.ForParty(creature.Party), creature.DisplayName),
                LocalPlayer.Controls(creature));

        /// <summary>
        /// What is known about a creature right now, falling back to what was known last.
        ///
        /// Asked fresh while the creature is still on the board, because the roster fills in as
        /// clients connect and a name cached at spawn can still be "Player 2".
        /// </summary>
        Known Lookup(uint turnId)
        {
            var creature = m_Creatures != null ? m_Creatures.ByTurnId(turnId) : null;

            if (creature != null)
            {
                var fresh = Describe(creature);
                m_Known[turnId] = fresh;
                return fresh;
            }

            return m_Known.TryGetValue(turnId, out var last) ? last : new Known("Someone", false);
        }

        string NameOf(uint turnId) => Lookup(turnId).Label;

        bool IsMine(uint turnId) => turnId != 0 && Lookup(turnId).Mine;

        void OnPresenting(CombatEvent e)
        {
            switch (e.Kind)
            {
                case CombatEventKind.Recovered:
                    Add($"{NameOf(e.Actor)} recovers {e.Amount} HP.", IsMine(e.Actor));
                    break;

                case CombatEventKind.Shot:
                    OnShot(e);
                    break;

                case CombatEventKind.Acted:
                    OnActed(e);
                    break;

                case CombatEventKind.ClashResolved:
                    OnClash(e);
                    break;

                case CombatEventKind.Damaged:
                    Add($"{NameOf(e.Target)} takes {CombatLogLines.Blow(e.Amount, e.Absorbed)}.",
                        IsMine(e.Target) || IsMine(e.Actor));
                    break;

                case CombatEventKind.Healed:
                    Add($"{NameOf(e.Actor)} heals {e.Amount} HP.", IsMine(e.Actor));
                    break;

                case CombatEventKind.HeldBack:
                    Add($"{NameOf(e.Actor)} lets {NameOf(e.Target)} go.", IsMine(e.Actor) || IsMine(e.Target));
                    break;

                case CombatEventKind.Fell:
                    Add(e.Amount > 0
                            ? $"{NameOf(e.Actor)} falls. {NameOf(e.Target)} earns {e.Amount} XP."
                            : $"{NameOf(e.Actor)} falls.",
                        IsMine(e.Actor) || IsMine(e.Target));
                    break;

                case CombatEventKind.WallChanged:
                    // Nobody's line, so nobody's colour: the board changed under everyone alike.
                    Add(CombatLogLines.Wall(new Wall(e.WallBefore), new Wall(e.WallAfter)), mine: false);
                    break;

                case CombatEventKind.Ended:
                    Add(e.HasWinner
                            ? $"<b>{PartyPalette.NameOf(e.Winner)} win.</b>"
                            : "<b>Nobody is left standing.</b>",
                        mine: false);
                    break;
            }
        }

        void OnActed(CombatEvent e)
        {
            var skill = SkillOf(e.Skill);

            if (skill == null)
            {
                return;
            }

            var line = $"{NameOf(e.Actor)} used <b>{skill.Name}</b> ({CombatLogLines.Cost(skill)})";

            switch (skill.Effect.Kind)
            {
                case SkillEffectKind.ReturnElement:
                    line += e.Returned.Count > 0
                        ? $" and regained {CombatLogLines.Runes(e.Returned)}"
                        : " and got nothing back";
                    break;

                case SkillEffectKind.RestoreAp:
                    line += $" and recovered {skill.Effect.Amount} AP";
                    break;

                case SkillEffectKind.Heal:
                    line += " to heal";
                    break;

                case SkillEffectKind.Damage:
                    // Uncontested damage: a tile, or somebody on the same side. Anything thrown at
                    // an enemy became a clash and is reported as one.
                    line += e.HasTarget ? $" on {NameOf(e.Target)}" : string.Empty;
                    break;
            }

            Add(line + ".", IsMine(e.Actor));
        }

        void OnClash(CombatEvent e)
        {
            var skill = SkillOf(e.Skill);
            var attacker = NameOf(e.Actor);
            var defender = NameOf(e.Target);

            var reader = IsMine(e.Actor)
                ? LogSide.Attacker
                : IsMine(e.Target)
                    ? LogSide.Defender
                    : LogSide.Neither;

            // A swing belongs to no catalogue, so it is assembled from whichever element was put
            // up -- which the record carries, because by now both sides are revealed.
            var opportunity = e.Skill == Opportunity.SkillId;

            var name = opportunity ? Opportunity.Name : skill != null ? skill.Name : "an attack";

            var cost = opportunity
                ? $" ({CombatLogLines.Runes(e.Elements)})"
                : skill != null ? $" ({CombatLogLines.Cost(skill)})" : string.Empty;

            var answer = e.Answer.Count > 0
                ? $"answered {CombatLogLines.Runes(e.Answer)}"
                : "did not answer";

            Add($"{attacker} used <b>{name}</b>{cost} on {defender}, who {answer} — "
                + CombatLogLines.Verdict(e.Outcome, attacker, defender, reader),
                reader != LogSide.Neither);
        }

        /// <summary>
        /// A shot, however it went. The chance is said either way: a hit at thirty percent and a
        /// miss at ninety are both worth knowing about.
        /// </summary>
        void OnShot(CombatEvent e)
        {
            var skill = SkillOf(e.Skill);

            if (skill == null)
            {
                return;
            }

            Add($"{NameOf(e.Actor)} loosed <b>{skill.Name}</b> "
                + $"({CombatLogLines.Cost(skill)}) at {NameOf(e.Target)} and "
                + (e.Landed ? "it flew true " : "missed ")
                + CombatLogLines.Tint("#8B93A5", $"({e.Amount}% to hit)"),
                IsMine(e.Actor) || IsMine(e.Target));
        }

        static SkillSpec SkillOf(int skillId) =>
            SkillCatalog.Current != null && SkillCatalog.Current.TryGetSkill(skillId, out var spec)
                ? spec
                : null;

        /// <summary>Open or shut. The lines are kept either way; only what is drawn changes.</summary>
        void SetOpen(bool open)
        {
            m_Open = open;
            m_Panel?.EnableInClassList("combat-log--small", !open);
            RefreshSliver();
        }

        /// <summary>
        /// The one line that is up when the record is shut.
        ///
        /// Driven from here in both directions rather than from a class in the stylesheet. It has
        /// two reasons to be hidden -- the log is open, or nothing has happened yet -- and a rule
        /// for one of them was overriding the other, leaving the sliver under the open record.
        /// </summary>
        void RefreshSliver()
        {
            if (m_Sliver != null)
            {
                m_Sliver.style.display = !m_Open && m_Anything ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// One line. Lines a player is part of are marked, so their own fight stands out of the
        /// traffic in a four-creature turn.
        /// </summary>
        void Add(string text, bool mine)
        {
            var label = new Label(text);
            label.AddToClassList("combat-log__line");
            label.EnableInClassList("combat-log__line--mine", mine);
            Append(label);
        }

        /// <summary>
        /// Puts a line at the top and drops the oldest off the bottom.
        ///
        /// A stack, not a transcript. The thing that just happened is the thing being read, and a
        /// log that grows downwards makes the newest line the one that keeps moving.
        /// </summary>
        void Append(Label line)
        {
            m_List.Insert(0, line);

            // The same text on the sliver. It is the newest line by construction: this is the only
            // place a line is added, and the newest one goes to the top of both.
            m_Anything = true;

            if (m_Sliver != null)
            {
                m_Sliver.text = line.text;
            }

            RefreshSliver();

            while (m_List.childCount > m_MaxLines)
            {
                m_List.RemoveAt(m_List.childCount - 1);
            }
        }
    }
}
