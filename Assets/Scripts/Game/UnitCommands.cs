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
    /// Clients send an intent; the server validates and writes. Two separate checks, and both are
    /// needed: <c>InvokePermission.Owner</c> stops a client issuing orders for a unit it does not
    /// own, and <see cref="MoveRules"/> then decides whether the *destination* is legal. Ownership
    /// says nothing about whether the target hex exists, is walkable, or is already taken.
    ///
    /// Note the permission is explicit. <c>[Rpc(SendTo.Server)]</c> on its own defaults to
    /// <c>RpcInvokePermission.Everyone</c>, which would let anyone move anyone's unit.
    /// </summary>
    [RequireComponent(typeof(UnitState))]
    [DisallowMultipleComponent]
    public sealed class UnitCommands : NetworkBehaviour
    {
        UnitState m_State;

        void Awake() => m_State = GetComponent<UnitState>();

        /// <summary>Client-side entry point. Asks the server to move this unit.</summary>
        public void RequestMove(Hex target)
        {
            if (IsOwner)
            {
                RequestMoveRpc(new NetCell(target));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestMoveRpc(NetCell cell)
        {
            var context = ArenaContext.Current;
            if (context == null || context.Map == null || context.Map.Map == null)
            {
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
    }
}
