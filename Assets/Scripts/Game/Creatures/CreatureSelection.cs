using System;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Which creature this player is looking at.
    ///
    /// Local only, and deliberately not replicated: what one player has selected is not another
    /// player's business, and sending it would put a per-frame UI concern on the wire.
    ///
    /// Two producers feed it -- a board click and a portrait click -- and both HUD views observe it,
    /// so the same card appears whichever way the creature was picked.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureSelection : MonoBehaviour
    {
        CreatureState m_Selected;

        public CreatureState Selected => m_Selected;

        public bool HasSelection => m_Selected != null;

        public event Action<CreatureState> SelectionChanged;

        void OnEnable() => CreatureRegistry.Changed += DropIfGone;

        void OnDisable() => CreatureRegistry.Changed -= DropIfGone;

        public void Select(CreatureState creature)
        {
            if (m_Selected == creature)
            {
                return;
            }

            m_Selected = creature;
            SelectionChanged?.Invoke(m_Selected);
        }

        public void Clear() => Select(null);

        /// <summary>A selected creature that despawns must not leave the card showing a ghost.</summary>
        void DropIfGone()
        {
            if (m_Selected == null && SelectionChanged != null)
            {
                SelectionChanged.Invoke(null);
            }
        }
    }
}
