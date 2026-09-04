namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The panels the main menu can show. Exactly one is visible at a time.
    ///
    /// An enum rather than a stack of booleans, so "which screen am I on" has one answer and a new
    /// screen cannot be added without deciding where Back goes.
    /// </summary>
    public enum MenuScreen
    {
        /// <summary>The title card. Any key moves on; nothing else happens here.</summary>
        Start,

        /// <summary>The characters saved on this machine. Where a session begins.</summary>
        Characters,

        /// <summary>Building or editing one of them.</summary>
        CreateCharacter,

        /// <summary>The main menu proper, once the player knows who they are.</summary>
        Home,

        // No screen for a match being set up. Once netcode is running the draft board owns the
        // window and carries its own controls; the menu simply waits underneath it.
        Multiplayer,
        Host,
        Join,
        Settings
    }
}
