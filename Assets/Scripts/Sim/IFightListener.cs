using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Sim
{
    /// <summary>
    /// Everything a fight has to say to the world outside it.
    ///
    /// The fight never calls anything it did not construct. It writes its record here, asks its
    /// questions here, and says where a wall went and who earned what -- and whoever holds the
    /// other end decides what that means: an announcer and a set of prompts on a server, a list
    /// and a scripted answer in a test, nothing at all in a benchmark. The fight cannot tell the
    /// difference, which is the point.
    ///
    /// Nothing here returns a value the fight then depends on. A question is asked and the
    /// answer arrives later through the fight's own methods, so a listener that never answers
    /// leaves a fight that is waiting rather than a fight that is wrong.
    /// </summary>
    public interface IFightListener
    {
        /// <summary>One thing the fight did, in the order it did it. The whole record passes here.</summary>
        void Announce(CombatEvent e);

        /// <summary>A person is asked what to put up. Answered through <see cref="Fight.AnswerClash"/>.</summary>
        void AskDefence(uint defenderId, DefenceRequest request);

        /// <summary>The question above is no longer open, answered or otherwise.</summary>
        void CloseDefence(uint defenderId);

        /// <summary>A person is offered a swing at somebody walking past. Answered through <see cref="Fight.AnswerOpportunity"/>.</summary>
        void OfferSwing(uint watcherId, uint moverId, SkillSpec swing);

        /// <summary>The offer above is no longer open.</summary>
        void CloseOffer(uint watcherId);

        /// <summary>A creature's action is being held while the creatures it walks away from decide.</summary>
        void Hold(uint moverId);

        /// <summary>The held action has run, or never will.</summary>
        void Release();

        /// <summary>The fight's own map changed. Anybody keeping a copy changes theirs the same way.</summary>
        void WallChanged(WallSegment segment, Wall wall);

        /// <summary>A kill was worth this much to whoever made it.</summary>
        void Award(uint killerId, int xp);

        /// <summary>Something is wrong that the fight has worked around. Worth a line somewhere.</summary>
        void Warn(string message);
    }
}
