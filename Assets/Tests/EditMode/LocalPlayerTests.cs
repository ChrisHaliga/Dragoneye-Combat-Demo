using Dragoneye.Game;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// The rule that decides whether a click becomes an order. It used to be netcode ownership,
    /// which made a solo host the owner of every unclaimed creature and let one player move both
    /// sides of the board.
    /// </summary>
    public class LocalPlayerControlTests
    {
        [Test]
        public void APlayerControlsTheirOwnCreatures()
        {
            Assert.IsTrue(LocalPlayer.Controls(controllerSlot: 2, localSlot: 2));
        }

        [Test]
        public void APlayerDoesNotControlAnotherPlayersCreatures()
        {
            Assert.IsFalse(LocalPlayer.Controls(controllerSlot: 1, localSlot: 2));
        }

        [Test]
        public void NobodyControlsAComputerCreature()
        {
            // The case that was broken: unclaimed creatures spawn server-owned, so the host's
            // IsOwner said yes for the entire enemy team.
            for (var slot = 0; slot < 8; slot++)
            {
                Assert.IsFalse(LocalPlayer.Controls(PartyInfo.Unclaimed, (byte)slot),
                    $"Slot {slot} must not control an unclaimed creature");
            }
        }

        [Test]
        public void UnclaimedNeverMatchesItself()
        {
            // Both sides equal, and both meaning "nobody". Plain equality would call that a match
            // and hand every computer creature to a player with no slot.
            Assert.IsFalse(LocalPlayer.Controls(PartyInfo.Unclaimed, PartyInfo.Unclaimed));
        }

        [Test]
        public void APlayerWithNoSlotControlsNothing()
        {
            Assert.IsFalse(LocalPlayer.Controls(controllerSlot: 0, localSlot: PartyInfo.Unclaimed));
        }

        [Test]
        public void SlotZeroIsARealSlot()
        {
            // Slot 0 is the host. A guard written as "slot > 0" would silently disarm them.
            Assert.IsTrue(LocalPlayer.Controls(controllerSlot: 0, localSlot: 0));
        }
    }
}
