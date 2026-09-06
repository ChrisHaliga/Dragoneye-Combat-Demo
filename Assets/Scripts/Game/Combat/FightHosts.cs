using Dragoneye.Combat;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// What the clash conductor needs from the thing running the fight.
    ///
    /// Three collaborators used to be one fifteen-hundred-line director. Each of them now asks for
    /// exactly what it needs through an interface like this one, which is what keeps them from
    /// reaching back into the director for whatever happens to be there -- and what lets each be
    /// read on its own.
    /// </summary>
    public interface IClashHost
    {
        /// <summary>Applies whatever a clash left of an attack.</summary>
        void LandContested(CreatureState attacker, SkillSpec skill, CreatureState defender,
            SkillEffect effect);

        /// <summary>Applies a skill nobody could contest, as written.</summary>
        void LandUncontested(CreatureState actor, SkillSpec skill, CreatureState target);

        /// <summary>The clash is over; whatever was waiting on it may go on.</summary>
        void ClashSettled();
    }

    /// <summary>What the opportunity conductor needs from the thing running the fight.</summary>
    public interface IOpportunityHost
    {
        /// <summary>The move, once nobody is owed a swing at it.</summary>
        bool PerformMove(CreatureState actor, Hex destination, Facing? facing);

        /// <summary>The skill, replayed from the top, once nobody is owed a swing at its approach.</summary>
        void ReplaySkill(CreatureState actor, int skillId, Hex target, Element element);

        /// <summary>Opens a clash for a swing.</summary>
        void BeginClash(CreatureState attacker, SkillSpec skill, CreatureState defender,
            Element? telegraphed);
    }

    /// <summary>What the brain runner needs from the thing running the fight.</summary>
    public interface IBrainHost
    {
        bool IsBusy { get; }

        bool CanAct(CreatureState actor);

        bool Move(CreatureState actor, Hex destination);

        bool UseSkillOn(CreatureState actor, int skillId, CreatureState target);

        void EndTurn();
    }
}
