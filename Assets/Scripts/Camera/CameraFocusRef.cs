using UnityEngine;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// A reference to the followed point that notices when it has been destroyed.
    ///
    /// Unity overloads <c>==</c> on <see cref="Object"/> so a destroyed object compares equal to
    /// null. That overload is chosen by the *static* type, and <see cref="ICameraFocus"/> is an
    /// interface -- so a plain <c>m_Focus != null</c> on an interface field keeps returning true
    /// after the object behind it is gone, and the next property read throws
    /// MissingReferenceException. That is exactly what happened when a match ended and the focus
    /// point despawned under the camera.
    ///
    /// Holding the Object alongside the interface is what makes the check work. The struct exists so
    /// the two fields cannot drift apart, and so the reasoning above lives in one place rather than
    /// at every call site that wants to ask "is my focus still there".
    /// </summary>
    public readonly struct CameraFocusRef
    {
        public static readonly CameraFocusRef None = default;

        readonly ICameraFocus m_Focus;

        // Null for a focus that is not a Unity object at all, which is legal -- the camera assembly
        // is a leaf and something in a test may implement the interface with a plain class.
        readonly Object m_Object;
        readonly bool m_IsUnityObject;

        public CameraFocusRef(ICameraFocus focus)
        {
            m_Focus = focus;
            m_Object = focus as Object;
            m_IsUnityObject = m_Object != null;
        }

        /// <summary>
        /// Whether there is still something to follow.
        ///
        /// A Unity focus is checked through the Object reference, which respects the destroyed-object
        /// overload; a plain one only has to be non-null.
        /// </summary>
        public bool IsAlive => m_Focus != null && (!m_IsUnityObject || m_Object != null);

        /// <summary>The focus, or null if it has gone. Never returns a destroyed object.</summary>
        public ICameraFocus Value => IsAlive ? m_Focus : null;
    }
}
