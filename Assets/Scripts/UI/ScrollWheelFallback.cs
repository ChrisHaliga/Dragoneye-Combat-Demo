using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Dragoneye.UI
{
    /// <summary>
    /// Scrolls a scroll view from the mouse wheel when nothing else is going to.
    ///
    /// None of the scenes carry an event system, so runtime UI Toolkit is on its default input
    /// path -- and on that path the wheel has, in practice, not reached a scroll view: the rules
    /// page and the combat log both sat still under it, four reports running. Rather than
    /// depend on which input module a scene happens to have, this reads the wheel off the Input
    /// System directly while the pointer is over the view and moves the offset itself.
    ///
    /// It stands aside the moment a real <see cref="WheelEvent"/> is seen, so a scene where the
    /// wheel does arrive is not scrolled twice.
    /// </summary>
    public static class ScrollWheelFallback
    {
        /// <summary>Lines per wheel notch, in multiples of the view's own wheel size.</summary>
        const float NotchScale = 2f;

        public static void Attach(ScrollView view)
        {
            if (view == null)
            {
                return;
            }

            var wheelArrives = false;

            view.RegisterCallback<WheelEvent>(_ => wheelArrives = true, TrickleDown.TrickleDown);

            view.schedule.Execute(() =>
            {
                if (wheelArrives)
                {
                    return;
                }

                var mouse = Mouse.current;

                if (mouse == null || view.panel == null)
                {
                    return;
                }

                var delta = mouse.scroll.ReadValue().y;

                if (Mathf.Abs(delta) < 0.01f)
                {
                    return;
                }

                var screen = mouse.position.ReadValue();
                var local = RuntimePanelUtils.ScreenToPanel(view.panel,
                    new Vector2(screen.x, Screen.height - screen.y));

                if (!view.worldBound.Contains(local))
                {
                    return;
                }

                var step = view.mouseWheelScrollSize > 0f ? view.mouseWheelScrollSize : 28f;
                var offset = view.scrollOffset;
                offset.y -= Mathf.Sign(delta) * step * NotchScale;
                view.scrollOffset = offset;
            }).Every(16);
        }
    }
}
