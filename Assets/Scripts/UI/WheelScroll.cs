using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Dragoneye.UI
{
    /// <summary>
    /// Scrolls a scroll view from the mouse wheel, by one path and one path only.
    ///
    /// Two things have failed here before. Runtime UI Toolkit's own wheel handling depends on the
    /// wheel event reaching the view, on its delta being a size the view expects, and on the
    /// view deciding its content overflows -- and at least one of those has been false in every
    /// scene this game has had, because the combat log has never once scrolled. A poller that
    /// read the wheel directly was tried, and disabled itself the moment a wheel event arrived,
    /// on the assumption the view would then scroll itself. It did not.
    ///
    /// So this decides. When a wheel event arrives it is consumed here, before the view's own
    /// handler, and the offset moves by a fixed notch regardless of what the delta's magnitude
    /// was. While no event is arriving, the wheel is read off the Input System instead and the
    /// same notch is applied. Exactly one of the two moves the view, whichever the scene happens
    /// to deliver, and neither depends on the other being right.
    /// </summary>
    public static class WheelScroll
    {
        /// <summary>Pixels one notch of the wheel moves the view.</summary>
        public const float Notch = 56f;

        /// <summary>Frames after a wheel event during which the poller stands aside.</summary>
        const int Quiet = 30;

        public static void Attach(ScrollView view)
        {
            if (view == null)
            {
                return;
            }

            var lastEventFrame = -Quiet;

            view.RegisterCallback<WheelEvent>(evt =>
            {
                lastEventFrame = Time.frameCount;

                // Down is positive on a wheel event, and down means further into the content.
                Nudge(view, evt.delta.y);

                // Consumed. The view's own handler would scroll it a second time by a delta
                // whose size is the framework's business, and this is the one that is not.
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            view.schedule.Execute(() =>
            {
                if (Time.frameCount - lastEventFrame < Quiet || view.panel == null)
                {
                    return;
                }

                var mouse = Mouse.current;

                if (mouse == null)
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

                // Up is positive on the device, and up means back toward the start.
                Nudge(view, -delta);
            }).Every(16);
        }

        /// <summary>One notch, by sign only. What the delta was worth is not this view's problem.</summary>
        public static void Nudge(ScrollView view, float direction)
        {
            var offset = view.scrollOffset;
            offset.y += Mathf.Sign(direction) * Notch;
            view.scrollOffset = offset;
        }

        /// <summary>A whole viewport, for a button.</summary>
        public static void Page(ScrollView view, float direction)
        {
            var offset = view.scrollOffset;
            offset.y += Mathf.Sign(direction) * Mathf.Max(Notch, view.contentViewport.layout.height * 0.9f);
            view.scrollOffset = offset;
        }
    }
}
