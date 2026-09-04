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
    /// Chosen to stay distinguishable side by side on a dark ground and to survive being drawn small:
    /// the seven are spread around the wheel rather than clustered, and Lux and Nyx are separated by
    /// value as well as hue so they read apart even in greyscale.
    /// </summary>
    public static class ElementPalette
    {
        static readonly Color k_Geo = new Color(0.62f, 0.48f, 0.28f);
        static readonly Color k_Hydro = new Color(0.31f, 0.60f, 0.86f);
        static readonly Color k_Pyro = new Color(0.88f, 0.36f, 0.24f);
        static readonly Color k_Aero = new Color(0.55f, 0.80f, 0.72f);
        static readonly Color k_Lux = new Color(0.95f, 0.86f, 0.55f);
        static readonly Color k_Nyx = new Color(0.51f, 0.38f, 0.70f);
        static readonly Color k_Arcana = new Color(0.85f, 0.45f, 0.72f);

        public static Color ForElement(Element element)
        {
            switch (element)
            {
                case Element.Geo: return k_Geo;
                case Element.Hydro: return k_Hydro;
                case Element.Pyro: return k_Pyro;
                case Element.Aero: return k_Aero;
                case Element.Lux: return k_Lux;
                case Element.Nyx: return k_Nyx;
                case Element.Arcana: return k_Arcana;
                default: return Color.white;
            }
        }
    }
}
