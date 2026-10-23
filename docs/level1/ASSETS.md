### Shipped asset provenance

All art is committed in `unity/Assets/Level1/Resources/Level1/`. No runtime hotlinks, external artwork APIs or downloaded-on-play content. The four transparent images were generated for this project with GPT Image 2.5 at 1024×1024; they are **generated artwork**, not falsely labeled CC0 or third-party licensed stock. The images have been downloaded into the repository. They are used as 2D uGUI textures, not as reference-photo crops on 3D materials.

| File | Content / source | Rights / notes |
|---|---|---|
| `grass.png` | Generated isometric grass/earth tile; generation URL `https://cdn.abacus.ai/images/0b62eaf7-5ee9-420d-8710-4a29012c8ff7.png` | Generated for this milestone. Actual PNG shipped; URL is provenance only. |
| `robot.png` | Generated cream/mint eco-robot with treads, cyan face and leaf antenna; `https://cdn.abacus.ai/images/f65d4e93-281c-4dad-b8e7-fcac4faa65d4.png` | Generated for this milestone. |
| `litter.png` | Generated orange can and blue bottle pile; `https://cdn.abacus.ai/images/e5921f89-8aec-4c5a-800d-c90999c075eb.png` | Generated for this milestone; no real product logos. |
| `stone.png` | Generated fern/rock/daisy obstacle; `https://cdn.abacus.ai/images/26d20e64-ccd0-433a-87af-bf580e73fa45.png` | Generated for this milestone. |
| `Interface.ttf` | DejaVu Sans, copied from installed DejaVu distribution | Bitstream Vera / DejaVu license; see adjacent `FONT-LICENSE.txt`. Original typeface not modified; font file renamed for resource lookup only. |
| `InterfaceBold.ttf` | DejaVu Sans Bold | Same license, included. |
| `FONT-LICENSE.txt` | `https://raw.githubusercontent.com/dejavu-fonts/dejavu-fonts/master/LICENSE` | License text shipped with both fonts. |
| Procedural sound | `IslandAudio.cs` synthesizes tap, move, turn, clean, empty, blocked, win and quiet sea ambience at startup | Original project code; no sampled recordings or external sounds. |

UI panels/lines and text are normal Unity UI primitives; pictorial robot, terrain, litter and obstacle artwork is generated, not hand-painted in code. `art-preview.png` is a PIL composition of the shipped sprites at the design coordinates, explicitly labeled as an art composition rather than a Unity screenshot.

### Visual inspection

The source assets and composite were opened and visually inspected. Match to ART_DIRECTION.md: readable treads/arms/leaf antenna, warm upper-left sprite shading, textured grassy diamond tiles with a soil perimeter, recognizable bottle/can piles, rock/fern landmark, ocean/cream UI contrast. The tile edges join into one coherent island in the offline composition. Differences: grass reads brighter yellow-green than the original emerald palette target; water is a minimal graphic backdrop, not a detailed painted ocean. The fixed robot illustration does not turn to face four directions; the separate heading arrow is the gameplay indicator.

Unity draw order, text wrapping, motion, touch targets, audio and package import have not been visually or interactively verified. No photorealism or gameplay feel score is assigned to an offline 2D asset composition.
