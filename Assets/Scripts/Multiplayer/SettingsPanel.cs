using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The settings controls, as a thing that can be put in more than one place.
    ///
    /// They used to be written out in <c>SessionMenu.uxml</c>, which was fine while the main menu
    /// was the only way to reach them. A pause menu is a second way, in a different scene and a
    /// different document, and copying the markup across would have meant two settings screens that
    /// agree until somebody edits one of them.
    ///
    /// Built rather than templated because a template instance puts a container of its own between
    /// the panel and its contents, and the layout here is a flex chain that does not survive an
    /// extra link. What matters to <see cref="SettingsScreen"/> is the names, and those are here.
    ///
    /// This decides what the controls *are*. <see cref="SettingsScreen"/> decides what they do, and
    /// still finds them with the same queries it always did.
    /// </summary>
    public static class SettingsPanel
    {
        /// <summary>Fills a container with the settings columns, replacing whatever was in it.</summary>
        public static void Build(VisualElement into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();
            into.Add(Display());
            into.Add(Camera());
        }

        static VisualElement Display()
        {
            var column = Column("DISPLAY");

            column.Add(Row("Window mode", Dropdown("mode-dropdown")));
            column.Add(Row("Monitor", Dropdown("monitor-dropdown")));
            column.Add(Row("Resolution", Dropdown("resolution-dropdown")));

            var note = new Label { name = "display-note", text = string.Empty };
            note.AddToClassList("setting-note");
            column.Add(note);

            return column;
        }

        static VisualElement Camera()
        {
            var column = Column("CAMERA");

            column.Add(Sensitivity("Pan speed", "pan-slider", "pan-value"));
            column.Add(Sensitivity("Zoom speed", "zoom-slider", "zoom-value"));
            column.Add(Sensitivity("Orbit speed", "orbit-slider", "orbit-value"));

            var invert = new Toggle { name = "invert-toggle" };
            invert.AddToClassList("setting-row__control");
            column.Add(Row("Invert orbit drag", invert));

            return column;
        }

        static VisualElement Column(string title)
        {
            var column = new VisualElement();
            column.AddToClassList("col");
            column.AddToClassList("col--settings");

            var label = new Label(title);
            label.AddToClassList("col__title");
            column.Add(label);

            return column;
        }

        static VisualElement Row(string label, params VisualElement[] controls)
        {
            var row = new VisualElement();
            row.AddToClassList("setting-row");

            var name = new Label(label);
            name.AddToClassList("setting-row__label");
            row.Add(name);

            foreach (var control in controls)
            {
                row.Add(control);
            }

            return row;
        }

        static DropdownField Dropdown(string name)
        {
            var dropdown = new DropdownField { name = name };
            dropdown.AddToClassList("setting-row__control");
            dropdown.AddToClassList("dropdown");
            return dropdown;
        }

        /// <summary>A slider and the number it is currently on.</summary>
        static VisualElement Sensitivity(string label, string sliderName, string valueName)
        {
            var slider = new Slider(0.25f, 3f) { name = sliderName };
            slider.AddToClassList("setting-row__control");
            slider.AddToClassList("slider");

            var value = new Label("1.00x") { name = valueName };
            value.AddToClassList("setting-row__value");

            return Row(label, slider, value);
        }
    }
}
