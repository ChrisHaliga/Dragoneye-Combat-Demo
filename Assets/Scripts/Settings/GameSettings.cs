using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Settings
{
    /// <summary>
    /// Player preferences that outlive a match: how the window is presented, and how hard the
    /// controls bite.
    ///
    /// A static store rather than an injected service, because these are process-wide facts in the
    /// same sense <see cref="Screen"/> is. Every consumer reads a multiplier at the point of use, so
    /// a change takes effect on the next frame with nothing to re-wire and no reload.
    ///
    /// This assembly deliberately references nothing. Settings are read by the camera, the cursor
    /// and the menu, which sit in three different assemblies that must not learn about each other.
    /// </summary>
    public static class GameSettings
    {
        const string k_ModeKey = "settings.display.mode";
        const string k_MonitorKey = "settings.display.monitor";
        const string k_WidthKey = "settings.display.width";
        const string k_HeightKey = "settings.display.height";
        const string k_PanKey = "settings.controls.pan";
        const string k_ZoomKey = "settings.controls.zoom";
        const string k_OrbitKey = "settings.controls.orbit";
        const string k_InvertKey = "settings.controls.invertOrbit";

        /// <summary>Sensitivity multipliers are clamped to this range in both directions.</summary>
        public const float MinSensitivity = 0.25f;

        public const float MaxSensitivity = 3f;

        static bool s_Loaded;

        static FullScreenMode s_Mode = FullScreenMode.FullScreenWindow;
        static int s_Monitor;
        static int s_Width;
        static int s_Height;
        static float s_Pan = 1f;
        static float s_Zoom = 1f;
        static float s_Orbit = 1f;
        static bool s_InvertOrbit;

        /// <summary>Raised after any setting changes, so an open menu can repaint.</summary>
        public static event Action Changed;

        public static FullScreenMode Mode
        {
            get { Load(); return s_Mode; }
            set => Set(ref s_Mode, value);
        }

        /// <summary>Index into <see cref="DisplayOptions.Monitors"/>. 0 is always valid.</summary>
        public static int Monitor
        {
            get { Load(); return s_Monitor; }
            set => Set(ref s_Monitor, Mathf.Max(0, value));
        }

        /// <summary>Zero means "whatever the display is already using".</summary>
        public static Vector2Int Resolution
        {
            get { Load(); return new Vector2Int(s_Width, s_Height); }
            set
            {
                Load();
                if (s_Width == value.x && s_Height == value.y)
                {
                    return;
                }

                s_Width = Mathf.Max(0, value.x);
                s_Height = Mathf.Max(0, value.y);
                Save();
            }
        }

        /// <summary>Scales cursor movement, both keyboard and drag.</summary>
        public static float PanSensitivity
        {
            get { Load(); return s_Pan; }
            set => Set(ref s_Pan, Clamp(value));
        }

        /// <summary>Scales scroll-wheel and drag zoom together, so they stay in proportion.</summary>
        public static float ZoomSensitivity
        {
            get { Load(); return s_Zoom; }
            set => Set(ref s_Zoom, Clamp(value));
        }

        /// <summary>Scales Q/E and orbit-drag together.</summary>
        public static float OrbitSensitivity
        {
            get { Load(); return s_Orbit; }
            set => Set(ref s_Orbit, Clamp(value));
        }

        /// <summary>Flips horizontal orbit drag. Q/E are unaffected -- they have no "drag" to invert.</summary>
        public static bool InvertOrbit
        {
            get { Load(); return s_InvertOrbit; }
            set => Set(ref s_InvertOrbit, value);
        }

        /// <summary>+1 or -1, folded into orbit drag by the camera.</summary>
        public static float OrbitDirection => InvertOrbit ? -1f : 1f;

        public static float Clamp(float sensitivity) =>
            Mathf.Clamp(sensitivity, MinSensitivity, MaxSensitivity);

        /// <summary>Returns every setting to its shipped default and persists that.</summary>
        public static void ResetToDefaults()
        {
            s_Loaded = true;
            s_Mode = FullScreenMode.FullScreenWindow;
            s_Monitor = 0;
            s_Width = 0;
            s_Height = 0;
            s_Pan = 1f;
            s_Zoom = 1f;
            s_Orbit = 1f;
            s_InvertOrbit = false;

            Save();
        }

        /// <summary>
        /// Reads stored values once. Lazy rather than boot-ordered: the camera may well ask for a
        /// sensitivity before any menu has been shown, and a settings store that depends on
        /// something else having run first is a boot-order bug waiting to happen.
        /// </summary>
        public static void Load()
        {
            if (s_Loaded)
            {
                return;
            }

            // Set first: PlayerPrefs access below can re-enter through a property getter.
            s_Loaded = true;

            s_Mode = (FullScreenMode)PlayerPrefs.GetInt(k_ModeKey, (int)FullScreenMode.FullScreenWindow);
            s_Monitor = Mathf.Max(0, PlayerPrefs.GetInt(k_MonitorKey, 0));
            s_Width = Mathf.Max(0, PlayerPrefs.GetInt(k_WidthKey, 0));
            s_Height = Mathf.Max(0, PlayerPrefs.GetInt(k_HeightKey, 0));
            s_Pan = Clamp(PlayerPrefs.GetFloat(k_PanKey, 1f));
            s_Zoom = Clamp(PlayerPrefs.GetFloat(k_ZoomKey, 1f));
            s_Orbit = Clamp(PlayerPrefs.GetFloat(k_OrbitKey, 1f));
            s_InvertOrbit = PlayerPrefs.GetInt(k_InvertKey, 0) != 0;
        }

        // EqualityComparer rather than a T : IEquatable<T> constraint: enums do not implement it,
        // and FullScreenMode is one of the settings stored here.
        static void Set<T>(ref T field, T value)
        {
            Load();

            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            Save();
        }

        static void Save()
        {
            PlayerPrefs.SetInt(k_ModeKey, (int)s_Mode);
            PlayerPrefs.SetInt(k_MonitorKey, s_Monitor);
            PlayerPrefs.SetInt(k_WidthKey, s_Width);
            PlayerPrefs.SetInt(k_HeightKey, s_Height);
            PlayerPrefs.SetFloat(k_PanKey, s_Pan);
            PlayerPrefs.SetFloat(k_ZoomKey, s_Zoom);
            PlayerPrefs.SetFloat(k_OrbitKey, s_Orbit);
            PlayerPrefs.SetInt(k_InvertKey, s_InvertOrbit ? 1 : 0);
            PlayerPrefs.Save();

            Changed?.Invoke();
        }
    }
}
