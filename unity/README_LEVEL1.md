### Eco-Bot Builders — Level 1: Tideleaf Island

**Handoff status: Level 1 source milestone complete; source-level audit passed.** The actual Unity scene, artwork, metadata, build settings and auto-wired offline implementation are shipped. This is a Unity source-project delivery, not an APK or a claim of runtime-verified gameplay.

**Delivered scope:** an offline Level 1 scene, generated sprite artwork, block editor, deterministic puzzle simulation, hints, sound and victory UI. This is not the completed ten-level game. The old backend, teacher dashboard and legacy scripts remain in this repository but are not used by this scene.

**Validation boundary:** Unity Editor compilation, actual Play Mode UI/render/audio interaction and an Android build have NOT been executed here. Source compilation against older reference DLLs and pure C# tests are not a substitute for Unity Editor verification. Do not describe this milestone as runtime-verified until the Windows checklist below passes.

### Open and play on your Windows computer

1. Pull/download the current `main` branch of `https://github.com/amirnazren/STEM-APS-UniMAP`.
2. In Unity Hub choose **Add project from disk**, select the repository's **unity** folder (inside `EcoBotBuilders-Level1` if using the supplied ZIP), and use **Unity 2022.3.21f1** (the pinned version). Another compatible 2022.3 patch may require Unity's normal project upgrade/import; that combination was not tested here.
3. Let the existing Unity packages import. First import requires Internet for Unity's own package dependencies; the game itself uses no Internet and ships all art, fonts and sounds locally. **Do not install SocketIOUnity and do not enable `ECOBOT_LEGACY_ENABLED`.** Safe Mode, if shown, should be used to inspect real compiler errors, not bypassed.
4. Open **Assets/Scenes/MainGameScene.unity** and press **Play**. It is also the first enabled scene in Build Settings. The serialized scene has the offline controller, camera and AudioListener. On Play, that controller creates and wires its complete Canvas, island, robot, EventSystem, palette, program list, dialogs and audio — no manual Inspector assignment.
5. Recovery only: **Eco-Bot Builders → Level 1 → Open Main Game Scene**. If the scene was accidentally changed, **Rebuild Scene (recovery)** recreates and wires it after a confirmation. This is a fallback, not the delivery mechanism.

### Build your program

- **Move:** one tile in the cream arrow's direction. The robot artwork is a single three-quarter illustration; the arrow, not the face, shows its heading.
- **Turn left / Turn right:** 90-degree turn in place; no movement.
- **Clean:** collects a litter pile under the robot. One pile gives one token. Cleaning an empty tile is harmless and awards nothing.
- **Repeat:** count 2–4 (default 3), containing action blocks only. Nested Repeats and empty bodies cannot run. Tap its **Body** button before tapping palette actions to add them inside. Tap **Main** to return to the outer program.
- Tap a palette button to append to the selected destination, or drag it onto a row (inserts before it) / an end drop area. Drag the **≡ grip** to reorder within the same Main/body container. Each row also has **↑**, **↓**, **×** buttons; no drag gesture is required. Moving between containers is intentionally not supported by drag; remove and add instead.
- **10 authored slots:** every action costs one; a Repeat costs one plus its body blocks once, independent of repeat count. Max expanded actions is 40. A program cannot grow past the budget through the UI.
- **Run** resets the board and executes one action every 0.55 seconds. Editing is locked while running. A second Run cannot start overlapping execution. Shore/rock collisions end the run safely with a retry message.
- **Stop / reset** cancels execution, restores litter/tokens and keeps your program. **Restart** asks before clearing the program and hints. Help/pause/focus loss freeze execution and animation; Resume continues. A portrait overlay requests landscape orientation.
- Three **Hint** stages progress from observation to the exact solution. The third can load the example automatically. Mute and best code cost persist on the device; no student names or telemetry are collected by this scene.
- **Win:** collect every litter pile. The goal is counted from the same level definition that spawns the litter. The victory dialog has retry and fresh-start actions.

### Known solution — tested with the actual runtime model

```text
Repeat 3
    Move
    Clean
Turn left
Repeat 3
    Move
    Clean
```

