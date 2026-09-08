using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;
using UnityEngine.Serialization;

namespace Dragoneye.Data
{
    /// <summary>
    /// The seven numbers a designer edits, in a shape Unity can serialise.
    ///
    /// <see cref="AttributeBlock"/> is immutable with readonly fields, which is right for a value
    /// the rules pass around and wrong for something an inspector writes into. This is the
    /// authoring form; it converts once, at load.
    /// </summary>
    [System.Serializable]
    public struct AttributeValues
    {
        public int Toughness;
        public int Dexterity;
        public int Strength;
        public int Skill;
        public int Vitality;
        public int Willpower;
        public int Endurance;

        public AttributeBlock ToBlock() =>
            new AttributeBlock(Toughness, Dexterity, Strength, Skill, Vitality, Willpower, Endurance);
    }

    /// <summary>
    /// Turns a list of assets into the list of ids the rules take.
    ///
    /// Nulls are dropped rather than passed through as zero, which is the reserved "nothing" id and
    /// would silently read as an empty slot.
    /// </summary>
    public static class ContentIds
    {
        public static List<int> SkillIds(List<SkillAsset> assets)
        {
            var ids = new List<int>();

            foreach (var asset in assets)
            {
                if (asset != null)
                {
                    ids.Add(asset.Id);
                }
            }

            return ids;
        }
    }
}
