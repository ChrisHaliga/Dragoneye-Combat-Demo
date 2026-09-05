using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Authors which element answers which, and hands the table to the catalog.
    ///
    /// Three tiers in a ring, so nothing is simply best and price buys reach rather than power:
    ///
    ///   * the four common elements answer Arcana, the dearest thing anybody can hold
    ///   * Arcana answers Lux and Nyx
    ///   * Lux and Nyx answer the four commons
    ///
    /// and inside the commons a second ring, so a fight between two creatures holding nothing but
    /// commons is still a real question: Pyro over Aero over Geo over Hydro over Pyro. The two
    /// facing pairs -- Pyro against Geo, Hydro against Aero -- are even, which is what makes the
    /// inner ring a ring rather than an order.
    ///
    /// Lux against Nyx is even. They are opposed rather than ranked, and a table where one of them
    /// beat the other would make the loser a strictly worse two-point element.
    ///
    /// All of it is content. Written here so a fresh clone has a table rather than a project where
    /// every clash ties, and editable in the inspector afterwards -- re-running this puts it back.
    /// </summary>
    static class ElementMatchupSetup
    {
        const string k_Asset = "Assets/Settings/Characters/ElementMatchups.asset";
        const string k_Catalog = "Assets/Settings/Characters/ContentCatalog.asset";

        static readonly Element[] k_Commons =
        {
            Element.Geo, Element.Hydro, Element.Pyro, Element.Aero
        };

        static readonly Element[] k_Opposed = { Element.Lux, Element.Nyx };

        internal static void Run()
        {
            var beats = new List<(Element Winner, Element Loser)>
            {
                // The inner ring, among the commons.
                (Element.Pyro, Element.Aero),
                (Element.Aero, Element.Geo),
                (Element.Geo, Element.Hydro),
                (Element.Hydro, Element.Pyro)
            };

            foreach (var common in k_Commons)
            {
                // The cheap answer the dearest thing in the game.
                beats.Add((common, Element.Arcana));

                foreach (var opposed in k_Opposed)
                {
                    beats.Add((opposed, common));
                }
            }

            foreach (var opposed in k_Opposed)
            {
                beats.Add((Element.Arcana, opposed));
            }

            Write(beats);
        }

        static void Write(List<(Element Winner, Element Loser)> beats)
        {
            var table = Upsert<ElementMatchupTable>(k_Asset);
            var serialized = new SerializedObject(table);
            var list = serialized.FindProperty("m_Beats");

            list.arraySize = beats.Count;

            for (var i = 0; i < beats.Count; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);

                entry.FindPropertyRelative("Winner").enumValueIndex = (int)beats[i].Winner;
                entry.FindPropertyRelative("Loser").enumValueIndex = (int)beats[i].Loser;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);

            Attach(table);
            AssetDatabase.SaveAssets();
        }

        static void Attach(ElementMatchupTable table)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(k_Catalog);

            if (catalog == null)
            {
                Debug.LogWarning($"No content catalog at {k_Catalog}; the matchups were not attached.");
                return;
            }

            var serialized = new SerializedObject(catalog);
            serialized.FindProperty("m_ElementMatchups").objectReferenceValue = table;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(catalog);
        }

        static T Upsert<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
