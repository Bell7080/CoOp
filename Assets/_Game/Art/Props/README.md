# Ship props

Made with the Tripo API (paid credits, commercial use allowed under Tripo's terms section 5.2.2; keep Tripo's
embedded AI-generation markers) from text prompts, H3.1 smart low-poly, and converted with
`tools/blender/glb_to_prop.py` (front to +Z, sitting on the floor). Textures are 256 px.

| Model | Used for |
| --- | --- |
| `ShipGenerator` | hand-crank generator body (the primitive rotor/colliders stay underneath) |
| `ShipCctvDesk` | CCTV desk; the live feed quad is placed on its CRT by `FitCctvDesk` |
| `ShipLaunchConsole` | launch button pedestal; the state lamp sits on its mushroom button |
| `ShipMic` | broadcast microphone on the desk |
| `ShipConsole` | the three wall consoles |
| `ShipCoreEngine` | the generator: core engine body (reactor window to the cabin) |
| `ShipCrank` | the generator's hand crank (made in Blender by `tools/blender/make_crank.py`, not Tripo) |
| `KrTV`, `KrMicrowave`, `KrRadio`, `KrFan`, `KrToaster`, `KrKettle`, `KrRiceCooker` | 1990s Korean home appliances over the collectable appliances (charge LED kept on the front) |
| `KrKimchiFridge` | kimchi refrigerator in the break room (decoration) |
| `CctvCam` | wall-mounted CCTV camera on its bracket (static mount; also the ripped-off salvage) |
| `SettleLift` | the old outdoor settlement lift (not used since the 2026-11-26 nose/energy-tank redesign; kept for later) |

`glb_to_prop.py` also strips stray floating pieces that Tripo sometimes leaves around a model.
