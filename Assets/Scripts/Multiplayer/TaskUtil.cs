using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Dragoneye.Multiplayer
{
    public static class TaskUtil
    {
        /// <summary>
        /// Runs a task without awaiting it, logging faults.
        ///
        /// An unobserved async void sends its exception to Unity's unhandled handler, where it is
        /// easy to miss. Routing fire-and-forget calls through here means the day one throws, it
        /// lands in the console with a stack trace.
        /// </summary>
        public static async void Forget(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
