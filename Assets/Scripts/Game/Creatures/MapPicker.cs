using System.Collections.Generic;
using Dragoneye.Scenarios;
using Unity.Netcode;
using UnityEngine.UIElements;

namespace Dragoneye.Game.Creatures
{
    /// <summary>
    /// The map, on the draft board: a dropdown the host picks from and everybody else reads.
    ///
    /// The pick lives on the draft, replicated, so every client sees the same name and the
    /// arena every client builds is the same arena. Only the host may change it; for anybody
    /// else the field is a label with an arrow it cannot use, which says more plainly than a
    /// missing control would that the host decides.
    /// </summary>
    public sealed class MapPicker
    {
        readonly DropdownField m_Field;
        readonly Label m_Summary;

        /// <summary>True when the markup had the controls. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        public MapPicker(VisualElement root)
        {
            m_Field = root.Q<DropdownField>("setup-map-field");
            m_Summary = root.Q<Label>("setup-map-summary");

            IsBound = m_Field != null && m_Summary != null;

            if (!IsBound)
            {
                return;
            }

            var titles = new List<string>();

            foreach (var choice in MapLibrary.All)
            {
                titles.Add(choice.Title);
            }

            m_Field.choices = titles;
            m_Field.RegisterValueChangedCallback(evt => OnPicked(evt.newValue));
        }

        /// <summary>Repaints from the draft, or from the absence of one.</summary>
        public void Refresh(DraftState draft)
        {
            if (!IsBound)
            {
                return;
            }

            var index = draft != null ? draft.MapIndex : 0;
            var choice = MapLibrary.At(index);
            var manager = NetworkManager.Singleton;
            var isHost = manager != null && manager.IsServer;

            m_Field.SetValueWithoutNotify(choice.Title);
            m_Field.SetEnabled(isHost && draft != null);
            m_Summary.text = choice.Summary;
        }

        /// <summary>
        /// Offers the pick to the draft. Read at the moment of the click rather than from a draft
        /// remembered at the last repaint: a picker built before the draft spawned used to hold a
        /// null and drop every pick on the floor, with the dropdown showing the new name anyway.
        /// </summary>
        void OnPicked(string title)
        {
            var draft = DraftState.Current;

            if (draft == null)
            {
                return;
            }

            for (var i = 0; i < MapLibrary.All.Count; i++)
            {
                if (MapLibrary.All[i].Title == title)
                {
                    draft.SetMapRpc(i);
                    return;
                }
            }
        }
    }
}
