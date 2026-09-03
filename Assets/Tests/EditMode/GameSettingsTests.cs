using Dragoneye.Settings;
using NUnit.Framework;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// Settings are read every frame by the camera and the cursor, so a value that escapes its
    /// range does not fail loudly -- it just makes the game feel broken.
    /// </summary>
    public class GameSettingsTests
    {
        [SetUp]
        public void ResetSettings() => GameSettings.ResetToDefaults();

        [TearDown]
        public void RestoreDefaults() => GameSettings.ResetToDefaults();

        [Test]
        public void DefaultsAreNeutral()
        {
            // 1x means "the serialised field on the component is what you get", which is the value
            // the camera was actually tuned at.
            Assert.AreEqual(1f, GameSettings.PanSensitivity);
            Assert.AreEqual(1f, GameSettings.ZoomSensitivity);
            Assert.AreEqual(1f, GameSettings.OrbitSensitivity);
            Assert.IsFalse(GameSettings.InvertOrbit);
            Assert.AreEqual(0, GameSettings.Monitor);
            Assert.AreEqual(Vector2Int.zero, GameSettings.Resolution);
        }

        [Test]
        public void SensitivityCannotBeDrivenToZero()
        {
            // Zero pan speed is a game that does not respond to input at all.
            GameSettings.PanSensitivity = 0f;

            Assert.AreEqual(GameSettings.MinSensitivity, GameSettings.PanSensitivity);
        }

        [Test]
        public void SensitivityCannotBeDrivenNegativeOrHuge()
        {
            GameSettings.ZoomSensitivity = -5f;
            Assert.AreEqual(GameSettings.MinSensitivity, GameSettings.ZoomSensitivity);

            GameSettings.OrbitSensitivity = 1000f;
            Assert.AreEqual(GameSettings.MaxSensitivity, GameSettings.OrbitSensitivity);
        }

        [Test]
        public void EachSensitivityIsIndependent()
        {
            GameSettings.PanSensitivity = 2f;

            Assert.AreEqual(2f, GameSettings.PanSensitivity);
            Assert.AreEqual(1f, GameSettings.ZoomSensitivity, "Zoom should not follow pan");
            Assert.AreEqual(1f, GameSettings.OrbitSensitivity, "Orbit should not follow pan");
        }

        [Test]
        public void OrbitDirectionFollowsTheInvertToggle()
        {
            Assert.AreEqual(1f, GameSettings.OrbitDirection);

            GameSettings.InvertOrbit = true;

            Assert.AreEqual(-1f, GameSettings.OrbitDirection);
        }

        [Test]
        public void MonitorIndexNeverGoesNegative()
        {
            // A negative index would index the monitor list out of bounds on the next Apply.
            GameSettings.Monitor = -3;

            Assert.AreEqual(0, GameSettings.Monitor);
        }

        [Test]
        public void ResolutionRoundTrips()
        {
            GameSettings.Resolution = new Vector2Int(2560, 1440);

            Assert.AreEqual(new Vector2Int(2560, 1440), GameSettings.Resolution);
        }

        [Test]
        public void ChangedFiresOnceForARealChangeAndNotForANoOp()
        {
            var count = 0;
            GameSettings.Changed += Count;

            try
            {
                GameSettings.PanSensitivity = 1.5f;
                Assert.AreEqual(1, count, "A real change should notify");

                GameSettings.PanSensitivity = 1.5f;
                Assert.AreEqual(1, count, "Writing the same value should not notify");
            }
            finally
            {
                GameSettings.Changed -= Count;
            }

            void Count() => count++;
        }

        [Test]
        public void ClampedWritesStillReportTheClampedValue()
        {
            // The settings menu reads back what it wrote to position its slider. If a clamped write
            // reported the raw value, the slider and the game would disagree.
            GameSettings.OrbitSensitivity = 99f;

            Assert.AreEqual(GameSettings.Clamp(99f), GameSettings.OrbitSensitivity);
        }
    }
}
