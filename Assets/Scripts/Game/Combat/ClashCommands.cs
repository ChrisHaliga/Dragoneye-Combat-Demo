using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Carries a clash's one question to whoever has to answer it, and the answer back.
    ///
    /// A postbox and nothing else. Every decision about a clash -- who may answer, how many
    /// elements, whether an answer was legal, and what the two commitments come to -- is made by
    /// <see cref="ClashSequence"/> in <c>Dragoneye.Combat</c>, on the machine running the fight.
    /// This exists because that machine and the defender are sometimes not the same machine.
    ///
    /// It is deliberately thin. Anything decided here would be decided a second time, differently,
    /// the first time somebody changed a rule -- and a fight that resolves one way on the host and
    /// another on a client is the hardest kind of bug this project could grow.
    ///
    /// **The concealment is structural rather than careful.** What goes out is a
    /// <see cref="DefenceRequest"/>, which has no field that could carry the attacker's skill or
    /// element, so there is no discipline to keep: the message cannot say the thing it must not say.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class ClashCommands : NetworkBehaviour
    {
        /// <summary>The one in the arena. Null outside a match.</summary>
        public static ClashCommands Current { get; private set; }

        /// <summary>
        /// Raised on the machine that has to answer, with everything it is allowed to know.
        ///
        /// Local, and never replicated onward: this is the prompt, not a fact about the fight.
        /// </summary>
        public static event Action<DefenceRequest> Asked;

        /// <summary>Raised when the question is no longer open, answered or otherwise.</summary>
        public static event Action Closed;

        // Server-side: who was asked, so an answer arriving from anybody else is ignored.
        CreatureState m_Asked;

        public override void OnNetworkSpawn() => Current = this;

        public override void OnNetworkDespawn()
        {
            if (Current == this)
            {
                Current = null;
            }

            // A match ending under an open prompt must not leave one on screen.
            Closed?.Invoke();
        }

        /// <summary>
        /// Server only. Puts the question to the client running the defender.
        ///
        /// Sent to that one client rather than broadcast. Not because the contents are secret --
        /// they are the defender's own business and nothing else -- but because a prompt is a thing
        /// to act on, and four players receiving one they cannot answer is four players wondering
        /// which of them the game is waiting for.
        /// </summary>
        public void ServerAsk(DefenceRequest request, CreatureState defender)
        {
            if (!IsServer || defender == null)
            {
                return;
            }

            m_Asked = defender;

            var options = new byte[request.Options.Count];

            for (var i = 0; i < options.Length; i++)
            {
                options[i] = (byte)request.Options[i];
            }

            AskRpc(request.DefenderId, request.AttackerId, request.Required, options,
                request.Flanked, request.Shielded,
                RpcTarget.Single(defender.OwnerClientId, RpcTargetUse.Temp));
        }

        /// <summary>Server only. Takes the prompt down once the clash is over.</summary>
        public void ServerClearPrompt()
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
        void AskRpc(int defenderId, int attackerId, int required, byte[] options, bool flanked,
            bool shielded, RpcParams rpc = default)
        {
            var elements = new List<Element>(options.Length);

            foreach (var option in options)
            {
                var element = (Element)option;

                // An element arrives as a byte, and casting to an enum is not a checked conversion.
                if (ElementInfo.IsDefined(element))
                {
                    elements.Add(element);
                }
            }

            Asked?.Invoke(new DefenceRequest(defenderId, attackerId, required, elements,
                flanked, shielded));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ClosedRpc(RpcParams rpc = default) => Closed?.Invoke();

        /// <summary>Client-side entry point. Answers with elements, or with nothing to decline.</summary>
        public void Answer(IReadOnlyList<Element> elements)
        {
            var payload = new byte[elements?.Count ?? 0];

            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)elements[i];
            }

            AnswerRpc(payload);
        }

        /// <summary>
        /// The answer, from the client that was asked.
        ///
        /// The sender decides nothing. Which creature they are answering for is resolved from who
        /// they are, not from the payload, and the answer itself is handed to the sequence, which
        /// refuses anything the defender cannot actually pay.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void AnswerRpc(byte[] elements, RpcParams rpc = default)
        {
            var director = CombatDirector.Current;

            if (director == null || m_Asked == null)
            {
                return;
            }

            if (m_Asked.OwnerClientId != rpc.Receive.SenderClientId)
            {
                Debug.LogWarning($"Client {rpc.Receive.SenderClientId} answered a clash it is not "
                    + "part of; ignoring it.", this);
                return;
            }

            var answer = new List<Element>(elements.Length);

            foreach (var raw in elements)
            {
                var element = (Element)raw;

                if (ElementInfo.IsDefined(element))
                {
                    answer.Add(element);
                }
            }

            if (!director.ServerAnswerClash(m_Asked, answer, out var refusal))
            {
                Debug.Log($"[ClashCommands] Answer refused: {refusal}.", this);
            }
        }
    }
}
