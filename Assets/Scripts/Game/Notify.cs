using System;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Tells watchers that something changed, without letting one of them stop the thing that
    /// changed.
    ///
    /// A C# event is a synchronous call into everybody listening, on the stack of whoever raised
    /// it. The things that raise these events are simulation objects -- a creature's state, a
    /// pool, the turn order -- and the things listening are all views. So a view that throws does
    /// not fail on its own: the exception unwinds back through the event into the rules, and
    /// whatever the server was in the middle of stops there.
    ///
    /// That is not a hypothetical. A ring under the token kept references to two renderers that
    /// were destroyed underneath it; it threw on every repaint, the repaint ran inside a creature
    /// state change, and the state change ran inside the move a brain had just ordered. The
    /// coroutine running the turn died, a turn ends in exactly one place, and the match stopped --
    /// because of a circle on the floor.
    ///
    /// Each watcher is called in its own try, so one that throws does not deprive the rest of the
    /// news either. The simulation and the screen are supposed to be separable; this is the seam
    /// between them, and a seam that passes exceptions in one direction is not one.
    /// </summary>
    public static class Notify
    {
        public static void Raise(Action watchers, UnityEngine.Object context = null)
        {
            if (watchers == null)
            {
                return;
            }

            foreach (var watcher in watchers.GetInvocationList())
            {
                try
                {
                    ((Action)watcher)();
                }
                catch (Exception exception)
                {
                    Blame(watcher, exception, context);
                }
            }
        }

        public static void Raise<T>(Action<T> watchers, T argument, UnityEngine.Object context = null)
        {
            if (watchers == null)
            {
                return;
            }

            foreach (var watcher in watchers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)watcher)(argument);
                }
                catch (Exception exception)
                {
                    Blame(watcher, exception, context);
                }
            }
        }

        /// <summary>Names the watcher, because the stack trace will be full of the raiser.</summary>
        static void Blame(Delegate watcher, Exception exception, UnityEngine.Object context)
        {
            var target = watcher.Target != null ? watcher.Target.GetType().Name : "a static handler";

            Debug.LogError($"{target}.{watcher.Method.Name} threw while being told something "
                + "changed. It has been skipped; the change itself stands.", context);
            Debug.LogException(exception, context);
        }
    }
}
