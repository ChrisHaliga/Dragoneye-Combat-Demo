namespace Dragoneye.Data
{
    /// <summary>
    /// Asks the operating system for a picture, where that is possible.
    ///
    /// Unity ships no runtime file dialog. Rather than pull in a native plugin for one field on one
    /// screen, this offers a real dialog in the editor -- which is where characters are actually
    /// being made right now -- and reports that it is unavailable everywhere else, so the screen can
    /// fall back to a path field instead of showing a button that does nothing.
    ///
    /// The seam is here rather than in the view so that swapping in a plugin later is one file.
    /// </summary>
    public static class PortraitBrowser
    {
        /// <summary>Whether <see cref="TryPick"/> can actually open anything.</summary>
        public static bool IsAvailable =>
#if UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>
        /// Opens a picker. False when cancelled, or when there is no picker to open.
        /// </summary>
        public static bool TryPick(out string path)
        {
#if UNITY_EDITOR
            path = UnityEditor.EditorUtility.OpenFilePanel(
                "Choose a portrait", "", "png,jpg,jpeg");

            return !string.IsNullOrEmpty(path);
#else
            path = null;
            return false;
#endif
        }
    }
}
