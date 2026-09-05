using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
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
    /// The pair is written through <see cref="ElementLedger"/> in one operation, so a spend can
    /// never lower the pool without raising the record.
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

        CreatureState m_Creature;

        void Awake() => m_Creature = GetComponent<CreatureState>();

        /// <summary>Raised on every peer when either half changes.</summary>
        public event Action Changed;

        /// <summary>
        /// What this creature can still spend.
        ///
        /// Reads as empty on a peer that is not the owner, because netcode never delivered it. That
        /// is the intended answer rather than a failure: use <see cref="CanSee"/> to tell "holds
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
        /// Whether this peer is entitled to <see cref="Pool"/>.
        ///
        /// Whether a *player* runs this creature, not whether this process owns the object. The two
        /// come apart on the host, which owns every computer creature -- so the host could read
        /// every enemy hand while everybody else was guessing, and saw no unknown count on the card
        /// because as far as the card was concerned there was nothing unknown.
        ///
        /// The server still reads the pool directly to run the brain. That is a different question
        /// from whether it may be drawn.
        /// </summary>
        public bool CanSee => LocalPlayer.Controls(m_Creature);

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

            Changed?.Invoke();
        }

        /// <summary>
        /// Server only. Spends an element, lowering the pool and raising the reveal record together.
        /// </summary>
        /// <returns>False if the creature does not hold it, in which case nothing changed.</returns>
        public bool ServerSpend(Element element, int amount, out SpendRefusal refusal)
        {
            refusal = SpendRefusal.None;

            if (!IsServer)
            {
                return false;
            }

            if (!Ledger.TrySpend(element, amount, out var next, out refusal))
            {
                return false;
            }

            Publish(next);
            return true;
        }

        /// <summary>
        /// Server only. Brings back the oldest outstanding spend, which is what Take a Breath does.
        /// </summary>
        public bool ServerReturn(out Element returned, out SpendRefusal refusal)
        {
            returned = default;
            refusal = SpendRefusal.None;

            if (!IsServer)
            {
                return false;
            }

            if (!Ledger.TryReturn(out var next, out returned, out refusal))
            {
                return false;
            }

            Publish(next);
            return true;
        }

        /// <summary>
        /// Writes a whole ledger back.
        ///
        /// All three or none. They are separate replicated fields only because they have different
        /// audiences; the ledger is what guarantees they agree.
        /// </summary>
        void Publish(ElementLedger ledger)
        {
            m_Pool.Value = new NetElementCounts(ledger.Pool);
            m_Revealed.Value = new NetElementCounts(ledger.Revealed);
            m_Identified.Value = new NetElementCounts(ledger.Identified);

            m_Outstanding.Clear();

            foreach (var element in ledger.Outstanding)
            {
                m_Outstanding.Add((byte)element);
            }
        }

        void OnCountsChanged(NetElementCounts previous, NetElementCounts current) => Changed?.Invoke();
    }
}
