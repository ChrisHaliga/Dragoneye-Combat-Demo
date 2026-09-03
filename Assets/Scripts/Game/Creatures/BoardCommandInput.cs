using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// What a click on the board means: select a creature, or order the selected one to move.
    ///
    /// One component, one subscription. Selection and orders used to be two components on the same
    /// click event, which made the outcome depend on subscription order -- and it is exactly the
    /// kind of race that looks fine until two handlers disagree about one click.
    ///
    /// Orders follow the selection rather than a single "my unit". A player who claims three
    /// creatures owns three units; picking one and telling it where to go is the standard tactics
    /// interaction, and it is the reason the draft exists at all.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardCommandInput : MonoBehaviour
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
                Debug.LogError($"{nameof(BoardCommandInput)} is missing references.", this);
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
            // Occupied: inspect whatever is standing there, whoever owns it. Selecting an enemy to
            // read its card is a normal thing to want.
            if (m_Units.TryGet(hex, out var occupant))
            {
                m_Selection.Select(occupant.GetComponent<CreatureState>());
                return;
            }

            // Empty ground: move the selection, if it is ours to move. The server checks ownership
            // again; this only avoids sending an order that is certain to be refused.
            var selected = m_Selection.Selected;
            if (selected == null || !selected.IsOwner)
            {
                m_Selection.Clear();
                return;
            }

            var commands = selected.GetComponent<UnitCommands>();
            if (commands != null)
            {
                commands.RequestMove(hex);
            }
        }
    }
}
