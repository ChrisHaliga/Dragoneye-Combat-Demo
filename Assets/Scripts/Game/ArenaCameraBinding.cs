using Dragoneye.CameraControl;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Points the arena camera at the local player's cursor once it spawns.
    ///
    /// The camera cannot be wired up in the scene any more: cursors are network objects that only
    /// exist after the match starts, and only one of them belongs to this player. This is the one
    /// place that knows how to connect the two, so neither the camera nor the cursor has to know
    /// the other might be networked.
    /// </summary>
    public static class ArenaCameraBinding
    {
        public static void BindLocalCursor(CameraCursor cursor)
        {
            if (cursor == null)
            {
                return;
            }

            var rig = Object.FindAnyObjectByType<CameraRig>();
            var input = Object.FindAnyObjectByType<CameraRigInput>();

            if (rig == null || input == null)
            {
                Debug.LogError("No camera rig in the arena scene; the camera will not follow the cursor.");
            }

            if (rig != null)
            {
                rig.SetCursor(cursor);
            }

            if (input != null)
            {
                input.SetCursor(cursor);
            }

            // Re-applies the arena bounds to the new cursor.
            var bounds = Object.FindAnyObjectByType<HexArenaCameraBounds>();
            if (bounds != null)
            {
                bounds.SetCursor(cursor);
            }
        }
    }
}
