using System;
using Unity.Netcode;
using UnityEngine;
using Dragoneye.Sim;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Carries the record of the fight -- every <see cref="CombatEvent"/> -- from the machine
    /// running it to every machine watching it, in the order it happened.
    ///
    /// The one channel. The fight used to announce itself through a handful of typed messages
    /// and a dozen replicated numbers, and a view had to read the two together and hope they
    /// agreed about when. They did not, always: a health bar could drop before the blow that
    /// dropped it was shown. Now the numbers travel inside the events, and the events travel
    /// here, reliably and in order, and <see cref="CombatPlayback"/> reads them one at a time.
    ///
    /// It decides nothing and says nothing of its own. Every event is written by the director or
    /// one of its conductors at the moment the thing happened.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class CombatAnnouncer : NetworkBehaviour
    {
        /// <summary>The one in the arena. Null outside a match.</summary>
        public static CombatAnnouncer Current { get; private set; }

        /// <summary>
        /// Raised on every peer, the server included, as each event arrives.
        ///
        /// In the order the fight sent them: the transport is reliable and sequenced, so nothing
        /// here has to number anything. The playback queues what it hears; the scenario runner
        /// writes it straight down.
        /// </summary>
        public static event Action<CombatEvent> Received;

        public override void OnNetworkSpawn() => Current = this;

        public override void OnNetworkDespawn()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>Server only. Tells every machine one thing the fight did.</summary>
        public void ServerSend(CombatEvent e)
        {
            if (IsServer && e != null)
            {
                SendRpc(CombatEventCodec.Pack(e));
            }
        }

        [Rpc(SendTo.Everyone)]
        void SendRpc(int[] packed)
        {
            var e = CombatEventCodec.Unpack(packed);

            if (e == null)
            {
                Debug.LogError("A combat event arrived that could not be read; it is dropped.", this);
                return;
            }

            Received?.Invoke(e);
        }
    }
}
