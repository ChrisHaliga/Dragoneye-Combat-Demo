using Dragoneye.Combat;
using UnityEngine;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Leans an attacker at whoever it just swung at.
    ///
    /// A fight made of numbers appearing needs a moment where something moves, or every exchange
    /// reads as the board changing its mind. The token throws itself a little way forward and
    /// comes back, which is the whole of it: enough to say who acted and which way, and short
    /// enough that a turn of three attacks is still a turn.
    ///
    /// It listens to the same announcements every other view does, so it happens on every
    /// machine and it happens for the computer's attacks as well as a player's. Nothing here
    /// decides anything; the fight is already over by the time this runs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackFlourish : MonoBehaviour
    {
        [SerializeField]
        CreatureRegistry m_Creatures;

        void OnEnable()
        {
            ClashCommands.Resolved += OnClash;
            CombatAnnouncer.Acted += OnActed;
            CombatAnnouncer.Shot += OnShot;
        }

        void OnDisable()
        {
            ClashCommands.Resolved -= OnClash;
            CombatAnnouncer.Acted -= OnActed;
            CombatAnnouncer.Shot -= OnShot;
        }

        void OnClash(ClashReport report) => Lean(report.AttackerId, report.DefenderId);

        void OnShot(ShotReport report) => Lean(report.AttackerId, report.TargetId);

        void OnActed(ActionReport report)
        {
            // A heal or a breath is aimed at the user, and a creature does not lunge at itself.
            if (report.HasTarget && report.TargetId != report.ActorId)
            {
                Lean(report.ActorId, report.TargetId);
            }
        }

        void Lean(uint attackerId, uint targetId)
        {
            if (m_Creatures == null)
            {
                return;
            }

            var attacker = m_Creatures.ByTurnId(attackerId);
            var target = m_Creatures.ByTurnId(targetId);

            if (attacker == null || target == null || attacker == target)
            {
                return;
            }

            attacker.View?.Lunge(target.transform.position);
        }
    }
}
