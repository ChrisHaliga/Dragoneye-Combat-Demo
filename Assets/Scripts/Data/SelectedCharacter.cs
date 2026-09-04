namespace Dragoneye.Data
{
    /// <summary>
    /// The character this player is currently playing as.
    ///
    /// One per install, like the save folder it comes from, which is why this is static rather than
    /// a component threaded through the menu. It is the seam between "who did I pick in the menu"
    /// and "what do I bring into a match": the menu writes it, and the lobby reads it when it hands
    /// a build to the host.
    ///
    /// Holds a whole <see cref="SavedCharacter"/> rather than an id, because the portrait never
    /// crosses the network and has to come from somewhere local when the arena wants to draw it.
    /// </summary>
    public static class SelectedCharacter
    {
        /// <summary>Null until the player has chosen. Nothing should start a match in that state.</summary>
        public static SavedCharacter Current { get; set; }

        public static bool HasSelection => Current != null;
    }
}
