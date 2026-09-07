using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Plays the fight back at a pace a person can follow.
    ///
    /// The simulation is somewhere ahead. It resolves the computer's turns the moment they are
    /// decided and only ever waits on a person -- so the events it sends arrive in a burst, and
    /// this is where they wait. One is taken off the queue, applied to <see cref="Fight"/>, shown
    /// to every view that listens, and left on screen for its beat; then the next. A walk waits
    /// for the token to arrive. Holding Space plays it four times faster.
    ///
    /// The board is part of what is shown, not a backdrop to it: a wall the fight has already
    /// brought down still stands on screen until its event comes round here, and then it falls
    /// while the watcher is looking at it.
    ///
    /// Nothing that draws the fight reads anything but <see cref="Fight"/>, the arena's drawn
    /// board, and <see cref="Presenting"/>.
    /// The prompts -- a defence to answer, a swing to take -- wait until the queue is empty, so a
    /// question is never asked about a blow the player has not yet seen thrown. And because the
    /// simulation stops on exactly those questions, the shown fight and the real one are the same
    /// fight at every moment a person is asked to decide anything.
    ///
    /// Added to the arena by <see cref="ArenaContext"/> at runtime, so it needs no scene wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatPlayback : MonoBehaviour
    {
        [SerializeField, Tooltip("Every creature on the board, to find the token a walk belongs to.")]
        CreatureRegistry m_Creatures;

        [SerializeField, Tooltip("The arena's map, whose drawn board a wall change is applied to "
             + "when the change is reached.")]
        ArenaMap m_Map;

        [SerializeField, Min(0.5f), Tooltip("Seconds a walk may take past its estimate before the "
             + "playback stops waiting for the token. A token that never arrives must not stop the fight.")]
        float m_WalkGrace = 2.5f;

        readonly Queue<CombatEvent> m_Queue = new Queue<CombatEvent>();

        /// <summary>The playback in the loaded arena, or null.</summary>
        public static CombatPlayback Current { get; private set; }

        /// <summary>The fight as far as it has been shown. What every view reads.</summary>
        public PresentedFight Fight { get; } = new PresentedFight();

        /// <summary>An event is being shown now. Views that animate listen here.</summary>
        public event Action<CombatEvent> Presenting;

        /// <summary>The shown fight changed. Views that redraw from <see cref="Fight"/> listen here.</summary>
        public event Action Changed;

        /// <summary>Whether everything the fight has said has been shown.</summary>
        public bool IsCaughtUp => m_Queue.Count == 0 && !m_Busy;

        /// <summary>How many events are waiting to be shown.</summary>
        public int Pending => m_Queue.Count;

        /// <summary>One, or the fast-forward multiple while the key is held.</summary>
        public float Speed { get; private set; } = 1f;

        /// <summary>The event on screen right now, or null between them.</summary>
        public CombatEvent Showing { get; private set; }

        bool m_Busy;
        float m_Elapsed;
        float m_Beat;
        float m_Cap;
        Func<bool> m_Until;

        /// <summary>Puts the playback on the arena, if it is not already there.</summary>
        public static CombatPlayback Ensure(GameObject host, CreatureRegistry creatures, ArenaMap map)
        {
            var existing = host.GetComponent<CombatPlayback>();
            var playback = existing == null ? host.AddComponent<CombatPlayback>() : existing;
            playback.m_Creatures = creatures;
            playback.m_Map = map;
            return playback;
        }

        void OnEnable()
        {
            Current = this;
            CombatAnnouncer.Received += Enqueue;
        }

        void OnDisable()
        {
            CombatAnnouncer.Received -= Enqueue;

            if (Current == this)
            {
                Current = null;
            }
        }

        void Enqueue(CombatEvent e)
        {
            if (e != null)
            {
                m_Queue.Enqueue(e);
            }
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            Speed = keyboard != null && keyboard.spaceKey.isPressed ? PresentationPacing.FastForward : 1f;

            if (m_Busy)
            {
                m_Elapsed += Time.deltaTime * Speed;

                var beatOver = m_Elapsed >= m_Beat;
                var arrived = m_Until == null || m_Until() || m_Elapsed >= m_Cap;

                if (!beatOver || !arrived)
                {
                    return;
                }

                m_Busy = false;
                m_Until = null;
                Showing = null;
            }

            // Everything queued that costs no time is shown in the same frame; the first that
            // does starts its beat. Nothing waits when there is nothing to wait for.
            while (!m_Busy && m_Queue.Count > 0)
            {
                Present(m_Queue.Dequeue());
            }
        }

        void Present(CombatEvent e)
        {
            Fight.Apply(e);
            ShowBoardChange(e);
            Showing = e;

            // A view that throws must not stop the fight being shown, and must not stop the
            // other views being told either -- which is why every watcher gets its own try
            // rather than the pair of calls sharing one.
            Notify.Raise(Presenting, e, this);
            Notify.Raise(Changed, this);

            var tiles = TilesOf(e);
            m_Beat = PresentationPacing.BeatFor(e, IsRanged(e), tiles, IsMine(e));
            m_Elapsed = 0f;
            m_Cap = m_Beat + m_WalkGrace;
            m_Until = e.Kind == CombatEventKind.Moved ? StillWalking(e.Actor) : null;
            m_Busy = m_Beat > 0f || m_Until != null;
        }

        /// <summary>
        /// A wall reaching the moment it changes. The fight's own board changed when the wall did;
        /// this is the drawn board catching up, with the watcher looking at it.
        /// </summary>
        void ShowBoardChange(CombatEvent e)
        {
            if (e.Kind == CombatEventKind.WallChanged && m_Map != null)
            {
                m_Map.ShowWall(e.Segment, e.WallAfter);
            }
        }

        /// <summary>A walk is over when the token says so, not when the clock does.</summary>
        Func<bool> StillWalking(uint id)
        {
            var creature = m_Creatures != null ? m_Creatures.ByTurnId(id) : null;
            var view = creature != null ? creature.View : null;

            return view == null ? null : (Func<bool>)(() => !view.IsMoving);
        }

        int TilesOf(CombatEvent e)
        {
            switch (e.Kind)
            {
                case CombatEventKind.Moved:
                    return e.Path.Count;

                case CombatEventKind.Swung:
                case CombatEventKind.Shot:
                case CombatEventKind.Acted:
                    var actor = Fight.Of(e.Actor);
                    var target = Fight.Of(e.Target);
                    return actor != null && target != null ? Cell.Distance(actor.Cell, target.Cell) : 1;

                default:
                    return 0;
            }
        }

        static bool IsRanged(CombatEvent e)
        {
            if (e.Skill == Opportunity.SkillId)
            {
                return false;
            }

            var catalog = SkillCatalog.Current;
            return catalog != null && catalog.TryGetSkill(e.Skill, out var skill) && skill.RollsToHit;
        }

        bool IsMine(CombatEvent e)
        {
            var creature = m_Creatures != null ? m_Creatures.ByTurnId(e.Actor) : null;
            return creature != null && LocalPlayer.Controls(creature);
        }
    }
}
