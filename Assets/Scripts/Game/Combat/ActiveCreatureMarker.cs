using Dragoneye.Combat;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Two rings on the board: a slow-breathing one of party colour under whichever creature's turn
    /// it is, and a still, pale one under whichever creature is being looked at.
    ///
    /// The turn bar says whose turn it is; this says *where* they are. On a board of eight tokens
    /// the eye goes to the one that is lit, and a ring that breathes is lit in a way a static one is
    /// not.
    ///
    /// Purely presentation, and read entirely from the shown fight: which creature's turn is being
    /// watched, and where its token is standing. It follows the token's transform rather than its
    /// cell, so it walks with a creature that is still being drawn arriving somewhere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActiveCreatureMarker : MonoBehaviour
    {
        [SerializeField, Tooltip("Every creature on the board, to find the active one.")]
        CreatureRegistry m_Creatures;

        [SerializeField, Tooltip("What the player has clicked on, to ring it. Optional.")]
        CreatureSelection m_Selection;

        [SerializeField, Min(0.5f), Tooltip("Ring diameter, as a multiple of the token's.")]
        float m_Size = 2.3f;

        [SerializeField, Range(0f, 0.3f), Tooltip("How much the ring swells at the top of a breath.")]
        float m_Swell = 0.07f;

        [SerializeField, Min(0.1f), Tooltip("Seconds per breath.")]
        float m_Breath = 2.2f;

        Transform m_Ring;
        Material m_Material;
        uint m_Shown;

        Transform m_Picked;
        Material m_PickedMaterial;

        void Update()
        {
            MarkSelection();

            var fight = Shown.Fight;
            var active = fight != null && fight.Began && !fight.IsOver && m_Creatures != null
                ? m_Creatures.ByTurnId(fight.ActiveId)
                : null;

            if (active == null)
            {
                Hide();
                return;
            }

            Build();

            if (m_Shown != active.TurnId)
            {
                m_Shown = active.TurnId;

                var tint = PartyPalette.ForParty(active.Party);
                m_Material.color = tint;
                m_Material.SetColor("_BaseColor", tint);
            }

            var phase = (Mathf.Sin(Time.time / m_Breath * Mathf.PI * 2f) + 1f) * 0.5f;
            var diameter = CreatureToken.Radius * 2f * m_Size * (1f + (m_Swell * phase));

            m_Ring.gameObject.SetActive(true);
            m_Ring.position = active.transform.position
                + (Vector3.up * (0.006f - GroundOf(active)));
            m_Ring.localScale = new Vector3(diameter, 1f, diameter);

            var tintNow = m_Material.color;
            tintNow.a = Mathf.Lerp(0.55f, 0.9f, phase);
            m_Material.color = tintNow;
            m_Material.SetColor("_BaseColor", tintNow);
        }

        /// <summary>How high the token sits above its tile, so the ring can be put on the tile itself.</summary>
        static float GroundOf(CreatureState creature)
        {
            var view = UnitView.Of(creature);
            return view != null ? view.GroundOffset : 0.5f;
        }

        void Hide()
        {
            if (m_Ring != null)
            {
                m_Ring.gameObject.SetActive(false);
            }

            m_Shown = 0;
        }

        /// <summary>
        /// The ring under whatever is selected. Still and pale, so it is obviously not the turn
        /// marker: one says "acting", the other says "being read".
        /// </summary>
        void MarkSelection()
        {
            var picked = m_Selection != null ? m_Selection.Selected : null;

            if (picked == null || !Shown.IsAlive(picked))
            {
                if (m_Picked != null)
                {
                    m_Picked.gameObject.SetActive(false);
                }

                return;
            }

            if (m_Picked == null)
            {
                m_PickedMaterial = new Material(WorldArt.Unlit("Halo", WorldArt.Halo, transparent: true))
                {
                    name = "Selection Halo"
                };

                var tint = new Color(0.93f, 0.9f, 0.82f, 0.5f);
                m_PickedMaterial.color = tint;
                m_PickedMaterial.SetColor("_BaseColor", tint);

                m_Picked = Ring("Selection Halo", m_PickedMaterial);
            }

            var diameter = CreatureToken.Radius * 2f * (m_Size - 0.35f);

            m_Picked.gameObject.SetActive(true);
            m_Picked.position = picked.transform.position + (Vector3.up * (0.005f - GroundOf(picked)));
            m_Picked.localScale = new Vector3(diameter, 1f, diameter);
        }

        Transform Ring(string name, Material material)
        {
            var ring = new GameObject(name);
            ring.transform.SetParent(transform, worldPositionStays: false);

            ring.AddComponent<MeshFilter>().sharedMesh = CreatureToken.Disc;

            var renderer = ring.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ring.transform;
        }

        void Build()
        {
            if (m_Ring != null)
            {
                return;
            }

            m_Material = new Material(WorldArt.Unlit("Halo", WorldArt.Halo, transparent: true))
            {
                name = "Active Halo"
            };

            m_Ring = Ring("Active Halo", m_Material);
        }

        void OnDestroy()
        {
            if (m_Material != null)
            {
                Destroy(m_Material);
            }

            if (m_PickedMaterial != null)
            {
                Destroy(m_PickedMaterial);
            }
        }
    }
}
