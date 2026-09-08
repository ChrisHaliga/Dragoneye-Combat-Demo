using Dragoneye.Combat;
using System.Collections.Generic;
using Dragoneye.Game;
using Dragoneye.Game.Combat;

namespace Dragoneye.Game.Creatures
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
        /// Which one of its kind this entry is, or zero when it is the only one.
        ///
        /// Three goblins on a board are three things a player has to be able to talk about, and a
        /// log line saying "Goblin attacked Goblin" is not a log. Numbered in roster order, so the
        /// first one drafted is the first one numbered.
        ///
        /// Zero for a creature with no twin, because "Ogre 1" beside no Ogre 2 is a number that
        /// answers a question nobody asked. That does mean a name changes when a second one is
        /// added -- which is right: it is the second one arriving that makes the first ambiguous.
        /// </summary>
        public static int OrdinalOf(IReadOnlyList<RosterEntry> roster, uint entryId)
        {
            if (roster == null)
            {
                return 0;
            }

            var kind = 0;
            var found = false;

            foreach (var entry in roster)
            {
                if (entry.EntryId == entryId)
                {
                    kind = entry.CreatureId;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return 0;
            }

            var ordinal = 0;
            var seen = 0;

            foreach (var entry in roster)
            {
                if (entry.CreatureId != kind)
                {
                    continue;
                }

                seen++;

                if (entry.EntryId == entryId)
                {
                    ordinal = seen;
                }
            }

            return seen > 1 ? ordinal : 0;
        }

        /// <summary>The name with its number on it, where it has one.</summary>
        public static string NumberedName(string name, int ordinal) =>
            ordinal > 0 ? name + " " + ordinal : name;

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

        /// <summary>
        /// The sides that would walk onto the board with nobody on them, and so want a premade or
        /// three dealt to them.
        ///
        /// A side counts as manned by either kind of fighter, because the arena places both: a
        /// creature drafted onto the roster, or a character a player built and brought. Asking
        /// only about the roster is what once dealt three premades onto each side of a
        /// one-against-one where both players had brought a character -- the roster was empty, so
        /// by that reading both sides were.
        ///
        /// There are always at least two sides in the answer's input. A host alone on the heroes
        /// has to be fighting somebody.
        /// </summary>
        /// <param name="chosen">The side each player slot picked, in slot order.</param>
        /// <param name="broughtCharacter">The slots that are bringing a built character.</param>
        public static List<Party> PartiesNeedingSeed(IReadOnlyList<RosterEntry> roster,
            IReadOnlyList<PartyChoice> chosen, IReadOnlyList<byte> broughtCharacter)
        {
            var sides = new List<Party>();

            if (chosen != null)
            {
                foreach (var choice in chosen)
                {
                    if (!sides.Contains(choice.Party))
                    {
                        sides.Add(choice.Party);
                    }
                }
            }

            if (!sides.Contains(Party.Heroes))
            {
                sides.Insert(0, Party.Heroes);
            }

            if (sides.Count < 2)
            {
                sides.Add(Party.Monsters);
            }

            var empty = new List<Party>();

            foreach (var side in sides)
            {
                if (!HasFighters(roster, chosen, broughtCharacter, side))
                {
                    empty.Add(side);
                }
            }

            return empty;
        }

        /// <summary>Whether anybody at all walks onto the board on this side.</summary>
        public static bool HasFighters(IReadOnlyList<RosterEntry> roster,
            IReadOnlyList<PartyChoice> chosen, IReadOnlyList<byte> broughtCharacter, Party party)
        {
            if (CreatureCountIn(roster, party) > 0)
            {
                return true;
            }

            if (broughtCharacter == null)
            {
                return false;
            }

            foreach (var slot in broughtCharacter)
            {
                if (TryGetParty(chosen, slot, out var side) && side == party)
                {
                    return true;
                }
            }

            return false;
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
