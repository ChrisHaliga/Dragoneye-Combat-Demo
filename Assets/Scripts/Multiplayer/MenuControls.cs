using System;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The small pieces the menu screens build out of.
    ///
    /// Extracted because three screens were each growing their own private "make a label with this
    /// class on it", and a helper duplicated three times is three places for the class name to drift
    /// away from the stylesheet.
    ///
    /// Nothing here knows what a character or a session is. These are shapes, not content.
    /// </summary>
    public static class MenuControls
    {
        public static Label Heading(string text) => Styled(new Label(text), "section-heading");

        public static Label FieldLabel(string text) => Styled(new Label(text), "field-label");

        public static Label Note(string text) => Styled(new Label(text), "setting-note");

        /// <summary>A label-and-value line, for read-only numbers.</summary>
        public static VisualElement ReadoutRow(string label, string value)
        {
            var row = Styled(new VisualElement(), "resolved-row");
            row.Add(Styled(new Label(label), "resolved-row__label"));
            row.Add(Styled(new Label(value), "resolved-row__value"));
            return row;
        }

        /// <summary>A square nudge button, for stepping a number up or down.</summary>
        public static Button StepButton(string text, Action onClick) =>
            Styled(new Button(onClick) { text = text }, "step-button");

        /// <summary>A button with one or more space-separated USS classes.</summary>
        public static Button TextButton(string text, string classes, Action onClick)
        {
            var button = new Button(onClick) { text = text };

            foreach (var name in classes.Split(' '))
            {
                if (name.Length > 0)
                {
                    button.AddToClassList(name);
                }
            }

            return button;
        }

        /// <summary>
        /// The letter shown when there is no portrait.
        ///
        /// One implementation, because the roster row and the creator preview both draw it and a
        /// second copy would eventually disagree about what an empty name looks like.
        /// </summary>
        public static string Initial(string name) =>
            string.IsNullOrWhiteSpace(name)
                ? "?"
                : name.Trim().Substring(0, 1).ToUpperInvariant();

        static T Styled<T>(T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }
    }
}
