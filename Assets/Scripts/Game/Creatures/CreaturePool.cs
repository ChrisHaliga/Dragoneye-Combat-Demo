using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Unity.Netcode;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Combat;

namespace Dragoneye.Game.Creatures
{
    /// <summary>
    /// Element counts in a form netcode can replicate.
    ///
    /// The same bargain <see cref="NetCell"/> makes: <see cref="ElementCounts"/> is unmanaged and
    /// equatable, but giving it a serialiser would force the Combat assembly to reference Netcode
    /// and destroy the empty-references invariant that is the most valuable thing about it. So the
    /// netcode boundary stays here and costs a four-field copy.
    /// </summary>
    public struct NetElementCounts : INetworkSerializable, IEquatable<NetElementCounts>
    {
        public int Geo;
        public int Hydro;
        public int Pyro;
        public int Aero;
        public int Lux;
        public int Nyx;
        public int Arcana;

        public NetElementCounts(ElementCounts counts)
        {
            Geo = counts.Geo;
            Hydro = counts.Hydro;
            Pyro = counts.Pyro;
            Aero = counts.Aero;
            Lux = counts.Lux;
            Nyx = counts.Nyx;
            Arcana = counts.Arcana;
        }

        public ElementCounts ToCounts() =>
            new ElementCounts(Geo, Hydro, Pyro, Aero, Lux, Nyx, Arcana);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Geo);
            serializer.SerializeValue(ref Hydro);
            serializer.SerializeValue(ref Pyro);
            serializer.SerializeValue(ref Aero);
            serializer.SerializeValue(ref Lux);
            serializer.SerializeValue(ref Nyx);
            serializer.SerializeValue(ref Arcana);
        }

        public bool Equals(NetElementCounts other)
        {
            foreach (var element in ElementInfo.All)
            {
                if (ToCounts()[element] != other.ToCounts()[element])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is NetElementCounts other && Equals(other);

        public override int GetHashCode() => ToCounts().GetHashCode();
    }

    /// <summary>
    /// A creature's elements: what it still holds, and what everyone has seen it spend.
    ///
    /// The two halves are replicated to different audiences, which is the only reason they are two
    /// NetworkVariables rather than one. The pool reads to the owner alone -- netcode never sends it
    /// to anyone else, so an opponent cannot learn it by reading memory or a packet capture. The
    /// reveal record reads to everyone, because it is public information by design.
    ///
    /// Ownership is exactly the right audience here: a claimed creature is owned by its controlling
    /// client, and an unclaimed one is owned by the server, which is also the only thing that needs
    /// to see a computer creature's pool.
    ///
    /// A mirror of the fight's own <see cref="Dragoneye.Sim.ElementPool"/>, written by
    /// <see cref="CombatDirector"/> after every order. Nothing here spends anything: what is
    /// spent, committed, announced or returned is decided in the fight, and the whole ledger is
    /// copied here in one operation so the pool can never be lowered without the record rising.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class CreaturePool : NetworkBehaviour
    {
        // Owner-read: this is the one piece of state in the game that is not public, and the
        // permission is what enforces it rather than the UI choosing not to draw it.
        readonly NetworkVariable<NetElementCounts> m_Pool = new NetworkVariable<NetElementCounts>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<NetElementCounts> m_Revealed = new NetworkVariable<NetElementCounts>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // What an opponent has proven, and how many there are altogether. Both public, and both
        // needed by anybody who is not the owner: they cannot see the pool, so without these they
        // have no way to count what they have *not* worked out. Neither says anything the creature
        // has not already shown them.
        readonly NetworkVariable<NetElementCounts> m_Identified =
            new NetworkVariable<NetElementCounts>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<int> m_Total = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Public: everyone watched these being spent, and Take a Breath draws from the front.
        readonly NetworkList<byte> m_Outstanding = new NetworkList<byte>();

        readonly List<Element> m_OutstandingView = new List<Element>();

        ElementCounts m_StartingPool;

        /// <summary>Raised on every peer when either half changes.</summary>
        public event Action Changed;

        /// <summary>
        /// What this creature can still spend.
        ///
        /// Reads as empty on a peer that is not the owner, because netcode never delivered it. That
        /// is the intended answer rather than a failure: whether the local player is entitled to
        /// it is <see cref="LocalPlayer.Controls(CreatureState)"/>'s question, and it tells "holds
        /// nothing" apart from "none of your business".
        /// </summary>
        public ElementCounts Pool => m_Pool.Value.ToCounts();

        /// <summary>What this creature has been seen to spend. Public to everyone.</summary>
        public ElementCounts Revealed => m_Revealed.Value.ToCounts();

        /// <summary>
        /// What an opponent has proven this creature holds. Public to everyone.
        ///
        /// Not the same as <see cref="Revealed"/>: an element spent, taken back and spent again is
        /// revealed twice and proven once, because only one of it ever existed.
        /// </summary>
        public ElementCounts Identified => m_Identified.Value.ToCounts();

        /// <summary>How many elements this creature owns altogether, spent or not. Public.</summary>
        public int Total => m_Total.Value;

        /// <summary>
        /// How many elements are in the hand right now. Public, and exact.
        ///
        /// Spending moves an element out and returning moves it back, so this is the total less
        /// whatever is outstanding -- and both of those are things everybody watched. It is the
        /// number an opponent counts to know how much answering somebody has left in them.
        /// </summary>
        public int InHand
        {
            get
            {
                var left = Total - m_OutstandingView.Count;
                return left < 0 ? 0 : left;
            }
        }

        /// <summary>How many of them nobody has put a name to yet.</summary>
        public int Unidentified
        {
            get
            {
                var left = Total - Identified.Total;
                return left < 0 ? 0 : left;
            }
        }

        /// <summary>
        /// What everybody has been told about this creature's elements.
        ///
        /// The published state: a commitment in flight is held back from the record on purpose --
        /// that is the whole of DE-005's concealment -- so anything drawing a hand or forecasting
        /// a clash reads this. There is no other state to read here by mistake; the fight keeps
        /// its uncommitted ledger to itself.
        /// </summary>
        public ElementLedger Ledger =>
            new ElementLedger(Pool, Revealed, m_OutstandingView, Total, Identified);

        /// <summary>Spends not yet returned, oldest first. Public information.</summary>
        public IReadOnlyList<Element> Outstanding => m_OutstandingView;

        /// <summary>Server only, and only before <c>Spawn()</c>. Sets the starting pool.</summary>
        public void ServerConfigure(ElementCounts pool) => m_StartingPool = pool;

        public override void OnNetworkSpawn()
        {
            // Published here rather than after the spawn call, so the creature is never briefly
            // holding nothing on the client that owns it.
            if (IsServer)
            {
                m_Pool.Value = new NetElementCounts(m_StartingPool);
                m_Revealed.Value = new NetElementCounts(ElementCounts.Empty);
                m_Identified.Value = new NetElementCounts(ElementCounts.Empty);
                m_Total.Value = m_StartingPool.Total;
            }

            m_Pool.OnValueChanged += OnCountsChanged;
            m_Revealed.OnValueChanged += OnCountsChanged;
            m_Identified.OnValueChanged += OnCountsChanged;
            m_Outstanding.OnListChanged += OnOutstandingChanged;

            RebuildOutstanding();
        }

        public override void OnNetworkDespawn()
        {
            m_Pool.OnValueChanged -= OnCountsChanged;
            m_Revealed.OnValueChanged -= OnCountsChanged;
            m_Identified.OnValueChanged -= OnCountsChanged;
            m_Outstanding.OnListChanged -= OnOutstandingChanged;
        }

        void OnOutstandingChanged(NetworkListEvent<byte> _) => RebuildOutstanding();

        void RebuildOutstanding()
        {
            m_OutstandingView.Clear();

            for (var i = 0; i < m_Outstanding.Count; i++)
            {
                m_OutstandingView.Add((Element)m_Outstanding[i]);
            }

            Notify.Raise(Changed, this);
        }

        /// <summary>
        /// Server only. Copies the fight's pool in: the hand for the owner, the record for everybody.
        ///
        /// Two things because they have two audiences. The hand is what is actually left,
        /// commitments already gone from it, and it reads to the owning client alone. The
        /// ledger is the published record -- what has been revealed, what is outstanding, what
        /// has been proven -- and a commitment in flight is deliberately not in it yet. That gap
        /// is the whole of DE-005's concealment, and it is the fight's to keep: this only copies
        /// what it is handed, when it is handed it.
        /// </summary>
        public void ServerMirror(ElementCounts hand, ElementLedger published)
        {
            if (!IsServer)
            {
                return;
            }

            Write(m_Pool, hand);
            Write(m_Revealed, published.Revealed);
            Write(m_Identified, published.Identified);

            if (m_Total.Value != published.Total)
            {
                m_Total.Value = published.Total;
            }

            if (!SameOutstanding(published.Outstanding))
            {
                m_Outstanding.Clear();

                foreach (var element in published.Outstanding)
                {
                    m_Outstanding.Add((byte)element);
                }
            }
        }

        static void Write(NetworkVariable<NetElementCounts> variable, ElementCounts counts)
        {
            var next = new NetElementCounts(counts);

            if (!variable.Value.Equals(next))
            {
                variable.Value = next;
            }
        }

        bool SameOutstanding(IReadOnlyList<Element> outstanding)
        {
            if (m_Outstanding.Count != outstanding.Count)
            {
                return false;
            }

            for (var i = 0; i < outstanding.Count; i++)
            {
                if (m_Outstanding[i] != (byte)outstanding[i])
                {
                    return false;
                }
            }

            return true;
        }

        void OnCountsChanged(NetElementCounts previous, NetElementCounts current) => Notify.Raise(Changed, this);
    }
}
