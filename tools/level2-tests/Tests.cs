using System;
using System.Collections.Generic;
using System.IO;
using EcoBotBuilders.Level1;

// Pure-C# logic tests for Level 2 "Solar Panel Path" and the shared progression rules.
// Compiled with the SAME Puzzle.cs + Levels.cs that the Unity runtime uses — no forked logic.
// NOT a Unity Editor / Play Mode / Android test.
static class Tests
{
    static int passed;
    static readonly List<string> results = new List<string>();
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Test(string name, Action run) { run(); passed++; results.Add("{\"name\":\"" + name + "\",\"pass\":true}"); Console.WriteLine("PASS " + name); }

    static PuzzleSession Session() { return new PuzzleSession(LevelCatalog.SolarPanelPath()); }
    static void Run(PuzzleSession s, IList<Block> p) { string e; Assert(s.Run(p, out e), e); }
    static void Finish(PuzzleSession s) { int n = 0; while (s.State == Phase.Running && n++ < 1000) s.Step(); Assert(n < 1000, "Unbounded execution"); }
    static List<Block> Flat(int pairs)
    {
        var p = new List<Block>();
        for (int i = 0; i < pairs; i++) { p.Add(new Block(Command.Move)); p.Add(new Block(Command.Clean)); }
        return p;
    }

