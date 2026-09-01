using UnityEngine;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// The point of interest a camera follows, and which movement input drives.
    ///
    /// An interface rather than a concrete component because the thing being followed is game
    /// state -- in this project a replicated network object -- while the camera is view. Depending
    /// on the interface lets the camera assembly stay a leaf: it can be dropped into a project with
    /// no grid, no netcode and no notion of a "player" and still work.
    /// </summary>
    public interface ICameraFocus
    {
        Vector3 Position { get; }

        /// <summary>Continuous movement from a held control. Time-scaled by the caller.</summary>
        /// <param name="input">x = right, y = forward, each -1 to 1.</param>
        void Move(Vector2 input, float yawDegrees, float deltaTime, float speedScale);

        /// <summary>Movement from a pointer delta in pixels. Never time-scaled.</summary>
        void Drag(Vector2 pixelDelta, float yawDegrees, float speedScale);
    }
}
