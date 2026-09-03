using System.Collections.Generic;

namespace Dragoneye.Game
{
    /// <summary>
    /// Every question that can be asked of a draft, as pure functions over plain lists.
    ///
    /// Separated from <see cref="DraftState"/> because the two have different reasons to change:
    /// that class owns replication and authority, this owns what the numbers mean. Keeping the
    /// questions here also makes them answerable in a test -- <c>NetworkList</c> cannot be
    /// constructed outside a live session, so anything reading it directly was untestable by
    /// construction.
    ///
    /// Both the server (validating a claim) and every client (deciding which buttons to offer) run
    /// these, so they have to agree exactly. Pure functions over the same replicated data is the
    /// cheapest way to guarantee that.
    /// </summary>
    public static class DraftQueries
    {
        /// <summary>
        /// The party a player has chosen, if any. Returns false rather than defaulting: a silent
        /// default makes "has not picked" indistinguishable from "picked the first party".
        /// </summary>
        public static bool TryGetParty(IReadOnlyList<PartyChoice> choices, byte slot, out Party party)
        {
            if (choices != null)
            {
                for (var i = 0; i < choices.Count; i++)
                {
                    if (choices[i].Slot == slot)
                    {
                        party = choices[i].Party;
                        return true;
                    }
                }
            }

            party = default;
            return false;
        }

        /// <summary>
        /// Slots in a party, ascending.
        ///
        /// The sort is what makes claim caps deterministic: a player's cap depends on their ordinal
        /// within the party, so every peer has to agree on the order. Replication order does not.
        /// </summary>
        public static List<byte> SlotsIn(IReadOnlyList<PartyChoice> choices, Party party)
        {
            var slots = new List<byte>();
            if (choices == null)
            {
                return slots;
            }

            for (var i = 0; i < choices.Count; i++)
            {
                if (choices[i].Party == party)
                {
                    slots.Add(choices[i].Slot);
                }
            }

            slots.Sort();
            return slots;
        }

        public static int CreatureCountIn(IReadOnlyList<RosterEntry> roster, Party party)
        {
            var count = 0;
            if (roster == null)
            {
                return count;
            }

            for (var i = 0; i < roster.Count; i++)
            {
                if (roster[i].Party == party)
                {
                    count++;
                }
            }

            return count;
        }

        public static int ClaimCountFor(IReadOnlyList<RosterEntry> roster, byte slot)
        {
            var count = 0;
            if (roster == null)
            {
                return count;
            }

            for (var i = 0; i < roster.Count; i++)
            {
                if (roster[i].ClaimedBySlot == slot)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>How many creatures this player may claim, given who else is in their party.</summary>
        public static int CapFor(IReadOnlyList<RosterEntry> roster, IReadOnlyList<PartyChoice> choices,
            byte slot)
        {
            if (!TryGetParty(choices, slot, out var party))
            {
                return 0;
            }

            var slots = SlotsIn(choices, party);
            var ordinal = slots.IndexOf(slot);

            return ordinal < 0 ? 0 : ClaimRules.CapFor(CreatureCountIn(roster, party), slots.Count, ordinal);
        }

        /// <summary>Position of an entry by its stable id, or -1. Never address entries by index.</summary>
        public static int IndexOf(IReadOnlyList<RosterEntry> roster, uint entryId)
        {
            if (roster == null)
            {
                return -1;
            }

            for (var i = 0; i < roster.Count; i++)
            {
                if (roster[i].EntryId == entryId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether a claim would be allowed. The server calls this to decide and clients call it to
        /// decide what to offer, so a button is never enabled for something that will be refused.
        /// </summary>
        public static bool CanClaim(IReadOnlyList<RosterEntry> roster, IReadOnlyList<PartyChoice> choices,
            byte slot, uint entryId)
        {
            var index = IndexOf(roster, entryId);
            if (index < 0 || slot == PartyInfo.Unclaimed)
            {
                return false;
            }

            if (!TryGetParty(choices, slot, out var party))
            {
                return false;
            }

            var entry = roster[index];
            return !entry.IsClaimed
                && entry.Party == party
                && ClaimCountFor(roster, slot) < CapFor(roster, choices, slot);
        }

        /// <summary>The parties that have at least one creature, in roster order.</summary>
        public static List<Party> PartiesPresent(IReadOnlyList<RosterEntry> roster)
        {
            var parties = new List<Party>();
            if (roster == null)
            {
                return parties;
            }

            for (var i = 0; i < roster.Count; i++)
            {
                if (!parties.Contains(roster[i].Party))
                {
                    parties.Add(roster[i].Party);
                }
            }

            return parties;
        }
    }
}
