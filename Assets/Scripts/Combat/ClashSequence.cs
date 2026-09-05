using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>Where a clash has got to.</summary>
    public enum ClashPhase
    {
        /// <summary>Waiting on the defender. Nothing about the attack may be shown yet.</summary>
        AwaitingDefence = 0,

        /// <summary>Both sides are in. Everything is readable.</summary>
        Resolved = 1
    }

    /// <summary>Why an answer was refused. <see cref="None"/> means it was taken.</summary>
    public enum DefenceRefusal
    {
        None,
        AlreadyResolved,
        TooMany,

        /// <summary>An element the defender does not hold enough of.</summary>
        NotHeld
    }

    /// <summary>
    /// What a defender is asked, and every single thing they are entitled to know.
    ///
    /// This is the feature. DE-005 asks that nothing reaching the defender before they have
    /// committed carries the attacker's skill or the attacker's element, and the way to keep that
    /// true as fields get added is for there to be exactly one type that crosses the gap and for it
    /// to have no room in it for either. Every field here is a fact about the defender or about
    /// their situation; none of them is a fact about the attack.
    ///
    /// The attacker's identity is here because it is not a secret: they walked up and swung, and
    /// everybody watching saw it. What they swung with is the secret.
    ///
    /// Remaining AP is deliberately absent even though DE-005 lists it. Answering costs elements
    /// and never action points, so it would be a number on the prompt that no decision on the
    /// prompt depends on -- and every field here is one more thing a later change could leak
    /// through.
    /// </summary>
    public readonly struct DefenceRequest
    {
        /// <summary>Who is being attacked. The one deciding.</summary>
        public readonly int DefenderId;

        /// <summary>Who is attacking. Public: everyone watched them do it.</summary>
        public readonly int AttackerId;

        /// <summary>How many elements this defender has to put up. One, or two.</summary>
        public readonly int Required;

        /// <summary>What they may answer with, taken from their own pool.</summary>
        public readonly IReadOnlyList<Element> Options;

        /// <summary>Whether the blow arrived outside their front. Why they may be committing two.</summary>
        public readonly bool Flanked;

        /// <summary>Whether something they carry gives them the better of two.</summary>
        public readonly bool Shielded;

        public DefenceRequest(int defenderId, int attackerId, int required,
            IReadOnlyList<Element> options, bool flanked, bool shielded)
        {
            DefenderId = defenderId;
            AttackerId = attackerId;
            Required = required;
            Options = options ?? System.Array.Empty<Element>();
            Flanked = flanked;
            Shielded = shielded;
        }

        /// <summary>Whether there is a decision to make at all.</summary>
        public bool HasAnswer => Options.Count > 0;
    }

    /// <summary>What each side put in, and what came of it. Only once both sides are in.</summary>
    public readonly struct ClashReveal
    {
        public readonly IReadOnlyList<Element> Attacker;
        public readonly IReadOnlyList<Element> Defender;
        public readonly ClashOutcome Outcome;

        public ClashReveal(IReadOnlyList<Element> attacker, IReadOnlyList<Element> defender,
            ClashOutcome outcome)
        {
            Attacker = attacker ?? System.Array.Empty<Element>();
            Defender = defender ?? System.Array.Empty<Element>();
            Outcome = outcome;
        }
    }

    /// <summary>One side's standing before a clash: who, and how well placed.</summary>
    public readonly struct ClashSide
    {
        public readonly int CreatureId;
        public readonly bool Advantage;
        public readonly bool Disadvantage;

        public ClashSide(int creatureId, bool advantage = false, bool disadvantage = false)
        {
            CreatureId = creatureId;
            Advantage = advantage;
            Disadvantage = disadvantage;
        }

        public int Commitment => ClashRules.CommitmentFor(Advantage, Disadvantage);

        public ClashBias Bias => ClashRules.BiasFor(Advantage, Disadvantage);
    }

    /// <summary>
    /// One attack, from the moment it is thrown to the moment it lands.
    ///
    /// DE-005's central awkwardness is that resolution suspends: an attack stops partway and waits
    /// for a decision that may be coming from another machine, so it cannot be one call that
    /// returns a number. This is that pause, made into an object.
    ///
    /// It knows nothing about netcode, coroutines or scenes, and it must not learn. What crosses a
    /// wire is <see cref="Request"/> going one way and a list of elements coming back; everything
    /// else -- who is allowed to answer, how many, whether the answer was legal, and what the two
    /// commitments come to -- is decided here, on whichever machine is running the fight. A second
    /// implementation of any of that on the transport side would be a second opinion, and the two
    /// would eventually disagree about a fight somebody was in the middle of.
    ///
    /// **Concealment is ordering, not access control.** The attacker's commitment is held here from
    /// the start and simply is not handed out until the defender has answered: <see cref="Request"/>
    /// has no room for it and <see cref="TryReveal"/> refuses before then. Nothing has to remember
    /// to withhold it, and there is no projection-per-recipient to get wrong -- which is what lets
    /// one stream of events go to everybody.
    /// </summary>
    public sealed class ClashSequence
    {
        readonly ClashCommitment m_Attacker;
        readonly IElementMatchup m_Matchup;
        readonly ClashSide m_Defence;

        ClashCommitment m_Defender = ClashCommitment.None;
        ClashOutcome m_Outcome = ClashOutcome.AttackerWins;

        ClashSequence(ClashCommitment attacker, ClashSide attack, ClashSide defence,
            IReadOnlyList<Element> options, IElementMatchup matchup)
        {
            m_Attacker = attacker;
            m_Defence = defence;
            m_Matchup = matchup;

            Request = new DefenceRequest(defence.CreatureId, attack.CreatureId,
                defence.Commitment, options, defence.Disadvantage, defence.Advantage);
        }

        /// <summary>
        /// Throws an attack, and waits.
        ///
        /// The attacker's elements have already left their pool by the time this is called: DE-005
        /// spends the commitment at step one, before anybody is asked anything, so an attack cannot
        /// be taken back once the defender has been made to think about it.
        /// </summary>
        /// <param name="element">What the skill commits, one entry per unit of its cost.</param>
        public static ClashSequence Begin(IReadOnlyList<Element> element, ClashSide attack,
            ClashSide defence, ElementLedger defenderPool, IElementMatchup matchup) =>
            new ClashSequence(new ClashCommitment(element, attack.Bias), attack, defence,
                ClashRules.AnswersFor(defenderPool), matchup);

        public ClashPhase Phase { get; private set; } = ClashPhase.AwaitingDefence;

        /// <summary>What the defender may be shown. Safe to send anywhere.</summary>
        public DefenceRequest Request { get; }

        /// <summary>
        /// Answers the attack.
        ///
        /// More elements than were asked for is refused rather than trimmed: a client sending three
        /// when two were required has either a bug or a motive, and quietly using the first two
        /// would hide both. Fewer is accepted, because a defender required to commit two while
        /// holding one commits the one -- that is a rule, not a mistake.
        /// </summary>
        public bool TryCommit(IReadOnlyList<Element> answer, ElementLedger pool,
            out DefenceRefusal refusal)
        {
            refusal = DefenceRefusal.None;

            if (Phase != ClashPhase.AwaitingDefence)
            {
                refusal = DefenceRefusal.AlreadyResolved;
                return false;
            }

            var elements = answer ?? System.Array.Empty<Element>();

            if (elements.Count > Request.Required)
            {
                refusal = DefenceRefusal.TooMany;
                return false;
            }

            // Checked against the pool rather than against the offered list, because the offered
            // list was built from the pool and a defender answering twice with an element they hold
            // one of would otherwise pass.
            var taken = ElementCounts.Empty;

            foreach (var element in elements)
            {
                taken = taken.Plus(element, 1);

                if (!pool.Pool.Holds(element, taken[element]))
                {
                    refusal = DefenceRefusal.NotHeld;
                    return false;
                }
            }

            m_Defender = new ClashCommitment(elements, m_Defence.Bias);
            Settle();
            return true;
        }

        /// <summary>
        /// Takes the attack rather than paying for it.
        ///
        /// A defender holding nothing they can spend reaches the same place, and so does one that
        /// simply would rather keep what they have. DE-005 allows both, and neither is a failure --
        /// spending an element to blunt a small hit is a bad trade a player should be free not to
        /// make.
        /// </summary>
        public void Decline()
        {
            if (Phase != ClashPhase.AwaitingDefence)
            {
                return;
            }

            m_Defender = ClashCommitment.None;
            Settle();
        }

        /// <summary>
        /// What both sides put in and what came of it, once both sides are in.
        ///
        /// False while the clash is still waiting, which is the whole of the concealment: there is
        /// no accessor that hands out the attacker's element early, so nothing above this has to
        /// remember not to ask.
        /// </summary>
        public bool TryReveal(out ClashReveal reveal)
        {
            if (Phase != ClashPhase.Resolved)
            {
                reveal = default;
                return false;
            }

            reveal = new ClashReveal(m_Attacker.Elements, m_Defender.Elements, m_Outcome);
            return true;
        }

        /// <summary>What the outcome leaves of an effect. Only meaningful once resolved.</summary>
        public SkillEffect Scale(SkillEffect effect) =>
            Phase == ClashPhase.Resolved ? ClashRules.Scale(effect, m_Outcome) : effect;

        void Settle()
        {
            m_Outcome = ClashRules.Resolve(m_Attacker, m_Defender, m_Matchup);
            Phase = ClashPhase.Resolved;
        }
    }
}
