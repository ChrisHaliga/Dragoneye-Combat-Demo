using Dragoneye.Game;
using NUnit.Framework;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    public class PlayerPaletteTests
    {
        [Test]
        public void SlotsWithinTheTableGetDistinctColours()
        {
            for (var a = 0; a < PlayerPalette.Count; a++)
            {
                for (var b = a + 1; b < PlayerPalette.Count; b++)
                {
                    Assert.AreNotEqual(PlayerPalette.ForSlot(a), PlayerPalette.ForSlot(b),
                        $"Slots {a} and {b} share a colour");
                }
            }
        }

        [Test]
        public void SlotIsStableForTheSameInput()
        {
            Assert.AreEqual(PlayerPalette.ForSlot(2), PlayerPalette.ForSlot(2));
        }

        [Test]
        public void SlotsWrapRatherThanThrow()
        {
            Assert.AreEqual(PlayerPalette.ForSlot(0), PlayerPalette.ForSlot(PlayerPalette.Count));
        }

        [Test]
        public void UnassignedSlotFallsBackInsteadOfThrowing()
        {
            // FocusState reports -1 until the server's slot replicates; a view drawn in that window
            // must not take an IndexOutOfRange.
            Assert.DoesNotThrow(() => PlayerPalette.ForSlot(-1));
        }
    }

    public class FixedStringTextTests
    {
        [Test]
        public void ShortAsciiNamesPassThroughUnchanged()
        {
            Assert.AreEqual("Chris", FixedStringText.Clamp("Chris"));
        }

        [Test]
        public void NullAndEmptyAreSafe()
        {
            Assert.AreEqual(string.Empty, FixedStringText.Clamp(null));
            Assert.AreEqual(string.Empty, FixedStringText.Clamp(string.Empty));
        }

        [Test]
        public void LongAsciiIsCutToTheByteBudget()
        {
            var clamped = FixedStringText.Clamp(new string('a', 200));

            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(clamped), FixedStringText.MaxBytes);
            Assert.AreEqual(FixedStringText.MaxBytes, clamped.Length);
        }

        [Test]
        public void MultiByteNamesAreMeasuredInBytesNotCharacters()
        {
            // Counting characters is the bug this exists to prevent: 40 three-byte characters is
            // well inside a 61-character budget and well outside a 61-byte one.
            var clamped = FixedStringText.Clamp(new string('\u4e2d', 40));

            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(clamped), FixedStringText.MaxBytes);
            Assert.Greater(clamped.Length, 0);
        }

        [Test]
        public void SurrogatePairsAreNeverSplit()
        {
            // A lone surrogate encodes as a replacement character rather than a shorter name.
            var clamped = FixedStringText.Clamp(string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 30)));

            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(clamped), FixedStringText.MaxBytes);
            Assert.IsFalse(clamped.Length > 0 && char.IsHighSurrogate(clamped[clamped.Length - 1]),
                "Clamped text ends on an unpaired high surrogate");
        }
    }
}
