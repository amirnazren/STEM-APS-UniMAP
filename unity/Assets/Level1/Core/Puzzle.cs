using System;
using System.Collections.Generic;

namespace EcoBotBuilders.Level1
{
    public enum Command { Move, Left, Right, Clean, Repeat }
    public enum Heading { East, North, West, South }
    public enum Phase { Editing, Running, Paused, Retry, Won }
    public enum Effect { None, Move, Turn, Clean, Empty, Blocked }

    public struct Cell : IEquatable<Cell>
    {
        public readonly int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell other) { return X == other.X && Y == other.Y; }
        public override bool Equals(object obj) { return obj is Cell && Equals((Cell)obj); }
        public override int GetHashCode() { return X * 397 ^ Y; }
        public override string ToString() { return "(" + X + "," + Y + ")"; }
    }

    public sealed class LevelDefinition
    {
        public readonly int Width, Height;
        public readonly Cell Spawn;
        public readonly Heading Facing;
        public readonly System.Collections.ObjectModel.ReadOnlyCollection<Cell> Trash, Obstacles;
        public LevelDefinition(int width, int height, Cell spawn, Heading facing,
            IEnumerable<Cell> trash, IEnumerable<Cell> obstacles)
        {
            if (width < 1 || height < 1 || trash == null || obstacles == null)
                throw new ArgumentException("A level needs positive dimensions and cell lists.");
            Width = width; Height = height; Spawn = spawn; Facing = facing;
            var dirty = new HashSet<Cell>(trash); var blocked = new HashSet<Cell>(obstacles);
            if (dirty.Count == 0 || !Contains(spawn) || blocked.Contains(spawn) || !Enum.IsDefined(typeof(Heading), facing))
                throw new ArgumentException("Invalid spawn or empty objective.");
            foreach (var cell in dirty)
                if (!Contains(cell) || blocked.Contains(cell)) throw new ArgumentException("Litter must be on a walkable tile.");
            foreach (var cell in blocked)
                if (!Contains(cell)) throw new ArgumentException("Obstacle outside board.");
            Trash = new List<Cell>(dirty).AsReadOnly(); Obstacles = new List<Cell>(blocked).AsReadOnly();
        }
        public bool Contains(Cell c) { return c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height; }
        public bool Walkable(Cell c) { return Contains(c) && !Obstacles.Contains(c); }
        public static LevelDefinition Tideleaf()
        {
            return new LevelDefinition(4, 4, new Cell(0, 0), Heading.East,
                new[] { new Cell(1,0), new Cell(2,0), new Cell(3,0), new Cell(3,1), new Cell(3,2), new Cell(3,3) },
                new[] { new Cell(1,2) });
        }
    }

    public sealed class Block
    {
        public readonly Command Kind;
        public int Count = 3;
        public readonly List<Block> Body = new List<Block>();
        public Block(Command kind) { Kind = kind; }
        public static Block Loop(int count, params Block[] body)
        {
            var loop = new Block(Command.Repeat) { Count = count };
            loop.Body.AddRange(body); return loop;
        }
    }

    public static class Programs
    {
        public const int Budget = 10, MaxActions = 40;
        public static List<Block> Example()
        {
            return new List<Block> {
                Block.Loop(3, new Block(Command.Move), new Block(Command.Clean)),
                new Block(Command.Left),
                Block.Loop(3, new Block(Command.Move), new Block(Command.Clean))
            };
        }
        // Counts the wrapper and each authored child ONCE, independent of repeat count.
        public static int Cost(IList<Block> program)
        {
            if (program == null) return 0;
            int cost = 0;
            foreach (var b in program) { cost++; if (b != null) cost += b.Body.Count; }
            return cost;
        }
        public static bool Compile(IList<Block> program, out List<Block> actions, out string error)
        {
            actions = new List<Block>(); error = "";
            if (program == null || program.Count == 0) { error = "Add a Move block to begin!"; return false; }
            if (Cost(program) > Budget) { error = "Use 10 blocks or fewer. Repeat helps save space!"; return false; }
            foreach (var b in program)
            {
                if (b == null || !Enum.IsDefined(typeof(Command), b.Kind)) { error = "Unknown block."; return false; }
                if (b.Kind != Command.Repeat)
                {
                    if (b.Body.Count > 0) { error = "Only Repeat can hold a body."; return false; }
                    actions.Add(b); continue;
                }
                if (b.Count < 2 || b.Count > 4) { error = "Repeat count must be 2, 3 or 4."; return false; }
                if (b.Body.Count == 0) { error = "Select Repeat Body, then add Move and Clean inside it."; return false; }
                foreach (var child in b.Body)
                    if (child == null || !Enum.IsDefined(typeof(Command), child.Kind) || child.Kind == Command.Repeat || child.Body.Count > 0)
                    { error = "Repeat bodies hold actions only, not another Repeat."; return false; }
                for (int i = 0; i < b.Count; i++) actions.AddRange(b.Body);
            }
            if (actions.Count > MaxActions) { error = "This program is too long to run safely."; return false; }
            return true;
        }
    }

    /// <summary>Shared editing rules: the UI never edits lists by itself.</summary>
    public sealed class ProgramEditor
    {
        public readonly List<Block> Main = new List<Block>();
        public List<Block> Container(Block parent) { return parent == null ? Main : parent.Body; }
        bool ValidParent(Block parent) { return parent == null || (Main.Contains(parent) && parent.Kind == Command.Repeat); }
        public bool Insert(Command kind, Block parent, int index, out string error)
        {
            error = "";
            if (!ValidParent(parent)) { error = "Choose Main or a Repeat body."; return false; }
            if (!Enum.IsDefined(typeof(Command), kind)) { error = "Unknown block."; return false; }
            if (parent != null && kind == Command.Repeat) { error = "Repeat goes in Main; its body holds actions."; return false; }
            if (Programs.Cost(Main) >= Programs.Budget) { error = "All 10 slots are full. Remove a block or use Repeat."; return false; }
            var list = Container(parent);
            list.Insert(Math.Max(0, Math.Min(index, list.Count)), new Block(kind)); return true;
        }
        public bool Remove(Block block, Block parent)
        {
            return ValidParent(parent) && Container(parent).Remove(block);
        }
        // Destination is an insertion boundary, BEFORE removal of the dragged source.
        public bool Move(Block block, Block parent, Block targetParent, int boundary)
        {
            if (!ValidParent(parent) || parent != targetParent) return false;
            var list = Container(parent); int from = list.IndexOf(block);
            if (from < 0) return false;
            boundary = Math.Max(0, Math.Min(boundary, list.Count));
            list.RemoveAt(from); if (boundary > from) boundary--;
            list.Insert(boundary, block); return true;
        }
        public bool ChangeCount(Block loop, int delta)
        {
            if (loop == null || loop.Kind != Command.Repeat || !Main.Contains(loop)) return false;
            loop.Count = Math.Max(2, Math.Min(4, loop.Count + delta)); return true;
        }
        public void LoadExample() { Main.Clear(); Main.AddRange(Programs.Example()); }
    }

    /// <summary>No Unity, clocks, I/O or random state. Used unchanged by the game and tests.</summary>
    public sealed class PuzzleSession
    {
        public readonly LevelDefinition Level;
        readonly HashSet<Cell> remaining = new HashSet<Cell>();
        List<Block> actions = new List<Block>();
        public Phase State { get; private set; }
        public Cell Position { get; private set; }
        public Heading Facing { get; private set; }
        public int Cursor { get; private set; }
        public int Tokens { get { return Level.Trash.Count - remaining.Count; } }
        public int Goal { get { return Level.Trash.Count; } }
        public int ActionCount { get { return actions.Count; } }
        public Block ActiveBlock { get; private set; }
        public string Message { get; private set; }
        public bool HasTrash(Cell cell) { return remaining.Contains(cell); }
        public PuzzleSession(LevelDefinition level) { Level = level ?? throw new ArgumentNullException("level"); Reset(); }
        public void Reset()
        {
            remaining.Clear(); foreach (var c in Level.Trash) remaining.Add(c);
            Position = Level.Spawn; Facing = Level.Facing; State = Phase.Editing;
            Cursor = 0; ActiveBlock = null; actions.Clear(); Message = "Move onto litter, then Clean. Let's help the island!";
        }
        public bool Run(IList<Block> program, out string error)
        {
            error = "";
            if (State == Phase.Running || State == Phase.Paused) { error = "Stop or finish this run first."; return false; }
            List<Block> compiled;
            if (!Programs.Compile(program, out compiled, out error)) return false;
            Reset(); actions = compiled; State = Phase.Running; Message = "Watch your program, one block at a time."; return true;
        }
        public void Pause() { if (State == Phase.Running) State = Phase.Paused; }
        public void Resume() { if (State == Phase.Paused) State = Phase.Running; }
        public Effect Step()
        {
            if (State != Phase.Running) return Effect.None;
            if (Cursor >= actions.Count) { Finish(); return Effect.None; }
            ActiveBlock = actions[Cursor++]; Effect effect;
            switch (ActiveBlock.Kind)
            {
                case Command.Move:
                    int[] dx = { 1, 0, -1, 0 }, dy = { 0, 1, 0, -1 };
                    var target = new Cell(Position.X + dx[(int)Facing], Position.Y + dy[(int)Facing]);
                    if (!Level.Walkable(target))
                    {
                        State = Phase.Retry; Message = "Oops, the shore or a rock is ahead. Try turning!"; return Effect.Blocked;
                    }
                    Position = target; effect = Effect.Move; Message = "One tile forward."; break;
                case Command.Left:
                    Facing = (Heading)(((int)Facing + 1) % 4); effect = Effect.Turn; Message = "Turn left. Stay on this tile."; break;
                case Command.Right:
                    Facing = (Heading)(((int)Facing + 3) % 4); effect = Effect.Turn; Message = "Turn right. Stay on this tile."; break;
                case Command.Clean:
                    effect = remaining.Remove(Position) ? Effect.Clean : Effect.Empty;
                    Message = effect == Effect.Clean ? "+1 clean token! Thank you, Eco-Bot." : "This tile is already clean. No token lost!";
                    break;
                default: throw new InvalidOperationException("Only compiled actions can execute.");
            }
            if (remaining.Count == 0) { State = Phase.Won; Message = "Island restored! You made a difference."; }
            else if (Cursor == actions.Count) Finish();
            return effect;
        }
        void Finish() { State = Phase.Retry; Message = "Good experiment! Some litter remains. Edit your code and try again."; }
    }
}
