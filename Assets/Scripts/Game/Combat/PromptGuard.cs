using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// Stops a prompt taking the click that opened it as its answer.
    ///
    /// The board acts on the mouse going *down*. On a host, the server suspends the move and the
    /// prompt is built in that same frame -- and then the UI event system, running later in the
    /// frame, delivers the same press to whichever button is now under the cursor. The release
    /// that follows is a click on that button, and the player has answered a question they were
    /// never shown.
    ///
    /// The rule: an answer is only taken from a press that arrived *after* the prompt opened. The
    /// frame the panel was built in is recorded, every press on the panel records its own frame,
    /// and a press from the opening frame or earlier is nobody's answer. Frames rather than time,
    /// so it is exact rather than tuned.
    /// </summary>
    public sealed class PromptGuard
    {
        int m_OpenedFrame = -1;
        int m_LastPressFrame = -1;

        /// <summary>Watches a freshly built panel. Call once, right after it is added to the tree.</summary>
        public void Open(VisualElement panel)
        {
            m_OpenedFrame = Time.frameCount;
            m_LastPressFrame = -1;

            // Trickle-down, so the press is seen on the way to the button rather than only if
            // the button lets it bubble back up.
            panel.RegisterCallback<PointerDownEvent>(_ => m_LastPressFrame = Time.frameCount,
                TrickleDown.TrickleDown);
        }

        /// <summary>Whether the click being handled began after the prompt was on screen.</summary>
        public bool Accepts => m_LastPressFrame > m_OpenedFrame;
    }
}
