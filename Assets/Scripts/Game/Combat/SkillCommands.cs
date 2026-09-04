using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
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
    /// transport-level, the controller slot is the game rule for whose creature this is, and
    /// <see cref="CombatDirector"/> decides whether the skill is affordable, in range and legal.
    /// </summary>
    [RequireComponent(typeof(CreatureState))]
    [DisallowMultipleComponent]
    public sealed class SkillCommands : NetworkBehaviour
    {
        readonly List<SkillSpec> m_Skills = new List<SkillSpec>();

        CreatureState m_Creature;
        ushort m_ResolvedId;
        byte m_ResolvedSlot;
        bool m_Resolved;

        void Awake() => m_Creature = GetComponent<CreatureState>();

        /// <summary>
        /// Everything this creature can do, resolved locally and cached.
        ///
        /// Keyed on both the creature id and the build slot, because either can be the source. A
        /// premade is named by its id and a player character by its slot -- caching on the id alone
        /// would give every built character the same empty list, since their id is never set.
        /// </summary>
        public IReadOnlyList<SkillSpec> Skills
        {
            get
            {
                var id = m_Creature != null ? m_Creature.CreatureId : (ushort)0;
                var slot = m_Creature != null ? m_Creature.BuildSlot : PartyInfo.Unclaimed;

                if (m_Resolved && m_ResolvedId == id && m_ResolvedSlot == slot)
                {
                    return m_Skills;
                }

                m_Skills.Clear();
                m_ResolvedId = id;
                m_ResolvedSlot = slot;
                m_Resolved = true;

                var catalog = SkillCatalog.Current;

                if (m_Creature == null || catalog == null)
                {
                    return m_Skills;
                }

                foreach (var skillId in m_Creature.SkillIds)
                {
                    if (catalog.TryGetSkill(skillId, out var spec))
                    {
                        m_Skills.Add(spec);
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

        /// <summary>Client-side entry point. Asks the server to use a skill on a hex.</summary>
        public void RequestUse(int skillId, Hex target)
        {
            if (LocalPlayer.Controls(m_Creature)
                && TurnState.Current != null && TurnState.Current.IsActive(m_Creature))
            {
                RequestUseRpc(skillId, new NetCell(target));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestUseRpc(int skillId, NetCell cell, RpcParams rpc = default)
        {
            if (!SenderControlsThis(rpc.Receive.SenderClientId) || CombatDirector.Current == null)
            {
                return;
            }

            if (!CombatDirector.Current.ServerUseSkill(m_Creature, skillId, cell.ToHex(), out var why))
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
        public static ISkillIndex Current { get; set; }
    }
}
