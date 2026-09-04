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
    /// Every field is fixed-size, which makes the whole message fixed-size: a client cannot make the
    /// host allocate by claiming a pool of four thousand. The pool is counts rather than a list of
    /// picks for exactly that reason, and because counts are what the rules actually use.
    /// </summary>
    public struct NetBuild : INetworkSerializable, IEquatable<NetBuild>
    {
        /// <summary>Most learned skills a build may carry. Generous next to any sane character.</summary>
        public const byte MaxLearned = 16;

        public byte Slot;
        public FixedString64Bytes Name;
        public int SpeciesId;
        public int ClassId;
        public int Level;
        public int Xp;

        public int Toughness;
        public int Dexterity;
        public int Strength;
        public int Skill;
        public int Vitality;
        public int Willpower;
        public int Endurance;

        public int Geo;
        public int Hydro;
        public int Pyro;
        public int Aero;
        public int Lux;
        public int Nyx;
        public int Arcana;

        public int WeaponId;
        public int ArmorId;
        public int OffhandId;

        public byte LearnedCount;
        public FixedList128Bytes<int> Learned;

        public static NetBuild From(byte slot, CharacterBuild build)
        {
            var a = build.Attributes;
            var p = build.StartingPool;

            var net = new NetBuild
            {
                Slot = slot,
                Name = new FixedString64Bytes(Clamp(build.Name)),
                SpeciesId = build.SpeciesId,
                ClassId = build.ClassId,
                Level = build.Level,
                Xp = build.Xp,
                Toughness = a.Toughness,
                Dexterity = a.Dexterity,
                Strength = a.Strength,
                Skill = a.Skill,
                Vitality = a.Vitality,
                Willpower = a.Willpower,
                Endurance = a.Endurance,
                Geo = p[Element.Geo],
                Hydro = p[Element.Hydro],
                Pyro = p[Element.Pyro],
                Aero = p[Element.Aero],
                Lux = p[Element.Lux],
                Nyx = p[Element.Nyx],
                Arcana = p[Element.Arcana],
                WeaponId = build.WeaponId,
                ArmorId = build.ArmorId,
                OffhandId = build.OffhandId,
                Learned = new FixedList128Bytes<int>()
            };

            var count = Math.Min(build.LearnedSkillIds.Count, MaxLearned);

            for (var i = 0; i < count; i++)
            {
                net.Learned.Add(build.LearnedSkillIds[i]);
            }

            net.LearnedCount = (byte)count;
            return net;
        }

        public CharacterBuild ToBuild()
        {
            var build = new CharacterBuild
            {
                Name = Name.ToString(),
                SpeciesId = SpeciesId,
                ClassId = ClassId,

                // A build that arrived without one is level one, not level zero: zero would give it
                // no pool budget and no skills, which reads as the character being broken rather
                // than as the field being missing.
                Level = Level < Progression.FirstLevel ? Progression.FirstLevel : Level,
                Xp = Xp,
                Attributes = new AttributeBlock(Toughness, Dexterity, Strength, Skill,
                    Vitality, Willpower, Endurance),
                StartingPool = new ElementCounts(Geo, Hydro, Pyro, Aero, Lux, Nyx, Arcana),
                WeaponId = WeaponId,
                ArmorId = ArmorId,
                OffhandId = OffhandId
            };

            var count = Math.Min((int)LearnedCount, Learned.Length);

            for (var i = 0; i < count; i++)
            {
                build.LearnedSkillIds.Add(Learned[i]);
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
            serializer.SerializeValue(ref SpeciesId);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref Xp);
            serializer.SerializeValue(ref ClassId);

            serializer.SerializeValue(ref Toughness);
            serializer.SerializeValue(ref Dexterity);
            serializer.SerializeValue(ref Strength);
            serializer.SerializeValue(ref Skill);
            serializer.SerializeValue(ref Vitality);
            serializer.SerializeValue(ref Willpower);
            serializer.SerializeValue(ref Endurance);

            serializer.SerializeValue(ref Geo);
            serializer.SerializeValue(ref Hydro);
            serializer.SerializeValue(ref Pyro);
            serializer.SerializeValue(ref Aero);
            serializer.SerializeValue(ref Lux);
            serializer.SerializeValue(ref Nyx);
            serializer.SerializeValue(ref Arcana);

            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref ArmorId);
            serializer.SerializeValue(ref OffhandId);
            serializer.SerializeValue(ref LearnedCount);

            // Element by element, because FixedList has no serialiser of its own -- and the count is
            // clamped on read before it drives the loop, so a client claiming two hundred learned
            // skills cannot make the host read past the message.
            if (LearnedCount > MaxLearned)
            {
                LearnedCount = MaxLearned;
            }

            if (serializer.IsReader)
            {
                Learned = new FixedList128Bytes<int>();

                for (var i = 0; i < LearnedCount; i++)
                {
                    var id = 0;
                    serializer.SerializeValue(ref id);
                    Learned.Add(id);
                }

                return;
            }

            for (var i = 0; i < LearnedCount && i < Learned.Length; i++)
            {
                var id = Learned[i];
                serializer.SerializeValue(ref id);
            }
        }

        public bool Equals(NetBuild other) =>
            Slot == other.Slot && Name.Equals(other.Name) && ClassId == other.ClassId
            && WeaponId == other.WeaponId && ArmorId == other.ArmorId
            && OffhandId == other.OffhandId && LearnedCount == other.LearnedCount
            && ToBuild().Attributes.Equals(other.ToBuild().Attributes)
            && ToBuild().StartingPool.Equals(other.ToBuild().StartingPool);

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
