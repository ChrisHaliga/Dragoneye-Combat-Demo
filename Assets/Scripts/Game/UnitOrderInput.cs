using Dragoneye.Hex;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Turns a click on a hex into an order for the unit this client controls.
    ///
    /// The seam between UX and systems: the pointer knows nothing about units, and
    /// <see cref="UnitCommands"/> knows nothing about mice. This is the only thing that knows a
    /// click should mean "move".
    ///
    /// Clicking a hex that already holds a unit is not treated specially yet -- the order is sent
    /// and the server refuses it as occupied. Selection and targeting will branch here, using
    /// <see cref="UnitIndex.TryGet"/>, without either side needing to change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitOrderInput : MonoBehaviour
    {
        [SerializeField]
        HexPointer m_Pointer;

        [SerializeField]
        UnitIndex m_Units;

        void OnEnable()
        {
            if (m_Pointer == null || m_Units == null)
            {
                Debug.LogError($"{nameof(UnitOrderInput)} is missing its pointer or unit index.", this);
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
            var unit = m_Units.LocalUnit;
            if (unit == null)
            {
                return;
            }

            var commands = unit.GetComponent<UnitCommands>();
            if (commands != null)
            {
                commands.RequestMove(hex);
            }
        }
    }
}
