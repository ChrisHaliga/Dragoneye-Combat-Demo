using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// What a creature does. Deliberately a stub: when species or class should contribute to stats
    /// they become modifiers applied by a resolver, and pre-building that machinery before anything
    /// needs it would be guessing at the shape of rules that do not exist yet.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Class", fileName = "Class")]
    public sealed class ClassDefinition : ScriptableObject
    {
        [SerializeField]
        string m_DisplayName = "Class";

        public string DisplayName => m_DisplayName;
    }
}
