using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// An authored creature: who it is and what it starts with.
    ///
    /// Holds no party and no owner. Both are decided in the draft, so putting them here would bake a
    /// single match's arrangement into a reusable asset.
    ///
    /// Stats sit directly on the definition rather than being composed from species and class. There
    /// is nothing to compose yet, and a resolver built before any modifier exists would be a guess at
    /// rules that have not been designed.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Creature", fileName = "Creature")]
    public sealed class CreatureDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("Stable identifier. Hashed into the network id -- changing it "
             + "changes that id, so treat it as permanent once a creature ships.")]
        string m_Id = "";

        [SerializeField]
        string m_DisplayName = "Creature";

        [SerializeField, Tooltip("Optional. The HUD draws a lettered tile when this is empty.")]
        Sprite m_Portrait;

        [SerializeField]
        SpeciesDefinition m_Species;

        [SerializeField]
        ClassDefinition m_Class;

        [SerializeField, Min(1)]
        int m_MaxHp = 20;

        [SerializeField, Min(0)]
        int m_MaxAp = 6;

        [SerializeField, Min(0)]
        int m_Speed = 5;

        public string Id => m_Id;

        public string DisplayName => m_DisplayName;

        public Sprite Portrait => m_Portrait;

        public SpeciesDefinition Species => m_Species;

        public ClassDefinition Class => m_Class;

        public int MaxHp => m_MaxHp;

        public int MaxAp => m_MaxAp;

        public int Speed => m_Speed;

        public string SpeciesName => m_Species != null ? m_Species.DisplayName : "Unknown";

        public string ClassName => m_Class != null ? m_Class.DisplayName : "Unknown";

        public string Description => m_Species != null ? m_Species.Description : string.Empty;

        void OnValidate()
        {
            // An empty id would hash to the same value for every creature that forgot to set one,
            // which surfaces as the wrong creature appearing rather than as an error.
            if (string.IsNullOrWhiteSpace(m_Id))
            {
                m_Id = name;
            }
        }
    }
}
