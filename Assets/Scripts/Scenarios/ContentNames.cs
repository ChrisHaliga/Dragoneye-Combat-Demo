namespace Dragoneye.Scenarios
{
    /// <summary>
    /// The authored content scenarios are written against, by the names the content uses.
    ///
    /// Ids, not assets: a scenario is pure and the catalog is not. A scenario that names
    /// content the catalog no longer has fails at the spawn, loudly, which is the right place.
    /// </summary>
    public static class Ground
    {
        public const string Grass = "grass";
        public const string Stone = "stone";
    }

    public static class Premade
    {
        public const string Recruit = "guard-recruit";
        public const string Archer = "guard-archer";
        public const string Sergeant = "guard-sergeant";
        public const string Knight = "hero-knight";
        public const string Ranger = "hero-ranger";
        public const string Cleric = "hero-cleric";
        public const string Goblin = "monster-goblin";
        public const string Wolf = "monster-wolf";
        public const string Ogre = "monster-ogre";
        public const string Brute = "bandit-brute";
        public const string Cutpurse = "bandit-cutpurse";
        public const string Scout = "bandit-scout";
    }

    /// <summary>Skill ids, as authored. Stable and hand-assigned; they cross the network.</summary>
    public static class Skills
    {
        public const int TakeABreath = 90;
        public const int Strike = 100;
        public const int Cleave = 101;
        public const int Loose = 102;
        public const int Jab = 103;
        public const int Ember = 104;
        public const int Smite = 105;
        public const int Recover = 110;
        public const int Fists = 120;
        public const int Shiv = 123;
        public const int SnapShot = 130;
        public const int Fireball = 140;
        public const int HoldTheLine = 150;
        public const int Backstab = 160;
        public const int HeavyBlow = 170;
        public const int Club = 200;
        public const int Bite = 201;
        public const int Maul = 202;
        public const int Sling = 203;
        public const int Torch = 204;
    }
}
