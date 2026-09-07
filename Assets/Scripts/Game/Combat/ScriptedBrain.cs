using System;
using System.Collections.Generic;
using Dragoneye.Scenarios;
using Dragoneye.Sim;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// A brain that does what it was told, in the order it was told, one turn's worth at a time.
    ///
    /// The scenario runner's way into the fight, through the same door the game's own opponent
    /// uses: the fight asks it for a decision and carries the decision out, one per step. Nothing about the fight is special-cased for a script -- a scripted
    /// order can be refused exactly as a thought-up one can, and when it is, the turn ends.
    ///
    /// An actor's turns are counted by the round they are asked in, so a refusal that ends a
    /// turn early leaves the next turn's orders for the next turn rather than spending them on
    /// the same one. An actor handed to the game is answered by the game's brain instead.
    /// </summary>
    public sealed class ScriptedBrain : ICreatureBrain
    {
        sealed class Script
        {
            public Actor Actor;
            public int LastRound;
            public int Turn = -1;
            public int Next;
        }

        readonly Dictionary<uint, Script> m_Scripts = new Dictionary<uint, Script>();
        readonly ICreatureBrain m_Thinker = new BasicBrain();
        readonly Func<string, uint> m_IdOf;
        readonly Func<int> m_Round;

        /// <summary>An order was handed to the fight, whether or not the fight takes it.</summary>
        public event Action<uint, Order> Ordered;

        /// <param name="idOf">An actor's turn id from its key, so orders can name their targets.</param>
        /// <param name="round">The round the fight is on, which is how turns are told apart.</param>
        public ScriptedBrain(Func<string, uint> idOf, Func<int> round)
        {
            m_IdOf = idOf;
            m_Round = round;
        }

        public void Register(uint turnId, Actor actor) => m_Scripts[turnId] = new Script { Actor = actor };

        /// <summary>Whether every scripted actor has run out of orders. Thinking actors never do.</summary>
        public bool AllScriptsSpent
        {
            get
            {
                foreach (var script in m_Scripts.Values)
                {
                    if (!script.Actor.Thinks && HasWorkLeft(script))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Whether any actor was given an order at all.
        ///
        /// A scenario with no orders anywhere is not a spent script -- it is a board somebody
        /// wants to watch for a few rounds, which is what proves the turn order. Its script is
        /// never spent, so its round limit is what ends it.
        /// </summary>
        public bool HasOrders
        {
            get
            {
                foreach (var script in m_Scripts.Values)
                {
                    foreach (var turn in script.Actor.Turns)
                    {
                        if (turn.Count > 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>Whether any actor at all is run by the game's own brain.</summary>
        public bool AnyThinks
        {
            get
            {
                foreach (var script in m_Scripts.Values)
                {
                    if (script.Actor.Thinks)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public BrainDecision Decide(BrainView actor, IReadOnlyList<BrainView> others, IBoardQuery board)
        {
            if (!m_Scripts.TryGetValue(actor.Id, out var script))
            {
                return BrainDecision.Pass;
            }

            if (script.Actor.Thinks)
            {
                return m_Thinker.Decide(actor, others, board);
            }

            var round = m_Round();

            if (round != script.LastRound)
            {
                script.LastRound = round;
                script.Turn++;
                script.Next = 0;
            }

            if (script.Turn >= script.Actor.Turns.Count || script.Next >= script.Actor.Turns[script.Turn].Count)
            {
                return BrainDecision.Pass;
            }

            var order = script.Actor.Turns[script.Turn][script.Next++];
            Ordered?.Invoke(actor.Id, order);

            return order.Kind == OrderKind.Move
                ? BrainDecision.MoveTo(order.Destination)
                : BrainDecision.UseSkill(order.SkillId, m_IdOf(order.Target));
        }

        /// <summary>Orders left in turns not yet begun, or in the one under way.</summary>
        static bool HasWorkLeft(Script script)
        {
            var turns = script.Actor.Turns;

            if (script.Turn + 1 < turns.Count)
            {
                return true;
            }

            return script.Turn >= 0 && script.Turn < turns.Count && script.Next < turns[script.Turn].Count;
        }
    }
}
