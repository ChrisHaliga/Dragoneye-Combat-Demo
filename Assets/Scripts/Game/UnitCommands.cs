using Dragoneye.Combat;
using Dragoneye.Hex;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The only way a client can ask a creature to do something.
    ///
    /// Clients send an intent; the server validates and resolves. The checks stack, and each one
    /// covers a hole the others do not:
    ///
    /// <c>InvokePermission.Owner</c> is transport-level -- it stops a client sending for an object it
    /// does not own at all. It is not the game rule: unclaimed creatures are owned by the server, so
    /// it passes for a host on every computer-run creature on the board.
    ///
    /// The controller slot is the game rule for *whose* creature this is, and it is checked against
    /// the sender, so a client cannot act as someone else.
    ///
    /// <see cref="CombatDirector"/> then checks whether it is that creature's turn, whether it can
    /// afford the action, and whether the action is legal. None of the checks above say anything
    /// about any of that.
    ///
    /// Note the permission is explicit. <c>[Rpc(SendTo.Server)]</c> on its own defaults to
    /// <c>RpcInvokePermission.Everyone</c>, which would let anyone move anyone's unit.
    /// </summary>
    [RequireComponent(typeof(UnitState))]
    [DisallowMultipleComponent]
    public sealed class UnitCommands : NetworkBehaviour
    {
        CreatureState m_Creature;

        void Awake() => m_Creature = GetComponent<CreatureState>();

        /// <summary>
        /// Client-side entry point. Asks the server to move this creature.
        ///
        /// The facing travels with the destination rather than following as a second order. DE-006
        /// calls it part of the move intent, and it has to be: two orders could be interrupted
        /// between, which would be a free turn for anybody who timed it.
        /// </summary>
        /// <param name="facing">
        /// Which way to end up turned, or null for the direction of travel -- which is what a
        /// creature that walked somewhere is looking at.
        /// </param>
        public void RequestMove(Hex target, Facing? facing = null)
        {
            if (CanCommand())
            {
                // Serialised as an index with -1 for "whichever way I was walking", because a
                // nullable does not cross the wire and a sentinel is cheaper than a second field.
                RequestMoveRpc(new NetCell(target), facing.HasValue ? facing.Value.Index : -1);
            }
        }

        /// <summary>
        /// Whether it is worth sending anything at all: this creature is ours and it is its turn.
        ///
        /// A courtesy that saves a round trip, never a security measure -- the server repeats both
        /// checks and does not trust this one having run.
        /// </summary>
        bool CanCommand() =>
            LocalPlayer.Controls(m_Creature)
            && TurnState.Current != null
            && TurnState.Current.IsActive(m_Creature);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestMoveRpc(NetCell cell, int facing, RpcParams rpc = default)
        {
            if (!SenderControlsThis(rpc.Receive.SenderClientId) || CombatDirector.Current == null)
            {
                return;
            }

            var chosen = facing >= 0 ? Facing.Of(facing) : (Facing?)null;

            // Refusals are ordinary -- a misclick on unreachable ground is one -- so this is verbose
            // rather than a warning. The client is simply not told; its creature does not move.
            if (!CombatDirector.Current.ServerMove(m_Creature, cell.ToHex(), chosen))
            {
                Debug.Log($"[UnitCommands] Move to {cell.ToHex()} refused.", this);
            }
        }

        /// <summary>
        /// Whether the client that sent this order is the one the creature answers to.
        ///
        /// Resolved from the sender's id through the roster rather than trusting anything in the
        /// payload, so the only slot a client can act as is its own.
        /// </summary>
        bool SenderControlsThis(ulong senderClientId)
        {
            var roster = PlayerRoster.Current;

            return m_Creature != null
                && roster != null
                && roster.TryGet(senderClientId, out var entry)
                && entry.Slot >= 0
                && entry.Slot <= byte.MaxValue
                && LocalPlayer.Controls(m_Creature.ControllerSlot, (byte)entry.Slot);
        }
    }
}
