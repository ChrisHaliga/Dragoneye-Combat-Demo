using Dragoneye.Hex;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game.Combat
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>A wall change on the wire: where, and what stands there now.</summary>
    public struct NetWall : INetworkSerializable
    {
        public int Q;
        public int R;
        public byte Index;
        public bool IsRay;
        public byte Flags;
        public ushort Integrity;

        public NetWall(WallSegment segment, Wall wall)
        {
            Q = segment.Tile.Q;
            R = segment.Tile.R;
            Index = (byte)segment.Index;
            IsRay = segment.IsRay;
            Flags = (byte)wall.Flags;
            Integrity = wall.Integrity;
        }

        public WallSegment Segment =>
            IsRay ? WallSegment.Ray(new Hex(Q, R), Index) : WallSegment.HalfEdge(new Hex(Q, R), Index);

        public Wall Wall => new Wall((WallFlags)Flags, Integrity);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Q);
            serializer.SerializeValue(ref R);
            serializer.SerializeValue(ref Index);
            serializer.SerializeValue(ref IsRay);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref Integrity);
        }
    }

    /// <summary>
    /// Tells every machine when a wall changes.
    ///
    /// The map is not replicated: each peer builds its own from the same definition, and that is
    /// right for a layout that is fixed for a fight. A wall that comes down mid-fight is the one
    /// thing about the map that is not fixed, so it travels here -- the fight changes its own
    /// map, the server says so, every client changes its copy the same way, and the grids agree
    /// again. The server's map is the fight's, already changed, which is why the message goes to
    /// everybody but the server.
    ///
    /// This is the fight's board and nobody sees it. The board on screen is changed by the
    /// playback when it reaches the <see cref="CombatEventKind.WallChanged"/> event, which is the
    /// same change arriving at the pace everything else about the fight is watched at.
    ///
    /// Beside the turn state, and with the same lifetime: a change to the board of one fight
    /// belongs to that fight.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class WallCommands : NetworkBehaviour
    {
        /// <summary>The postbox for the match in progress, or null outside one.</summary>
        public static WallCommands Current { get; private set; }

        public override void OnNetworkSpawn() => Current = this;

        public override void OnNetworkDespawn()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>Server only. Changes a wall on every client. The server's is the fight's, already changed.</summary>
        public void ServerSet(WallSegment segment, Wall wall)
        {
            if (IsServer)
            {
                ApplyRpc(new NetWall(segment, wall));
            }
        }

        [Rpc(SendTo.NotServer)]
        void ApplyRpc(NetWall change)
        {
            var arena = ArenaContext.Current;
            var map = arena != null && arena.Map != null ? arena.Map.Map : null;

            if (map == null)
            {
                Debug.LogError("A wall changed with no map to change it on.", this);
                return;
            }

            map.SetWall(change.Segment, change.Wall);
        }
    }
}
