using System;
using Unity.Netcode;

namespace Dragoneye.Game
{
    /// <summary>One creature placed in a party, and who (if anyone) has claimed it.</summary>
    public struct RosterEntry : INetworkSerializable, IEquatable<RosterEntry>
    {
        public ushort CreatureId;

        /// <summary>Stored as a byte so the enum's wire size is explicit rather than implied.</summary>
        public byte PartyId;

        /// <summary><see cref="PartyInfo.Unclaimed"/> means computer-controlled.</summary>
        public byte ClaimedBySlot;

        /// <summary>
        /// Order in which this claim was made. Lets an over-cap release drop the newest claims
        /// first, which is what a player expects: the ones they just took, not the ones they have
        /// been building around.
        /// </summary>
        public uint ClaimSequence;

        public Party Party => (Party)PartyId;

        public bool IsClaimed => ClaimedBySlot != PartyInfo.Unclaimed;

        public RosterEntry(ushort creatureId, Party party)
        {
            CreatureId = creatureId;
            PartyId = (byte)party;
            ClaimedBySlot = PartyInfo.Unclaimed;
            ClaimSequence = 0;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref CreatureId);
            serializer.SerializeValue(ref PartyId);
            serializer.SerializeValue(ref ClaimedBySlot);
            serializer.SerializeValue(ref ClaimSequence);
        }

        public bool Equals(RosterEntry other) =>
            CreatureId == other.CreatureId
            && PartyId == other.PartyId
            && ClaimedBySlot == other.ClaimedBySlot
            && ClaimSequence == other.ClaimSequence;

        public override bool Equals(object obj) => obj is RosterEntry other && Equals(other);

        public override int GetHashCode() =>
            unchecked((CreatureId * 397) ^ (PartyId << 8) ^ ClaimedBySlot);
    }

    /// <summary>Which party a player has chosen to fight for.</summary>
    public struct PartyChoice : INetworkSerializable, IEquatable<PartyChoice>
    {
        public byte Slot;
        public byte PartyId;

        public Party Party => (Party)PartyId;

        public PartyChoice(byte slot, Party party)
        {
            Slot = slot;
            PartyId = (byte)party;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref PartyId);
        }

        public bool Equals(PartyChoice other) => Slot == other.Slot && PartyId == other.PartyId;

        public override bool Equals(object obj) => obj is PartyChoice other && Equals(other);

        public override int GetHashCode() => (Slot << 8) | PartyId;
    }
}
