using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Sim;
using Unity.Netcode;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Whose turn it is, what round it is, and who is left, as every client sees it.
    ///
    /// A mirror of the fight's <see cref="TurnQueue"/>, written by <see cref="CombatDirector"/>
    /// after every order and read by everything on screen. It decides nothing: the queue is
    /// advanced, wrapped and emptied inside the fight, and what arrives here is the result.
    ///
    /// Server-authoritative in the strict sense: clients read it and never write it. A client
    /// deciding locally that its turn had ended would be a client that could act twice.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class TurnState : NetworkBehaviour
    {
        readonly NetworkList<uint> m_Order = new NetworkList<uint>();

        readonly NetworkVariable<int> m_Index = new NetworkVariable<int>(-1);
        readonly NetworkVariable<int> m_Round = new NetworkVariable<int>(0);

        // An int rather than a nullable Party: NetworkVariable needs an unmanaged type, and Party
        // has no member for either of the two ways a fight is not being won. The two sentinels
        // are the queue's, so the mirror is a copy and not a translation.
        readonly NetworkVariable<int> m_Outcome = new NetworkVariable<int>(TurnQueue.Running);

        readonly List<uint> m_OrderView = new List<uint>();

        /// <summary>The turn state for the match in progress, or null outside one.</summary>
        public static TurnState Current { get; private set; }

        /// <summary>Raised on every peer when anything here changes.</summary>
        public event Action Changed;

        /// <summary>Initiative order as creature turn ids. Fastest first.</summary>
        public IReadOnlyList<uint> Order => m_OrderView;

        /// <summary>Rounds completed plus one. Zero before the match starts.</summary>
        public int Round => m_Round.Value;

        /// <summary>
        /// True once no more turns will be taken, however that came about: a side won, everybody
        /// fell, or the fight was stopped.
        /// </summary>
        public bool IsOver => m_Outcome.Value != TurnQueue.Running;

        /// <summary>
        /// Whether a side actually won it.
        ///
        /// Separate from <see cref="IsOver"/> because a fight can end with nobody standing, and
        /// because a test scenario stops the fight the moment its script runs out -- neither is
        /// a victory, and awarding one would put a lie on the screen.
        /// </summary>
        public bool HasWinner => m_Outcome.Value >= 0;

        /// <summary>The winning party. Only meaningful when <see cref="HasWinner"/>.</summary>
        public Party Winner => (Party)Mathf.Max(0, m_Outcome.Value);

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
            m_Outcome.OnValueChanged += OnIntChanged;

            RebuildView();
        }

        public override void OnNetworkDespawn()
        {
            m_Order.OnListChanged -= OnOrderChanged;
            m_Index.OnValueChanged -= OnIntChanged;
            m_Round.OnValueChanged -= OnIntChanged;
            m_Outcome.OnValueChanged -= OnIntChanged;

            if (Current == this)
            {
                Current = null;
            }
        }

        void OnOrderChanged(NetworkListEvent<uint> _) => RebuildView();

        void OnIntChanged(int previous, int current) => Notify.Raise(Changed, this);

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

            Notify.Raise(Changed, this);
        }

        /// <summary>
        /// Server only. Copies the fight's queue in.
        ///
        /// The order is rewritten only when it differs, and the index, round and outcome are
        /// each written only when they differ, so a mirror after an order that changed nothing
        /// sends nothing. The order is written before the index: a client that read a new index
        /// against an old order would name the wrong creature for a frame.
        /// </summary>
        public void ServerMirror(IReadOnlyList<uint> order, int index, int round, int outcome)
        {
            if (!IsServer)
            {
                return;
            }

            if (!SameOrder(order))
            {
                m_Order.Clear();

                foreach (var id in order)
                {
                    m_Order.Add(id);
                }
            }

            if (m_Round.Value != round)
            {
                m_Round.Value = round;
            }

            if (m_Outcome.Value != outcome)
            {
                m_Outcome.Value = outcome;
            }

            if (m_Index.Value != index)
            {
                m_Index.Value = index;
            }
        }

        bool SameOrder(IReadOnlyList<uint> order)
        {
            if (m_Order.Count != order.Count)
            {
                return false;
            }

            for (var i = 0; i < order.Count; i++)
            {
                if (m_Order[i] != order[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
