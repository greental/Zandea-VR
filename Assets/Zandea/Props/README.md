# Zandea Slice 8: physical outpost props

The original downloaded archives remain in `C:/Users/green/Downloads/Props`. `Tools/Props/audit_archives.py` inventories them without altering the sources. Blender 4.5 was used to export selected models and authored fragment meshes. Export and texture scripts live in `Tools/Props`; no geometry is generated during gameplay.

## Selected asset pass

| Asset | Source triangles | Export triangles | Main texture | Intended behavior |
|---|---:|---:|---:|---|
| Large ceramic container | 704 | 704 | 1024 → 512 | Grab, throw, melee break; eight capped ceramic sectors |
| Wooden stool | 11,146 | 2,400 | 4096 → 1024 | Grab, move, throw, break into seat and four frame pieces |
| Treasure chest | 103,330 | 7,996 | 4096 → 1024 | Physical hinged lid, reachable contents |
| Wooden barrel | 11,184 | 3,000 | 4096 → 1024 | Roll, carry, break and spill contents |
| Weapon rack | 680 | 680 | 2048 → 512 | Static rack with removable existing Bo Staff |
| Wooden crate | 6,576 | 2,496 | 4096 → 1024 | Grabbable hinged container |
| Burlap sack | 4,576 | 1,800 | 4096 → 512 | Grabbable storage clutter |
| Wooden table | 196 | 196 | 4096 → 1024 | Static table beside stools |
| Ksitigarbha statue scan | 50,198 | 4,500 | 8192 → 1024 | Static POI accent |

Triangle counts are from Blender before and after the selected export. These are inspection measurements, not a claim about headset rendering cost. The imported prefab builder logs Unity bounds for visual scale review. Normal maps and metallic/smoothness maps are reduced to 1024 where source maps exist; base textures keep lower limits for small objects.

The ceramic and rack archives include CC-BY-4.0 license files. Credit **Asylum Nox** for [Large ceramic container](https://sketchfab.com/3d-models/large-ceramic-container-2f8cf49400954961866ff97e4d74080c) and **Big2XG** for [Weapon Rack](https://sketchfab.com/3d-models/weapon-rack-0921f267541c4dbe971f33c122101d48). The other selected downloaded archives did not include a license file in the inspected archive contents; their source-page terms should be verified before public redistribution. The statue is a static cultural artifact used only as environmental art, with no gameplay or breakage.

## Runtime design

`PhysicalProp` uses the existing XR grab component and Rigidbody. `BreakableProp` accepts existing melee `IDamageable` packets and collision energy. It changes to a broken logical state once, then instantiates a pre-authored fragment prefab. At most three debris bursts remain active, and each cleans itself up. Gentle placement stays below the impact threshold.

`PropContainer` holds existing `NpcItemInstance` objects. Its contents are generated once with stable IDs from the POI slot. `NpcWorldItem` marks the physical representation; taking an item detaches that same instance from the container. Closing a chest removes only the presentation, then recreates it with the same identity on reopening. Breaking a barrel transfers its remaining items to world objects once. The existing physical `BandageRoll.prefab` and Bo Staff prefab provide usable items.

`PropRackSocket` mounts a real Bo Staff instance. Removing it clears the socket; returning that same weapon within the socket radius remounts it. A backpack-held or still-held weapon cannot snap back. The rack does not regenerate a replacement on POI reload.

`OutpostPropPopulation` uses the existing POI's future-prop reservations and path exclusions. The population is created once per session and remains outside the streamed structure root. Unloading deactivates nearby unheld props; held props and items taken from containers or racks stay active. Reloading restores the same objects and contents. The existing floating-origin manager shifts scene roots and Rigidbody poses, including these props. There is no disk save format in this slice.

The saved Main scene also contains 16 hand-arranged prop samples around the editor-only `Layout Preview`. They provided the scale and orientation reference: larger pots and sacks, an upright crate/chest, a subtly skewed sack, and clusters around the shade side. They stay visible while editing. Play Mode hides those scene-root samples and instantiates the stateful prop population at the outpost's stable terrain coordinates, so the samples cannot become duplicate physics objects in the old editor frame. The authored preview's individual transforms are not a runtime placement asset; future edits to them do not automatically change the generated layout.

The treadmill retains its 121-tile authored TileSet and 11×11 visible window. Those generated assets are ignored by normal text search but are present on disk; they are required for runtime terrain. Outpost unload is synchronized with terrain recentering, before the next physics step, and load waits for enabled terrain and collider data at the site. The prop grounding code samples active terrain directly and aligns the instantiated collider bottom to it.

Use **Tools > Zandea > Props > Build Slice 8 Prefabs and Test Scene** after importing the authored FBX and texture files. The builder creates URP materials, physical prefabs, a reusable outpost prop definition in `Resources`, and `ZandeaPropInteractions.unity`. Main's existing bandit outpost loads that definition automatically. The test scene places each representative asset with the existing player prefab.

`python Tools/Props/validate_offline.py` checks the TileSet reference/fallback and imported prefab wiring without starting Unity. In Unity, use **Tools > Zandea > Props > Run Main Outpost Streaming Validation** to exercise the physical outpost lifecycle.

## Known limits and headset checks

The representative barrel is a closed container that spills when smashed; it has no removable lid. The source rack contains one mesh, so the separate usable Bo Staff is mounted on a socket. Additional assets in the 44-archive download set remain audit candidates. Complex scanned statues require a visual suitability and license review before more are added.

Headset checks still need to confirm grip offsets, lid reach, joint stability, collision feel, bandage retrieval, weapon return, and Quest 2 CPU/GPU frame time. Desktop Play Mode tests and editor geometry checks do not establish Quest 2 framerate.

An earlier Main streaming run found rigidbodies hundreds of metres below the outpost after a 12-tile teleport. A later run with an accidentally substituted nine-tile TileSet timed out before the outpost loaded; the original 121-tile reference has been restored. The corrected path needs a fresh Main streaming run controlled by the user. The earlier focused interaction scene passed chest identity/open-close, one-time barrel spill, melee breakage, debris cap, rack removal, and static statue checks; it did not exercise XR grab or headset performance.
