namespace EcoBotBuilders.Level1.View
{
    /// <summary>Art + layout the board uses for one level. Level 1 keeps grass/litter/stone.</summary>
    public struct BoardTheme
    {
        public string Tile;      // ground tile sprite stem under Resources/Level1
        public string Target;    // dirty objective sprite (litter / dusty panel)
        public string Cleaned;   // sprite to swap in when cleaned; null/"" hides the target instead
        public string Obstacle;  // obstacle sprite stem
        public float TargetW, TargetH, TargetLift;
    }

    /// <summary>
    /// All themed copy, palette and board art for a single level. One shared controller
    /// (Level1Game) renders either level from this data — no forked scene logic.
    /// </summary>
    public sealed class LevelTheme
    {
        public int Id;
        public string Title;        // large header title
        public string Concept;      // short STEM concept shown on the level-select card
        public string HeaderTag;    // "01  /  SKY-ISLE CLEANUP"
        public string HeaderSub;    // one-line sub heading
        public string Objective;    // tail of the progress line
        public Command[] Palette;   // blocks offered in this level
        public string Intro;        // board message shown on boot / reset
        public string FreshStart;   // message after a full restart
        public string EmptyHelp;    // guidance shown in an empty program list
        public string[] Hints;      // index 1..3 (index 0 unused)
        public string HelpTitle, HelpBody;
        public string WinTitle;     // victory modal title
        public string WinNounPlural;// "litter piles" / "solar panels"
        public string RewardName;   // friendly cosmetic name shown on victory
        public string BestKey;      // PlayerPrefs best-blocks key
        public BoardTheme Board;

        public static LevelTheme For(int levelId)
        {
            return levelId == 2 ? SolarPanelPath() : Tideleaf();
        }

        static LevelTheme Tideleaf()
        {
            return new LevelTheme
            {
                Id = 1,
                Title = "Tideleaf Island",
                Concept = "Logic & grid navigation",
                HeaderTag = "01  /  SKY-ISLE CLEANUP",
                HeaderSub = "A little code. A cleaner island.",
                Objective = "Collect every litter pile",
                Palette = new[] { Command.Move, Command.Left, Command.Right, Command.Clean, Command.Repeat },
                Intro = "Move onto litter, then Clean. Let's help the island!",
                FreshStart = "Fresh start! Tap Move, then Clean. Watch what each block does.",
                EmptyHelp = "1. Tap Move in the palette.\n2. Tap Clean.\n3. Press Run to test your idea.\n\nLook for a pattern you can Repeat!",
                Hints = new[] { "",
                    "Look at the litter trail. Move onto a pile, then Clean. The little arrow shows where the robot will go next.",
                    "The first three piles make a straight line. Repeat [Move, Clean] three times. At the corner, turn LEFT and find the next pattern.",
                    "Try this 7-block program:\nRepeat 3 [Move, Clean]\nTurn left\nRepeat 3 [Move, Clean]\nEach Repeat costs 1 + its 2 body blocks = 3 slots." },
                HelpTitle = "You are the robot's coder!",
                HelpBody = "Move goes one tile in the arrow's direction. Left / Right turns in place. Clean picks up litter UNDER the robot.\n\nRepeat runs its body 2-4 times. Wrapper + each body block costs one slot. Select Body before adding actions inside it.",
                WinTitle = "Island restored!",
                WinNounPlural = "litter piles",
                RewardName = "Leaf Antenna",
                BestKey = "TideleafBest",
                Board = new BoardTheme { Tile = "grass", Target = "litter", Cleaned = null, Obstacle = "stone", TargetW = 64, TargetH = 64, TargetLift = 40 }
            };
        }

        static LevelTheme SolarPanelPath()
        {
            return new LevelTheme
            {
                Id = 2,
                Title = "Solar Panel Path",
                Concept = "Sequencing & Loops",
                HeaderTag = "02  /  SOLAR PANEL PATH",
                HeaderSub = "Short code. Shiny panels.",
                Objective = "Clean every solar panel",
                Palette = new[] { Command.Move, Command.Clean, Command.Repeat },
                Intro = "The panels are dusty! Move onto a panel, then Clean. Spot the repeating pattern.",
                FreshStart = "Fresh start! The panels repeat: Move, Clean, Move, Clean... can a Repeat block help?",
                EmptyHelp = "6 panels, only 6 blocks!\n\n1. Build Move + Clean.\n2. Wrap them in a Repeat.\n3. Pick how many times it runs.\n\nA flat list would need 12 blocks - too many!",
                Hints = new[] { "",
                    "Do you see a pattern? Move, clean, move, clean... the panels line up in a straight row.",
                    "A Repeat block runs the blocks inside it again and again. Count the panels, then pick the number of times. (Each Repeat can run 2-4 times.)",
                    "Try this 6-block program:\nRepeat 3 [Move, Clean]\nRepeat 3 [Move, Clean]\nTwo loops of 3 clean all 6 panels and fit in 6 blocks." },
                HelpTitle = "Loops save blocks!",
                HelpBody = "Move goes one tile forward. Clean wipes the dust off the panel UNDER the robot.\n\nThere are 6 panels but only 6 blocks. A flat Move/Clean list needs 12 blocks, so you MUST use Repeat. A Repeat runs its body 2-4 times; wrapper + each body block costs one slot.",
                WinTitle = "Panels gleaming!",
                WinNounPlural = "solar panels",
                RewardName = "Solar Visor",
                BestKey = "SolarPanelBest",
                Board = new BoardTheme { Tile = "grass", Target = "panel_dusty", Cleaned = "panel_clean", Obstacle = "stone", TargetW = 120, TargetH = 96, TargetLift = 60 }
            };
        }
    }
}
