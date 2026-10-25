using System;
using System.Collections.Generic;

namespace EcoBotBuilders.Level1
{
    /// <summary>
    /// Shared, Unity-free level catalogue. Both levels feed the SAME deterministic
    /// <see cref="PuzzleSession"/> state machine in Puzzle.cs — there is no forked logic.
    /// Level 1 keeps its original <see cref="LevelDefinition.Tideleaf"/> definition and budget (10).
    /// </summary>
    public static class LevelCatalog
    {
        /// <summary>Lowest and highest playable level ids shipped in this build.</summary>
        public const int First = 1, Last = 2;

        public static bool Exists(int levelId) { return levelId >= First && levelId <= Last; }

        /// <summary>Returns the shared level data for the given id (defaults to Level 1).</summary>
        public static LevelDefinition Load(int levelId)
        {
            switch (levelId)
            {
                case 2: return SolarPanelPath();
                default: return LevelDefinition.Tideleaf();
            }
        }

        /// <summary>The next level id, or 0 when there is no further level.</summary>
        public static int Next(int levelId) { return levelId >= Last ? 0 : levelId + 1; }

        /// <summary>
        /// Level 2 "Solar Panel Path": a straight row of six dusty solar panels on a 7x3 grid.
        /// Budget 6. A flat Move/Clean sequence would cost 12 blocks, so a Repeat loop is REQUIRED.
        /// Known solution: Repeat 3 [Move, Clean]; Repeat 3 [Move, Clean]  (cost 6, 12 actions).
        /// Allowed palette: Move, Clean, Repeat (no Turn) per schemas/level-configs.json.
        /// </summary>
        public static LevelDefinition SolarPanelPath()
        {
            return new LevelDefinition(7, 3, new Cell(0, 0), Heading.East,
                new[] { new Cell(1, 0), new Cell(2, 0), new Cell(3, 0), new Cell(4, 0), new Cell(5, 0), new Cell(6, 0) },
                new Cell[0], 6);
        }

        /// <summary>The deterministic known-good program for Level 2, usable by game and tests alike.</summary>
        public static List<Block> SolarPanelSolution()
        {
            return new List<Block>
            {
                Block.Loop(3, new Block(Command.Move), new Block(Command.Clean)),
                Block.Loop(3, new Block(Command.Move), new Block(Command.Clean))
            };
        }
    }

    /// <summary>
    /// Pure, Unity-free unlock rules. This is the AUTHORITATIVE boundary for what a player
    /// may enter: Level 1 is always open; every later level unlocks only once the level
    /// immediately before it has been completed. The PlayerPrefs persistence layer in the
    /// runtime assembly consults these rules; it never decides unlock state on its own.
    /// </summary>
    public static class Progression
    {
        /// <summary>True when <paramref name="levelId"/> may be played given the set of completed levels.</summary>
        public static bool IsUnlocked(int levelId, ICollection<int> completedLevels)
        {
            if (!LevelCatalog.Exists(levelId)) return false;
            if (levelId == LevelCatalog.First) return true;
            return completedLevels != null && completedLevels.Contains(levelId - 1);
        }

        /// <summary>The next level id if it is unlocked by completing <paramref name="justCompleted"/>, else 0.</summary>
        public static int UnlockedNext(int justCompleted, ICollection<int> completedLevels)
        {
            int next = LevelCatalog.Next(justCompleted);
            return next != 0 && IsUnlocked(next, completedLevels) ? next : 0;
        }
    }
}
