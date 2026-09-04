using System;

namespace Dragoneye.Game
{
    /// <summary>How a floating number should read.</summary>
    public enum NoticeTone
    {
        /// <summary>Something was gained. Experience, health.</summary>
        Gain,

        /// <summary>Something was taken. Damage.</summary>
        Loss
    }

    /// <summary>
    /// Numbers worth showing over a creature's head, and where they come from.
    ///
    /// One channel rather than an event per kind, because the view that draws them does not care
    /// what happened -- it needs a creature, a line, and whether the line is good news. Adding
    /// healing or a miss later is a call to <see cref="Raise"/>, not another subscriber.
    ///
    /// Deliberately transient. These are announcements of moments, replicated as fire-and-forget
    /// RPCs, so a peer that misses one has missed a number rather than a rule. Nothing reads state
    /// back out of here.
    /// </summary>
    public static class CombatNotices
    {
        /// <summary>The creature it happened to, what to say, and how it reads.</summary>
        public static event Action<uint, string, NoticeTone> Raised;

        public static void Raise(uint turnId, string text, NoticeTone tone)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Raised?.Invoke(turnId, text, tone);
            }
        }

        /// <summary>
        /// What a blow did, and why it was not worse.
        ///
        /// "-2 HP" is the outcome and "5 - 3 armour" is the reason. A player who is wearing plate
        /// and taking two damage a turn should be able to see that the plate is the reason, without
        /// working it out from a health bar. The reason is left off when there is none, because
        /// "5 - 0 armour" is noise.
        /// </summary>
        public static string Damage(int landed, int raw, int reduction) =>
            reduction > 0
                ? $"-{landed} HP  ({raw} - {reduction} armour)"
                : $"-{landed} HP";
    }
}
