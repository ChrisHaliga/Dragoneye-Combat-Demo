using System;
using Dragoneye.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>The chance to swing at somebody walking past, as it reaches whoever gets it.</summary>
    public readonly struct OpportunityOffer
    {
        /// <summary>The creature being offered the swing.</summary>
        public readonly uint WatcherId;

        /// <summary>Who is trying to move.</summary>
        public readonly uint MoverId;

        /// <summary>The attack it would swing with, or null if it somehow has none.</summary>
        public readonly SkillSpec Swing;

        public OpportunityOffer(uint watcherId, uint moverId, SkillSpec swing)
        {
            WatcherId = watcherId;
            MoverId = moverId;
            Swing = swing;
        }
    }

    /// <summary>
    /// Carries the offer of an opportunity attack out, and the answer back.
    ///
    /// A postbox, exactly like <see cref="ClashCommands"/> and for the same reasons. Whether a
    /// swing is allowed, what it costs and what it comes to are decided by
    /// <see cref="CombatDirector"/> and the rules underneath it; this only asks the question on the
    /// right machine.
    ///
    /// Separate from the clash postbox because the two ask opposite people. A clash asks a defender
    /// what they will put up against something already committed; this asks an attacker whether to
    /// commit at all. Folding them together would mean one message meaning two things depending on
    /// a flag, and one prompt deciding which of two panels to be.
    ///
    /// What crosses the wire is the element and what it costs -- three numbers -- rather than a
    /// skill. The receiver already knows what its own weapon is; sending the attack whole would be
    /// sending them their own inventory back.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class OpportunityCommands : NetworkBehaviour
    {
        /// <summary>The one in the arena. Null outside a match.</summary>
        public static OpportunityCommands Current { get; private set; }

        /// <summary>Raised on the machine that gets the swing.</summary>
        public static event Action<OpportunityOffer> Offered;

        /// <summary>Raised when the offer is no longer open, answered or otherwise.</summary>
        public static event Action Closed;

        // Server-side: who was asked, so an answer from anybody else is ignored.
        CreatureState m_Asked;

        // Whose action is being held while the watchers decide. Replicated, unlike the offer,
        // because the mover's own client has nothing on screen during it -- and a mover who cannot
        // see that the game is waiting clicks again and is refused for it.
        readonly NetworkVariable<uint> m_HoldingId = new NetworkVariable<uint>();

        public override void OnNetworkSpawn() => Current = this;

        public override void OnNetworkDespawn()
        {
            if (Current == this)
            {
                Current = null;
            }

            // A match ending under an open offer must not leave one on screen.
            Closed?.Invoke();
        }

        /// <summary>Whether an action is being held while somebody decides, as every client sees it.</summary>
        public bool IsHolding => m_HoldingId.Value != 0;

        /// <summary>Server only. The mover's action is held from here until it runs.</summary>
        public void ServerHold(CreatureState mover)
        {
            if (IsServer && mover != null)
            {
                m_HoldingId.Value = mover.TurnId;
            }
        }

        /// <summary>Server only. The held action has run, or will never.</summary>
        public void ServerRelease()
        {
            if (IsServer)
            {
                m_HoldingId.Value = 0;
            }
        }

        /// <summary>Server only. Offers the swing to whoever runs the watching creature.</summary>
        public void ServerOffer(CreatureState watcher, CreatureState mover, SkillSpec swing)
        {
            if (!IsServer || watcher == null || mover == null || swing == null)
            {
                return;
            }

            m_Asked = watcher;

            OfferRpc(watcher.TurnId, mover.TurnId, (byte)swing.Element, swing.ElementCost,
                swing.Effect.Amount,
                RpcTarget.Single(watcher.OwnerClientId, RpcTargetUse.Temp));
        }

        /// <summary>Server only. Takes the offer down once it has been answered or lapsed.</summary>
        public void ServerClearOffer()
        {
            if (!IsServer)
            {
                return;
            }

            var asked = m_Asked;
            m_Asked = null;

            if (asked != null)
            {
                ClosedRpc(RpcTarget.Single(asked.OwnerClientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void OfferRpc(uint watcherId, uint moverId, byte element, int elementCost, int damage,
            RpcParams rpc = default)
        {
            var chosen = (Element)element;

            // An element arrives as a byte, and casting to an enum is not a checked conversion.
            if (!ElementInfo.IsDefined(chosen))
            {
                return;
            }

            // Rebuilt rather than sent: what a swing is made of is three numbers, and reassembling
            // it here keeps the message small and the shape of the thing in one place.
            var swing = Opportunity.From(new SkillSpec(Opportunity.SkillId, Opportunity.Name,
                chosen, Ap.Zero, elementCost, Opportunity.Range, SkillTarget.Creature,
                new SkillEffect(SkillEffectKind.Damage, damage)));

            Offered?.Invoke(new OpportunityOffer(watcherId, moverId, swing));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ClosedRpc(RpcParams rpc = default) => Closed?.Invoke();

        /// <summary>Client-side entry point. Take the swing, or let them go.</summary>
        public void Answer(bool swings) => AnswerRpc(swings);

        /// <summary>
        /// The answer, from the client that was asked.
        ///
        /// Which creature is swinging is resolved from who sent this, not from the payload, and the
        /// director refuses anything the watcher cannot actually pay.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void AnswerRpc(bool swings, RpcParams rpc = default)
        {
            var director = CombatDirector.Current;

            if (director == null || m_Asked == null)
            {
                return;
            }

            if (m_Asked.OwnerClientId != rpc.Receive.SenderClientId)
            {
                Debug.LogWarning($"Client {rpc.Receive.SenderClientId} answered an opportunity it "
                    + "is not part of; ignoring it.", this);
                return;
            }

            director.ServerAnswerOpportunity(m_Asked, swings);
        }
    }
}
