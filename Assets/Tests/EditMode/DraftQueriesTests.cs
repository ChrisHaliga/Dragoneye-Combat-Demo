using System.Collections.Generic;
using System.Linq;
using Dragoneye.Game;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// These exist because the server and every client run the same predicates to decide what is
    /// allowed and what to offer. If they disagreed, a player would see an enabled button that the
    /// server silently refuses.
    /// </summary>
    public class DraftQueriesTests
    {
        static RosterEntry Entry(uint id, Party party, byte claimedBy = PartyInfo.Unclaimed,
            uint sequence = 0) =>
            new RosterEntry(id, 1, party)
            {
                ClaimedBySlot = claimedBy,
                ClaimSequence = sequence
            };

        static List<PartyChoice> Choices(params (byte Slot, Party Party)[] entries) =>
            entries.Select(e => new PartyChoice(e.Slot, e.Party)).ToList();

        [Test]
        public void PartyIsReportedOnlyWhenChosen()
        {
            var choices = Choices((0, Party.Monsters));

            Assert.IsTrue(DraftQueries.TryGetParty(choices, 0, out var party));
            Assert.AreEqual(Party.Monsters, party);
            Assert.IsFalse(DraftQueries.TryGetParty(choices, 1, out _), "Slot 1 never chose");
        }

        [Test]
        public void AnUnchosenSlotDoesNotDefaultToTheFirstParty()
        {
            // The bug this replaced: defaulting made "has not picked" look like "picked Heroes",
            // and left claim rejection resting on a cap of zero computed two calls away.
            Assert.IsFalse(DraftQueries.TryGetParty(new List<PartyChoice>(), 3, out _));
        }

        [Test]
        public void SlotsInAPartyComeBackAscending()
        {
            // Order decides each player's ordinal and therefore their cap, so it cannot depend on
            // replication order.
            var choices = Choices((5, Party.Heroes), (1, Party.Heroes), (3, Party.Monsters), (2, Party.Heroes));

            CollectionAssert.AreEqual(new byte[] { 1, 2, 5 }, DraftQueries.SlotsIn(choices, Party.Heroes));
        }

        [Test]
        public void CountsAreScopedToTheirParty()
        {
            var roster = new List<RosterEntry>
            {
                Entry(1, Party.Heroes), Entry(2, Party.Heroes), Entry(3, Party.Monsters)
            };

            Assert.AreEqual(2, DraftQueries.CreatureCountIn(roster, Party.Heroes));
            Assert.AreEqual(1, DraftQueries.CreatureCountIn(roster, Party.Monsters));
            Assert.AreEqual(0, DraftQueries.CreatureCountIn(roster, Party.Guards));
        }

        [Test]
        public void CapSplitsAPartyBetweenItsPlayers()
        {
            var roster = Enumerable.Range(1, 5).Select(i => Entry((uint)i, Party.Heroes)).ToList();
            var choices = Choices((0, Party.Heroes), (1, Party.Heroes));

            // 5 creatures, 2 players -> 3 and 2.
            Assert.AreEqual(3, DraftQueries.CapFor(roster, choices, 0));
            Assert.AreEqual(2, DraftQueries.CapFor(roster, choices, 1));
        }

        [Test]
        public void APlayerInAnotherPartyGetsNoShareOfThisOne()
        {
            var roster = new List<RosterEntry> { Entry(1, Party.Heroes), Entry(2, Party.Heroes) };
            var choices = Choices((0, Party.Heroes), (1, Party.Monsters));

            Assert.AreEqual(2, DraftQueries.CapFor(roster, choices, 0));
            Assert.AreEqual(0, DraftQueries.CapFor(roster, choices, 1), "No Monsters creatures exist");
        }

        [Test]
        public void CapIsZeroForAPlayerWhoHasNotChosen()
        {
            var roster = new List<RosterEntry> { Entry(1, Party.Heroes) };

            Assert.AreEqual(0, DraftQueries.CapFor(roster, new List<PartyChoice>(), 0));
        }

        [Test]
        public void EntriesAreFoundByIdNotPosition()
        {
            // Identity has to survive the list shifting under an in-flight RPC.
            var roster = new List<RosterEntry> { Entry(7, Party.Heroes), Entry(9, Party.Heroes) };

            Assert.AreEqual(0, DraftQueries.IndexOf(roster, 7));
            Assert.AreEqual(1, DraftQueries.IndexOf(roster, 9));

            roster.RemoveAt(0);

            Assert.AreEqual(0, DraftQueries.IndexOf(roster, 9), "Id must follow the entry");
            Assert.AreEqual(-1, DraftQueries.IndexOf(roster, 7), "Removed entry must not resolve");
        }

        [Test]
        public void ClaimIsAllowedWhenUnclaimedInPartyAndUnderCap()
        {
            var roster = new List<RosterEntry> { Entry(1, Party.Heroes), Entry(2, Party.Heroes) };
            var choices = Choices((0, Party.Heroes));

            Assert.IsTrue(DraftQueries.CanClaim(roster, choices, 0, 1));
        }

        [Test]
        public void AlreadyClaimedIsRefused()
        {
            var roster = new List<RosterEntry> { Entry(1, Party.Heroes, claimedBy: 1) };
            var choices = Choices((0, Party.Heroes), (1, Party.Heroes));

            Assert.IsFalse(DraftQueries.CanClaim(roster, choices, 0, 1));
        }

        [Test]
        public void ClaimingAcrossPartyLinesIsRefused()
        {
            var roster = new List<RosterEntry> { Entry(1, Party.Monsters) };
            var choices = Choices((0, Party.Heroes));

            Assert.IsFalse(DraftQueries.CanClaim(roster, choices, 0, 1));
        }

        [Test]
        public void ClaimingBeyondTheCapIsRefused()
        {
            // Two players, two creatures -> one each. Slot 0 already has one.
            var roster = new List<RosterEntry>
            {
                Entry(1, Party.Heroes, claimedBy: 0), Entry(2, Party.Heroes)
            };
            var choices = Choices((0, Party.Heroes), (1, Party.Heroes));

            Assert.AreEqual(1, DraftQueries.CapFor(roster, choices, 0));
            Assert.IsFalse(DraftQueries.CanClaim(roster, choices, 0, 2), "Slot 0 is at cap");
            Assert.IsTrue(DraftQueries.CanClaim(roster, choices, 1, 2), "Slot 1 still has room");
        }

        [Test]
        public void APlayerWithNoSlotCannotClaim()
        {
            var roster = new List<RosterEntry> { Entry(1, Party.Heroes) };

            Assert.IsFalse(DraftQueries.CanClaim(roster, Choices((0, Party.Heroes)), PartyInfo.Unclaimed, 1));
        }

        [Test]
        public void AMissingEntryCannotBeClaimed()
        {
            var roster = new List<RosterEntry> { Entry(1, Party.Heroes) };

            Assert.IsFalse(DraftQueries.CanClaim(roster, Choices((0, Party.Heroes)), 0, 99));
        }

        [Test]
        public void PartiesPresentFollowsRosterOrderWithoutRepeats()
        {
            var roster = new List<RosterEntry>
            {
                Entry(1, Party.Monsters), Entry(2, Party.Heroes), Entry(3, Party.Monsters)
            };

            CollectionAssert.AreEqual(
                new[] { Party.Monsters, Party.Heroes }, DraftQueries.PartiesPresent(roster));
        }

        [Test]
        public void EveryQueryToleratesNullInput()
        {
            Assert.IsFalse(DraftQueries.TryGetParty(null, 0, out _));
            Assert.IsEmpty(DraftQueries.SlotsIn(null, Party.Heroes));
            Assert.AreEqual(0, DraftQueries.CreatureCountIn(null, Party.Heroes));
            Assert.AreEqual(0, DraftQueries.ClaimCountFor(null, 0));
            Assert.AreEqual(0, DraftQueries.CapFor(null, null, 0));
            Assert.AreEqual(-1, DraftQueries.IndexOf(null, 1));
            Assert.IsFalse(DraftQueries.CanClaim(null, null, 0, 1));
            Assert.IsEmpty(DraftQueries.PartiesPresent(null));
        }

        [Test]
        public void EveryCreatureInAPartyIsClaimableBySomebody()
        {
            // Caps sum to the creature count, so no creature can be left unclaimable by everyone --
            // which would silently hand it to the computer.
            for (var creatures = 1; creatures <= 9; creatures++)
            {
                for (var players = 1; players <= 4; players++)
                {
                    var roster = Enumerable.Range(1, creatures)
                        .Select(i => Entry((uint)i, Party.Heroes)).ToList();
                    var choices = Choices(Enumerable.Range(0, players)
                        .Select(i => ((byte)i, Party.Heroes)).ToArray());

                    var total = Enumerable.Range(0, players)
                        .Sum(i => DraftQueries.CapFor(roster, choices, (byte)i));

                    Assert.AreEqual(creatures, total, $"{creatures} creatures, {players} players");
                }
            }
        }

        [Test]
        public void APlayerWhoControlsNothingStillHasTheirParty()
        {
            // The HUD used to infer the local party from a creature the player controlled, and fell
            // back to "the first party present" when they controlled none -- showing a Heroes player
            // the Monsters column. The choice is stored, so it survives holding nothing.
            var choices = Choices((0, Party.Monsters), (1, Party.Heroes));
            var roster = new List<RosterEntry>
            {
                Entry(1, Party.Heroes, claimedBy: 1),
                Entry(2, Party.Monsters, claimedBy: 1)
            };

            Assert.AreEqual(0, DraftQueries.ClaimCountFor(roster, 0), "Slot 0 holds nothing");
            Assert.IsTrue(DraftQueries.TryGetParty(choices, 0, out var party));
            Assert.AreEqual(Party.Monsters, party, "Their own choice, not the first party present");
        }

        [Test]
        public void APartyIsReportedEvenWhenItHasNoCreaturesLeft()
        {
            // Same failure the other way round: every creature on the side claimed by teammates.
            var choices = Choices((0, Party.Heroes));

            Assert.IsTrue(DraftQueries.TryGetParty(choices, 0, out var party));
            Assert.AreEqual(Party.Heroes, party);
            Assert.AreEqual(0, DraftQueries.CreatureCountIn(new List<RosterEntry>(), Party.Heroes));
        }

        [Test]
        public void EveryCreatureOnAJoinedSideIsClaimableSoNothingIsStrandedUnclaimed()
        {
            // A host who builds a roster by hand and never presses Claim used to end up with every
            // creature unclaimed -- server-owned, so nobody could move anything. Match start now
            // fills up to the cap, and the cap covers the whole side when one player is on it.
            var roster = new List<RosterEntry>
            {
                Entry(1, Party.Heroes), Entry(2, Party.Heroes), Entry(3, Party.Heroes)
            };
            var choices = Choices((0, Party.Heroes));

            Assert.AreEqual(3, DraftQueries.CapFor(roster, choices, 0));

            foreach (var entry in roster)
            {
                Assert.IsTrue(DraftQueries.CanClaim(roster, choices, 0, entry.EntryId),
                    $"Entry {entry.EntryId} should be claimable by the only player on its side");
            }
        }

        [Test]
        public void APartyNobodyJoinedStaysUnclaimed()
        {
            // The other half of the same rule: filling up to caps must not hand the enemy side to a
            // player. A party with no players has a cap of zero, so it stays computer-run.
            var roster = new List<RosterEntry>
            {
                Entry(1, Party.Heroes), Entry(2, Party.Monsters)
            };
            var choices = Choices((0, Party.Heroes));

            Assert.AreEqual(1, DraftQueries.CapFor(roster, choices, 0));
            Assert.IsFalse(DraftQueries.CanClaim(roster, choices, 0, 2), "Monsters is not their side");
        }
    }
}