    public static int Main(string[] args)
    {
        try
        {
            // --- Level 2 data comes straight from the shared catalogue ---
            Test("level 2 spawn, size, objective and budget from shared data", () =>
            {
                var lvl = LevelCatalog.SolarPanelPath();
                Assert(lvl.Width == 7 && lvl.Height == 3, "grid 7x3");
                Assert(lvl.Spawn.Equals(new Cell(0, 0)) && lvl.Facing == Heading.East, "spawn east");
                Assert(lvl.Trash.Count == 6, "six panels");
                Assert(lvl.Obstacles.Count == 0, "no obstacles");
                Assert(lvl.Budget == 6, "budget six");
                for (int x = 1; x <= 6; x++) Assert(lvl.Trash.Contains(new Cell(x, 0)), "panel row");
            });

            Test("catalogue load, existence and next-level links", () =>
            {
                Assert(LevelCatalog.First == 1 && LevelCatalog.Last == 2, "ids");
                Assert(LevelCatalog.Exists(1) && LevelCatalog.Exists(2) && !LevelCatalog.Exists(0) && !LevelCatalog.Exists(3), "exists");
                Assert(LevelCatalog.Next(1) == 2 && LevelCatalog.Next(2) == 0, "next");
                Assert(LevelCatalog.Load(2).Budget == 6 && LevelCatalog.Load(1).Budget == 10, "per-level budget");
                Assert(LevelCatalog.Load(99).Width == 4, "unknown id defaults to Level 1");
            });

            // --- The known 6-block looped solution wins ---
            Test("known six-block loop solution cleans every panel", () =>
            {
                var s = Session();
                Assert(Programs.Cost(LevelCatalog.SolarPanelSolution()) == 6, "solution costs six");
                Run(s, LevelCatalog.SolarPanelSolution());
                Finish(s);
                Assert(s.State == Phase.Won, "won");
                Assert(s.Tokens == 6 && s.Tokens == s.Goal, "all six cleaned");
                Assert(s.Position.Equals(new Cell(6, 0)), "ends on last panel");
                Assert(s.ActionCount == 12, "twelve expanded actions");
                Assert(s.Step() == Effect.None, "won cannot advance");
            });

            Test("an alternative in-budget loop split (4+2) also solves", () =>
            {
                var s = Session();
                var prog = new List<Block>
                {
                    Block.Loop(4, new Block(Command.Move), new Block(Command.Clean)),
                    Block.Loop(2, new Block(Command.Move), new Block(Command.Clean))
                };
                Assert(Programs.Cost(prog) == 6, "cost six");
                Run(s, prog); Finish(s);
                Assert(s.State == Phase.Won && s.Tokens == 6, "solved");
            });

            // --- Budget enforcement (the whole point of Level 2) ---
            Test("over-budget program rejected at the level budget of six", () =>
            {
                List<Block> a; string e;
                var seven = Flat(3); seven.Add(new Block(Command.Move)); // 7 blocks
                Assert(Programs.Cost(seven) == 7, "seven blocks");
                Assert(!Programs.Compile(seven, 6, out a, out e) && e.Length > 0, "rejected over budget");
                var six = Flat(3); // exactly 6 blocks
                Assert(Programs.Cost(six) == 6, "six blocks");
                Assert(Programs.Compile(six, 6, out a, out e), "six fits exactly: " + e);
            });

            Test("flat no-loop clean-all needs twelve blocks and cannot fit in six", () =>
            {
                var flat = Flat(6); // the only flat program that could clean all six panels
                Assert(Programs.Cost(flat) == 12, "flat needs twelve");
                List<Block> a; string e;
                Assert(!Programs.Compile(flat, 6, out a, out e), "twelve rejected at budget six");
                // Any flat program that DOES fit (<=6 blocks) cannot reach/clean all six panels.
                var s = Session();
                Run(s, Flat(3)); Finish(s);
                Assert(s.State != Phase.Won && s.Tokens == 3, "flat within budget leaves panels dusty");
            });

            Test("session enforces the per-level budget, not the global default", () =>
            {
                // Seven flat blocks would be legal under Level 1's budget (10) but must fail on Level 2 (6).
                var s = Session();
                var seven = Flat(3); seven.Add(new Block(Command.Clean));
                string e;
                Assert(!s.Run(seven, out e) && e.Length > 0, "session rejects over Level-2 budget");
                List<Block> a; string ok;
                Assert(Programs.Compile(seven, 10, out a, out ok), "same program is legal under budget 10");
            });

            Test("repeat counts still bounded 2..4 on level two", () =>
            {
                List<Block> a; string e;
                Assert(!Programs.Compile(new[] { Block.Loop(1, new Block(Command.Move)) }, 6, out a, out e), "count 1 rejected");
                Assert(!Programs.Compile(new[] { Block.Loop(5, new Block(Command.Move)) }, 6, out a, out e), "count 5 rejected");
                Assert(Programs.Compile(new[] { Block.Loop(4, new Block(Command.Clean)) }, 6, out a, out e) && a.Count == 4, "count 4 ok");
            });

            Test("every panel is reachable from spawn by grid search", () =>
            {
                var l = LevelCatalog.SolarPanelPath();
                var seen = new HashSet<Cell>(); var q = new Queue<Cell>();
                q.Enqueue(l.Spawn); seen.Add(l.Spawn);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    foreach (var dd in new[] { new Cell(1, 0), new Cell(0, 1), new Cell(-1, 0), new Cell(0, -1) })
                    {
                        var n = new Cell(c.X + dd.X, c.Y + dd.Y);
                        if (l.Walkable(n) && seen.Add(n)) q.Enqueue(n);
                    }
                }
                foreach (var c in l.Trash) Assert(seen.Contains(c), "unreachable panel");
            });

            // --- Pure progression / unlock rules (authoritative boundary) ---
            Test("progression locks level two until level one is complete", () =>
            {
                var none = new HashSet<int>();
                Assert(Progression.IsUnlocked(1, none), "level 1 always open");
                Assert(!Progression.IsUnlocked(2, none), "level 2 locked initially");
                var doneOne = new HashSet<int> { 1 };
                Assert(Progression.IsUnlocked(2, doneOne), "level 2 opens after level 1");
                Assert(!Progression.IsUnlocked(3, doneOne), "no level 3");
                Assert(Progression.UnlockedNext(1, doneOne) == 2, "finishing 1 unlocks 2");
                Assert(Progression.UnlockedNext(2, new HashSet<int> { 1, 2 }) == 0, "nothing after 2");
                Assert(Progression.UnlockedNext(1, none) == 0, "no jump without completion");
            });

            // --- Shared model still passes Level 1 ---
            Test("level one still solves with its original seven-block program", () =>
            {
                var s = new PuzzleSession(LevelDefinition.Tideleaf());
                Assert(s.Level.Budget == 10 && s.Goal == 6, "level 1 unchanged");
                Run(s, Programs.Example());
                Finish(s);
                Assert(s.State == Phase.Won && s.Tokens == 6 && s.Position.Equals(new Cell(3, 3)), "level 1 solution still wins");
            });

            Test("level one default-budget compile overload unchanged", () =>
            {
                List<Block> a; string e;
                var ten = new List<Block>();
                for (int i = 0; i < 10; i++) ten.Add(new Block(Command.Clean));
                Assert(Programs.Compile(ten, out a, out e), "ten fits default budget");
                ten.Add(new Block(Command.Clean));
                Assert(!Programs.Compile(ten, out a, out e), "eleven rejected at default budget");
            });

            string output = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "LEVEL2_TESTS.json"),
                "{\"compiler\":\"Mono mcs 6.8.0.105\",\"runtime\":\"Mono\"," +
                "\"source\":\"unity/Assets/Level1/Core/Puzzle.cs + Levels.cs (same as runtime)\"," +
                "\"checks\":[" + string.Join(",", results) + "],\"passed\":" + passed +
                ",\"unity_editor_verified\":false}");
            var lvl2 = LevelCatalog.SolarPanelPath();
            var cells = new List<string>(); foreach (var c in lvl2.Trash) cells.Add("[" + c.X + "," + c.Y + "]");
            File.WriteAllText(Path.Combine(output, "level2-layout.json"),
                "{\"width\":" + lvl2.Width + ",\"height\":" + lvl2.Height + ",\"spawn\":[" + lvl2.Spawn.X + "," + lvl2.Spawn.Y +
                "],\"budget\":" + lvl2.Budget + ",\"panels\":[" + string.Join(",", cells) + "],\"obstacles\":[]}");
            Console.WriteLine("RESULT: " + passed + " passed, 0 failed. Unity Editor / UI / Android NOT executed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
    }
}
