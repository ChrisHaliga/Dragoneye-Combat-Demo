using Dragoneye.Combat;
using Unity.Netcode;
using Dragoneye.Game.Combat;
using Dragoneye.Game.Creatures;

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
        /// <summary>
        /// The side this player is on, or null for a spectator and for anybody who has not picked.
        ///
        /// A side, not a set of creatures. Which creatures answer to this player is a different
        /// question with a different answer -- an ally's creature is on your side and is not yours
        /// to move -- and things that colour a result by "did my team win" want this one.
        /// </summary>
        public static Party? Side()
        {
            var draft = DraftState.Current;

            return draft != null && TryGetSlot(out var slot)
                && DraftQueries.TryGetParty(draft.Choices, slot, out var party)
                ? party
                : (Party?)null;
        }

        /// <summary>
        /// The creature this player would act with, whether or not it is their turn.
        ///
        /// The bar along the bottom does not go away between turns, so it needs somebody to be
        /// about when nobody is acting. The lowest turn id of the ones this player controls: a
        /// stable answer that does not change as the round goes round, which is what matters when
        /// it decides where a row of skills sits.
        /// </summary>
        public static CreatureState Mine(CreatureRegistry creatures)
        {
            if (creatures == null)
            {
                return null;
            }

            CreatureState mine = null;

            foreach (var creature in creatures.All)
            {
                if (creature != null && Controls(creature)
                    && (mine == null || creature.TurnId < mine.TurnId))
                {
                    mine = creature;
                }
            }

            return mine;
        }

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
