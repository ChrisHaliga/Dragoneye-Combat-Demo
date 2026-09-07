using System.Collections.Generic;
using Dragoneye.Game.Combat;
using Dragoneye.Game.Creatures;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Tells the wall shader where every creature is on screen, so the walls in front of one
    /// open a tunnel to it.
    ///
    /// The shader does the fading; this only feeds it. Positions are the tokens' own, a little
    /// above their feet so the tunnel centres on the body, and only creatures the watcher has
    /// been shown standing are handed over.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallCutaway : MonoBehaviour
    {
        const int MaxCuts = 16;

        static readonly int k_Cuts = Shader.PropertyToID("_DragoneyeCuts");
        static readonly int k_Count = Shader.PropertyToID("_DragoneyeCutCount");

        readonly Vector4[] m_Cuts = new Vector4[MaxCuts];

        void OnDisable()
        {
            Shader.SetGlobalInt(k_Count, 0);
        }

        void LateUpdate()
        {
            var context = ArenaContext.Current;
            var camera = context != null ? context.OutputCamera : null;
            var creatures = context != null ? context.Creatures : null;

            if (camera == null || creatures == null)
            {
                Shader.SetGlobalInt(k_Count, 0);
                return;
            }

            var count = 0;

            foreach (var creature in creatures.All)
            {
                if (creature == null || !Shown.IsAlive(creature) || count >= MaxCuts)
                {
                    continue;
                }

                var view = UnitView.Of(creature);
                var world = view != null
                    ? view.transform.position + Vector3.up * 0.35f
                    : context.Map.ToWorld(Shown.Cell(creature)) + Vector3.up * 0.6f;

                var viewport = camera.WorldToViewportPoint(world);

                if (viewport.z <= 0f)
                {
                    continue;
                }

                m_Cuts[count++] = new Vector4(viewport.x, viewport.y, viewport.z, 0f);
            }

            Shader.SetGlobalVectorArray(k_Cuts, m_Cuts);
            Shader.SetGlobalInt(k_Count, count);
        }
    }
}
