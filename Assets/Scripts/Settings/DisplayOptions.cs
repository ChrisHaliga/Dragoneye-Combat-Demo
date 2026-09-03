using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Settings
{
    /// <summary>
    /// What the machine can actually do: which monitors exist and which resolutions they offer.
    ///
    /// Separate from <see cref="GameSettings"/> because one is the player's choice and the other is
    /// the hardware's answer. A stored choice can outlive the monitor it named -- someone unplugs a
    /// screen between sessions -- so every lookup here clamps rather than trusting the stored index.
    /// </summary>
    public static class DisplayOptions
    {
        static readonly List<DisplayInfo> s_Monitors = new List<DisplayInfo>();

        /// <summary>
        /// Connected monitors, refreshed on every call. Displays come and go while the game is
        /// running, so a cached list would offer the player a screen that is no longer there.
        /// </summary>
        public static IReadOnlyList<DisplayInfo> Monitors
        {
            get
            {
                s_Monitors.Clear();
                Screen.GetDisplayLayout(s_Monitors);
                return s_Monitors;
            }
        }

        /// <summary>Distinct width x height pairs, largest first. Refresh rates are collapsed.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var sizes = new List<Vector2Int>();

            foreach (var resolution in Screen.resolutions)
            {
                var size = new Vector2Int(resolution.width, resolution.height);
                if (!sizes.Contains(size))
                {
                    sizes.Add(size);
                }
            }

            sizes.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));

            return sizes;
        }

        /// <summary>Human-readable label for a monitor, falling back to its ordinal.</summary>
        public static string Describe(int index, DisplayInfo info) =>
            string.IsNullOrEmpty(info.name)
                ? $"Display {index + 1} ({info.width}x{info.height})"
                : $"{index + 1} - {info.name} ({info.width}x{info.height})";

        /// <summary>
        /// True when these settings can actually change anything.
        ///
        /// False in the editor, where Unity ignores <see cref="Screen.SetResolution"/> and the
        /// fullscreen mode outright -- the Game view is not an OS window they can act on. The
        /// settings are still stored and still apply to a build; there is simply nothing to see
        /// until one is made. The menu says so rather than leaving the player clicking a dropdown
        /// that appears to do nothing.
        /// </summary>
        public static bool CanApply => !Application.isEditor;

        /// <summary>Pushes the stored settings onto the window.</summary>
        public static void Apply()
        {
            if (!CanApply)
            {
                return;
            }

            var monitors = Monitors;
            if (monitors.Count > 0)
            {
                var index = Mathf.Clamp(GameSettings.Monitor, 0, monitors.Count - 1);
                var target = monitors[index];

                if (!SameDisplay(target, Screen.mainWindowDisplayInfo))
                {
                    // Position within the target display, not a desktop coordinate: top-left of the
                    // chosen monitor is the only placement that is correct on every layout.
                    //
                    // Never reached in the editor, and must not be: this moves the *main window* of
                    // the running process, which there is the Unity editor itself.
                    Screen.MoveMainWindowTo(target, Vector2Int.zero);
                }
            }

            var size = GameSettings.Resolution;
            var width = size.x > 0 ? size.x : Screen.width;
            var height = size.y > 0 ? size.y : Screen.height;

            Screen.SetResolution(width, height, GameSettings.Mode);
        }

        static bool SameDisplay(DisplayInfo a, DisplayInfo b) =>
            a.name == b.name && a.width == b.width && a.height == b.height;

        /// <summary>
        /// Applies stored settings once, before any scene loads.
        ///
        /// Attribute rather than a component so the settings cannot be lost by someone deleting an
        /// object from the boot scene, and so they are already correct on the first rendered frame.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyOnBoot()
        {
            GameSettings.Load();
            Apply();
        }
    }
}
