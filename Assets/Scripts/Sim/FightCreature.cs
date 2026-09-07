using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Sim
{
    /// <summary>What a blow came to: what got through, what the armour held, what is left.</summary>
    public readonly struct DamageResult
    {
        public readonly int Landed;
        public readonly int Absorbed;
        public readonly int HpAfter;
        public readonly int ArmourAfter;
        public readonly bool Killed;

        public DamageResult(int landed, int absorbed, int hpAfter, int armourAfter, bool killed)
        {
            Landed = landed;
            Absorbed = absorbed;
            HpAfter = hpAfter;
            ArmourAfter = armourAfter;
            Killed = killed;
        }
    }

    /// <summary>
    /// One creature in a fight: what it is, where it stands, and what is left of it.
    ///
    /// A plain object. The Unity side mirrors it into replicated state for the views; a test
    /// reads it directly. What it *is* -- the numbers a species, class and kit come to -- is
    /// decided before it arrives here and never changes during the fight; what is left of it is
    /// changed only by the <see cref="Fight"/>, which is why every mutator is internal.
    /// </summary>
    public sealed class FightCreature
    {
        readonly List<int> m_Seen = new List<int>();

        public FightCreature(uint id, Party party, byte controllerSlot, int level,
            bool isPlayerCharacter, Cell cell, Facing facing, int maxHp, int maxArmour, Ap maxAp,
            int regen, Ap stepCost, int speed, bool advantage, IReadOnlyList<SkillSpec> skills,
            SkillSpec weapon, ElementCounts startingPool, int startingHp = 0,
            int startingArmour = -1)
        {
            Id = id;
            Party = party;
            ControllerSlot = controllerSlot;
            Level = level < Progression.FirstLevel ? Progression.FirstLevel : level;
            IsPlayerCharacter = isPlayerCharacter;
            Cell = cell;
            Facing = facing;
            MaxHp = maxHp < 1 ? 1 : maxHp;
            MaxArmour = maxArmour < 0 ? 0 : maxArmour;
            MaxAp = maxAp;
            Regen = regen < 0 ? 0 : regen;
            StepCost = stepCost;
            Speed = speed;
            HasAdvantage = advantage;
            Skills = skills ?? System.Array.Empty<SkillSpec>();
            Weapon = weapon;
            StartingPool = startingPool;

            // Whole unless the caller says otherwise. A fight begins from where the board is,
            // which is how a scenario can put a wounded creature on it and watch it be healed.
            Hp = startingHp > 0 && startingHp < MaxHp ? startingHp : MaxHp;
            Armour = startingArmour >= 0 && startingArmour < MaxArmour ? startingArmour : MaxArmour;
            Ap = MaxAp;
            OnBoard = true;
            Pool = new ElementPool(startingPool);
        }

        /// <summary>A stable per-fight identifier. On the network it is the object id.</summary>
        public uint Id { get; }

        public Party Party { get; }

        /// <summary><see cref="PartyInfo.Unclaimed"/> means the computer runs it.</summary>
        public byte ControllerSlot { get; }

        public bool IsComputerControlled => ControllerSlot == PartyInfo.Unclaimed;

        /// <summary>What killing this is worth.</summary>
        public int Level { get; }

        /// <summary>Whether this came out of the character creator, and so can keep what it earns.</summary>
        public bool IsPlayerCharacter { get; }

        public int MaxHp { get; }

        /// <summary>What the armour pool was when the fight began. It only goes down.</summary>
        public int MaxArmour { get; }

        public Ap MaxAp { get; }

        /// <summary>Health back at the start of every one of its turns.</summary>
        public int Regen { get; }

        /// <summary>What one tile costs this creature, which its speed decides.</summary>
        public Ap StepCost { get; }

        public int Speed { get; }

        /// <summary>Whether this creature answers a clash with the better of two elements.</summary>
        public bool HasAdvantage { get; }

        /// <summary>Everything it can do, its attributes already folded into the numbers.</summary>
        public IReadOnlyList<SkillSpec> Skills { get; }

        /// <summary>
        /// The attack it swings at somebody walking past, or null when it has nothing to swing.
        ///
        /// Decided by whoever built the creature: a built character swings with its weapon and
        /// with nothing else, a premade with the first attack it was authored holding.
        /// </summary>
        public SkillSpec Weapon { get; }

        public ElementCounts StartingPool { get; }

        public ElementPool Pool { get; }

        /// <summary>Skills this creature has been watched using, in the order it first used them.</summary>
        public IReadOnlyList<int> Seen => m_Seen;

        /// <summary>Whether everybody has watched this creature use that skill.</summary>
        public bool HasShown(int skillId) => m_Seen.Contains(skillId);

        public Cell Cell { get; private set; }

        public Facing Facing { get; private set; }

        /// <summary>Whether it still holds a cell. False once it has fallen.</summary>
        public bool OnBoard { get; private set; }

        public int Hp { get; private set; }

        public int Armour { get; private set; }

        public Ap Ap { get; private set; }

        public bool IsAlive => CombatRules.IsAlive(Hp);

        public bool TryGetSkill(int id, out SkillSpec skill)
        {
            foreach (var candidate in Skills)
            {
                if (candidate.Id == id)
                {
                    skill = candidate;
                    return true;
                }
            }

            skill = null;
            return false;
        }

        // ---------- what the fight may do to it ----------

        internal bool SpendAp(Ap amount)
        {
            if (amount.Units < 0 || Ap.Units < amount.Units)
            {
                return false;
            }

            Ap = Ap.FromUnits(Ap.Units - amount.Units);
            return true;
        }

        internal void RestoreAp(Ap amount) =>
            Ap = Ap.FromUnits(SkillRules.Apply(
                new SkillEffect(SkillEffectKind.RestoreAp, amount.Units), Ap.Units, MaxAp.Units));

        internal void RefillAp() => Ap = MaxAp;

        /// <returns>What actually came back, which is less than asked for at full health.</returns>
        internal int Heal(int amount)
        {
            if (!IsAlive)
            {
                return 0;
            }

            var before = Hp;
            Hp = SkillRules.Apply(new SkillEffect(SkillEffectKind.Heal, amount), before, MaxHp);
            return Hp - before;
        }

        /// <summary>
        /// Damage: armour first, health second.
        ///
        /// Both here rather than in the caller, so what lands and what is written down cannot
        /// disagree: one subtraction, one set of numbers, handed back for the record.
        /// </summary>
        internal DamageResult ApplyDamage(int damage)
        {
            if (!IsAlive)
            {
                return new DamageResult(0, 0, Hp, Armour, false);
            }

            var through = CombatRules.Absorb(damage, Armour, out var armourLeft);
            var absorbed = Armour - armourLeft;

            Armour = armourLeft;
            Hp = CombatRules.Damaged(Hp, through);

            return new DamageResult(through, absorbed, Hp, Armour, !IsAlive);
        }

        internal void Face(Facing facing) => Facing = facing;

        internal void SetCell(Cell cell) => Cell = cell;

        /// <summary>Gives up the cell. The creature stays in the fight's list; the board forgets it.</summary>
        internal void LeaveBoard() => OnBoard = false;

        /// <summary>
        /// Records that this creature has now been seen using a skill.
        ///
        /// Called when the skill's effect lands rather than when it is asked for, which for a
        /// contested skill is after the defender has committed. Recording it at the moment of use
        /// would publish the attacking skill -- and therefore its element -- into the very window
        /// DE-005 exists to keep empty.
        /// </summary>
        internal void RecordUse(int skillId)
        {
            if (!m_Seen.Contains(skillId))
            {
                m_Seen.Add(skillId);
            }
        }
    }
}
