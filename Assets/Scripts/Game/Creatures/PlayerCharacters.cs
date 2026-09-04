using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// A character build in a form netcode can replicate.
    ///
    /// Ids and numbers only. Everything a build refers to -- the class, the items, the skills they
    /// grant -- is authored content every peer already has, so what crosses the wire is the choices,
    /// not the content. The portrait deliberately does not travel at all.
    ///
    /// The pool is a fixed-size inline array rather than a list. NGO needs an unmanaged type, and a
    /// bounded array makes the message a fixed size -- a client cannot make the host allocate by
    /// claiming a level of four thousand.
    /// </summary>
    public struct NetBuild : INetworkSerializable, IEquatable<NetBuild>
    {
        /// <summary>Most element picks a build may carry. Generous next to any sane level.</summary>
        public const byte MaxPicks = 12;

        public byte Slot;
        public FixedString64Bytes Name;
        public int ClassId;
        public int Vitality;
        public int Speed;
        public int Power;
        public int Focus;
        public int WeaponId;
        public int ArmorId;
        public int OffhandId;
        public byte PickCount;

        // Fixed inline rather than an array field: NGO serialises what it is told to, and twelve
        // named bytes cost less than a length-prefixed collection per creature per match.
        public FixedList32Bytes<byte> Picks;

        public static NetBuild From(byte slot, CharacterBuild build)
        {
            var net = new NetBuild
            {
                Slot = slot,
                Name = new FixedString64Bytes(Clamp(build.Name)),
                ClassId = build.ClassId,
                Vitality = build.Allocation.Vitality,
                Speed = build.Allocation.Speed,
                Power = build.Allocation.Power,
                Focus = build.Allocation.Focus,
                WeaponId = build.WeaponId,
                ArmorId = build.ArmorId,
                OffhandId = build.OffhandId,
                Picks = new FixedList32Bytes<byte>()
            };

            var count = Math.Min(build.ElementPicks.Count, MaxPicks);

            for (var i = 0; i < count; i++)
            {
                net.Picks.Add((byte)build.ElementPicks[i]);
            }

            net.PickCount = (byte)count;
            return net;
        }

        public CharacterBuild ToBuild()
        {
            var build = new CharacterBuild
            {
                Name = Name.ToString(),
                ClassId = ClassId,
                Allocation = new StatBlock(Vitality, Speed, Power, Focus),
                WeaponId = WeaponId,
                ArmorId = ArmorId,
                OffhandId = OffhandId
            };

            var count = Math.Min((int)PickCount, Picks.Length);

            for (var i = 0; i < count; i++)
            {
                build.ElementPicks.Add((Element)Picks[i]);
            }

            return build;
        }

        static string Clamp(string name)
        {
            var trimmed = (name ?? string.Empty).Trim();

            return trimmed.Length <= CharacterBuild.MaxNameLength
                ? trimmed
                : trimmed.Substring(0, CharacterBuild.MaxNameLength);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref ClassId);
            serializer.SerializeValue(ref Vitality);
            serializer.SerializeValue(ref Speed);
            serializer.SerializeValue(ref Power);
            serializer.SerializeValue(ref Focus);
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref ArmorId);
            serializer.SerializeValue(ref OffhandId);
            serializer.SerializeValue(ref PickCount);

            // Element by element, because FixedList has no serialiser of its own -- and the count is
            // clamped on read before it drives the loop, so a client claiming two hundred picks
            // cannot make the host read past the message.
            if (PickCount > MaxPicks)
            {
                PickCount = MaxPicks;
            }

            if (serializer.IsReader)
            {
                Picks = new FixedList32Bytes<byte>();

                for (var i = 0; i < PickCount; i++)
                {
                    byte pick = 0;
                    serializer.SerializeValue(ref pick);
                    Picks.Add(pick);
                }

                return;
            }

            for (var i = 0; i < PickCount && i < Picks.Length; i++)
            {
                var pick = Picks[i];
                serializer.SerializeValue(ref pick);
            }
        }

        public bool Equals(NetBuild other) =>
            Slot == other.Slot && Name.Equals(other.Name) && ClassId == other.ClassId
            && Vitality == other.Vitality && Speed == other.Speed && Power == other.Power
            && Focus == other.Focus && WeaponId == other.WeaponId && ArmorId == other.ArmorId
            && OffhandId == other.OffhandId && PickCount == other.PickCount;

        public override bool Equals(object obj) => obj is NetBuild other && Equals(other);

        public override int GetHashCode() => Slot ^ ClassId;
    }

    /// <summary>
    /// The characters players brought to this match, one per slot.
    ///
    /// A player's character is theirs: it is submitted by them, permanently claimed by them, and
    /// cannot be taken by anyone else. What the host controls is which side it fights on, which is
    /// the slot's party choice and already exists.
    ///
    /// Every build is validated where it is accepted, not only in the creation screen -- DE-004 asks
    /// for that explicitly, and it is the only check that counts, because this arrives from a client.
    ///
    /// This object also owns the content catalog for the match. It lives from the moment the server
    /// starts until the match ends, which is a longer and simpler life than the arena's.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class PlayerCharacters : NetworkBehaviour
    {
        [SerializeField, Tooltip("Classes, equipment and skills. Also handed to the skill seam.")]
        ContentCatalog m_Content;

        readonly NetworkList<NetBuild> m_Builds = new NetworkList<NetBuild>();
        readonly List<NetBuild> m_View = new List<NetBuild>();
        readonly List<BuildFault> m_Faults = new List<BuildFault>();

        /// <summary>The characters for the match in progress, or null outside one.</summary>
        public static PlayerCharacters Current { get; private set; }

        /// <summary>Raised on every peer when a character is submitted or replaced.</summary>
        public event Action Changed;

        /// <summary>Every submitted character, in submission order.</summary>
        public IReadOnlyList<NetBuild> All => m_View;

        public ContentCatalog Content => m_Content;

        public override void OnNetworkSpawn()
        {
            if (Current != null && Current != this)
            {
                Debug.LogError("A second PlayerCharacters spawned; the match expects one.", this);
            }
            else
            {
                Current = this;
            }

            // Owned here rather than by the arena: this object exists from server start to match
            // end, so the seam is never briefly empty between the lobby and the board.
            SkillCatalog.Current = m_Content;

            m_Builds.OnListChanged += OnListChanged;
            RebuildView();
        }

        public override void OnNetworkDespawn()
        {
            m_Builds.OnListChanged -= OnListChanged;

            if (Current != this)
            {
                return;
            }

            Current = null;
            SkillCatalog.Current = null;
        }

        void OnListChanged(NetworkListEvent<NetBuild> _) => RebuildView();

        /// <summary>
        /// Mirrors the NetworkList into a plain list.
        ///
        /// NetworkList cannot be handed to pure code or iterated cheaply by a view, and the lobby
        /// redraws whenever anything changes.
        /// </summary>
        void RebuildView()
        {
            m_View.Clear();

            for (var i = 0; i < m_Builds.Count; i++)
            {
                m_View.Add(m_Builds[i]);
            }

            Changed?.Invoke();
        }

        public bool TryGet(byte slot, out NetBuild build)
        {
            foreach (var candidate in m_View)
            {
                if (candidate.Slot == slot)
                {
                    build = candidate;
                    return true;
                }
            }

            build = default;
            return false;
        }

        /// <summary>The build for a slot as the rules see it, or null if that slot brought nobody.</summary>
        public CharacterBuild BuildFor(byte slot) =>
            TryGet(slot, out var net) ? net.ToBuild() : null;

        /// <summary>The resolved loadout for a slot, or null.</summary>
        public Loadout LoadoutFor(byte slot)
        {
            var build = BuildFor(slot);
            return build != null && m_Content != null
                ? LoadoutResolver.Resolve(build, m_Content)
                : null;
        }

        /// <summary>Client-side entry point. Offers this player's character to the host.</summary>
        public void Submit(CharacterBuild build)
        {
            if (build != null && LocalPlayer.TryGetSlot(out var slot))
            {
                SubmitRpc(NetBuild.From(slot, build));
            }
        }

        /// <summary>
        /// A player offering their character.
        ///
        /// The slot is taken from the sender rather than the payload, so a client can only ever
        /// submit as itself. The build is then validated with the same rules the creation screen
        /// used -- a client that skipped or patched that screen is refused here.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SubmitRpc(NetBuild submitted, RpcParams rpc = default)
        {
            if (m_Content == null)
            {
                Debug.LogError("No content catalog; character submissions cannot be validated.", this);
                return;
            }

            var roster = PlayerRoster.Current;

            if (roster == null || !roster.TryGet(rpc.Receive.SenderClientId, out var entry)
                || entry.Slot < 0 || entry.Slot > byte.MaxValue)
            {
                return;
            }

            // Sender-derived. Whatever slot the payload claimed is discarded.
            submitted.Slot = (byte)entry.Slot;

            BuildValidator.Validate(submitted.ToBuild(), m_Content, m_Faults);

            if (m_Faults.Count > 0)
            {
                Debug.LogWarning($"Refused a character from slot {submitted.Slot}: "
                    + $"{m_Faults[0].Problem}.", this);
                return;
            }

            Replace(submitted);
        }

        /// <summary>Server only. One character per slot, so re-submitting swaps rather than adds.</summary>
        void Replace(NetBuild build)
        {
            for (var i = 0; i < m_Builds.Count; i++)
            {
                if (m_Builds[i].Slot == build.Slot)
                {
                    m_Builds[i] = build;
                    return;
                }
            }

            m_Builds.Add(build);
        }

        /// <summary>
        /// Server only. Drops a slot's character, for a player who left.
        /// </summary>
        public void ServerRemove(byte slot)
        {
            if (!IsServer)
            {
                return;
            }

            for (var i = 0; i < m_Builds.Count; i++)
            {
                if (m_Builds[i].Slot == slot)
                {
                    m_Builds.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
