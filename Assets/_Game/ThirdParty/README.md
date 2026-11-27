# Third-party assets

All packs here are CC0 1.0 (public domain). Each folder keeps the pack's original `License.txt`.
Only the models the level builder uses are copied in; the full packs are not part of the repository.

| Folder | Source | Used for |
| --- | --- | --- |
| `KenneyFurniture` | Kenney, Furniture Kit (kenney.nl) | desks, monitors, chairs, tables, bench, kitchen counter, fridge, boxes, bookcase, small props |
| `KenneyFactory` | Kenney, Factory Kit | boiler hoppers, crates, traffic post |
| `KenneySpaceStation` | Kenney, Space Station Kit | ship consoles, crates |
| `KenneyCityIndustrial` | Kenney, City Kit (Industrial) | not placed yet (tanks, container) |
| `QuaterniusZombie` | Quaternius, Zombie Apocalypse Kit | barrels, pallet, trash bags, cones, pipes, blood decals |
| `QuaterniusHouse` | Quaternius, Ultimate House Interior Pack | night stands, plants, bins |
| `QuaterniusSciFi` | Quaternius, Modular Sci-Fi MegaKit (Standard) | server racks, crates, barrels, floor cables |
| `PolyHaven` | Poly Haven (polyhaven.com), converted by `tools/blender/polyhaven_to_prop.py` | pictures, wall clock, fire extinguishers, power/utility boxes, wall lamps, dead fluorescent fittings, filing cabinets, steel shelving, tool cart, desk paperwork |

The builder (`Assets/_Game/Editor/ElecMoleProps.cs`) keeps the primitive blockout's colliders, fits a model into the
same box, re-tints its materials into `Materials/Prototype/Props/` and imports textures point-filtered at 64-128 px.
