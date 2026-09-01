using Unity.Netcode.Components;

namespace Dragoneye.Game
{
    /// <summary>
    /// A NetworkTransform the owning client writes, rather than the server.
    ///
    /// Appropriate here specifically because a cursor is a presentation affordance -- where a
    /// player is looking -- not authoritative game state. A cheating client can move its own
    /// pointer around, which costs nothing. Do not reuse this for anything that decides outcomes;
    /// unit positions and combat need server authority.
    ///
    /// The Multiplayer Center package ships an equivalent sample, but its own documentation says to
    /// copy it into your project rather than depend on it, so this is that copy.
    /// </summary>
    [UnityEngine.DisallowMultipleComponent]
    public sealed class OwnerAuthoritativeTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
