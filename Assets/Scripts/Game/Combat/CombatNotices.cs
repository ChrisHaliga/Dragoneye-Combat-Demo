using System;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
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
    /// The shape drawn beside a note, where one says something the number cannot.
    ///
    /// Two of them, because there are two things a player needs to read off an exchange at a
    /// glance and neither is a quantity: it got through, or it did not.
    /// </summary>
    public enum NoticeMark
    {
        None,

        /// <summary>A blade. The blow landed on health.</summary>
        Hit,

        /// <summary>A guard. It was turned aside, by an answer or by armour or by missing.</summary>
        Guard
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
        /// <summary>The creature it happened to, what to say, how it reads, and what it means.</summary>
        public static event Action<uint, string, NoticeTone, NoticeMark> Raised;

        public static void Raise(uint turnId, string text, NoticeTone tone,
            NoticeMark mark = NoticeMark.None)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Raised?.Invoke(turnId, text, tone, mark);
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
        public static string Damage(int landed, int absorbed)
        {
            if (absorbed <= 0)
            {
                return $"-{landed} HP";
            }

            return landed > 0
                ? $"-{landed} HP  ({absorbed} on armour)"
                : $"{absorbed} on armour";
        }
    }
}
