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
        [SerializeField, Tooltip("Used to notice when the selected creature despawns.")]
        CreatureRegistry m_Creatures;

        CreatureState m_Selected;

        public CreatureState Selected => m_Selected;

        public bool HasSelection => m_Selected != null;

        public event Action<CreatureState> SelectionChanged;

        void OnEnable()
        {
            if (m_Creatures != null)
            {
                m_Creatures.Changed += DropIfGone;
            }
        }

        void OnDisable()
        {
            if (m_Creatures != null)
            {
                m_Creatures.Changed -= DropIfGone;
            }
        }

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

        /// <summary>
        /// A selected creature that despawns must not leave the card showing a ghost.
        ///
        /// ReferenceEquals distinguishes "the field is genuinely null" from Unity's fake-null, which
        /// a destroyed object compares equal to. Without it this fired on every spawn and despawn
        /// -- the ordinary nothing-selected case -- and the field was never actually cleared, so it
        /// kept a destroyed reference that the equality guard in Select behaved unpredictably against.
        /// </summary>
        void DropIfGone()
        {
            if (!ReferenceEquals(m_Selected, null) && m_Selected == null)
            {
                m_Selected = null;
                SelectionChanged?.Invoke(null);
            }
        }
    }
}
