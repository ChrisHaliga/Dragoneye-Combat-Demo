using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Game.Creatures;
using UnityEngine;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Everywhere the acting creature could walk this turn, lit on the floor while Move is armed.
    ///
    /// Each reachable cell gets its area's own fan, so a split tile lights only the half that can
    /// be reached -- which is the whole point of drawing this once walls exist. Read from the
    /// board's own search, so what lights up is exactly what a click would be allowed to do.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReachPreview : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which knows the actor and what is armed.")]
        BoardActionInput m_Input;

        [SerializeField, Range(0f, 1f)]
        float m_Opacity = 0.16f;

        [SerializeField, Min(0f), Tooltip("Height above the floor, to avoid z-fighting.")]
        float m_Lift = 0.025f;

        readonly Dictionary<Cell, int> m_Reach = new Dictionary<Cell, int>();
        readonly List<GameObject> m_Pieces = new List<GameObject>();

        Material m_Material;
        Transform m_Root;

        // What the overlay was last built for, so it is rebuilt only when that changes.
        uint m_ForCreature;
        int m_ForAp = -1;
        Cell m_ForCell;
        int m_ForFrame = -1;

        void Update()
        {
            var actor = m_Input != null ? m_Input.Actor : null;
            var bar = m_Input != null ? m_Input.SkillBar : null;
            var arena = ArenaContext.Current != null ? ArenaContext.Current.Map : null;

            var wanted = actor != null && bar != null && bar.SelectedSkill == SkillBarView.MoveSkill
                && !m_Input.PendingMove.HasValue && arena != null && arena.Map != null
                && m_Input.Board != null && !FightPause.IsPaused;

            if (!wanted)
            {
                Hide();
                m_ForAp = -1;
                return;
            }

            // Occupancy can change under a still actor, so the overlay is refreshed on a slow
            // cadence as well as on every change that is cheap to notice.
            var stale = m_ForCreature != actor.TurnId || m_ForAp != actor.CurrentAp.Units
                || m_ForCell != actor.Cell || Time.frameCount - m_ForFrame > 20;

            if (!stale)
            {
                return;
            }

            m_ForCreature = actor.TurnId;
            m_ForAp = actor.CurrentAp.Units;
            m_ForCell = actor.Cell;
            m_ForFrame = Time.frameCount;

            var budget = CombatRules.StepsAffordable(actor.CurrentAp, actor.StepCost);
            m_Input.Board.Reachable(actor.Cell, budget, m_Reach);

            Build(actor);

            var tint = PartyPalette.ForParty(actor.Party);
            tint.a = m_Opacity;
            m_Material.color = tint;
            m_Material.SetColor("_BaseColor", tint);

            var index = 0;

            foreach (var pair in m_Reach)
            {
                var piece = Piece(index++);
                piece.GetComponent<MeshFilter>().sharedMesh = AreaMeshes.For(arena.Map, pair.Key);
                piece.transform.position = arena.ToWorld(pair.Key.Tile) + Vector3.up * m_Lift;
                piece.transform.rotation = arena.transform.rotation;
                piece.SetActive(true);
            }

            for (var i = index; i < m_Pieces.Count; i++)
            {
                m_Pieces[i].SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (m_Material != null)
            {
                Destroy(m_Material);
            }
        }

        void Hide()
        {
            foreach (var piece in m_Pieces)
            {
                piece.SetActive(false);
            }
        }

        GameObject Piece(int index)
        {
            while (m_Pieces.Count <= index)
            {
                var piece = new GameObject($"Reach {m_Pieces.Count}", typeof(MeshFilter), typeof(MeshRenderer));
                piece.transform.SetParent(m_Root, false);

                var renderer = piece.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = m_Material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                m_Pieces.Add(piece);
            }

            return m_Pieces[index];
        }

        void Build(CreatureState actor)
        {
            if (m_Material != null)
            {
                return;
            }

            m_Root = new GameObject("Reach").transform;
            m_Root.SetParent(transform, false);

            m_Material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")) { name = "Reach" };
            m_Material.SetFloat("_Surface", 1f);
            m_Material.SetFloat("_ZWrite", 0f);
            m_Material.SetOverrideTag("RenderType", "Transparent");
            m_Material.renderQueue = 2999;
            m_Material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m_Material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m_Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