Cost: **7 authored blocks**, **13 executed actions**, **6 clean tokens**. Spawn `(0,0)` faces East; the route follows the two litter-lined edges to `(3,3)`. The landmark rock occupies `(1,2)`.

### Architecture and compile boundaries

| Assembly / path | Role |
|---|---|
| `Assets/Level1/Core/Puzzle.cs` | Unity-free level definition, program validation/editing and one deterministic session state machine. Exact file compiled into the tests and runtime. |
| `EcoBotBuilders.Level1.Runtime` | One `Level1Game` controller; island presentation, uGUI pointer/drop controls and procedural audio. References only Core and `UnityEngine.UI`. |
| `EcoBotBuilders.Level1.Editor` | Scene recovery menu; Editor-only; references Core and Runtime. |
| `Assets/Editor/URPBootstrap.cs` | Explicit Editor-only assembly with Core/Universal runtime references. Waits for imports and fills URP renderer resources using the public API reviewed in official 2022.3 source. The overlay sprite presentation itself does not depend on a 3D lighting pipeline. |
| `Assets/Scripts/EcoBotBuilders.asmdef` | Preserved **legacy** code, compile-disabled by `ECOBOT_LEGACY_ENABLED`, not auto-referenced. No dangling SocketIO assembly reference. No legacy manager exists in MainGameScene. |

The legacy parser/grid/manager were inspected, not silently wired up. They couple Unity coroutine execution, mutable grid state, ten-level orchestration and network types. Reusing them would not yield the same independently testable model. Level 1 deliberately uses a contained replacement assembly; it does **not** synchronize with those old managers. To reactivate legacy systems later, install and validate SocketIOUnity, add its correct assembly reference to Legacy and then enable the define. This is separate work, not a requirement for Level 1.

The previous handwritten `URPProjectSettings.asset` referenced an unresolvable script GUID and was removed; Unity/URP owns generation of its project settings. All existing legacy source files are otherwise preserved. Stable `.meta` GUIDs are committed for all imported Assets, including the scene. Runtime references to textures/fonts are local Resources paths validated by the project checker. All public UI uses the bundled DejaVu font with its license.

### Evidence from this Linux authoring machine

- **30/30 pure C# checks passed**, zero failed (existing results reused: model and test harness verified byte-identical to tested commit `c7de4eb`, so the unchanged suite was not rerun); compiled with Mono mcs 6.8.0.105, C# 7.2, warnings-as-errors. Includes movement/turns; single/double/empty Clean; every boundary; rock collision; Repeat count/body/nesting validation; budget; no overlapping runs; pause/resume; retry; win; reset; known solution; insertion/reorder/removal; reachable litter; and 1,000 seeded random programs checking bounds and token invariants. See `../docs/level1/LOGIC_TESTS.json`.
- Secondary compilation covers **Puzzle.cs and all runtime presentation scripts**, against UnityEngine.Modules 2021.3.33 and UnityEngine.UI 2018.3.5.1 redistributed reference DLLs: **0 errors, 0 warnings**. These are older API references, not the target Unity 2022.3 toolchain; no DLLs are shipped. Editor helpers were reviewed against source, not compiled by this check. See `../docs/level1/API_COMPILE.json`.
- **27/27 static audit checks passed**: unique/matching metadata, scene fileIDs and MonoScript GUID, active controller/camera/listener, component ownership and roots, first build scene, settings YAML/built-in resource references, complete assembly ownership and declared dependency graph, inactive legacy define, Editor-only helpers, MonoBehaviour filenames, input axes/EventSystem, local Resources and valid artwork/fonts. See `../docs/level1/STATIC_CHECKS.json`. This is source/serialization validation, not proof Unity has imported the files.
- `../docs/level1/art-preview.png` is an **offline asset composition, NOT a Unity screenshot**. It was visually inspected for readable robot/litter, tile joins, palette contrast and layout. The intended style matches the art direction; real Unity text layout, draw order, input and device scaling remain unverified.
- No Unity Editor installed/licensed/executed for this milestone. No Android APK produced. No gameplay feel/performance scores invented from a still image.

Reproduce logic checks **on this Linux authoring machine or another Linux system with Mono and Python**:

