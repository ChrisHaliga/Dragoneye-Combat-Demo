using System.Collections.Generic;

namespace Dragoneye.Multiplayer
{
    /// <summary>Where the session is in its lifecycle. Replaces a human-readable status string.</summary>
    public enum SessionPhase
    {
        /// <summary>Reaching Unity services for the first time.</summary>
        Connecting,

        /// <summary>Signed in, not in a session.</summary>
        Idle,

        Hosting,
        Joining,
        InLobby,
        Leaving,

        /// <summary>Services could not be reached. <see cref="SessionRunner.Fault"/> says why.</summary>
        Unavailable
    }

    /// <summary>
    /// Why the last operation failed, if it did.
    ///
    /// An enum rather than a message so failure paths can be asserted in a test without matching on
    /// English, and so the wording can be localised in one place in the UI.
    /// </summary>
    public enum SessionFault
    {
        None,
        NotReady,
        RemovedFromSession,
        NotFound,
        Deleted,
        Forbidden,
        NotAuthorized,
        RateLimited,
        NetcodeFailed,
        AlreadyInSession,
        NoJoinCode,
        ServicesUnreachable,
        NameRejected,
        LeaveNotConfirmed,
        Unknown
    }

    /// <summary>One row of the lobby roster, in terms this project owns.</summary>
    public readonly struct LobbyPlayerView
    {
        public readonly string Name;
        public readonly bool IsReady;
        public readonly bool IsHost;
        public readonly bool IsSelf;

        public LobbyPlayerView(string name, bool isReady, bool isHost, bool isSelf)
        {
            Name = name;
            IsReady = isReady;
            IsHost = isHost;
            IsSelf = isSelf;
        }
    }

    /// <summary>
    /// A snapshot of the lobby, projected out of the Multiplayer SDK's types.
    ///
    /// The UI used to walk <c>ISession</c> and <c>IReadOnlyPlayer</c> directly, which meant a view
    /// depended on the shape of a vendor SDK: an SDK upgrade would land in the presentation layer,
    /// and there was no way to render a lobby in a test without constructing SDK objects. This is
    /// plain owned data, built in one place.
    /// </summary>
    public readonly struct LobbyView
    {
        public readonly string Code;
        public readonly int PlayerCount;
        public readonly int MaxPlayers;
        public readonly bool IsHost;
        public readonly bool EveryoneReady;
        public readonly bool SelfIsReady;
        public readonly IReadOnlyList<LobbyPlayerView> Players;

        public LobbyView(
            string code,
            int playerCount,
            int maxPlayers,
            bool isHost,
            bool everyoneReady,
            bool selfIsReady,
            IReadOnlyList<LobbyPlayerView> players)
        {
            Code = code;
            PlayerCount = playerCount;
            MaxPlayers = maxPlayers;
            IsHost = isHost;
            EveryoneReady = everyoneReady;
            SelfIsReady = selfIsReady;
            Players = players;
        }
    }
}
