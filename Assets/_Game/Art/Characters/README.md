# Characters

Generated with the Tripo API (paid credits, so commercial use is allowed under Tripo's terms, section 5.2.2;
do not remove Tripo's embedded AI-generation markers) and rigged/animated in Blender with
`tools/blender/rig_animate.py` and the `cfg_*.py` files next to it.

| Model | Source | Clips |
| --- | --- | --- |
| `MoleAlien` | text prompt, H3.1 smart low-poly | Idle, Walk, Run, Crouch |
| `TvWalker` | concept image (CRT TV on human legs), H3.1 smart low-poly | Idle, Walk, Chase, Attack |
| `FanCrawler` | concept image (desk fan on long arms), H3.1 smart low-poly | Idle, Crawl, Chase, Attack |

Re-export: `blender --background --python tools/blender/rig_animate.py -- tools/blender/cfg_mole.py <model.glb> <out.fbx>`.
The level builder (`Assets/_Game/Editor/ElecMoleCharacters.cs`) sets the importer up, builds the Animator
Controllers and spawns the models; `RigAnimator` picks clips from movement and adds floppy nose/arm motion.
The mole's texture is pale so the player colour tints it.
