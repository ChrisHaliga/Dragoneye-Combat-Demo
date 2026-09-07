using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// Every element the arena's views look up by name is in the markup, and every class the markup
    /// asks for is in a stylesheet.
    ///
    /// A view finds its elements with Q&lt;T&gt;("name") and logs an error when one is missing, which
    /// is a message in a console during a playtest rather than a failure anybody is told about. A
    /// renamed element in the markup and a stale lookup in a view compile perfectly and produce a
    /// panel that quietly is not there -- which is exactly what a HUD rebuild threatens, and what
    /// this catches in a second without opening a scene.
    ///
    /// The list is written out by hand and mirrors the views. That is the price of checking it at
    /// all outside play mode; a stale list that fails loudly beats a silent hole in a panel.
    /// </summary>
    public class HudMarkupTests
    {
        const string Markup = "Assets/UI/ArenaHud.uxml";

        static readonly string[] Names =
        {
            // The turn order, across the top.
            "turn-bar", "turn-order", "turn-pace", "turn-shade",

            // The band along the bottom: points, health, the button that ends it, the bar.
            "turn-footer", "footer-shade", "ap-pips", "ap-text",
            "own-health-fill", "own-health-text", "end-turn-button",
            "action-menu", "menu-equipment", "menu-skills", "menu-items", "skill-bar",

            // One skill, in full.
            "skill-window", "skill-window-name", "skill-window-cost", "skill-window-text",
            "skill-window-stats", "skill-window-close",

            // The log, in both its sizes.
            "combat-log", "combat-log-list", "combat-log-sliver", "combat-log-minimize",

            // The party, and the card that reads one creature.
            "party-column", "portrait-list",
            "summary-card", "card-pin", "card-close", "card-party", "card-portrait", "card-name",
            "card-subtitle", "card-controller", "card-description", "card-hp", "card-armour",
            "card-armour-row", "card-ap", "card-ap-pips", "card-speed", "card-xp",
            "card-elements", "card-elements-title", "card-skills", "card-skills-title",

            // Everything else the arena draws.
            "cursor-action", "floating-text", "outcome-banner", "outcome-title",
            "scenario-report", "scenario-title", "scenario-status", "scenario-checks",
            "scenario-next-button", "scenario-back-button"
        };

        static VisualElement Tree()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Markup);
            Assert.NotNull(asset, $"{Markup} is missing");
            return asset.Instantiate();
        }

        [Test]
        public void EveryElementTheViewsLookUpIsInTheMarkup()
        {
            var tree = Tree();
            var missing = Names.Where(name => tree.Q(name) == null).ToList();

            Assert.IsEmpty(missing, "Named elements missing from " + Markup + ": "
                + string.Join(", ", missing));
        }

        [Test]
        public void EveryClassTheMarkupAsksForIsStyled()
        {
            var markup = System.IO.File.ReadAllText(Markup);
            var styles = System.IO.File.ReadAllText("Assets/UI/ArenaHud.uss")
                + System.IO.File.ReadAllText("Assets/UI/Chrome.uss");

            var asked = new HashSet<string>();

            foreach (System.Text.RegularExpressions.Match attribute in
                     System.Text.RegularExpressions.Regex.Matches(markup, @"class=""([^""]*)"""))
            {
                foreach (var name in attribute.Groups[1].Value.Split(' '))
                {
                    if (name.Length > 0)
                    {
                        asked.Add(name);
                    }
                }
            }

            var unstyled = asked
                .Where(name => !System.Text.RegularExpressions.Regex.IsMatch(
                    styles, @"\." + System.Text.RegularExpressions.Regex.Escape(name) + @"(?![\w-])"))
                .ToList();

            Assert.IsEmpty(unstyled, "Classes with no rule anywhere: " + string.Join(", ", unstyled));
        }
    }
}
