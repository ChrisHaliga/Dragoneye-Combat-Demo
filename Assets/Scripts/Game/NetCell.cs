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
    /// A hex coordinate in a form netcode can replicate.
    ///
    /// <see cref="Hex"/> is unmanaged and equatable, which is necessary but not sufficient: NGO also
    /// needs a serialiser. Giving it one by making <see cref="Hex"/> implement
    /// <c>INetworkSerializable</c> would force the hex assembly to reference Netcode and destroy its
    /// empty-references invariant -- the most valuable property that module has. So the netcode
    /// boundary stays here, in the game assembly, and costs a two-field DTO.
    /// </summary>
    public struct NetCell : INetworkSerializable, IEquatable<NetCell>
    {
        public int Q;
        public int R;

        public NetCell(Hex hex)
        {
            Q = hex.Q;
            R = hex.R;
        }

        public Hex ToHex() => new Hex(Q, R);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Q);
            serializer.SerializeValue(ref R);
        }

        public bool Equals(NetCell other) => Q == other.Q && R == other.R;

        public override bool Equals(object obj) => obj is NetCell other && Equals(other);

        public override int GetHashCode() => unchecked((Q * 397) ^ R);

        public override string ToString() => $"NetCell({Q}, {R})";
    }
}
