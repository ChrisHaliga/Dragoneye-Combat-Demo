using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>One element answering another well. Everything unlisted is even.</summary>
    [System.Serializable]
    public struct ElementBeats
    {
        public Element Winner;
        public Element Loser;
    }

    /// <summary>
    /// Which element answers which.
    ///
    /// Authored as a list of wins rather than as a grid of outcomes. Seven elements make
    /// forty-nine cells, of which most are ties and all of which have to stay consistent with their
    /// mirror -- and a grid a designer edits by hand is a grid that will one day say Pyro beats
    /// Hydro and Hydro beats Pyro. Here a matchup is stated once, in the direction it is true, and
    /// its opposite follows.
    ///
    /// The shipped table is three tiers in a ring. The four common elements answer Arcana, Arcana
    /// answers Lux and Nyx, and Lux and Nyx answer the common four -- so nothing is simply best,
    /// and the price of an element buys reach rather than power. Inside the commons there is a
    /// second ring: Pyro over Aero over Geo over Hydro over Pyro, with the two facing pairs even.
    ///
    /// The whole thing is content, so all of that can be argued with in the inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Element Matchups", fileName = "ElementMatchups")]
    public sealed class ElementMatchupTable : ScriptableObject, IElementMatchup
    {
        [SerializeField, Tooltip("Each entry says one element answers another. State a matchup once "
             + "in the direction it is true; the reverse follows, and anything unlisted is even.")]
        List<ElementBeats> m_Beats = new List<ElementBeats>();

        // Built from the list on first use: attacker * Count + defender.
        sbyte[] m_Grid;

        public ClashOutcome Compare(Element attacker, Element defender)
        {
            Index();

            var a = ElementInfo.IndexOf(attacker);
            var d = ElementInfo.IndexOf(defender);

            if (a < 0 || d < 0)
            {
                // Reachable: an element crosses the network as an integer, and casting to an enum
                // is not a checked conversion.
                return ClashOutcome.Tie;
            }

            return (ClashOutcome)m_Grid[(a * ElementInfo.Count) + d];
        }

        void Index()
        {
            if (m_Grid != null)
            {
                return;
            }

            m_Grid = new sbyte[ElementInfo.Count * ElementInfo.Count];

            foreach (var beats in m_Beats)
            {
                var winner = ElementInfo.IndexOf(beats.Winner);
                var loser = ElementInfo.IndexOf(beats.Loser);

                if (winner < 0 || loser < 0 || winner == loser)
                {
                    Debug.LogWarning($"{name} lists a matchup that is not between two elements; "
                        + "ignoring it.", this);
                    continue;
                }

                var forward = (winner * ElementInfo.Count) + loser;
                var back = (loser * ElementInfo.Count) + winner;

                // A pair stated in both directions cannot be honoured either way round, and
                // silently keeping the last one read would make the table depend on list order.
                if (m_Grid[forward] != 0)
                {
                    Debug.LogError($"{name} states {beats.Winner} against {beats.Loser} twice, "
                        + "or in both directions. Leaving that pair even.", this);
                    m_Grid[forward] = 0;
                    m_Grid[back] = 0;
                    continue;
                }

                m_Grid[forward] = (sbyte)ClashOutcome.AttackerWins;
                m_Grid[back] = (sbyte)ClashOutcome.DefenderWins;
            }
        }

        void OnValidate() => m_Grid = null;
    }

    /// <summary>
    /// Where a clash finds out which element answered better.
    ///
    /// A seam rather than a reference, for the same reason <see cref="Portraits"/> is one: a clash
    /// is resolved from a component on a spawned prefab, which cannot carry a serialised pointer to
    /// a content asset.
    ///
    /// Filled by <see cref="ContentCatalog"/> when it builds. A clash with nothing here treats
    /// every pair as even, which is wrong but is at least wrong the same way for everybody.
    /// </summary>
    public static class ElementMatchups
    {
        public static ElementMatchupTable Current { get; set; }

        /// <summary>Never null, so a resolver does not have to decide what to do without one.</summary>
        public static IElementMatchup Table => Current != null ? Current : EvenTable.Instance;

        /// <summary>Every pair even. What a project with no table authored yet behaves as.</summary>
        sealed class EvenTable : IElementMatchup
        {
            public static readonly EvenTable Instance = new EvenTable();

            public ClashOutcome Compare(Element attacker, Element defender) => ClashOutcome.Tie;
        }
    }
}
