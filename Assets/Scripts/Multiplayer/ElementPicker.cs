using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Seven rows, one per element, each with a gem and a pair of steppers.
    ///
    /// Extracted because two screens spend the same budget on the same seven things: the creator
    /// buys a starting pool, and a level-up spends the point a level is worth. Two copies would
    /// eventually disagree about which of them can still be afforded, which is exactly the kind of
    /// disagreement that reaches a player as a button that does nothing.
    ///
    /// It owns the rows and nothing else. What the pool currently is, and what happens when it
    /// changes, belong to the screen -- this asks for both.
    /// </summary>
    public sealed class ElementPicker
    {
        readonly Dictionary<Element, VisualElement> m_Rows = new Dictionary<Element, VisualElement>();
        readonly Dictionary<Element, Label> m_Values = new Dictionary<Element, Label>();
        readonly Dictionary<Element, Button> m_Minus = new Dictionary<Element, Button>();
        readonly Dictionary<Element, Button> m_Plus = new Dictionary<Element, Button>();

        /// <summary>Builds the rows into a container the caller owns.</summary>
        /// <param name="adjust">Called with the element and either +1 or -1.</param>
        public ElementPicker(VisualElement into, Action<Element, int> adjust)
        {
            into.Clear();
            m_Rows.Clear();
            m_Values.Clear();
            m_Minus.Clear();
            m_Plus.Clear();

            foreach (var element in ElementInfo.All)
            {
                into.Add(Row(element, adjust));
            }
        }

        /// <summary>
        /// One element, its rune lit by how much of it is held.
        ///
        /// A rune rather than a coloured word: a pool is a hand of resources a player counts at a
        /// glance mid-fight, and seven colour-coded labels read as a legend instead.
        /// </summary>
        VisualElement Row(Element element, Action<Element, int> adjust)
        {
            var row = new VisualElement();
            row.AddToClassList("element-row");

            // What it costs is a tooltip rather than a column, because the price never changes and
            // seven fixed numbers down the middle of the column would read as part of the pool.
            var cost = ElementPricing.CostOf(element);
            row.tooltip = $"{ElementInfo.NameOf(element)} costs {cost} "
                + (cost == 1 ? "point" : "points") + " of the pool budget";

            var gem = new VisualElement();
            gem.AddToClassList("element-row__gem");
            CharacterSheet.PaintElement(gem, element);
            row.Add(gem);

            var label = new Label(ElementInfo.ShortNameOf(element));
            label.AddToClassList("element-row__name");
            row.Add(label);

            var minus = MenuControls.StepButton("-", () => adjust(element, -1));
            var value = new Label();
            value.AddToClassList("element-row__value");
            var plus = MenuControls.StepButton("+", () => adjust(element, +1));

            row.Add(minus);
            row.Add(value);
            row.Add(plus);

            m_Rows[element] = row;
            m_Values[element] = value;
            m_Minus[element] = minus;
            m_Plus[element] = plus;

            return row;
        }

        /// <summary>
        /// Repaints against a pool and a budget: the values, whether a row is lit, and which steps
        /// are still affordable.
        ///
        /// Dimming an element the character does not hold is what makes the pool read as a hand
        /// rather than as seven fields that all happen to be zero. Affordability is per element,
        /// because the last point left buys some of these and not others.
        /// </summary>
        /// <param name="floor">
        /// The pool the player may not go below -- what they already owned before this screen. Zero
        /// in the creator, where everything is still being chosen.
        /// </param>
        public void Refresh(ElementCounts pool, int budget, ElementCounts floor = default)
        {
            foreach (var element in ElementInfo.All)
            {
                var held = pool[element];

                if (m_Values.TryGetValue(element, out var label))
                {
                    label.text = held.ToString();
                }

                if (m_Rows.TryGetValue(element, out var row))
                {
                    row.EnableInClassList("element-row--empty", held == 0);
                }

                if (m_Minus.TryGetValue(element, out var minus))
                {
                    minus.SetEnabled(held > floor[element]);
                }

                if (m_Plus.TryGetValue(element, out var plus))
                {
                    plus.SetEnabled(ElementPricing.CanAdd(pool, element, budget));
                }
            }
        }
    }
}
