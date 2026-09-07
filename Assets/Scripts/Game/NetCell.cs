using System;
using Dragoneye.Hex;
using Unity.Netcode;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// A position on the wire: the tile, and which area of it.
    ///
    /// The area is one byte and it is part of every position update, so a client and a server
    /// built from different revisions of this struct would read each other's positions wrongly.
    /// The struct and its serialiser change together, always.
    /// </summary>
    public struct NetCell : INetworkSerializable, IEquatable<NetCell>
    {
        public int Q;
        public int R;
        public byte Area;

        public NetCell(Cell cell)
        {
            Q = cell.Tile.Q;
            R = cell.Tile.R;
            Area = cell.Area;
        }

        public Cell ToCell() => new Cell(new Hex(Q, R), Area);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Q);
            serializer.SerializeValue(ref R);
            serializer.SerializeValue(ref Area);
        }

        public bool Equals(NetCell other) => Q == other.Q && R == other.R && Area == other.Area;

        public override bool Equals(object obj) => obj is NetCell other && Equals(other);

        public override int GetHashCode() => unchecked(((Q * 397) ^ R) * 31 + Area);

        public override string ToString() => $"NetCell({Q}, {R}/{Area})";
    }
}
