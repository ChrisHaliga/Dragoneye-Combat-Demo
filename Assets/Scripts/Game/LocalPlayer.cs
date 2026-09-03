using Unity.Netcode;

namespace Dragoneye.Game
{
    /// <summary>
    /// Who the player at this keyboard is, and what they are allowed to move.
    ///
    /// Control is decided by <see cref="CreatureState.ControllerSlot"/>, never by netcode ownership.
    /// The two agree for a claimed creature, and disagree for every unclaimed one: those spawn owned
    /// by the server, so <c>IsOwner</c> is true for the host and a solo player could order the
    /// entire board, both sides of it. Slot is the authored answer to "whose creature is this" and
    /// is the same on every peer.
    /// </summary>
    public static class LocalPlayer
    {
        /// <summary>
        /// The local player's draft slot, or false before the roster has issued one.
        ///
        /// Slots come from <see cref="PlayerRoster"/> rather than the client id: ids are assigned by
        /// the transport and differ per peer, while a slot is the same number everywhere.
        /// </summary>
        public static bool TryGetSlot(out byte slot)
        {
            var roster = PlayerRoster.Current;
            var manager = NetworkManager.Singleton;

            if (roster != null && manager != null
                && roster.TryGet(manager.LocalClientId, out var entry)
                && entry.Slot >= 0 && entry.Slot <= byte.MaxValue)
            {
                slot = (byte)entry.Slot;
                return true;
            }

            slot = PartyInfo.Unclaimed;
            return false;
        }

        /// <summary>Whether the local player may give this creature orders.</summary>
        public static bool Controls(CreatureState creature) =>
            creature != null && TryGetSlot(out var slot) && Controls(creature.ControllerSlot, slot);

        /// <summary>
        /// The rule itself, free of scene state so it can be tested.
        ///
        /// Unclaimed is a real slot value meaning "the computer runs this", so it must never match --
        /// including against a player who somehow holds the unclaimed value themselves.
        /// </summary>
        public static bool Controls(byte controllerSlot, byte localSlot) =>
            controllerSlot != PartyInfo.Unclaimed
            && localSlot != PartyInfo.Unclaimed
            && controllerSlot == localSlot;
    }
}
