using Dragoneye.Combat;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// What each of the four stats is, for a hover: where the number came from, and what the game
    /// does with it.
    ///
    /// Written once, here, because five screens show the same four numbers -- the creator, the
    /// level-up sheet, the roster, the hero card and the arena's creature card -- and a player who
    /// learns what Speed does on one of them should not find a different sentence on the next.
    ///
    /// Two shapes of each. The long one shows the sum, for a character whose attributes are on the
    /// screen beside it; the short one only says what the stat does, for a premade whose numbers
    /// were authored rather than derived.
    /// </summary>
    public static class StatLore
    {
        // ---------- what each stat does ----------

        const string HealthDoes =
            "What this creature has left to lose. At zero it falls.";

        const string ApDoes =
            "Spent on every step and every skill. Back in full at the start of each turn.";

        const string SpeedDoes =
            "Decides who acts first, highest speed leading, and how far a point of movement goes.";

        const string ArmourDoes =
            "Takes every blow before health does, and what it cannot hold goes through. It never "
            + "comes back: health heals, armour does not.";

        // ---------- for a character, with the working shown ----------

        public static string Health(Loadout loadout)
        {
            var a = loadout.Attributes;

            return $"{Vitals.BaseHealth} base + {loadout.Vitals.Level} level + {a.Vitality} Vitality"
                + $"\n\n{HealthDoes}{Heals(loadout.Vitals.Regen)}";
        }

        public static string ActionPoints(Loadout loadout)
        {
            var species = loadout.Species != null ? loadout.Species.Name : "species";
            var baseAp = loadout.Species != null ? loadout.Species.BaseAp : Vitals.DefaultBaseAp;

            return $"{baseAp} {species} base + {loadout.Attributes.Willpower} Willpower\n\n{ApDoes}";
        }

        public static string Speed(Loadout loadout)
        {
            var worn = ArmourRules.SpeedCostOf(loadout.Armour);
            var armour = worn > 0 ? $" - {worn} {ArmourRules.NameOf(loadout.Armour)}" : string.Empty;

            return $"{Vitals.BaseSpeed} base + {loadout.Attributes.Endurance} Endurance{armour}"
                + $"\n\n{SpeedDoes}{Step(loadout.Vitals.Speed)}";
        }

        public static string Armour(Loadout loadout)
        {
            var suit = ArmourRules.PointsFor(loadout.Armour);
            var extra = loadout.ArmourPoints - suit;

            var sum = suit > 0 || extra > 0
                ? $"{suit} {ArmourRules.NameOf(loadout.Armour)}"
                  + (extra > 0 ? $" + {extra} carried" : string.Empty)
                : "Nothing worn gives any";

            return $"{sum}\n\n{ArmourDoes}";
        }

        // ---------- for anybody, saying only what the stat does ----------

        public static string Health(int regen) => HealthDoes + Heals(regen);

        public static string ActionPoints() => ApDoes;

        public static string Speed(int speed) => SpeedDoes + Step(speed);

        public static string Armour(int points) =>
            points > 0 ? ArmourDoes : "No armour. Every blow reaches health.\n\n" + ArmourDoes;

        static string Heals(int regen) =>
            regen > 0 ? $" Heals {regen} at the start of every turn." : string.Empty;

        static string Step(int speed) =>
            $" At this speed a tile costs {CombatRules.StepCostFor(speed)} AP.";
    }
}
