using Dragoneye.Hex;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>Why a move was refused. <see cref="None"/> means it was allowed.</summary>
    public enum MoveRejection
    {
        None,
        OffMap,
        NotWalkable,
        Occupied,
        AlreadyThere
    }

    /// <summary>
    /// Whether a unit may enter a hex. Pure and static, so the rules can be exercised without a
    /// scene, a network or a unit.
    ///
    /// Occupancy is passed in rather than looked up, which keeps this free of any dependency on how
    /// occupancy happens to be tracked and makes every branch trivially reachable from a test.
    /// </summary>
    public static class MoveRules
    {
        public static bool CanEnter(HexMap map, Hex from, Hex target, bool occupied,
            out MoveRejection rejection)
        {
            if (map == null || !map.TryGetTile(target, out var tile))
            {
                rejection = MoveRejection.OffMap;
                return false;
            }

            if (target == from)
            {
                rejection = MoveRejection.AlreadyThere;
                return false;
            }

            if (!tile.IsWalkable)
            {
                rejection = MoveRejection.NotWalkable;
                return false;
            }

            if (occupied)
            {
                rejection = MoveRejection.Occupied;
                return false;
            }

            rejection = MoveRejection.None;
            return true;
        }
    }
}
