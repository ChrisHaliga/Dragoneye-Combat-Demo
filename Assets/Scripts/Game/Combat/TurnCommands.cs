using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The player's request to end their creature's turn.
    ///
    /// Separate from <see cref="UnitCommands"/> and sent on the shared turn object, because ending a
    /// turn is a statement about the match rather than about a creature -- and because the creature
    /// whose turn is ending may be about to stop existing.
    ///
    /// <c>InvokePermission.Everyone</c> is correct here and is not a hole: nobody owns the turn
    /// object, so restricting by ownership would let nobody end a turn at all. The handler resolves
    /// the sender's slot and refuses anyone who is not the active creature's controller, which is
    /// the check that actually matters.
    /// </summary>
    [RequireComponent(typeof(TurnState))]
    [DisallowMultipleComponent]
    public sealed class TurnCommands : NetworkBehaviour
    {
        /// <summary>Client-side entry point. Asks the server to end the active creature's turn.</summary>
        public void RequestEndTurn()
        {
            if (TurnState.Current != null && !TurnState.Current.IsOver)
            {
                RequestEndTurnRpc();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void RequestEndTurnRpc(RpcParams rpc = default)
        {
            var director = CombatDirector.Current;
            var turns = TurnState.Current;

            if (director == null || turns == null || turns.IsOver)
            {
                return;
            }

            if (!SenderOwnsTheTurn(rpc.Receive.SenderClientId))
            {
                Debug.Log($"[TurnCommands] End turn refused: client {rpc.Receive.SenderClientId} "
                    + "does not control the active creature.", this);
                return;
            }

            director.ServerEndTurn();
        }

        /// <summary>
        /// Whether the sender controls the creature whose turn it currently is.
        ///
        /// Without this any client could end any other player's turn, which is a more annoying grief
        /// than it sounds: it costs the victim their whole round.
        /// </summary>
        bool SenderOwnsTheTurn(ulong senderClientId)
        {
            var roster = PlayerRoster.Current;
            var context = ArenaContext.Current;

            if (roster == null || context == null || context.Creatures == null
                || !roster.TryGet(senderClientId, out var entry)
                || entry.Slot < 0 || entry.Slot > byte.MaxValue)
            {
                return false;
            }

            var activeId = TurnState.Current.ActiveId;

            foreach (var creature in context.Creatures.All)
            {
                if (creature != null && creature.TurnId == activeId)
                {
                    return LocalPlayer.Controls(creature.ControllerSlot, (byte)entry.Slot);
                }
            }

            return false;
        }
    }
}
