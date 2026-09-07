using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// A creature as far as the watcher has been shown.
    ///
    /// Its vitals, where it stands, which way it is turned, and the public half of its elements:
    /// what it has been seen to spend, in what order, and how much of it has been proven. The
    /// private half -- what it still holds -- is not here, because nobody but its controller may
    /// know it and its controller reads it live.
    /// </summary>
    public sealed class PresentedCreature
    {
        public uint Id;
        public int Hp;
        public int Armour;
        public int ApUnits;
        public Cell Cell;
        public Facing Facing;
        public bool IsAlive = true;

        /// <summary>Spent and not yet taken back, oldest first. What Take a Breath draws from.</summary>
        public readonly List<Element> Outstanding = new List<Element>();

        /// <summary>Seen spent, cumulatively. Never falls.</summary>
        public ElementCounts Revealed;

        /// <summary>Proven held: the most of an element ever outstanding at once.</summary>
        public ElementCounts Identified;

        /// <summary>Skills this creature has been watched using, in the order first seen.</summary>
        public readonly List<int> Seen = new List<int>();

        public Ap Ap => Ap.FromUnits(ApUnits);

        /// <summary>
        /// Spends, as the watcher saw it: into the outstanding queue, onto the record, and proven
        /// to however many are out together. The same rule <see cref="ElementLedger.TrySpend"/>
        /// applies to the private pool, kept here for the half of it everybody can see.
        /// </summary>
        public void Spend(Element element)
        {
            Outstanding.Add(element);
            Revealed = Revealed.Plus(element, 1);
            Prove(element, Count(element));
        }

        /// <summary>
        /// Shown and not spent: a defence that won its clash keeps what it put up, but nobody
        /// unsees it. Proven to what is out plus what was shown; the queue is left alone.
        /// </summary>
        public void Show(Element element, int shown)
        {
            Revealed = Revealed.Plus(element, shown);
            Prove(element, Count(element) + shown);
        }

        /// <summary>Taken back, oldest first. The record and the proof stand.</summary>
        public void Return(Element element)
        {
            var index = Outstanding.IndexOf(element);

            if (index >= 0)
            {
                Outstanding.RemoveAt(index);
            }
        }

        public void SeeSkill(int skillId)
        {
            if (!Seen.Contains(skillId))
            {
                Seen.Add(skillId);
            }
        }

        void Prove(Element element, int held)
        {
            if (held > Identified[element])
            {
                Identified = Identified.With(element, held);
            }
        }

        int Count(Element element)
        {
            var count = 0;

            foreach (var spent in Outstanding)
            {
                if (spent == element)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// The fight as far as it has been shown.
    ///
    /// The simulation is somewhere ahead of this -- possibly several turns ahead -- and nothing
    /// that draws the fight reads it there. Every view reads here, and here only changes when
    /// the playback applies the next event. So the health bar, the turn bar, the token and the log
    /// agree with one another and with what the player has actually watched happen, whatever the
    /// server has got on with in the meantime.
    ///
    /// Pure: events in, state out, no clock and no scene. That is what lets it be checked outside
    /// the engine against every kind of event the fight sends.
    /// </summary>
    public sealed class PresentedFight
    {
        readonly Dictionary<uint, PresentedCreature> m_Creatures = new Dictionary<uint, PresentedCreature>();
        readonly List<uint> m_Order = new List<uint>();

        /// <summary>Whether the opening has been shown. Nothing here means anything before it.</summary>
        public bool Began { get; private set; }

        public int Round { get; private set; }

        /// <summary>Whose turn it is, as shown, or zero.</summary>
        public uint ActiveId { get; private set; }

        public bool IsOver { get; private set; }

        public bool HasWinner { get; private set; }

        public Party Winner { get; private set; }

        /// <summary>Initiative, fastest first. The fallen leave it as they are shown falling.</summary>
        public IReadOnlyList<uint> Order => m_Order;

        /// <summary>The creature, or null for one the fight has not introduced.</summary>
        public PresentedCreature Of(uint id) => m_Creatures.TryGetValue(id, out var creature) ? creature : null;

        public bool IsAlive(uint id) => Of(id)?.IsAlive ?? false;

        public bool IsActive(uint id) => Began && !IsOver && id != 0 && id == ActiveId;

        /// <summary>Forgets everything. For an arena being reused for another fight.</summary>
        public void Reset()
        {
            m_Creatures.Clear();
            m_Order.Clear();
            Began = false;
            Round = 0;
            ActiveId = 0;
            IsOver = false;
            HasWinner = false;
        }

        /// <summary>Moves the shown fight on by one event.</summary>
        public void Apply(CombatEvent e)
        {
            if (e == null)
            {
                return;
            }

            switch (e.Kind)
            {
                case CombatEventKind.Began:
                    Reset();
                    Began = true;
                    Round = e.Round;
                    m_Order.AddRange(e.Order);

                    foreach (var start in e.Starts)
                    {
                        m_Creatures[start.Id] = new PresentedCreature
                        {
                            Id = start.Id,
                            Cell = start.Cell,
                            Facing = Facing.Of(start.Facing),
                            Hp = start.Hp,
                            Armour = start.Armour,
                            ApUnits = start.ApUnits,
                            IsAlive = CombatRules.IsAlive(start.Hp)
                        };
                    }

                    break;

                case CombatEventKind.RoundBegan:
                    Round = e.Round;
                    break;

                case CombatEventKind.TurnBegan:
                    Round = e.Round;
                    ActiveId = e.Actor;
                    With(e.Actor, c => c.ApUnits = e.ApUnits);
                    break;

                case CombatEventKind.Recovered:
                case CombatEventKind.Healed:
                    With(e.Actor, c => c.Hp = e.Hp);
                    break;

                case CombatEventKind.Moved:
                    With(e.Actor, c =>
                    {
                        if (e.Path.Count > 0)
                        {
                            c.Cell = e.Path[e.Path.Count - 1];
                        }

                        Turn(c, e.Facing);
                        c.ApUnits = e.ApUnits;
                    });
                    break;

                case CombatEventKind.Faced:
                    With(e.Actor, c => Turn(c, e.Facing));
                    break;

                case CombatEventKind.Swung:
                    With(e.Actor, c =>
                    {
                        Turn(c, e.Facing);
                        c.ApUnits = e.ApUnits;
                    });
                    break;

                case CombatEventKind.Shot:
                    With(e.Actor, c =>
                    {
                        Turn(c, e.Facing);
                        c.ApUnits = e.ApUnits;

                        // A miss reveals the arrow. A hit is revealed by the clash it opened.
                        if (!e.Landed)
                        {
                            SpendAll(c, e.Elements);
                            c.SeeSkill(e.Skill);
                        }
                    });
                    break;

                case CombatEventKind.Acted:
                    With(e.Actor, c =>
                    {
                        Turn(c, e.Facing);
                        c.ApUnits = e.ApUnits;
                        SpendAll(c, e.Elements);

                        foreach (var element in e.Returned)
                        {
                            c.Return(element);
                        }

                        c.SeeSkill(e.Skill);
                    });
                    break;

                case CombatEventKind.ClashResolved:
                    With(e.Actor, c =>
                    {
                        SpendAll(c, e.Elements);
                        c.SeeSkill(e.Skill);
                    });

                    With(e.Target, c =>
                    {
                        if (ClashRules.Refunds(e.Outcome))
                        {
                            foreach (var element in ElementInfo.All)
                            {
                                var shown = CountOf(e.Answer, element);

                                if (shown > 0)
                                {
                                    c.Show(element, shown);
                                }
                            }
                        }
                        else
                        {
                            SpendAll(c, e.Answer);
                        }
                    });
                    break;

                case CombatEventKind.Damaged:
                    With(e.Target, c =>
                    {
                        c.Hp = e.Hp;
                        c.Armour = e.Armour;
                        c.IsAlive = CombatRules.IsAlive(e.Hp);
                    });
                    break;

                case CombatEventKind.ApRestored:
                    With(e.Actor, c => c.ApUnits = e.ApUnits);
                    break;

                case CombatEventKind.Fell:
                    With(e.Actor, c =>
                    {
                        c.IsAlive = false;
                        c.Hp = 0;
                    });
                    m_Order.Remove(e.Actor);
                    break;

                case CombatEventKind.Ended:
                    IsOver = true;
                    HasWinner = e.HasWinner;
                    Winner = e.Winner;
                    ActiveId = 0;
                    break;

                case CombatEventKind.HeldBack:
                case CombatEventKind.WallChanged:
                    break;
            }
        }

        static void SpendAll(PresentedCreature creature, IReadOnlyList<Element> elements)
        {
            foreach (var element in elements)
            {
                creature.Spend(element);
            }
        }

        static int CountOf(IReadOnlyList<Element> elements, Element element)
        {
            var count = 0;

            foreach (var candidate in elements)
            {
                if (candidate == element)
                {
                    count++;
                }
            }

            return count;
        }

        static void Turn(PresentedCreature creature, int facing)
        {
            if (facing != CombatEvent.NoFacing)
            {
                creature.Facing = Facing.Of(facing);
            }
        }

        void With(uint id, Action<PresentedCreature> change)
        {
            if (m_Creatures.TryGetValue(id, out var creature))
            {
                change(creature);
            }
        }
    }
}
