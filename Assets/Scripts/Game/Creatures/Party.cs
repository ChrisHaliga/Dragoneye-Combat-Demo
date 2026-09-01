namespace Dragoneye.Game
{
    /// <summary>
    /// Which side a creature fights for.
    ///
    /// Not an authored property of a creature: the same definition can be a guard in one match and a
    /// bandit in the next, so party is draft state and lives on <see cref="RosterEntry"/>.
    /// </summary>
    public enum Party : byte
    {
        Heroes = 0,
        Monsters = 1,
        Guards = 2,
        Bandits = 3
    }

    public static class PartyInfo
    {
        /// <summary>Every party, in declaration order. Cached so UI code does not allocate per frame.</summary>
        public static readonly Party[] All =
        {
            Party.Heroes, Party.Monsters, Party.Guards, Party.Bandits
        };

        /// <summary>Slot value meaning "nobody claimed this creature; the computer runs it".</summary>
        public const byte Unclaimed = 255;
    }
}
