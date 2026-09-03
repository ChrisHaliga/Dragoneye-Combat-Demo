using System;
using System.Collections.Generic;
using Dragoneye.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Binds the settings panel to <see cref="GameSettings"/>.
    ///
    /// A plain class rather than a MonoBehaviour: it owns a subtree of one document, not a
    /// GameObject, and there is nothing here that wants a lifecycle of its own.
    ///
    /// Every control writes straight through on change. There is no Apply button, because there is
    /// nothing to apply -- sensitivity is read at the point of use, so the player can drag a slider
    /// and feel the difference without leaving the menu.
    /// </summary>
    public sealed class SettingsScreen
    {
        // Order matters: these line up with the dropdown choices below.
        static readonly FullScreenMode[] k_Modes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.Windowed
        };

        static readonly string[] k_ModeNames =
        {
            "Fullscreen",
            "Borderless",
            "Windowed"
        };

        readonly DropdownField m_Mode;
        readonly DropdownField m_Monitor;
        readonly DropdownField m_Resolution;
        readonly Label m_Note;

        readonly Slider m_Pan;
        readonly Slider m_Zoom;
        readonly Slider m_Orbit;
        readonly Toggle m_Invert;
        readonly Label m_PanValue;
        readonly Label m_ZoomValue;
        readonly Label m_OrbitValue;

        readonly Button m_Reset;

        List<Vector2Int> m_Resolutions = new List<Vector2Int>();

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        public SettingsScreen(VisualElement root, Action onBack)
        {
            m_Mode = root.Q<DropdownField>("mode-dropdown");
            m_Monitor = root.Q<DropdownField>("monitor-dropdown");
            m_Resolution = root.Q<DropdownField>("resolution-dropdown");
            m_Note = root.Q<Label>("display-note");

            m_Pan = root.Q<Slider>("pan-slider");
            m_Zoom = root.Q<Slider>("zoom-slider");
            m_Orbit = root.Q<Slider>("orbit-slider");
            m_Invert = root.Q<Toggle>("invert-toggle");
            m_PanValue = root.Q<Label>("pan-value");
            m_ZoomValue = root.Q<Label>("zoom-value");
            m_OrbitValue = root.Q<Label>("orbit-value");

            m_Reset = root.Q<Button>("settings-reset-button");
            var back = root.Q<Button>("settings-back-button");

            IsBound = m_Mode != null && m_Monitor != null && m_Resolution != null && m_Note != null
                && m_Pan != null && m_Zoom != null && m_Orbit != null && m_Invert != null
                && m_PanValue != null && m_ZoomValue != null && m_OrbitValue != null
                && m_Reset != null && back != null;

            if (!IsBound)
            {
                return;
            }

            m_Mode.choices = new List<string>(k_ModeNames);

            m_Mode.RegisterValueChangedCallback(_ => OnModeChanged());
            m_Monitor.RegisterValueChangedCallback(_ => OnMonitorChanged());
            m_Resolution.RegisterValueChangedCallback(_ => OnResolutionChanged());

            m_Pan.RegisterValueChangedCallback(evt => GameSettings.PanSensitivity = evt.newValue);
            m_Zoom.RegisterValueChangedCallback(evt => GameSettings.ZoomSensitivity = evt.newValue);
            m_Orbit.RegisterValueChangedCallback(evt => GameSettings.OrbitSensitivity = evt.newValue);
            m_Invert.RegisterValueChangedCallback(evt => GameSettings.InvertOrbit = evt.newValue);

            m_Reset.clicked += OnResetClicked;
            back.clicked += onBack;
        }

        /// <summary>
        /// Rebuilds the panel from stored settings and current hardware.
        ///
        /// Called every time the screen is opened rather than once, because a monitor can be
        /// plugged in while the game is sitting on the menu.
        /// </summary>
        public void Refresh()
        {
            if (!IsBound)
            {
                return;
            }

            RefreshDisplay();
            RefreshControls();
        }

        void RefreshDisplay()
        {
            var modeIndex = Array.IndexOf(k_Modes, GameSettings.Mode);
            m_Mode.SetValueWithoutNotify(k_ModeNames[modeIndex < 0 ? 1 : modeIndex]);

            var monitors = DisplayOptions.Monitors;
            var monitorNames = new List<string>(Mathf.Max(1, monitors.Count));

            for (var i = 0; i < monitors.Count; i++)
            {
                monitorNames.Add(DisplayOptions.Describe(i, monitors[i]));
            }

            if (monitorNames.Count == 0)
            {
                monitorNames.Add("Display 1");
            }

            m_Monitor.choices = monitorNames;
            m_Monitor.SetValueWithoutNotify(
                monitorNames[Mathf.Clamp(GameSettings.Monitor, 0, monitorNames.Count - 1)]);
            m_Monitor.SetEnabled(monitorNames.Count > 1);

            m_Resolutions = DisplayOptions.Resolutions();

            // Index 0 is not a resolution: it means "leave it alone", which is what a player who has
            // never opened this screen already has.
            var resolutionNames = new List<string> { "Use display default" };
            foreach (var size in m_Resolutions)
            {
                resolutionNames.Add($"{size.x} x {size.y}");
            }

            var stored = GameSettings.Resolution;
            var storedIndex = stored.x > 0 ? m_Resolutions.IndexOf(stored) + 1 : 0;

            m_Resolution.choices = resolutionNames;
            m_Resolution.SetValueWithoutNotify(
                resolutionNames[Mathf.Clamp(storedIndex, 0, resolutionNames.Count - 1)]);

            // Stated up front rather than left to be discovered: in the editor these controls
            // store a preference and change nothing on screen, which is indistinguishable from
            // broken unless the panel says otherwise.
            var editorOnly = !DisplayOptions.CanApply;

            m_Note.text = editorOnly
                ? "Saved, but the editor ignores window mode, monitor and resolution. "
                    + "These take effect in a built player."
                : string.Empty;

            m_Note.EnableInClassList("setting-note--warning", editorOnly);
        }

        void RefreshControls()
        {
            m_Pan.SetValueWithoutNotify(GameSettings.PanSensitivity);
            m_Zoom.SetValueWithoutNotify(GameSettings.ZoomSensitivity);
            m_Orbit.SetValueWithoutNotify(GameSettings.OrbitSensitivity);
            m_Invert.SetValueWithoutNotify(GameSettings.InvertOrbit);

            RefreshReadouts();
        }

        void RefreshReadouts()
        {
            m_PanValue.text = Format(GameSettings.PanSensitivity);
            m_ZoomValue.text = Format(GameSettings.ZoomSensitivity);
            m_OrbitValue.text = Format(GameSettings.OrbitSensitivity);
        }

        void OnModeChanged()
        {
            var index = m_Mode.index;
            if (index < 0 || index >= k_Modes.Length)
            {
                return;
            }

            GameSettings.Mode = k_Modes[index];
            DisplayOptions.Apply();
        }

        void OnMonitorChanged()
        {
            if (m_Monitor.index < 0)
            {
                return;
            }

            GameSettings.Monitor = m_Monitor.index;
            DisplayOptions.Apply();
        }

        void OnResolutionChanged()
        {
            var index = m_Resolution.index;

            GameSettings.Resolution = index > 0 && index - 1 < m_Resolutions.Count
                ? m_Resolutions[index - 1]
                : Vector2Int.zero;

            DisplayOptions.Apply();
        }

        void OnResetClicked()
        {
            GameSettings.ResetToDefaults();
            DisplayOptions.Apply();
            Refresh();
        }

        static string Format(float sensitivity) => $"{sensitivity:0.00}x";
    }
}
