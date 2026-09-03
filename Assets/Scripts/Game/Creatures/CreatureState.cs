using System;
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
        readonly NetworkVariable<int> m_CurrentAp = new NetworkVariable<int>();

        // Identity handed over before the spawn, held until there are NetworkVariables to put it in.
        ushort m_StartCreatureId;
        Party m_StartParty;
        byte m_StartControllerSlot = PartyInfo.Unclaimed;

        CreatureDefinition m_Definition;
        CreatureRegistry m_Registry;

        public ushort CreatureId => m_CreatureId.Value;

        public Party Party => (Party)m_PartyId.Value;

        /// <summary><see cref="PartyInfo.Unclaimed"/> means the computer runs it.</summary>
        public byte ControllerSlot => m_ControllerSlot.Value;

        public bool IsComputerControlled => m_ControllerSlot.Value == PartyInfo.Unclaimed;

        public int CurrentHp => m_CurrentHp.Value;

        public int CurrentAp => m_CurrentAp.Value;

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

        public string DisplayName => Definition != null ? Definition.DisplayName : "Unknown";

        public int MaxHp => Definition != null ? Definition.MaxHp : 1;

        public int MaxAp => Definition != null ? Definition.MaxAp : 0;

        public int Speed => Definition != null ? Definition.Speed : 0;

        /// <summary>Raised on every client when anything replicated here changes.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            // Before subscribing and before registering with the HUD, so the creature is never
            // briefly a party-zero, full-health nobody that the portrait column has to redraw.
            if (IsServer)
            {
                var definition = Catalog != null ? Catalog.Resolve(m_StartCreatureId) : null;

                m_CreatureId.Value = m_StartCreatureId;
                m_PartyId.Value = (byte)m_StartParty;
                m_ControllerSlot.Value = m_StartControllerSlot;
                m_CurrentHp.Value = definition != null ? definition.MaxHp : 1;
                m_CurrentAp.Value = definition != null ? definition.MaxAp : 0;
            }

            m_CreatureId.OnValueChanged += OnIdChanged;
            m_PartyId.OnValueChanged += OnByteChanged;
            m_ControllerSlot.OnValueChanged += OnByteChanged;
            m_CurrentHp.OnValueChanged += OnIntChanged;
            m_CurrentAp.OnValueChanged += OnIntChanged;

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
            m_CurrentAp.OnValueChanged -= OnIntChanged;

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
        public void ServerConfigure(ushort creatureId, Party party, byte controllerSlot)
        {
            m_StartCreatureId = creatureId;
            m_StartParty = party;
            m_StartControllerSlot = controllerSlot;
        }

        void OnIdChanged(ushort previous, ushort current)
        {
            m_Definition = null;
            Changed?.Invoke();
        }

        void OnByteChanged(byte previous, byte current) => Changed?.Invoke();

        void OnIntChanged(int previous, int current) => Changed?.Invoke();
    }
}
