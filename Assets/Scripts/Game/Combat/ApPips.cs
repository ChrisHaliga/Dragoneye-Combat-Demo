using Dragoneye.Combat;
using UnityEngine.UIElements;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Action points as a row of circles, each drawn as the two half-points it is made of.
    ///
    /// Half a point is spendable, so the pips have to show halves, and the two halves are drawn
    /// as the left and right of one circle. A whole point is a circle; a point with half of it
    /// spent is a circle with one side dark. Nobody has to be told that, which is the difference
    /// between this and the row of identical bars it replaces -- those marked the pairing with a
    /// slightly wider gap every second pip, and a gap is only a boundary if you already know to
    /// look for it.
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

                // Which half of its circle this is. Rounded on its outer side and square against
                // its twin, so the pair reads as one shape rather than two.
                var half = i % Ap.UnitsPerPoint;
                pip.AddToClassList(half == 0 ? "pip--opening" : "pip--closing");

                into.Add(pip);
            }
        }
    }
}
