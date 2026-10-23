using System;
using System.Collections.Generic;
using System.IO;
using EcoBotBuilders.Level1;

static class Tests
{
    static int passed;
    static readonly List<string> results = new List<string>();
    static void Assert(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void Test(string name,Action run) { run(); passed++; results.Add("{\"name\":\""+name+"\",\"pass\":true}"); Console.WriteLine("PASS " + name); }
    static List<Block> Code(params Command[] kinds) { var p=new List<Block>(); foreach(var k in kinds)p.Add(new Block(k));return p; }
    static PuzzleSession Session() { return new PuzzleSession(LevelDefinition.Tideleaf()); }
    static void Run(PuzzleSession s,IList<Block> p) { string e; Assert(s.Run(p,out e),e); }
    static void Finish(PuzzleSession s) { int n=0; while(s.State==Phase.Running && n++<100)s.Step();Assert(n<100,"Unbounded execution"); }
    static void Invalid(IList<Block> code) { List<Block>a;string e;Assert(!Programs.Compile(code,out a,out e)&&e.Length>0,"Expected validation error"); }
    public static int Main(string[] args)
    {
        try
        {
            Test("spawn and objective from shared data",()=>{var s=Session();Assert(s.Position.Equals(new Cell(0,0))&&s.Facing==Heading.East&&s.Goal==s.Level.Trash.Count&&s.Goal==6,"spawn");});
            Test("move follows heading",()=>{var s=Session();Run(s,Code(Command.Move));Assert(s.Step()==Effect.Move&&s.Position.Equals(new Cell(1,0)),"east move");});
            Test("left turns in place and forward follows it",()=>{var s=Session();Run(s,Code(Command.Left,Command.Move));s.Step();Assert(s.Facing==Heading.North&&s.Position.Equals(new Cell(0,0)),"left");s.Step();Assert(s.Position.Equals(new Cell(0,1)),"north");});
            Test("right turns in place and wraps",()=>{var s=Session();Run(s,Code(Command.Right,Command.Right,Command.Right,Command.Right));Finish(s);Assert(s.Facing==Heading.East&&s.Position.Equals(s.Level.Spawn),"right wrap");});
            Test("clean and double clean reward exactly once",()=>{var s=Session();Run(s,Code(Command.Move,Command.Clean,Command.Clean));s.Step();Assert(s.Step()==Effect.Clean&&s.Tokens==1,"clean");Assert(s.Step()==Effect.Empty&&s.Tokens==1&&!s.HasTrash(new Cell(1,0)),"double clean");});
            Test("clean empty tile is safe",()=>{var s=Session();Run(s,Code(Command.Clean));Assert(s.Step()==Effect.Empty&&s.Tokens==0,"empty clean");});
            Test("east boundary is clamped",()=>{var s=Session();Run(s,Code(Command.Move,Command.Move,Command.Move,Command.Move));Finish(s);Assert(s.Position.Equals(new Cell(3,0))&&s.State==Phase.Retry,"boundary");});
            Test("south boundary is clamped",()=>{var s=Session();Run(s,Code(Command.Right,Command.Move));s.Step();Assert(s.Step()==Effect.Blocked&&s.Position.Equals(s.Level.Spawn),"south");});
            Test("west boundary is clamped",()=>{var s=Session();Run(s,Code(Command.Left,Command.Left,Command.Move));Finish(s);Assert(s.Position.Equals(s.Level.Spawn),"west");});
            Test("north boundary is clamped",()=>{var s=Session();Run(s,Code(Command.Left,Command.Move,Command.Move,Command.Move,Command.Move));Finish(s);Assert(s.Position.Equals(new Cell(0,3)),"north");});
            Test("rock collision stops immediately",()=>{var s=Session();Run(s,Code(Command.Move,Command.Left,Command.Move,Command.Move,Command.Clean));s.Step();s.Step();s.Step();Assert(s.Step()==Effect.Blocked&&s.Position.Equals(new Cell(1,1))&&s.Cursor==4&&s.State==Phase.Retry,"rock");Assert(s.Step()==Effect.None&&s.Cursor==4,"stopped");});
            Test("repeat expansion preserves source identity and order",()=>{var loop=Block.Loop(3,new Block(Command.Move),new Block(Command.Clean));List<Block>a;string e;Assert(Programs.Compile(new[]{loop},out a,out e)&&a.Count==6&&a[0]==a[2]&&a[1].Kind==Command.Clean,"repeat");});
            Test("repeat counts bounded 2 to 4",()=>{Invalid(new[]{Block.Loop(1,new Block(Command.Move))});Invalid(new[]{Block.Loop(5,new Block(Command.Move))});List<Block>a;string e;Assert(Programs.Compile(new[]{Block.Loop(4,new Block(Command.Clean))},out a,out e)&&a.Count==4,"max");});
            Test("nested empty and cyclic repeats rejected",()=>{Invalid(new[]{Block.Loop(3)});Invalid(new[]{Block.Loop(3,Block.Loop(2,new Block(Command.Move)))});var loop=Block.Loop(3);loop.Body.Add(loop);Invalid(new[]{loop});});
            Test("invalid null and empty program rejected",()=>{Invalid(null);Invalid(new List<Block>());Invalid(new Block[]{null});Invalid(new[]{new Block((Command)99)});var b=new Block(Command.Move);b.Body.Add(new Block(Command.Clean));Invalid(new[]{b});});
            Test("budget counts wrappers plus body once",()=>{Assert(Programs.Cost(Programs.Example())==7,"example cost");var p=Code(Command.Clean,Command.Clean,Command.Clean,Command.Clean,Command.Clean,Command.Clean,Command.Clean,Command.Clean,Command.Clean,Command.Clean);List<Block>a;string e;Assert(Programs.Compile(p,out a,out e),e);p.Add(new Block(Command.Clean));Invalid(p);});
            Test("no overlapping executions",()=>{var s=Session();Run(s,Programs.Example());s.Step();string e;Assert(!s.Run(Programs.Example(),out e)&&s.Cursor==1,"second run");s.Pause();Assert(!s.Run(Programs.Example(),out e)&&s.State==Phase.Paused,"paused run");});
            Test("pause freezes steps and resume continues",()=>{var s=Session();Run(s,Programs.Example());s.Step();var p=s.Position;s.Pause();for(int i=0;i<100;i++)Assert(s.Step()==Effect.None,"paused step");Assert(s.Cursor==1&&s.Position.Equals(p),"paused changed");s.Resume();s.Step();Assert(s.Tokens==1,"resume");});
            Test("incomplete run reaches retry",()=>{var s=Session();Run(s,Code(Command.Move));Finish(s);Assert(s.State==Phase.Retry&&s.Tokens==0,"retry");});
            Test("known seven-block solution wins in thirteen steps",()=>{var s=Session();Run(s,Programs.Example());Finish(s);Assert(s.State==Phase.Won&&s.Tokens==6&&s.Cursor==13&&s.Position.Equals(new Cell(3,3)),"solution");Assert(s.Step()==Effect.None,"won cannot advance");});
            Test("reset clears progress cursor and restores litter",()=>{var s=Session();Run(s,Programs.Example());Finish(s);s.Reset();Assert(s.State==Phase.Editing&&s.Tokens==0&&s.Cursor==0&&s.Position.Equals(s.Level.Spawn)&&s.ActiveBlock==null,"reset");foreach(var c in s.Level.Trash)Assert(s.HasTrash(c),"restored");});
            Test("repeated run restores clean baseline",()=>{var s=Session();Run(s,Programs.Example());Finish(s);Run(s,Programs.Example());Assert(s.Tokens==0,"baseline");Finish(s);Assert(s.State==Phase.Won&&s.Tokens==s.Goal,"second win");});
            Test("insert body selection and nested refusal",()=>{var e=new ProgramEditor();string msg;Assert(e.Insert(Command.Repeat,null,0,out msg),msg);var loop=e.Main[0];Assert(e.Insert(Command.Move,loop,0,out msg),msg);Assert(!e.Insert(Command.Repeat,loop,0,out msg),"nested edit");Assert(Programs.Cost(e.Main)==2,"body count");});
            Test("editing budget enforced before insertion",()=>{var e=new ProgramEditor();string msg;for(int i=0;i<10;i++)Assert(e.Insert(Command.Move,null,i,out msg),msg);Assert(!e.Insert(Command.Clean,null,10,out msg)&&e.Main.Count==10,"edit budget");});
            Test("drag reorder uses pre-removal insertion boundaries",()=>{var e=new ProgramEditor();string msg;e.Insert(Command.Move,null,0,out msg);e.Insert(Command.Clean,null,1,out msg);e.Insert(Command.Left,null,2,out msg);var first=e.Main[0];Assert(e.Move(first,null,null,3)&&e.Main[2]==first,"to end");Assert(e.Move(first,null,null,0)&&e.Main[0]==first,"to start");Assert(e.Move(first,null,null,1)&&e.Main[0]==first,"same gap");});
            Test("remove repeat removes entire subtree and cost",()=>{var e=new ProgramEditor();e.LoadExample();var loop=e.Main[0];Assert(e.Remove(loop,null)&&Programs.Cost(e.Main)==4,"remove tree");string msg;Assert(!e.Insert(Command.Move,loop,0,out msg),"stale parent");});
            Test("count buttons clamp and cross-container drag refused",()=>{var e=new ProgramEditor();e.LoadExample();var a=e.Main[0];for(int i=0;i<8;i++)e.ChangeCount(a,1);Assert(a.Count==4,"upper clamp");for(int i=0;i<8;i++)e.ChangeCount(a,-1);Assert(a.Count==2,"lower clamp");Assert(!e.Move(a.Body[0],a,e.Main[2],0),"cross parent");});
            Test("every litter cell reachable by graph search",()=>{var l=LevelDefinition.Tideleaf();var seen=new HashSet<Cell>();var q=new Queue<Cell>();q.Enqueue(l.Spawn);seen.Add(l.Spawn);while(q.Count>0){var c=q.Dequeue();foreach(var d in new[]{new Cell(1,0),new Cell(0,1),new Cell(-1,0),new Cell(0,-1)}){var n=new Cell(c.X+d.X,c.Y+d.Y);if(l.Walkable(n)&&seen.Add(n))q.Enqueue(n);}}foreach(var c in l.Trash)Assert(seen.Contains(c),"unreachable trash");});
            Test("invalid definitions rejected and duplicate trash deduplicated",()=>{bool caught=false;try{new LevelDefinition(1,1,new Cell(0,0),Heading.East,new[]{new Cell(0,0)},new[]{new Cell(0,0)});}catch(ArgumentException){caught=true;}Assert(caught,"blocked spawn");var l=new LevelDefinition(2,1,new Cell(0,0),Heading.East,new[]{new Cell(1,0),new Cell(1,0)},new Cell[0]);Assert(new PuzzleSession(l).Goal==1,"dedupe");});
            Test("bounded fuzz execution never escapes board or double awards",()=>{var random=new Random(42);for(int i=0;i<1000;i++){var s=Session();var p=new List<Block>();for(int j=0;j<10;j++)p.Add(new Block((Command)random.Next(4)));Run(s,p);Finish(s);Assert(s.Level.Walkable(s.Position)&&s.Tokens>=0&&s.Tokens<=s.Goal&&s.Cursor<=10,"invariant");}});
            string output=args.Length>0?args[0]:".";Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"LOGIC_TESTS.json"),"{\"compiler\":\"Mono mcs 6.8.0.105\",\"runtime\":\"Mono\",\"source\":\"unity/Assets/Level1/Core/Puzzle.cs (same as runtime)\",\"checks\":["+string.Join(",",results)+"],\"passed\":"+passed+",\"unity_editor_verified\":false}");
            var level=LevelDefinition.Tideleaf();var cells=new List<string>();foreach(var c in level.Trash)cells.Add("["+c.X+","+c.Y+"]");var rocks=new List<string>();foreach(var c in level.Obstacles)rocks.Add("["+c.X+","+c.Y+"]");
            File.WriteAllText(Path.Combine(output,"level-layout.json"),"{\"width\":"+level.Width+",\"height\":"+level.Height+",\"spawn\":["+level.Spawn.X+","+level.Spawn.Y+"],\"trash\":["+string.Join(",",cells)+"],\"obstacles\":["+string.Join(",",rocks)+"]}");
            Console.WriteLine("RESULT: "+passed+" passed, 0 failed. Unity Editor / UI / Android NOT executed.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("FAIL "+ex);return 1;}
    }
}
