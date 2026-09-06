using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// A running account of the fight, bottom-left, newest at the bottom.
    ///
    /// Everything else on the HUD shows a state: how much health, whose turn, what is in a hand
    /// right now. None of it shows a *change*, and a fight is made of changes -- so a creature that
    /// caught its breath and got an element back did something invisible, and a player who looked
    /// away for a second had no way to find out what had happened. Knowing an opponent spent a turn
    /// recovering is not a nicety; it is most of what there is to read in an element game.
    ///
    /// It listens and never asks. The clash half arrives on <see cref="ClashCommands.Resolved"/>
    /// and everything else on <see cref="CombatAnnouncer"/>, both of which are broadcast to every
    /// peer, so this draws the same log on every machine without knowing which machine it is on.
    /// The one thing it reads directly is the round number, which is already replicated.
    ///
    /// Names are read live and kept as they are read, because the most interesting line in the log
    /// is about a creature that has just stopped existing.
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
        int m_Round;

        void Start()
        {
            var document = GetComponent<UIDocument>().rootVisualElement;

            m_List = document.Q<ScrollView>("combat-log-list");
            m_Panel = document.Q<VisualElement>("combat-log");
            m_Panel?.AddToClassList("combat-log--empty");

            if (m_List == null)
            {
                Debug.LogError($"{nameof(CombatLogView)} could not find its list; "
                    + "check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }

            if (m_Creatures != null)
            {
                m_Creatures.Changed += Remember;
                Remember();
            }

            ClashCommands.Resolved += OnClash;
            CombatAnnouncer.Acted += OnActed;
            CombatAnnouncer.Fell += OnFell;
        }

        void OnDestroy()
        {
            if (m_Creatures != null)
            {
                m_Creatures.Changed -= Remember;
            }

            ClashCommands.Resolved -= OnClash;
            CombatAnnouncer.Acted -= OnActed;
            CombatAnnouncer.Fell -= OnFell;
        }

        /// <summary>
        /// Watches the round over, since nothing announces it.
        ///
        /// The number is on <see cref="TurnState"/> and replicated to everybody, so reading it is
        /// cheaper and more honest than a message that would say the same thing a frame later and
        /// could disagree with it.
        /// </summary>
        void Update()
        {
            var turns = TurnState.Current;
            var round = turns != null && !turns.IsOver ? turns.Round : 0;

            if (round == m_Round)
            {
                return;
            }

            m_Round = round;

            if (round > 0)
            {
                AddRound($"ROUND {round}");
            }
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

        /// <summary>
        /// What to call a creature, in its party colour.
        ///
        /// A creature somebody is playing is named with them -- "Kaya&apos;s Ranger" -- because the
        /// two facts a reader wants from the front of a line are which creature acted and whether a
        /// person chose it. A premade is just itself; there is nobody to credit.
        /// </summary>
        static Known Describe(CreatureState creature)
        {
            // The creature, and only the creature. Who is running it is on the turn bar and on
            // the card; putting it in front of every line as well made each one longer than the
            // thing it was reporting, and creatures are what a player points at.
            return new Known(
                CombatLogLines.Tint(PartyPalette.ForParty(creature.Party), creature.DisplayName),
                LocalPlayer.Controls(creature));
        }

        /// <summary>
        /// What is known about a creature right now, falling back to what was known last.
        ///
        /// Asked fresh while the creature is still on the board, because the roster fills in as
        /// clients connect and a name cached at spawn can still be "Player 2". The cache is there
        /// for the one case a live lookup cannot serve: a creature that has just been despawned,
        /// which is exactly what the line about it is reporting.
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

        bool IsMine(uint turnId) => Lookup(turnId).Mine;

        void OnActed(ActionReport report)
        {
            var skill = SkillOf(report.SkillId);

            if (skill == null)
            {
                return;
            }

            var line = $"{NameOf(report.ActorId)} used <b>{skill.Name}</b> "
                + $"({CombatLogLines.Cost(skill)})";

            switch (skill.Effect.Kind)
            {
                case SkillEffectKind.ReturnElement:
                    line += report.Returned.Count > 0
                        ? $" and regained {CombatLogLines.Runes(report.Returned)}"
                        : " and got nothing back";
                    break;

                case SkillEffectKind.RestoreAp:
                    line += $" and recovered {skill.Effect.Amount} AP";
                    break;

                case SkillEffectKind.Heal:
                    line += $" and healed {skill.Effect.Amount} HP";
                    break;

                case SkillEffectKind.Damage:
                    // Uncontested damage: a tile, or somebody on the same side. Anything thrown at
                    // an enemy became a clash and is reported as one.
                    line += report.HasTarget ? $" on {NameOf(report.TargetId)}" : string.Empty;
                    break;
            }

            Add(line, IsMine(report.ActorId));
        }

        void OnClash(ClashReport report)
        {
            var skill = SkillOf(report.SkillId);
            var attacker = NameOf(report.AttackerId);
            var defender = NameOf(report.DefenderId);

            var reader = IsMine(report.AttackerId)
                ? LogSide.Attacker
                : IsMine(report.DefenderId)
                    ? LogSide.Defender
                    : LogSide.Neither;

            // A swing belongs to nobody catalogue, so it is assembled from whichever element
            // was put up -- which the report carries, because by now both sides are revealed.
            var opportunity = report.SkillId == Opportunity.SkillId;

            var name = opportunity ? Opportunity.Name : skill != null ? skill.Name : "an attack";

            var cost = opportunity
                ? $" ({CombatLogLines.Runes(report.Attacker)})"
                : skill != null ? $" ({CombatLogLines.Cost(skill)})" : string.Empty;

            var answer = report.Defender.Count > 0
                ? $"answered {CombatLogLines.Runes(report.Defender)}"
                : "did not answer";

            Add($"{attacker} used <b>{name}</b>{cost} on {defender}, who {answer} — "
                + CombatLogLines.Verdict(report.Outcome, attacker, defender, reader),
                reader != LogSide.Neither);
        }

        void OnFell(uint creatureId) =>
            Add($"{NameOf(creatureId)} falls.", IsMine(creatureId));

        static SkillSpec SkillOf(int skillId) =>
            SkillCatalog.Current != null && SkillCatalog.Current.TryGetSkill(skillId, out var spec)
                ? spec
                : null;

        void AddRound(string text)
        {
            var label = new Label(text);
            label.AddToClassList("combat-log__round");
            Append(label);
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
        /// log that grows downwards makes the newest line the one that keeps moving -- so either it
        /// scrolls itself and steals the line you were reading, or it does not and the newest line
        /// is off screen. Newest at a fixed place solves both.
        /// </summary>
        void Append(VisualElement line)
        {
            m_List.Insert(0, line);
            m_Panel?.RemoveFromClassList("combat-log--empty");

            while (m_List.childCount > m_MaxLines)
            {
                m_List.RemoveAt(m_List.childCount - 1);
            }
        }
    }
}
