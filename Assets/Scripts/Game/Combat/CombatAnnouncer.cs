using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Unity.Netcode;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>One thing a creature did that nobody contested.</summary>
    public readonly struct ActionReport
    {
        public readonly uint ActorId;
        public readonly int SkillId;

        /// <summary>Who it landed on, where that was somebody other than the actor.</summary>
        public readonly uint TargetId;

        public readonly bool HasTarget;

        /// <summary>Elements the skill handed back, if it was that kind of skill.</summary>
        public readonly IReadOnlyList<Element> Returned;

        public ActionReport(uint actorId, int skillId, uint targetId, bool hasTarget,
            IReadOnlyList<Element> returned)
        {
            ActorId = actorId;
            SkillId = skillId;
            TargetId = targetId;
            HasTarget = hasTarget;
            Returned = returned ?? Array.Empty<Element>();
        }
    }

    /// <summary>A shot that rolled: what the chance was, and whether it landed.</summary>
    public readonly struct ShotReport
    {
        public readonly uint AttackerId;
        public readonly int SkillId;
        public readonly uint TargetId;

        /// <summary>The percent chance, so the log can say how lucky or unlucky it was.</summary>
        public readonly int Chance;

        public readonly bool Landed;

        public ShotReport(uint attackerId, int skillId, uint targetId, int chance, bool landed)
        {
            AttackerId = attackerId;
            SkillId = skillId;
            TargetId = targetId;
            Chance = chance;
            Landed = landed;
        }
    }

    /// <summary>
    /// Says out loud the things a fight does that are not clashes.
    ///
    /// A clash announces itself through <see cref="ClashCommands"/>, which already has to carry the
    /// question and the answer. Everything else -- a creature catching its breath, a creature
    /// falling over -- happens on the server and, until this existed, happened silently as far as
    /// everyone else was concerned. A player could watch an ogre's action points go down and had no
    /// way at all to learn what it had spent them on.
    ///
    /// Numbers cross the wire, never sentences. Each client words what it hears for itself, which
    /// keeps English out of the rules and makes a translation one file rather than a protocol
    /// change.
    ///
    /// Rounds are deliberately not announced here: the round number is already replicated on
    /// <see cref="TurnState"/>, and a message saying a thing every peer can read is a second source
    /// of truth waiting to disagree with the first.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class CombatAnnouncer : NetworkBehaviour
    {
        /// <summary>The one in the arena. Null outside a match.</summary>
        public static CombatAnnouncer Current { get; private set; }

        /// <summary>A creature used a skill that nobody contested.</summary>
        public static event Action<ActionReport> Acted;

        /// <summary>A creature ran out of health.</summary>
        public static event Action<uint> Fell;

        /// <summary>A watcher let a mover go: (watcher, mover).</summary>
        public static event Action<uint, uint> HeldBack;

        /// <summary>A shot rolled and missed.</summary>
        public static event Action<ShotReport> Shot;

        /// <summary>Health came back at the start of a turn: (creature, amount).</summary>
        public static event Action<uint, int> Recovered;

        /// <summary>A creature walked: (creature, from, to). The rules put it there instantly.</summary>
        public static event Action<uint, Dragoneye.Hex.Cell, Dragoneye.Hex.Cell> Moved;

        /// <summary>A creature's turn began.</summary>
        public static event Action<uint> TurnBegan;

        public override void OnNetworkSpawn() => Current = this;

        public override void OnNetworkDespawn()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>Server only.</summary>
        public void ServerActed(uint actorId, int skillId, uint targetId, bool hasTarget,
            IReadOnlyList<Element> returned)
        {
            if (IsServer)
            {
                ActedRpc(actorId, skillId, targetId, hasTarget, Pack(returned));
            }
        }

        /// <summary>Server only.</summary>
        public void ServerFell(uint creatureId)
        {
            if (IsServer)
            {
                FellRpc(creatureId);
            }
        }

        /// <summary>Server only. Somebody had a swing at a creature walking past, and did not take it.</summary>
        public void ServerHeldBack(uint watcherId, uint moverId)
        {
            if (IsServer)
            {
                HeldBackRpc(watcherId, moverId);
            }
        }

        [Rpc(SendTo.Everyone)]
        void HeldBackRpc(uint watcherId, uint moverId) => HeldBack?.Invoke(watcherId, moverId);

        /// <summary>Server only. A shot rolled, and this is how it went.</summary>
        public void ServerShot(uint attackerId, int skillId, uint targetId, int chance, bool landed)
        {
            if (IsServer)
            {
                ShotRpc(attackerId, skillId, targetId, chance, landed);
            }
        }

        /// <summary>Server only. A creature moved, as far as the rules are concerned.</summary>
        public void ServerMoved(uint creatureId, Dragoneye.Hex.Cell from, Dragoneye.Hex.Cell to)
        {
            if (IsServer)
            {
                MovedRpc(creatureId, new NetCell(from), new NetCell(to));
            }
        }

        [Rpc(SendTo.Everyone)]
        void MovedRpc(uint creatureId, NetCell from, NetCell to) =>
            Moved?.Invoke(creatureId, from.ToCell(), to.ToCell());

        /// <summary>Server only. A creature's turn began.</summary>
        public void ServerTurnBegan(uint creatureId)
        {
            if (IsServer)
            {
                TurnBeganRpc(creatureId);
            }
        }

        [Rpc(SendTo.Everyone)]
        void TurnBeganRpc(uint creatureId) => TurnBegan?.Invoke(creatureId);

        /// <summary>Server only. Toughness put health back at the start of a turn.</summary>
        public void ServerRecovered(uint creatureId, int amount)
        {
            if (IsServer)
            {
                RecoveredRpc(creatureId, amount);
            }
        }

        [Rpc(SendTo.Everyone)]
        void RecoveredRpc(uint creatureId, int amount) => Recovered?.Invoke(creatureId, amount);

        [Rpc(SendTo.Everyone)]
        void ShotRpc(uint attackerId, int skillId, uint targetId, int chance, bool landed) =>
            Shot?.Invoke(new ShotReport(attackerId, skillId, targetId, chance, landed));

        [Rpc(SendTo.Everyone)]
        void ActedRpc(uint actorId, int skillId, uint targetId, bool hasTarget, byte[] returned) =>
            Acted?.Invoke(new ActionReport(actorId, skillId, targetId, hasTarget,
                Unpack(returned)));

        [Rpc(SendTo.Everyone)]
        void FellRpc(uint creatureId) => Fell?.Invoke(creatureId);

        static byte[] Pack(IReadOnlyList<Element> elements)
        {
            var packed = new byte[elements?.Count ?? 0];

            for (var i = 0; i < packed.Length; i++)
            {
                packed[i] = (byte)elements[i];
            }

            return packed;
        }

        static List<Element> Unpack(byte[] packed)
        {
            var elements = new List<Element>(packed.Length);

            foreach (var raw in packed)
            {
                var element = (Element)raw;

                // An element arrives as a byte, and casting to an enum is not a checked conversion.
                if (ElementInfo.IsDefined(element))
                {
                    elements.Add(element);
                }
            }

            return elements;
        }
    }
}
