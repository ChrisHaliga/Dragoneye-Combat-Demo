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
    /// The only way a client can ask a unit to move.
    ///
    /// Clients send an intent; the server validates and writes. Three separate checks, and all three
    /// are needed:
    ///
    /// <c>InvokePermission.Owner</c> is transport-level -- it stops a client sending for an object
    /// it does not own at all. It is not the game rule: unclaimed creatures are owned by the server,
    /// so it passes for a host on every computer-run creature on the board.
    ///
    /// The controller slot is the game rule, and it is checked against the *sender*, so a client
    /// cannot move a creature by claiming to be someone else.
    ///
    /// <see cref="MoveRules"/> then decides whether the destination is legal. Neither of the other
    /// two says anything about whether the target hex exists, is walkable, or is already taken.
    ///
    /// Note the permission is explicit. <c>[Rpc(SendTo.Server)]</c> on its own defaults to
    /// <c>RpcInvokePermission.Everyone</c>, which would let anyone move anyone's unit.
    /// </summary>
    [RequireComponent(typeof(UnitState))]
    [DisallowMultipleComponent]
    public sealed class UnitCommands : NetworkBehaviour
    {
        UnitState m_State;
        CreatureState m_Creature;

        void Awake()
        {
            m_State = GetComponent<UnitState>();
            m_Creature = GetComponent<CreatureState>();
        }

        /// <summary>Client-side entry point. Asks the server to move this unit.</summary>
        public void RequestMove(Hex target)
        {
            if (LocalPlayer.Controls(m_Creature))
            {
                RequestMoveRpc(new NetCell(target));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestMoveRpc(NetCell cell, RpcParams rpc = default)
        {
            var context = ArenaContext.Current;
            if (context == null || context.Map == null || context.Map.Map == null)
            {
                return;
            }

            if (!SenderControlsThis(rpc.Receive.SenderClientId))
            {
                Debug.Log($"[UnitCommands] Move refused: client {rpc.Receive.SenderClientId} "
                    + "does not control this creature.", this);
                return;
            }

            var target = cell.ToHex();
            var units = context.Units;
            var occupied = units != null && units.IsOccupiedByOther(target, m_State);

            if (!MoveRules.CanEnter(context.Map.Map, m_State.Cell, target, occupied, out var rejection))
            {
                // Refusals are ordinary -- a misclick on empty space is one -- so this is verbose
                // rather than a warning. The client is simply not told; its unit does not move.
                Debug.Log($"[UnitCommands] Move to {target} refused: {rejection}", this);
                return;
            }

            m_State.ServerSetCell(target);
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
