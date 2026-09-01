using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Turns a board click into a selection, and Escape into dismissing it.
    ///
    /// The click branch is the one <see cref="UnitIndex"/> was built for: a click already resolves
    /// to a hex, so "clicked a unit" is a dictionary lookup rather than a second raycast.
    ///
    /// Escape is deliberately not handled here. Two components racing for the same action would
    /// make the outcome depend on subscription order -- clear-then-leave or leave-then-clear.
    /// <see cref="MatchInput"/> owns it and dismisses the card before it will leave a match.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardSelectionInput : MonoBehaviour
    {
        [SerializeField]
        HexPointer m_Pointer;

        [SerializeField]
        UnitIndex m_Units;

        [SerializeField]
        CreatureSelection m_Selection;

        void OnEnable()
        {
            if (m_Pointer == null || m_Units == null || m_Selection == null)
            {
                Debug.LogError($"{nameof(BoardSelectionInput)} is missing references.", this);
                enabled = false;
                return;
            }

            m_Pointer.Clicked += OnHexClicked;
        }

        void OnDisable()
        {
            if (m_Pointer != null)
            {
                m_Pointer.Clicked -= OnHexClicked;
            }
        }

        void OnHexClicked(Hex hex)
        {
            // Clicking a creature selects it; clicking bare ground clears the card. The move order
            // is issued separately by UnitOrderInput, so both can respond to the same click.
            m_Selection.Select(
                m_Units.TryGet(hex, out var unit) ? unit.GetComponent<CreatureState>() : null);
        }
    }
}
