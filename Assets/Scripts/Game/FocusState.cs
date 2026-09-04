using Dragoneye.Hex.Systems;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The replicated identity of one player's focus point: which slot it belongs to.
    ///
    /// Data and network lifecycle only. It renders nothing, and nothing renders it any more: the
    /// disc and the floating name that used to sit on this are gone, because a coloured puck told a
    /// player nothing they could act on and the name over it answered a question nobody asked.
    /// What survives is the point the camera follows, which is the whole reason it is replicated --
    /// so the rules of who owns what stay readable without wading through material and label code.
    ///
    /// The name is not replicated here: <see cref="PlayerRoster"/> already carries it for every
    /// player, and duplicating it would mean two sources that can disagree.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class FocusState : NetworkBehaviour
    {
        [SerializeField, Tooltip("Local movement. Disabled on focus points we do not own.")]
        FocusPoint m_Focus;

        // Written by the server, which is the only place that hands out slots.
        readonly NetworkVariable<int> m_Slot = new NetworkVariable<int>(-1);

        /// <summary>Stable player slot, or -1 before the server has assigned one.</summary>
        public int Slot => m_Slot.Value;

        /// <summary>Raised when the slot arrives or changes, so views can repaint.</summary>
        public event System.Action SlotChanged;

        public override void OnNetworkSpawn()
        {
            if (m_Focus == null)
            {
                Debug.LogError($"{nameof(FocusState)} has no {nameof(FocusPoint)} assigned.", this);
                enabled = false;
                return;
            }

            m_Slot.OnValueChanged += OnSlotChanged;

            if (IsOwner)
            {
                var context = ArenaContext.Current;
                if (context != null)
                {
                    context.FollowFocus(m_Focus);
                }
                else
                {
                    Debug.LogError($"No {nameof(ArenaContext)} in the arena; the camera has nothing to follow.", this);
                }
            }
            else
            {
                // Remote focus points are display only -- their position arrives over the network.
                m_Focus.enabled = false;
            }
        }

        public override void OnNetworkDespawn() => m_Slot.OnValueChanged -= OnSlotChanged;

        /// <summary>Server only. Called by the spawner once the roster has issued a slot.</summary>
        public void AssignSlot(int slot)
        {
            if (IsServer)
            {
                m_Slot.Value = slot;
            }
        }

        void OnSlotChanged(int previous, int current) => SlotChanged?.Invoke();
    }
}
