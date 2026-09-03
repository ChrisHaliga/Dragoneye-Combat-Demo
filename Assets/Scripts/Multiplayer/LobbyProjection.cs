using System;
using System.Collections.Generic;
using Unity.Services.Multiplayer;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Translates the Multiplayer SDK's types into this project's own.
    ///
    /// Lifted out of <see cref="SessionRunner"/> so that class is only responsible for the session
    /// lifecycle. Translation is a separate job with separate reasons to change -- an SDK upgrade
    /// moves this file and nothing else -- and pulling it out makes the failure mapping testable
    /// without a live session.
    /// </summary>
    public static class LobbyProjection
    {
        /// <summary>Player property key holding "1" when that player has readied up.</summary>
        public const string ReadyKey = "ready";

        /// <summary>
        /// A snapshot of the lobby in owned types.
        ///
        /// Built here once rather than letting the UI walk <c>ISession</c> and
        /// <c>IReadOnlyPlayer</c>: a view should not depend on the shape of a vendor SDK, and a
        /// projection can be constructed in a test without one.
        /// </summary>
        public static LobbyView Project(ISession session)
        {
            var selfId = session.CurrentPlayer?.Id;
            var players = new List<LobbyPlayerView>(session.PlayerCount);
            var everyoneReady = session.PlayerCount > 0;
            var selfIsReady = false;

            foreach (var player in session.Players)
            {
                var ready = IsReady(player);
                var isSelf = player.Id == selfId;

                everyoneReady &= ready;
                selfIsReady |= isSelf && ready;

                players.Add(new LobbyPlayerView(
                    DisplayNameOf(player), ready, player.Id == session.Host, isSelf));
            }

            return new LobbyView(
                session.Code, session.PlayerCount, session.MaxPlayers,
                session.IsHost, everyoneReady, selfIsReady, players);
        }

        public static bool IsReady(IReadOnlyPlayer player) =>
            player.Properties != null
            && player.Properties.TryGetValue(ReadyKey, out var property)
            && property.Value == "1";

        public static string DisplayNameOf(IReadOnlyPlayer player)
        {
            var name = SessionRunner.StripDiscriminator(player.GetPlayerName());
            return string.IsNullOrEmpty(name) ? player.Id : name;
        }

        /// <summary>
        /// Maps the SDK's failure codes onto this project's own.
        ///
        /// Returning an enum rather than a sentence keeps English out of a systems class, lets
        /// failure paths be asserted without string matching, and leaves wording to the layer that
        /// does wording.
        /// </summary>
        public static SessionFault Classify(Exception e)
        {
            if (e is not SessionException sessionException)
            {
                return SessionFault.Unknown;
            }

            switch (sessionException.Error)
            {
                case SessionError.SessionNotFound:
                    return SessionFault.NotFound;
                case SessionError.SessionDeleted:
                    return SessionFault.Deleted;
                case SessionError.Forbidden:
                    return SessionFault.Forbidden;
                case SessionError.NotAuthorized:
                    return SessionFault.NotAuthorized;
                case SessionError.RateLimitExceeded:
                    return SessionFault.RateLimited;
                case SessionError.NetworkManagerNotInitialized:
                case SessionError.NetworkManagerStartFailed:
                case SessionError.NetworkSetupFailed:
                    return SessionFault.NetcodeFailed;
                case SessionError.SessionTypeAlreadyExists:
                    return SessionFault.AlreadyInSession;
                default:
                    return SessionFault.Unknown;
            }
        }
    }
}
