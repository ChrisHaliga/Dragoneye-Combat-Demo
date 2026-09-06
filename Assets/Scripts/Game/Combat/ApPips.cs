using Dragoneye.Combat;
using UnityEngine.UIElements;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Action points as a row of pips, one per half-point, paired into whole points.
    ///
    /// Shared by the inspect card and the turn footer, because the same currency drawn two ways
    /// on the same screen is two things a player has to learn to read. Players count remaining
    /// actions; they do not estimate them, so this is discrete rather than a bar.
    /// </summary>
    public static class ApPips
    {
        /// <summary>Fills a container with the pips, replacing whatever was in it.</summary>
        public static void Fill(VisualElement into, Ap filled, Ap total)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();

            for (var i = 0; i < total.Units; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");
                pip.EnableInClassList("pip--filled", i < filled.Units);

                // Every second pip closes a whole point, so a half-unit budget stays countable.
                pip.EnableInClassList("pip--whole", (i + 1) % Ap.UnitsPerPoint == 0);

                into.Add(pip);
            }
        }
    }
}
