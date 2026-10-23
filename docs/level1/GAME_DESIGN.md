### Tideleaf Island — offline Level 1

Core loop: arrange a small block program → run the eco-robot → collect every litter pile → refine and retry when the route is incomplete.

**Win:** all spawned litter cleaned. **Retry outcome:** program ends without all litter, or hits a shore/blocked tile; editing and retry are unlimited. There is no death or time penalty for children. Initial learning session target: 2–5 minutes (design estimate, not playtested).

**One state machine:** Editing → Running ↔ Paused → Won / Retry. Stop resets robot and litter but preserves code; Restart also clears code and hints. Run always starts from the same initial board; repeat clicks while running cannot create another execution. Help and app focus loss pause a run. Editing is locked during running/paused. No backend, PIN, telemetry, networking or Internet required at play time.

**Controls:** click/tap palette to insert at selected destination; drag palette to a program row/end to insert; drag row grip to another row/end to reorder within its current container; up/down and remove buttons are accessible alternatives. Click `Main` or a Repeat's `Body` to select insert destination. Repeat's +/- changes count 2–4. Dragging between containers is deliberately rejected with guidance (remove/re-add instead). No nested Repeats. Empty Repeat is invalid. Run, Stop/reset, Restart, Hint, Help, Pause/resume and Mute are explicit buttons. Keyboard arrows/Enter use Unity Selectable navigation after focusing a button; Escape toggles pause. Touch uses the same EventSystem pointer handlers and scroll area. Native orientation is landscape; a portrait overlay asks for rotation.

**Budget:** 10 authored blocks total; a Repeat wrapper costs 1 plus each body block once. Expanded action limit is 40; each repeat count is 2–4. No unbounded loops. Victory depends only on clean state, not a preset program.

**Shared data/model:** `LevelDefinition.Tideleaf()` in `Assets/Level1/Core/Puzzle.cs` is used by rendering, simulation and tests. 4×4 grid, spawn (0,0), facing East (+x). Litter at (1,0), (2,0), (3,0), (3,1), (3,2), (3,3). Obstacle at (1,2). Bounds are the visible island shore. Cardinal order East, North, West, South; Left turns East→North. Known solution: Repeat 3 [Move, Clean]; Left; Repeat 3 [Move, Clean]. Cost 7, expanded 13 actions. Spawn itself has no litter.

**Timing:** 0.55 seconds per action, motion eased from old to new cell. Pause freezes the action clock and audio; no camera movement. Clean gives one token per pile exactly once and a rising +1 pop/chime. Heading marker rotates on turns. Goal count is `level.Trash.Count`, never a separate HUD constant. Hints progressively explain clean-underfoot, the two straight stretches and the exact solution. Last hint can fill the example program for accessibility.

**Sound:** locally synthesized Unity AudioClips for tap, step, turn, clean, blocked move and victory; a quiet looping ocean-like bed. Audio stops/pauses with game pause; mute persists in PlayerPrefs. Best valid code cost persists locally. No external audio files required.

**Scope boundary:** old `BlockParserEngine`, `GridEnvironmentManager` and `LevelManager` were inspected. Their coroutine / mutable Unity-state APIs and LevelManager's networking coupling do not give a deterministic independently testable offline milestone. Existing `Assets/Scripts` is preserved but compile-disabled behind `ECOBOT_LEGACY_ENABLED`. No legacy manager is present in MainGameScene. The new pure model and single scene controller are intentionally independent, not falsely advertised as integrated. Legacy reactivation needs separate networking/package validation; not part of this milestone.

**Presentation:** generated sprite artwork per ART_DIRECTION.md, bundled font, scalable 1280×720 canvas, program scrolling, large mobile buttons, real MainGameScene plus auto-wiring bootstrap and editor menu. Static art composition can be inspected here, but Unity Editor input/render performance and Android are not verified on this Linux authoring machine. Browser playtest hooks and WebGL quality tiers do not apply to this Unity-only sprite game.
