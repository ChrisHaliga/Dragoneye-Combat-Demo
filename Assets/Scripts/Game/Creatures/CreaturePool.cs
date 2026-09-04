using System;
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
        public int Fire;
        public int Water;
        public int Earth;
        public int Air;

        public NetElementCounts(ElementCounts counts)
        {
            Fire = counts.Fire;
            Water = counts.Water;
            Earth = counts.Earth;
            Air = counts.Air;
        }

        public ElementCounts ToCounts() => new ElementCounts(Fire, Water, Earth, Air);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Fire);
            serializer.SerializeValue(ref Water);
            serializer.SerializeValue(ref Earth);
            serializer.SerializeValue(ref Air);
        }

        public bool Equals(NetElementCounts other) =>
            Fire == other.Fire && Water == other.Water
            && Earth == other.Earth && Air == other.Air;

        public override bool Equals(object obj) => obj is NetElementCounts other && Equals(other);

        public override int GetHashCode() =>
            unchecked(((Fire * 397 ^ Water) * 397 ^ Earth) * 397 ^ Air);
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

        ElementCounts m_StartingPool;

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

        /// <summary>Whether this peer is entitled to <see cref="Pool"/>.</summary>
        public bool CanSee => IsOwner;

        public ElementLedger Ledger => new ElementLedger(Pool, Revealed);

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
            }

            m_Pool.OnValueChanged += OnCountsChanged;
            m_Revealed.OnValueChanged += OnCountsChanged;

            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_Pool.OnValueChanged -= OnCountsChanged;
            m_Revealed.OnValueChanged -= OnCountsChanged;
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

            // Both writes or neither. They are separate NetworkVariables only because they have
            // different audiences; the ledger is what guarantees they agree.
            m_Pool.Value = new NetElementCounts(next.Pool);
            m_Revealed.Value = new NetElementCounts(next.Revealed);

            return true;
        }

        void OnCountsChanged(NetElementCounts previous, NetElementCounts current) => Changed?.Invoke();
    }
}
