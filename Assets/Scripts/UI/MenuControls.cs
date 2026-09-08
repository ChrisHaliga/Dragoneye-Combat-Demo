using System;
using UnityEngine.UIElements;

namespace Dragoneye.UI
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
        public static Label FieldLabel(string text) => Styled(new Label(text), "field-label");

        public static Label Note(string text) => Styled(new Label(text), "setting-note");

        /// <summary>A square nudge button, for stepping a number up or down.</summary>
        public static Button StepButton(string text, Action onClick) =>
            Styled(new Button(onClick) { text = text }, "step-button");

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
