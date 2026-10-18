# Bandit outpost — slice 1

Zandea owns this implementation in `Zandea.POI`. The older planet/scavenger POI code remains available but is not used to create this encounter. No antennas, pipes, tanks, vehicles, loot containers or new prop art are included.

## Audit and adaptation

Inspected `D:/applications/Unity/Projects/VR_Koombar`, including `Assets/Scenes/TerrainTools/PlanetPopulation_POC.unity`, `ExistingTreadmillPoiPlanter`, its editor planter/preview/terrain preparation, `DesertOutpostExpansion`, `OutpostModule`, and `OutpostFactionTheme`. Zandea already contained copies of these systems and the desert prefabs.

Reusable ideas: seeded courtyard compositions, measured module dimensions, baked-terrain site sampling, smooth grading, logical tile coordinates and distance hysteresis. The POC's small/medium/large layouts use fixed coordinates; its large expansion adds authored market/fuel-yard arrangements. Its walls are incomplete, its NPC bake only includes terrain, its runtime may clone scene previews, and it destroys/recreates encounters after movement/unload. The old terrain preparation modifies shared baked data, is tile-local and does not fully flatten the foundation. Its faction definition includes spacecraft, sci-fi props and scavenger roles. Those parts were replaced rather than carried into Zandea's definition.

Zandea's treadmill wraps authored terrain records but assigns each visible tile a logical coordinate. It creates private runtime TerrainData instances and shares those instances with TerrainCollider. Its floating-origin manager moves scene roots and notifies listeners. Existing bandits use `StarvingBandit`, `BanditGroup`, NavMeshAgent, `ScavengerHealth`, ragdoll, equipment and inventory systems. No full world-state save framework is added here.

## Configuration and layout

`BanditOutpost.asset` holds measured prefab bounds, 4/5 building selection, gate orientation, site and grading limits, streaming distances and patrol speed. Six canopies and six bandits are fixed requirements. Assets: AdobeMain; Adobe, Adobe2, AdobeRound and AdobeSmallDome; Canopy, canopyGray, canopyRed and CanopyYellow2; WallSingle, Gate, WatchTower; existing ShortSaber bandit.

`OutpostLayout` separates placement from Unity instantiation. Buildings face a central courtyard/access lane; paired canopy clusters leave the entrance axis clear. Tower and storage sides vary by seed. Module runs span the perimeter and gate shoulders with measured widths, modest length scaling and 4 cm joint overlaps. Rectangular corners meet at the same foundation elevation. The gate keeps its existing separated post/lintel colliders. Buildings retain their existing collision model; interior navigation is not introduced.

Generation tries 15 bounded variations and then a fixed fallback. Each candidate must satisfy counts, perimeter clearance, structure and path clearance and spawn clearance. Invalid configuration raises an explicit error rather than silently omitting content. Regenerate the selected component from its Inspector. Gizmos show footprints, paths, NPC starts and future sockets. Future tower ladder/platform/ranged-guard work is intentionally absent.

## Terrain and coordinates

The installation tool searches deterministic stable coordinates near Main's player. It samples footprint and approach height/slope, checks existing colliders, and stores the selected site and average elevation in Main. It does not move the site at runtime or repeat it on every wrapped tile.

`PlanetTerrainTreadmill.PrepareTerrainTile` runs on a private authored-data clone before assignment to Terrain and TerrainCollider; the generated fallback also calls it. `OutpostTerrain` stamps a level rectangular foundation plus blended approach in stable coordinates. Smooth weights are identical across tile edges. Shared TerrainData assets are not modified. Recycled logical tiles receive the same stamp; distant tiles are rejected before reading heights. Existing terrain/biome generation is otherwise unchanged.

Stable coordinates are logical-tile meters relative to the treadmill frame, not displayed floating-origin coordinates. Conversion uses `GetTileWorldPosition(0,0)`. Reservations remain independent of origin shifts. Runtime roots move with FloatingOriginManager; local navigation is re-registered at the shifted center and living agents are warped onto it. BanditGroup also shifts its cached home and waypoint.

## Navigation and encounter state

One local NavMesh uses terrain and static physics colliders, including buildings, walls and canopy supports. Moving NPC/body colliders are excluded. All six ground spawns and courtyard patrol routes must be reachable before the encounter activates. The gate remains navigable from outside. Two bandits guard the gate, two use a courtyard route, one idles at the stall edge and one guards the tower at ground level.

Each bandit uses an existing BanditGroup for perception and engagement, with an optional explicit route added to that class. Existing melee, health, knockout, ragdoll and equipment behavior remain responsible for combat. The outpost does not introduce a new combat or sensing implementation.

Stable IDs are `<poiId>/bandit/0..5`. Structures and navigation unload with hysteresis; six inactive NPC objects retain health, consciousness, injuries, inventory, equipment and position for this play session. Previously observed dropped encounter weapons can be cached with them; player-held weapons are excluded. This bounded cache is intentional for one encounter and is not a general save system. Existing knockout timers may expire while the site is unloaded. Scene exit ends the session.

## Future extension hooks

`IsReserved(double stableX, double stableZ, float radius)` queries the layout without needing structures loaded. `Layout.reservations` exposes typed oriented rectangles for buildings, shades, walls, gate, paths, tower, props and approach. Future environment systems should convert their tile coordinates into stable meters before querying. `GatePosition` and `ApproachDirection` expose the displayed-world road connection; `stableSite + rotated Layout.gate` gives the stable connection. Road width and length are configurable. No road surface is generated.

Future prop categories include entrance-side, under-canopy, courtyard social, storage, guard post/future ladder and perimeter clutter. These are empty reservations, not placeholder objects. Future placement must still check the actual asset footprint and navigation clearances.

## Tools and tests

Use **Tools > Zandea > POI > Install Bandit Outpost Slice** to create/update configuration and Main integration. `BanditOutpostTest.unity` provides an editor preview and a focused flat-ground runtime fixture. The component Inspector regenerates previews and validates seeds. Runtime Main uses the selected authored terrain site, not the test terrain.

The editor validation writes `Temp/ZandeaPOI.layout-validation.txt`. The Play Mode validation writes `Temp/ZandeaPOI.play-validation.txt`. These are execution reports, not claims about headset testing. Final verified results and remaining manual checks are recorded in `VALIDATION.md`.
