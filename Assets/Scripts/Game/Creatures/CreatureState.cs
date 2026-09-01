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

        CreatureDefinition m_Definition;

        public ushort CreatureId => m_CreatureId.Value;

        public Party Party => (Party)m_PartyId.Value;

        /// <summary><see cref="PartyInfo.Unclaimed"/> means the computer runs it.</summary>
        public byte ControllerSlot => m_ControllerSlot.Value;

        public bool IsComputerControlled => m_ControllerSlot.Value == PartyInfo.Unclaimed;

        public int CurrentHp => m_CurrentHp.Value;

        public int CurrentAp => m_CurrentAp.Value;

        /// <summary>The authored definition, resolved locally. Null if the id does not resolve.</summary>
        public CreatureDefinition Definition
        {
            get
            {
                if (m_Definition == null)
                {
                    var draft = DraftState.Current;
                    m_Definition = draft != null && draft.Catalog != null
                        ? draft.Catalog.Resolve(m_CreatureId.Value)
                        : null;
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
            m_CreatureId.OnValueChanged += OnIdChanged;
            m_PartyId.OnValueChanged += OnByteChanged;
            m_ControllerSlot.OnValueChanged += OnByteChanged;
            m_CurrentHp.OnValueChanged += OnIntChanged;
            m_CurrentAp.OnValueChanged += OnIntChanged;

            CreatureRegistry.Add(this);
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_CreatureId.OnValueChanged -= OnIdChanged;
            m_PartyId.OnValueChanged -= OnByteChanged;
            m_ControllerSlot.OnValueChanged -= OnByteChanged;
            m_CurrentHp.OnValueChanged -= OnIntChanged;
            m_CurrentAp.OnValueChanged -= OnIntChanged;

            CreatureRegistry.Remove(this);
        }

        /// <summary>Server only. Sets identity and fills vitals from the authored definition.</summary>
        public void ServerInitialise(ushort creatureId, Party party, byte controllerSlot,
            CreatureDefinition definition)
        {
            if (!IsServer)
            {
                return;
            }

            m_CreatureId.Value = creatureId;
            m_PartyId.Value = (byte)party;
            m_ControllerSlot.Value = controllerSlot;
            m_CurrentHp.Value = definition != null ? definition.MaxHp : 1;
            m_CurrentAp.Value = definition != null ? definition.MaxAp : 0;
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
