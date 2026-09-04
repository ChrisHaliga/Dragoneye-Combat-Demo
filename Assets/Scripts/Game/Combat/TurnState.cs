using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Whose turn it is, what round it is, and who is left.
    ///
    /// Server-authoritative in the strict sense: clients read it and never write it. A client
    /// deciding locally that its turn had ended would be a client that could act twice.
    ///
    /// The order is replicated as ids rather than rebuilt per peer. Rebuilding would be cheap and
    /// would agree today, but only because <see cref="TurnOrder"/> is careful; replicating it means
    /// agreement is a property of the transport rather than of every peer running identical code
    /// over identically-replicated inputs.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class TurnState : NetworkBehaviour
    {
        readonly NetworkList<uint> m_Order = new NetworkList<uint>();

        readonly NetworkVariable<int> m_Index = new NetworkVariable<int>(-1);
        readonly NetworkVariable<int> m_Round = new NetworkVariable<int>(0);

        // -1 rather than a nullable: NetworkVariable needs an unmanaged type, and Party has no
        // "nobody has won" member that would not also be a legal party.
        readonly NetworkVariable<int> m_Winner = new NetworkVariable<int>(-1);

        readonly List<uint> m_OrderView = new List<uint>();

        /// <summary>The turn state for the match in progress, or null outside one.</summary>
        public static TurnState Current { get; private set; }

        /// <summary>Raised on every peer when anything here changes.</summary>
        public event Action Changed;

        /// <summary>Initiative order as creature turn ids. Fastest first.</summary>
        public IReadOnlyList<uint> Order => m_OrderView;

        /// <summary>Rounds completed plus one. Zero before the match starts.</summary>
        public int Round => m_Round.Value;

        /// <summary>True once one side is all that remains.</summary>
        public bool IsOver => m_Winner.Value >= 0;

        /// <summary>The winning party. Only meaningful when <see cref="IsOver"/>.</summary>
        public Party Winner => (Party)Mathf.Max(0, m_Winner.Value);

        /// <summary>The creature whose turn it is, or 0 if there is none.</summary>
        public uint ActiveId =>
            m_Index.Value >= 0 && m_Index.Value < m_OrderView.Count ? m_OrderView[m_Index.Value] : 0u;

        public bool IsActive(CreatureState creature) =>
            creature != null && !IsOver && creature.TurnId == ActiveId;

        public override void OnNetworkSpawn()
        {
            if (Current != null && Current != this)
            {
                Debug.LogError("A second TurnState spawned; the match expects one.", this);
            }
            else
            {
                Current = this;
            }

            m_Order.OnListChanged += OnOrderChanged;
            m_Index.OnValueChanged += OnIntChanged;
            m_Round.OnValueChanged += OnIntChanged;
            m_Winner.OnValueChanged += OnIntChanged;

            RebuildView();
        }

        public override void OnNetworkDespawn()
        {
            m_Order.OnListChanged -= OnOrderChanged;
            m_Index.OnValueChanged -= OnIntChanged;
            m_Round.OnValueChanged -= OnIntChanged;
            m_Winner.OnValueChanged -= OnIntChanged;

            if (Current == this)
            {
                Current = null;
            }
        }

        void OnOrderChanged(NetworkListEvent<uint> _) => RebuildView();

        void OnIntChanged(int previous, int current) => Changed?.Invoke();

        /// <summary>
        /// Mirrors the NetworkList into a plain list.
        ///
        /// NetworkList cannot be handed to pure code or constructed in a test, and the turn bar
        /// wants to iterate the order every repaint. One copy per change beats a wrapper on a type
        /// that only exists inside a running match.
        /// </summary>
        void RebuildView()
        {
            m_OrderView.Clear();

            for (var i = 0; i < m_Order.Count; i++)
            {
                m_OrderView.Add(m_Order[i]);
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Server only. Builds the initiative order and starts the first turn.
        /// </summary>
        public void ServerBegin(IReadOnlyList<Combatant> combatants, Func<uint, bool> isActive)
        {
            if (!IsServer)
            {
                return;
            }

            m_Order.Clear();

            foreach (var id in TurnOrder.Build(combatants))
            {
                m_Order.Add(id);
            }

            m_Winner.Value = -1;
            m_Round.Value = 1;

            m_Index.Value = TurnOrder.TryFirst(m_OrderView, isActive, out var first) ? first : -1;
        }

        /// <summary>
        /// Server only. Hands the turn to the next creature that can still act, rolling the round
        /// over when the order wraps.
        /// </summary>
        /// <returns>False when nobody can act, which means the match is over.</returns>
        public bool ServerAdvance(Func<uint, bool> isActive)
        {
            if (!IsServer || IsOver)
            {
                return false;
            }

            if (!TurnOrder.TryAdvance(m_OrderView, m_Index.Value, isActive, out var next, out var wrapped))
            {
                m_Index.Value = -1;
                return false;
            }

            if (wrapped)
            {
                m_Round.Value++;
            }

            m_Index.Value = next;
            return true;
        }

        /// <summary>Server only. Records the winner, which ends the match.</summary>
        public void ServerDeclareWinner(Party party)
        {
            if (IsServer)
            {
                m_Winner.Value = (int)party;
                m_Index.Value = -1;
            }
        }

        /// <summary>
        /// Removes a dead creature from the order.
        ///
        /// Kept as a separate call rather than folded into death handling, because the index has to
        /// be corrected in the same breath: entries before the current one shift everything down,
        /// and without the adjustment the turn silently passes to whoever moved into the slot.
        /// </summary>
        public void ServerRemove(uint turnId)
        {
            if (!IsServer)
            {
                return;
            }

            for (var i = 0; i < m_Order.Count; i++)
            {
                if (m_Order[i] != turnId)
                {
                    continue;
                }

                m_Order.RemoveAt(i);

                if (i < m_Index.Value)
                {
                    m_Index.Value--;
                }

                return;
            }
        }
    }
}
