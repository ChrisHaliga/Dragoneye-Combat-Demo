using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Unity.Netcode;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;
using Dragoneye.Hex;

namespace Dragoneye.Game.Combat
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// What a creature can do, and the only way a client can ask it to.
    ///
    /// Skills are not replicated. Every peer resolves the same list from the creature's replicated
    /// id through the catalog, exactly as it resolves the name and the portrait -- sending a list
    /// that cannot change would be the same unchanging bytes once per creature per match.
    ///
    /// The checks stack the same way <see cref="UnitCommands"/> stacks them: ownership is
    /// transport-level, the controller slot is the game rule for whose creature this is, and the
    /// fight behind <see cref="CombatDirector"/> decides whether the skill is affordable, in range
    /// and legal.
    /// </summary>
    [RequireComponent(typeof(CreatureState))]
    [DisallowMultipleComponent]
    public sealed class SkillCommands : NetworkBehaviour
    {
        readonly List<SkillSpec> m_Skills = new List<SkillSpec>();

        // Skills this creature has been watched using. Public, because everybody was watching --
        // and it is what lets an opponent narrow down what the next attack might be made of.
        readonly NetworkList<int> m_Seen = new NetworkList<int>();
        readonly List<int> m_SeenView = new List<int>();

        CreatureState m_Creature;
        ushort m_ResolvedId;
        byte m_ResolvedSlot;
        int m_ResolvedLevel;
        bool m_Resolved;

        void Awake() => m_Creature = GetComponent<CreatureState>();

        /// <summary>
        /// Which skills this creature has been seen to use, in the order it first used them.
        ///
        /// Public information: a skill is used in front of everybody. What it is *for* here is the
        /// forecast -- an attack costs the element its skill is made of, so knowing which skills
        /// somebody has thrown narrows what the next one could be.
        /// </summary>
        public IReadOnlyList<int> SeenSkillIds => m_SeenView;

        /// <summary>Raised on every peer when this creature is watched using something new.</summary>
        public event System.Action SeenChanged;

        public override void OnNetworkSpawn()
        {
            m_Seen.OnListChanged += OnSeenChanged;
            RebuildSeen();
        }

        public override void OnNetworkDespawn() => m_Seen.OnListChanged -= OnSeenChanged;

        void OnSeenChanged(NetworkListEvent<int> _) => RebuildSeen();

        void RebuildSeen()
        {
            m_SeenView.Clear();

            for (var i = 0; i < m_Seen.Count; i++)
            {
                m_SeenView.Add(m_Seen[i]);
            }

            Notify.Raise(SeenChanged, this);
        }

        /// <summary>
        /// Server only. Copies which skills the fight says this creature has been watched using.
        ///
        /// The list only ever grows, in the order skills were first used, so the mirror appends
        /// what is missing. The fight records a use when the skill's effect lands rather than
        /// when it is asked for -- for a contested skill, after the defender has committed --
        /// so the attacking skill, and therefore its element, never reaches this public list
        /// inside the window DE-005 exists to keep empty.
        /// </summary>
        public void ServerMirrorSeen(IReadOnlyList<int> seen)
        {
            if (!IsServer)
            {
                return;
            }

            for (var i = m_Seen.Count; i < seen.Count; i++)
            {
                m_Seen.Add(seen[i]);
            }
        }

        /// <summary>
        /// Everything this creature can do, resolved locally and cached.
        ///
        /// Keyed on the creature id, the build slot and the level, because all three decide the
        /// answer. A premade is named by its id and a player character by its slot -- caching on the
        /// id alone would give every built character the same empty list, since their id is never
        /// set -- and the level is what a skill requirement is measured against, so a level that
        /// replicates a frame later has to invalidate what was worked out before it arrived.
        /// </summary>
        public IReadOnlyList<SkillSpec> Skills
        {
            get
            {
                var id = m_Creature != null ? m_Creature.CreatureId : (ushort)0;
                var slot = m_Creature != null ? m_Creature.BuildSlot : PartyInfo.Unclaimed;
                var level = m_Creature != null ? m_Creature.Level : Progression.FirstLevel;

                if (m_Resolved && m_ResolvedId == id && m_ResolvedSlot == slot
                    && m_ResolvedLevel == level)
                {
                    return m_Skills;
                }

                m_Skills.Clear();

                var catalog = SkillCatalog.Current;

                // Not cached until there was something to resolve against. Marking it resolved here
                // and returning an empty list -- which is what this used to do -- left the creature
                // permanently unable to do anything, because the answer was remembered before the
                // question could be asked.
                if (m_Creature == null || catalog == null)
                {
                    return m_Skills;
                }

                m_ResolvedId = id;
                m_ResolvedSlot = slot;
                m_ResolvedLevel = level;
                m_Resolved = true;

                // Scaled here, at the one place the catalog's formulas meet a creature, so the
                // bar, the prompt and the server all read the number this fighter does and none
                // of them has to know that "4 + STR" was ever written.
                foreach (var skillId in m_Creature.SkillIds)
                {
                    if (catalog.TryGetSkill(skillId, out var spec))
                    {
                        m_Skills.Add(spec.Scaled(m_Creature.Attributes));
                    }
                }

                return m_Skills;
            }
        }

        public bool TryGetSkill(int id, out SkillSpec spec)
        {
            foreach (var skill in Skills)
            {
                if (skill.Id == id)
                {
                    spec = skill;
                    return true;
                }
            }

            spec = null;
            return false;
        }

        /// <summary>
        /// Client-side entry point. Asks the server to use a skill on a hex.
        ///
        /// The element is only meaningful for a skill that offers a choice of them. Sent rather
        /// than decided on the server so that what the player picked is what happens -- an element
        /// chosen for them, however sensibly, is a decision they cannot learn from.
        /// </summary>
        public void RequestUse(int skillId, Cell target, Element? element = null)
        {
            if (LocalPlayer.Controls(m_Creature)
                && TurnState.Current != null && TurnState.Current.IsActive(m_Creature))
            {
                RequestUseRpc(skillId, new NetCell(target),
                    element.HasValue ? (byte)element.Value : (byte)0, element.HasValue);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestUseRpc(int skillId, NetCell cell, byte element, bool chose,
            RpcParams rpc = default)
        {
            if (!SenderControlsThis(rpc.Receive.SenderClientId) || CombatDirector.Current == null)
            {
                return;
            }

            var picked = (Element)element;

            // An element arrives as a byte, and casting to an enum is not a checked conversion.
            // Whether the skill actually offers it is the director question, not this one.
            var chosen = chose && ElementInfo.IsDefined(picked) ? picked : (Element?)null;

            if (!CombatDirector.Current.ServerUseSkill(m_Creature, skillId, cell.ToCell(),
                    out var why, chosen))
            {
                // Refusals are ordinary -- a misclick out of range is one -- so this is verbose
                // rather than a warning.
                Debug.Log($"[SkillCommands] Skill {skillId} refused: {why}.", this);
            }
        }

        /// <summary>
        /// Whether the client that sent this order is the one the creature answers to.
        ///
        /// Resolved from the sender's id through the roster rather than trusting the payload, so the
        /// only slot a client can act as is its own.
        /// </summary>
        bool SenderControlsThis(ulong senderClientId)
        {
            var roster = PlayerRoster.Current;

            return m_Creature != null
                && roster != null
                && roster.TryGet(senderClientId, out var entry)
                && entry.Slot >= 0
                && entry.Slot <= byte.MaxValue
                && LocalPlayer.Controls(m_Creature.ControllerSlot, (byte)entry.Slot);
        }
    }

    /// <summary>
    /// Where a skill id is resolved into a skill.
    ///
    /// A seam rather than a direct reference to the catalog asset: creatures live on a spawned
    /// prefab and cannot carry a serialised reference to it, and a lookup through the arena on every
    /// access would tie the rules to a scene being loaded.
    /// </summary>
    public static class SkillCatalog
    {
        /// <summary>Set by <see cref="PlayerCharacters"/>, which lives the whole match. Null outside one.</summary>
        public static ISkillIndex Current { get; internal set; }
    }
}
