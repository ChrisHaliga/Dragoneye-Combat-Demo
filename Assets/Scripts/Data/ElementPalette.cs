using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// What each element looks like.
    ///
    /// Presentation, so it lives outside <see cref="Dragoneye.Combat"/> -- an element's colour has
    /// no bearing on which element beats which, and the rules assembly holds no engine types to
    /// express a colour with anyway.
    ///
    /// Constants rather than an authored asset. Four fixed colours are not a tuning dial, and an
    /// asset would mean every screen that draws a pool needs a reference to it.
    /// </summary>
    public static class ElementPalette
    {
        static readonly Color k_Fire = new Color(0.90f, 0.42f, 0.24f);
        static readonly Color k_Water = new Color(0.35f, 0.62f, 0.90f);
        static readonly Color k_Earth = new Color(0.55f, 0.72f, 0.38f);
        static readonly Color k_Air = new Color(0.78f, 0.76f, 0.92f);

        public static Color ForElement(Element element)
        {
            switch (element)
            {
                case Element.Fire: return k_Fire;
                case Element.Water: return k_Water;
                case Element.Earth: return k_Earth;
                case Element.Air: return k_Air;
                default: return Color.white;
            }
        }
    }
}