```bash
mkdir -p /tmp/ecobot-level1-tests
mcs -langversion:7.2 -warnaserror -out:/tmp/ecobot-level1-tests/Tests.exe unity/Assets/Level1/Core/Puzzle.cs tools/level1-tests/Tests.cs
mono /tmp/ecobot-level1-tests/Tests.exe docs/level1
```

Optional secondary API check (downloads reference DLLs only into a temporary directory):

```bash
python3 tools/level1-tests/api_compile.py
```

### Final source audit fixes

- `BlockDrop` now has its own matching `.cs`/`.meta` MonoScript rather than sharing another component's file.
- URP bootstrap has an explicit Editor-only assembly and only the two relevant pipeline runtime assembly references. `ResourceReloader` is in `UnityEngine.Rendering`; the unnecessary Editor namespace import was removed. The helper was reviewed against the public Unity Graphics 2022.3 source, **not compiled by Unity**.
- Input Handling is explicitly **Input Manager (Old)** (`activeInputHandler: 0`), matching `StandaloneInputModule` and the existing axes. This avoids requiring two input backends for the offline scene. The unused Input System package remains installed for legacy compatibility; no Level 1 script imports it.
- Backend/frontend and old game script implementations were preserved. No new network dependency was introduced.

Reproduce the static audit on this authoring machine (Python packages: PyYAML, Pillow, fonttools):

```bash
python3 tools/level1-tests/static_audit.py
```

The downloadable ZIP contains tracked `unity/Assets`, `unity/Packages`, `unity/ProjectSettings`, this README and the adjacent evidence docs. It excludes caches, secrets, dependency folders and build binaries. `SOURCE_MANIFEST.json` inside the ZIP records the exact source commit and each file hash; `START_HERE.txt` gives the opening steps. The packaging script is committed at `tools/level1-tests/package_unity.py`; the redundant binary ZIP is not committed.

**Remaining verification boundary:** Unity Package Manager dependency resolution, Editor helper compilation/import, Play Mode rendering/input/audio and Android builds have not been exercised. No Unity executable is available on this authoring machine; no licensing/install workaround was attempted. A general registry lookup did not expose the pinned URP package, so neither that lookup nor API source review is claimed as a successful package install. The manifest retains the existing Unity 2022.3/URP 14 pins; actual Editor import must confirm resolution.

### Windows Unity verification checklist — not yet executed

1. Import the fresh clone in the pinned Editor; Console has **no compiler errors, missing assembly references, missing scripts or missing font/texture errors**. No SocketIOUnity installation. Confirm the URP helper completes without errors.
2. Open the shipped scene and press Play. See island, one eco-robot, visible heading arrow, a rock/fern, six litter piles, palette, empty program and `0 / 6` tokens. Listen for quiet ambience; Mute toggles it.
3. Tap Move then Clean, run; robot moves smoothly, litter disappears, `+1 token` feedback and sound occur, incomplete run permits editing. Stop during motion resets accurately and preserves code.
4. Drag from palette into a row, drag row grips both directions and to end; repeat using ↑/↓/× buttons only. Scroll a long program. Select each Repeat body, add/remove actions, vary count 2–4, return to Main. An empty Repeat is rejected; an 11th authored block is refused.
5. Use the known solution above, or Hint three times → Use example. Run: six tokens, victory dialog, correct 7-block cost. Repeat Run presses during execution do not speed up the robot. Try another idea and Fresh adventure work.
6. Test right-turn into the shore; see a friendly retry rather than falling or walking off-grid. Route into the rock; it blocks movement. Clean an empty tile; no token awarded.
7. Pause mid-move and wait; robot, progress and step do not advance. Resume continues. Open Help mid-run. Switch window focus away; return and Resume explicitly. Restart asks before clearing code.
8. Test Game view at **1280×720**, **1920×1080**, **2340×1080**, and **844×390**; verify text remains readable, no button occlusion, scrolling works, and safe-area/portrait handling is sensible. Check real mouse and touch separately.
9. With Android Build Support installed, build the first enabled scene to a landscape device. Verify taps/drags, sound/mute, pause on background, orientation and return to app. This build and runtime interaction are outstanding, not proven by the source checks.
