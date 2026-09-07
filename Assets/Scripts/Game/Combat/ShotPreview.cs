using Dragoneye.Data;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;
using Dragoneye.Hex;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The arc a ranged skill would fly, drawn under the cursor while one is armed and aimed.
    ///
    /// Purely presentation. What the shot is -- where it starts, what it can hit, who is in the
    /// way -- is the board input's; this only draws it, in the element's colour, and turns red
    /// where somebody is standing under it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShotPreview : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which prices the hover and owns the shot.")]
        BoardActionInput m_Input;

        [SerializeField, Min(2), Tooltip("Points along the arc. More is smoother.")]
        int m_Samples = 24;

        [SerializeField, Range(0f, 1f)]
        float m_Opacity = 0.8f;

        LineRenderer m_Line;
        Vector3[] m_Points;
        Material m_Material;

        static readonly Color k_Cover = new Color(0.94f, 0.35f, 0.24f, 1f);

        void Update()
        {
            var shot = m_Input != null ? m_Input.HoveredShot : null;
            var arena = ArenaContext.Current != null ? ArenaContext.Current.Map : null;

            if (!shot.HasValue || arena == null || m_Input.PendingMove.HasValue
                || m_Input.Hovered.Skill == null)
            {
                if (m_Line != null)
                {
                    m_Line.enabled = false;
                }

                return;
            }

            Build();

            var plan = shot.Value;
            var from = arena.ToWorld(plan.From) + Vector3.up * Lift(m_Input.Actor);
            var to = arena.ToWorld(plan.To) + Vector3.up * 0.5f;
            var tiles = Cell.Distance(plan.From, plan.To);

            ShotArc.Sample(from, to, ShotArc.Height(tiles), m_Points);
            m_Line.SetPositions(m_Points);

            var tint = ElementPalette.ForElement(m_Input.Hovered.Skill.Element);

            if (plan.IsCovered)
            {
                tint = Color.Lerp(tint, k_Cover, 0.75f);
            }

            tint.a = m_Opacity;
            m_Line.startColor = tint;
            m_Line.endColor = new Color(tint.r, tint.g, tint.b, tint.a * 0.35f);
            m_Line.enabled = true;
        }

        static float Lift(CreatureState actor)
        {
            var view = UnitView.Of(actor);
            return view != null ? view.GroundOffset + 0.2f : 0.7f;
        }

        void Build()
        {
            if (m_Line != null)
            {
                return;
            }

            m_Points = new Vector3[Mathf.Max(2, m_Samples)];

            m_Material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")) { name = "Shot Arc" };
            m_Material.SetFloat("_Surface", 1f);
            m_Material.SetFloat("_ZWrite", 0f);
            m_Material.SetOverrideTag("RenderType", "Transparent");
            m_Material.renderQueue = 3001;
            m_Material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m_Material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m_Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            m_Line = gameObject.AddComponent<LineRenderer>();
            m_Line.useWorldSpace = true;
            m_Line.positionCount = m_Points.Length;
            m_Line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.06f), new Keyframe(1f, 0.025f));
            m_Line.numCapVertices = 4;
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows = false;
            m_Line.sharedMaterial = m_Material;
            m_Line.textureMode = LineTextureMode.Stretch;
        }

        void OnDestroy()
        {
            if (m_Material != null)
            {
                Destroy(m_Material);
            }
        }
    }
}
