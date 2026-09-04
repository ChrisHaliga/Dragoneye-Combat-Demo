using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// A unit's identity and vitals. <see cref="UnitState"/> keeps where it stands; this keeps what
    /// it is. One prefab, two NetworkBehaviours, one responsibility each.
    ///
    /// Only mutable state is replicated. Name, portrait, species, class, max HP, max AP and speed are
    /// all authored constants, resolved locally from <see cref="CreatureId"/> through the catalog --
    /// replicating them would send the same unchanging bytes once per unit per match.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class CreatureState : NetworkBehaviour
    {
        readonly NetworkVariable<ushort> m_CreatureId = new NetworkVariable<ushort>();
        readonly NetworkVariable<byte> m_PartyId = new NetworkVariable<byte>();
        readonly NetworkVariable<byte> m_ControllerSlot =
            new NetworkVariable<byte>(PartyInfo.Unclaimed);

        readonly NetworkVariable<int> m_CurrentHp = new NetworkVariable<int>();
        // Half-units, per DE-000. Replicated as the integer it is stored as, so no rounding happens
        // on the wire and a client cannot disagree with the host about whether a move is affordable.
        readonly NetworkVariable<int> m_CurrentApUnits = new NetworkVariable<int>();

        // The slot whose character this is, or Unclaimed for an authored premade. It decides which
        // of the two sources below answers "what is this creature".
        readonly NetworkVariable<byte> m_BuildSlot = new NetworkVariable<byte>(PartyInfo.Unclaimed);

        // Replicated, because a premade's level is a draft decision rather than something a client
        // can work out from the asset. A built character ignores it and reads its own build.
        readonly NetworkVariable<byte> m_PremadeLevel =
            new NetworkVariable<byte>(Progression.FirstLevel);

        // Identity handed over before the spawn, held until there are NetworkVariables to put it in.
        ushort m_StartCreatureId;
        byte m_StartBuildSlot = PartyInfo.Unclaimed;
        Party m_StartParty;
        byte m_StartControllerSlot = PartyInfo.Unclaimed;
        int m_StartLevel = Progression.FirstLevel;

        CreatureDefinition m_Definition;
        CreatureRegistry m_Registry;
        UnitState m_Unit;

        public ushort CreatureId => m_CreatureId.Value;

        public Party Party => (Party)m_PartyId.Value;

        /// <summary><see cref="PartyInfo.Unclaimed"/> means the computer runs it.</summary>
        public byte ControllerSlot => m_ControllerSlot.Value;

        public bool IsComputerControlled => m_ControllerSlot.Value == PartyInfo.Unclaimed;

        public int CurrentHp => m_CurrentHp.Value;

        public Ap CurrentAp => Ap.FromUnits(m_CurrentApUnits.Value);

        /// <summary>
        /// The one catalog in play.
        ///
        /// <see cref="DraftState"/> owns it because it needs it first -- it builds the roster in the
        /// lobby, before any arena exists -- and it survives into the arena as a spawned object. A
        /// second serialised copy on the arena was a second thing to point at the wrong asset, which
        /// would resolve ids in the spawner and not in the HUD and look exactly like a replication
        /// bug.
        /// </summary>
        public static CreatureCatalog Catalog =>
            DraftState.Current != null ? DraftState.Current.Catalog : null;

        /// <summary>The authored definition, resolved locally. Null if the id does not resolve.</summary>
        public CreatureDefinition Definition
        {
            get
            {
                if (m_Definition == null)
                {
                    var catalog = Catalog;
                    m_Definition = catalog != null ? catalog.Resolve(m_CreatureId.Value) : null;
                }

                return m_Definition;
            }
        }

        /// <summary>
        /// What this creature is, whichever of the two sources answered.
        ///
        /// A premade reads its authored definition; a player character reads the build its owner
        /// submitted. Resolving both into one shape here is what keeps the turn bar, the card, the
        /// spawner and the initiative order from each having to know which kind they are looking at.
        /// </summary>
        public CreatureProfile Profile =>
            ProfileFor(m_BuildSlot.Value, m_CreatureId.Value, m_PremadeLevel.Value);

        public string DisplayName => Profile.Name;

        public int MaxHp => Profile.MaxHealth;

        /// <summary>Authored in whole points; carried everywhere else in half-units.</summary>
        public Ap MaxAp => Profile.MaxAp;

        public int Speed => Profile.Initiative;

        /// <summary>What this creature is worth to whoever kills it.</summary>
        public int Level => Profile.Level;

        /// <summary>What this creature can do. Empty until the catalog is available.</summary>
        public IReadOnlyList<int> SkillIds => Profile.SkillIds;

        /// <summary>The elements it starts holding.</summary>
        public ElementCounts StartingPool => Profile.StartingPool;

        /// <summary>
        /// Resolves a creature from whichever source owns it.
        ///
        /// Static and parameterised so the spawner can ask before the object exists, which is when
        /// it needs the starting pool.
        /// </summary>
        public static CreatureProfile ProfileFor(byte buildSlot, ushort creatureId,
            int level = Progression.FirstLevel)
        {
            if (buildSlot != PartyInfo.Unclaimed && PlayerCharacters.Current != null)
            {
                var loadout = PlayerCharacters.Current.LoadoutFor(buildSlot);
                var build = PlayerCharacters.Current.BuildFor(buildSlot);

                if (loadout != null && build != null)
                {
                    return CreatureProfile.FromLoadout(build.Name, loadout);
                }
            }

            var catalog = Catalog;
            var definition = catalog != null ? catalog.Resolve(creatureId) : null;

            return CreatureProfile.FromDefinition(definition, level);
        }

        /// <summary>Raised on every client when anything replicated here changes.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            // Before subscribing and before registering with the HUD, so the creature is never
            // briefly a party-zero, full-health nobody that the portrait column has to redraw.
            if (IsServer)
            {
                var profile = ProfileFor(m_StartBuildSlot, m_StartCreatureId, m_StartLevel);

                m_PremadeLevel.Value = RosterEntry.Clamp(m_StartLevel);
                m_BuildSlot.Value = m_StartBuildSlot;
                m_CreatureId.Value = m_StartCreatureId;
                m_PartyId.Value = (byte)m_StartParty;
                m_ControllerSlot.Value = m_StartControllerSlot;
                m_CurrentHp.Value = profile.MaxHealth;
                m_CurrentApUnits.Value = profile.MaxAp.Units;
            }

            m_CreatureId.OnValueChanged += OnIdChanged;
            m_PartyId.OnValueChanged += OnByteChanged;
            m_ControllerSlot.OnValueChanged += OnByteChanged;
            m_CurrentHp.OnValueChanged += OnIntChanged;
            m_CurrentApUnits.OnValueChanged += OnIntChanged;
            m_BuildSlot.OnValueChanged += OnByteChanged;
            m_PremadeLevel.OnValueChanged += OnByteChanged;

            var context = ArenaContext.Current;
            m_Registry = context != null ? context.Creatures : null;

            if (m_Registry != null)
            {
                m_Registry.Add(this);
            }
            else
            {
                Debug.LogError("CreatureState found no creature registry; it will not appear in the HUD.", this);
            }

            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_CreatureId.OnValueChanged -= OnIdChanged;
            m_PartyId.OnValueChanged -= OnByteChanged;
            m_ControllerSlot.OnValueChanged -= OnByteChanged;
            m_CurrentHp.OnValueChanged -= OnIntChanged;
            m_CurrentApUnits.OnValueChanged -= OnIntChanged;
            m_BuildSlot.OnValueChanged -= OnByteChanged;
            m_PremadeLevel.OnValueChanged -= OnByteChanged;

            if (m_Registry != null)
            {
                m_Registry.Remove(this);
                m_Registry = null;
            }
        }

        /// <summary>
        /// Server only, and only before <c>Spawn()</c>. Sets what this creature is; vitals are
        /// filled from the authored definition when the object spawns.
        ///
        /// Takes an id rather than a definition: the definition is authored data every peer can
        /// resolve locally, so passing one in would invite a caller to hand over a definition that
        /// disagrees with the id being replicated.
        /// </summary>
        public void ServerConfigure(ushort creatureId, Party party, byte controllerSlot,
            byte buildSlot = PartyInfo.Unclaimed, int level = Progression.FirstLevel)
        {
            m_StartCreatureId = creatureId;
            m_StartParty = party;
            m_StartControllerSlot = controllerSlot;
            m_StartBuildSlot = buildSlot;
            m_StartLevel = level;
        }

        /// <summary>
        /// The slot whose built character this is, or <see cref="PartyInfo.Unclaimed"/> for a
        /// premade.
        /// </summary>
        public byte BuildSlot => m_BuildSlot.Value;

        /// <summary>True when this creature came out of the character creator.</summary>
        public bool IsPlayerCharacter => m_BuildSlot.Value != PartyInfo.Unclaimed;

        /// <summary>
        /// Where this creature stands.
        ///
        /// Position lives on <see cref="UnitState"/> and identity lives here, which is the right
        /// split -- but combat needs both in the same breath, and every caller reaching across with
        /// GetComponent would be the same lookup written eight times.
        /// </summary>
        public Dragoneye.Hex.Hex Cell => Unit != null ? Unit.Cell : default;

        /// <summary>The position half of this creature. Cached; both live on the one prefab.</summary>
        public UnitState Unit
        {
            get
            {
                if (m_Unit == null)
                {
                    m_Unit = GetComponent<UnitState>();
                }

                return m_Unit;
            }
        }

        /// <summary>Alive until its health reaches zero.</summary>
        public bool IsAlive => CombatRules.IsAlive(m_CurrentHp.Value);

        /// <summary>
        /// A stable per-match identifier, used to name this creature in the replicated turn order.
        ///
        /// The NetworkObjectId rather than the creature id: several creatures in a match can share a
        /// definition, so the catalog id names a *kind* and would put three dire wolves in the same
        /// slot of the initiative queue.
        /// </summary>
        public uint TurnId => (uint)NetworkObjectId;

        /// <summary>
        /// Server only. Deducts AP, refusing to go below zero.
        /// </summary>
        /// <returns>False if the creature could not afford it, in which case nothing was spent.</returns>
        public bool ServerSpendAp(Ap amount)
        {
            if (!IsServer || amount.Units < 0 || m_CurrentApUnits.Value < amount.Units)
            {
                return false;
            }

            m_CurrentApUnits.Value -= amount.Units;
            return true;
        }

        /// <summary>Server only. Restores health, never past the maximum.</summary>
        public void ServerHeal(int amount)
        {
            if (IsServer && IsAlive)
            {
                m_CurrentHp.Value = SkillRules.Apply(
                    new SkillEffect(SkillEffectKind.Heal, amount), m_CurrentHp.Value, MaxHp);
            }
        }

        /// <summary>Server only. Restores action points, never past the maximum.</summary>
        public void ServerRestoreAp(Ap amount)
        {
            if (IsServer)
            {
                m_CurrentApUnits.Value = SkillRules.Apply(
                    new SkillEffect(SkillEffectKind.RestoreAp, amount.Units),
                    m_CurrentApUnits.Value, MaxAp.Units);
            }
        }

        /// <summary>Server only. Restores AP to full at the start of a turn.</summary>
        public void ServerRefillAp()
        {
            if (IsServer)
            {
                m_CurrentApUnits.Value = MaxAp.Units;
            }
        }

        /// <summary>
        /// Server only. Applies damage.
        /// </summary>
        /// <returns>True if this killed the creature, so the caller can clear it off the board.</returns>
        public bool ServerApplyDamage(int damage, int reduction = 0)
        {
            if (!IsServer || !IsAlive)
            {
                return false;
            }

            // The reduction is applied here rather than by the caller so that what lands and what is
            // announced cannot disagree: one subtraction, one number, told to everybody.
            var landed = CombatRules.DamageAfter(damage, reduction);

            m_CurrentHp.Value = CombatRules.Damaged(m_CurrentHp.Value, damage, reduction);
            ShowDamageRpc(landed, damage, reduction);

            return !IsAlive;
        }

        /// <summary>
        /// Tells every peer what this creature just took, and why it was not worse.
        ///
        /// The numbers cross the wire, not the sentence: each client words it for itself, so the
        /// rules never carry English and a translation later changes one file.
        /// </summary>
        [Rpc(SendTo.Everyone)]
        void ShowDamageRpc(int landed, int raw, int reduction) =>
            CombatNotices.Raise(TurnId, CombatNotices.Damage(landed, raw, reduction),
                NoticeTone.Loss);

        void OnIdChanged(ushort previous, ushort current)
        {
            m_Definition = null;
            Changed?.Invoke();
        }

        void OnByteChanged(byte previous, byte current) => Changed?.Invoke();

        void OnIntChanged(int previous, int current) => Changed?.Invoke();
    }
}
