using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The whole matchup table, on one strip.
    ///
    /// The tooltips answer "what does this rune beat" wherever a rune appears, which is the right
    /// shape for a question asked mid-fight. This answers a different one -- "how does any of this
    /// fit together" -- and that is a question people ask once, before they play, while they are
    /// deciding what to buy. So it lives on the pre-match screen and not in the arena, where a
    /// player already has a decision in front of them and does not want a lecture.
    ///
    /// Built from the shipped table through <see cref="ElementLore"/> rather than laid out by hand.
    /// A chart somebody typed in is a second copy of the rules, and it goes wrong the first time
    /// the first copy is retuned.
    /// </summary>
    public static class ElementChart
    {
        /// <summary>Fills a container with the chart, replacing whatever was in it.</summary>
        public static void Build(VisualElement into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();
            into.Add(Legend());

            foreach (var element in ElementInfo.All)
            {
                into.Add(Column(element));
            }
        }

        /// <summary>
        /// The two row names, once, down the left.
        ///
        /// Repeating them in every column would be seven copies of a word to read past before
        /// reaching the runes that are the point.
        /// </summary>
        static VisualElement Legend()
        {
            var legend = new VisualElement();
            legend.AddToClassList("element-chart__legend");

            var head = new VisualElement();
            head.AddToClassList("element-chart__head");
            legend.Add(head);

            legend.Add(RowLabel("BEATS", "element-chart__row-label--beats"));
            legend.Add(RowLabel("LOSES TO", "element-chart__row-label--loses"));

            return legend;
        }

        static Label RowLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList("element-chart__row-label");
            label.AddToClassList(className);
            return label;
        }

        /// <summary>One element: its rune, what it answers, and what answers it.</summary>
        static VisualElement Column(Element element)
        {
            var column = new VisualElement();
            column.AddToClassList("element-chart__column");
            column.tooltip = ElementLore.Describe(element);

            var head = new VisualElement();
            head.AddToClassList("element-chart__head");

            var mark = new VisualElement();
            mark.AddToClassList("element-chart__mark");
            CharacterSheet.PaintElement(mark, element);
            head.Add(mark);

            var name = new Label(ElementInfo.ShortNameOf(element));
            name.AddToClassList("element-chart__name");
            head.Add(name);

            column.Add(head);
            column.Add(Cell(ElementLore.Beats(element)));
            column.Add(Cell(ElementLore.LosesTo(element)));

            return column;
        }

        /// <summary>A handful of runes, or a dash where there are none.</summary>
        static VisualElement Cell(IReadOnlyList<Element> elements)
        {
            var cell = new VisualElement();
            cell.AddToClassList("element-chart__cell");

            if (elements.Count == 0)
            {
                var none = new Label("—");
                none.AddToClassList("element-chart__none");
                cell.Add(none);
                return cell;
            }

            foreach (var element in elements)
            {
                var mark = new VisualElement();
                mark.AddToClassList("element-chart__rune");
                mark.tooltip = ElementInfo.NameOf(element);
                CharacterSheet.PaintElement(mark, element);
                cell.Add(mark);
            }

            return cell;
        }
    }
}
