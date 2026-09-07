using UnityEngine.UIElements;

namespace Dragoneye.UI
{
    /// <summary>
    /// The small icons on the HUD's corner buttons, drawn out of plain elements.
    ///
    /// A cross is two bars crossed and a pin is a head on a stem. Built rather than imported
    /// because they are geometry: three shapes at one size, in one colour, that have to match the
    /// panels they sit on. A letter in a typeface would read as text -- an x is not a close button,
    /// it is the letter x -- and an imported sprite would be three more files to keep in step with
    /// the palette.
    ///
    /// The pieces carry their own classes; where they sit is the stylesheet's business.
    /// </summary>
    public static class HudIcons
    {
        /// <summary>A cross. Closes whatever it is in the corner of.</summary>
        public static void DrawClose(VisualElement into)
        {
            into.Add(Bar("icon-bar--slash"));
            into.Add(Bar("icon-bar--backslash"));
        }

        /// <summary>A bar along the bottom: what is left of a panel once it is shut.</summary>
        public static void DrawMinimise(VisualElement into) => into.Add(Bar("icon-bar--floor"));

        /// <summary>
        /// A drawing pin, leaning when it is out and upright when it is pushed in.
        ///
        /// The lean is the whole of the state: pinned and unpinned have to be told apart at a
        /// glance and at sixteen pixels, and turning the shape does that where a colour change
        /// alone would not.
        /// </summary>
        public static void DrawPin(VisualElement into)
        {
            var pin = new VisualElement();
            pin.AddToClassList("icon-pin");
            pin.pickingMode = PickingMode.Ignore;

            var head = new VisualElement();
            head.AddToClassList("icon-pin__head");
            pin.Add(head);

            var stem = new VisualElement();
            stem.AddToClassList("icon-pin__stem");
            pin.Add(stem);

            into.Add(pin);
        }

        static VisualElement Bar(string variant)
        {
            var bar = new VisualElement();
            bar.AddToClassList("icon-bar");
            bar.AddToClassList(variant);
            bar.pickingMode = PickingMode.Ignore;
            return bar;
        }
    }
}
