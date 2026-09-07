using System.Collections;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;
using Dragoneye.Hex;
using Dragoneye.Sim;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The thing that flies when a ranged skill is used.
    ///
    /// A placeholder, deliberately: a glowing bead in the element's colour, along the same arc
    /// the preview drew, with a short trail. It flies when the shot is shown, for exactly the
    /// time the playback leaves for it -- both read <see cref="PresentationPacing.Flight"/>, so
    /// the result of the shot is never on screen before the arrow has arrived. A miss carries on
    /// past the target and fades, and says so where it lands.
    ///
    /// Nothing here decides anything. The server resolved the shot long before this heard about
    /// it; the bead is the reveal, not the roll.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileView : MonoBehaviour
    {
        [SerializeField, Min(0.02f)]
        float m_BeadSize = 0.16f;

        Mesh m_Bead;
        CombatPlayback m_Playback;

        void OnDestroy()
        {
            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }
        }

        void Update()
        {
            if (m_Playback == CombatPlayback.Current)
            {
                return;
            }

            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }

            m_Playback = CombatPlayback.Current;

            if (m_Playback != null)
            {
                m_Playback.Presenting += OnPresenting;
            }
        }

        void OnPresenting(CombatEvent e)
        {
            switch (e.Kind)
            {
                case CombatEventKind.Shot:
                    Fire(e.Actor, e.Target, e.Skill, missed: !e.Landed);
                    break;

                case CombatEventKind.Acted when e.HasTarget:
                    Fire(e.Actor, e.Target, e.Skill, missed: false);
                    break;
            }
        }

        void Fire(uint attackerId, uint targetId, int skillId, bool missed)
        {
            var catalog = SkillCatalog.Current;
            var arena = ArenaContext.Current;

            if (catalog == null || arena == null || arena.Map == null
                || !catalog.TryGetSkill(skillId, out var skill) || !skill.RollsToHit)
            {
                return;
            }

            var attacker = Shown.Of(attackerId);
            var target = Shown.Of(targetId);

            if (attacker == null || target == null)
            {
                return;
            }

            var from = arena.Map.ToWorld(attacker.Cell) + Vector3.up * Lift(attackerId);
            var to = arena.Map.ToWorld(target.Cell) + Vector3.up * Lift(targetId);
            var tiles = Cell.Distance(attacker.Cell, target.Cell);

            StartCoroutine(Fly(from, to, tiles, ElementPalette.ForElement(skill.Element),
                missed, targetId));
        }

        static float Lift(uint id)
        {
            var creature = ArenaContext.Current != null && ArenaContext.Current.Creatures != null
                ? ArenaContext.Current.Creatures.ByTurnId(id)
                : null;
            var view = UnitView.Of(creature);
            return view != null ? view.GroundOffset + 0.15f : 0.65f;
        }

        IEnumerator Fly(Vector3 from, Vector3 to, int tiles, Color tint, bool missed,
            uint targetId)
        {
            var bead = Bead(tint);
            var trail = bead.GetComponent<TrailRenderer>();
            var height = ShotArc.Height(tiles);
            var seconds = PresentationPacing.Flight(tiles);

            // A miss keeps going. The arc is stretched a little past the target and the bead
            // fades along the extra, so what the player sees is a shot that did not stop.
            var end = missed ? 1.25f : 1f;
            var elapsed = 0f;

            while (elapsed < seconds * end)
            {
                elapsed += Time.deltaTime * (m_Playback != null ? m_Playback.Speed : 1f);
                var t = Mathf.Min(end, elapsed / seconds);

                bead.transform.position = ShotArc.Point(from, to, t, height);

                if (t > 1f)
                {
                    var fade = 1f - (t - 1f) / (end - 1f);
                    bead.transform.localScale = Vector3.one * (m_BeadSize * fade);
                }

                if (missed && t >= 1f && targetId != 0)
                {
                    CombatNotices.Raise(targetId, "MISS", NoticeTone.Gain);
                    targetId = 0;
                }

                yield return null;
            }

            // The trail outlives the bead by its own length, so the last of the arc is not cut off.
            bead.GetComponent<MeshRenderer>().enabled = false;
            yield return new WaitForSeconds(trail != null ? trail.time : 0f);
            Destroy(bead);
        }

        GameObject Bead(Color tint)
        {
            var bead = new GameObject("Shot");
            bead.transform.SetParent(transform, worldPositionStays: true);
            bead.transform.localScale = Vector3.one * m_BeadSize;

            bead.AddComponent<MeshFilter>().sharedMesh = BeadMesh;

            var material = Glow(tint);

            var renderer = bead.AddComponent<MeshRenderer>();
            renderer.material = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var trail = bead.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, m_BeadSize * 0.7f),
                new Keyframe(1f, 0f));
            trail.material = material;
            trail.startColor = tint;
            trail.endColor = new Color(tint.r, tint.g, tint.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.minVertexDistance = 0.02f;

            return bead;
        }

        Mesh BeadMesh
        {
            get
            {
                if (m_Bead != null)
                {
                    return m_Bead;
                }

                // The engine's own sphere, borrowed from a throwaway primitive rather than
                // authored: a placeholder should not come with an asset.
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m_Bead = primitive.GetComponent<MeshFilter>().sharedMesh;
                Destroy(primitive);
                return m_Bead;
            }
        }

        static Material Glow(Color tint)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")) { name = "Shot" };

            // Lifted towards white so it reads as light rather than paint.
            var bright = Color.Lerp(tint, Color.white, 0.35f);
            material.color = bright;
            material.SetColor("_BaseColor", bright);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3002;
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }
    }
}
