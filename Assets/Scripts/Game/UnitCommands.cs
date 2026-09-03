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

        /// <summary>Client-side entry point. Asks the server to move this creature.</summary>
        public void RequestMove(Hex target)
        {
            if (CanCommand())
            {
                RequestMoveRpc(new NetCell(target));
            }
        }

        /// <summary>Client-side entry point. Asks the server to attack the creature on a hex.</summary>
        public void RequestAttack(Hex target)
        {
            if (CanCommand())
            {
                RequestAttackRpc(new NetCell(target));
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
        void RequestMoveRpc(NetCell cell, RpcParams rpc = default)
        {
            if (!SenderControlsThis(rpc.Receive.SenderClientId) || CombatDirector.Current == null)
            {
                return;
            }

            // Refusals are ordinary -- a misclick on unreachable ground is one -- so this is verbose
            // rather than a warning. The client is simply not told; its creature does not move.
            if (!CombatDirector.Current.ServerMove(m_Creature, cell.ToHex()))
            {
                Debug.Log($"[UnitCommands] Move to {cell.ToHex()} refused.", this);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestAttackRpc(NetCell cell, RpcParams rpc = default)
        {
            if (!SenderControlsThis(rpc.Receive.SenderClientId) || CombatDirector.Current == null)
            {
                return;
            }

            var context = ArenaContext.Current;
            if (context == null || context.Units == null
                || !context.Units.TryGet(cell.ToHex(), out var occupant))
            {
                return;
            }

            // Resolved from the hex rather than taken as a creature reference: the client names a
            // place, and the server decides what is standing there. A client naming an object
            // directly could name one that has since moved or died.
            if (!CombatDirector.Current.ServerAttack(m_Creature, occupant.GetComponent<CreatureState>()))
            {
                Debug.Log($"[UnitCommands] Attack on {cell.ToHex()} refused.", this);
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
