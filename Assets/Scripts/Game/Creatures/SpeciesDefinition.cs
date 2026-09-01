using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>What a creature is. Flavour today; a source of stat modifiers later.</summary>
    [CreateAssetMenu(menuName = "Dragoneye/Species", fileName = "Species")]
    public sealed class SpeciesDefinition : ScriptableObject
    {
        [SerializeField]
        string m_DisplayName = "Species";

        [SerializeField, TextArea(2, 5)]
        string m_Description = "";

        public string DisplayName => m_DisplayName;

        public string Description => m_Description;
    }
}
