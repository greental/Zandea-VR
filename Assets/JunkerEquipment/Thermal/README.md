# Junker PLS-T thermal tool

Open `Assets/Scenes/JunkerThermalDemo.unity`. This is a separate copy of the saved JunkerBody scene and its existing XR rig. The thermal work area is 2.8 metres to the right of the starting rig. The original scene is not edited.

## Controls

- Grip either hand to pick up the PLS-T. Only the selected hand controls it.
- Click that hand's thumbstick once to switch CUT / WELD. CUT is orange; WELD is blue.
- Hold the existing XRI activation trigger to fire. Release to stop.
- After picking up or changing mode, release the trigger before firing. Clicking never fires the beam.
- While holding the tool, that hand's click is reserved from JunkerSprint's sprint/crouch toggle. Stick axes, locomotion, and the other hand's click remain available.
- Work within the default 22 cm beam reach. Aim the nozzle at the surface, start at the green dot, and follow the guide in authored order. Scanner activation remains the existing helmet gesture.

## Demo and assets

The current demo uses `Assets/MyAssets/prefabs/Tools/PLS-T ScrapCutter.prefab`. `Assets/JunkerEquipment/Thermal/Generated/` contains the original prototype `ScrapCutter.prefab`, `CuttableBoard.prefab`, `WeldablePipes.prefab`, and placeholder materials. The tool uses `Assets/MyAssets/mesh/tools/ScrapCutter/PLS-T.glb`, with its imported materials. It is not a replacement model.

The board has four collidable outer bars and one initially kinematic center. Completing the rectangular path releases the center and enables its grab. There is no full-board collider behind the opening.

The demo pipe starts welded on its stand. Grab pipe A to pick up the single assembly; B has no independent collisions or grab while attached. Release the assembly and trace the front seam in CUT to restore B's independent physics. B falls to the catch shelf. Pick it up to rearm the snap, touch B to A's working end and release grip; the placement aid positions and rotates B. Trace the seam in WELD to reattach it.

While welded, B is a kinematic child of A with its colliders and grabs disabled. Cutter impacts cannot move it independently. A remains the assembly collision body; B follows A even when A is dynamic. Cutting restores B's previous Rigidbody, collider, grab, and parent state. An anchored A keeps the assembly anchored.

## Responsibilities and changed files

New runtime scripts in `Assets/JunkerEquipment/Thermal/`:

- `ThermalToolController.cs`: selected-hand click callbacks, existing equipment activation, trigger rearming, obstruction-aware nozzle ray, one target, reach/power/material checks, haptic cadence, and effect shutdown.
- `ThermalPathProgress.cs`: sequential projection onto the current polyline segment, distance-limited progress, retained partial work, optional single-point dwell weld, and progress gizmo.
- `ThermalWorkTarget.cs`: common validation, one successful advancement per target per rendered frame, ordered paths, completion hooks, scanner data, and independent visibility requests.
- `CuttableSection.cs`: one-time release of an authored body and enabling its XR grab.
- `WeldConnection.cs`: position-only alignment, rotation-correcting contact snap, one-body welded state, grab gating, cut/break/reset, and repeatable welding.
- `ThermalFXController.cs`: reused particles/beam, distinct mode colors, spatial audio sources and event hooks.
- `ThermalTargetLink.cs`: resolves a second body or remote collider to its connection.

Editor scripts in `Thermal/Editor/`: `ThermalDemoBuilder.cs` builds a new scene and prefabs without overwriting an existing demo; `ThermalValidation.cs` validates references, renders previews, and exercises the runtime lifecycle and input. `ThermalDemoUpgrade.cs` installs the PLS-T inventory and right-hip setup in the current prefabs and demo.

Existing files modified:

- `Assets/JunkerScanner/ScanData.cs`: `IScanPathProvider` extension contract.
- `Assets/JunkerScanner/HelmetScannerController.cs`: gathers paths from that contract alongside existing cut access providers.
- `Assets/JunkerBody/JunkerSprint.cs`: per-hand click ownership, checked before sprint/crouch toggles.

`AuthoredCutPath` is reused unchanged. The scanner owns the red guide's visibility. The tool draws only its heated progress ribbon, so the red outline does not compete with the cut or weld glow during normal tool use.

## Inspector assignments

The demo is wired. Both PLS-T prefabs have `VRInventoryItem` and an `InventoryAnchor` for the existing backpack storage volume. The Junker and JunkerWeapon player prefabs have a dedicated `Right hip - PLS-T` slot; the demo inherits it. The pistol keeps its own right-hip slot. Drop the tool with its anchor inside an open backpack to store it, or release it close to its right-hip slot to holster it. When placing the tool prefab in another scene, assign `JunkerEquipmentItem.rig` to that player's equipment rig. Check `Grip` and `Nozzle +Z` against the real mesh and your controller pose; the nozzle's positive Z axis is the beam direction.

`ThermalToolController` exposes nozzle, FX, per-hand click action properties, ray distance, beam reach, interaction radius, guide radius, progression multiplier, power, material compatibility, collision mask, cooldown and haptics. Dedicated click actions use the screwdriver's `Primary2DAxisClick` binding convention with performed/canceled callbacks. You can instead assign rig-owned left/right button action references; the tool only disables actions it enabled itself. Do not bind these to stick axes or trigger.

`ThermalPathProgress` exposes tolerance, metres per second, and single-point dwell duration. Progress is a distance along the current segment, capped by the hit projection plus half the tolerance. Holding a fixed point stalls instead of repeatedly adding time to the whole path. Authored paths should be larger than their tolerance; keep tolerance well below the spacing between separate sections.

`ThermalWorkTarget` exposes ordered paths, material ID, minimum power and completion event. An empty tool compatibility string accepts any target material ID.

## Add a cuttable panel

1. Author an outer frame with a real hole and separate inner geometry. Use a collider per solid frame part; never place a solid box collider across the hole.
2. Add Rigidbody and XRGrabInteractable to the inner piece. Start it kinematic, gravity off, grab disabled. Leave room for it to fall without being trapped by frame geometry.
3. Add `CuttableSection` to the frame root and assign `cutout` and `cutoutGrab`.
4. Add a child with LineRenderer, `AuthoredCutPath`, and `ThermalPathProgress`. Author ordered child transforms just above the reachable surface; assign them as control points and enable `loop` if appropriate. Assign a line material and width. Add the progress component to the section's paths array. Multiple paths complete in array order.
5. Add `ScannableObject`, give it a name, and include the section in its providers. Optional existing `CuttableAccessPoint` can be assigned to `scannerAccess` for its opened-state integration.
6. If hit colliders are elsewhere in the hierarchy, add `ThermalTargetLink` to their parent and assign the section. Keep guides close enough to physical surfaces for nozzle ray hits.

## Author a weld joint

1. Give both distinct pieces a Rigidbody and appropriate collision. An anchored part should use a kinematic Rigidbody, not a null world connection.
2. Add `WeldConnection` to one body. Assign body A/B and optional grabs. Add matching anchor transforms to each body at the desired end-to-end position. The snap corrects rotation automatically.
3. Author an accessible short seam with `AuthoredCutPath` and `ThermalPathProgress`; assign it to the connection. A single control point enables a designated point weld. For a traceable seam, use multiple points with spacing larger than the tolerance.
4. Put the seam under body A so it follows the assembly. Add `ThermalTargetLink` to body B pointing at the connection. Add `ScannableObject` providers on both pieces if each should scan independently.
5. Tune `positionTolerance` and `snapDistance`. With `snapOnRelease`, releasing B while any of its own colliders is near A's work end positions and rotates it. It does not create a weld. Disable the aid for custom fixtures.
6. Welding requires releasing both pieces. B becomes kinematic and follows A as a child; its colliders and grabs are disabled. Keep the seam on A so it remains cuttable. Cutting detaches B and restores its previous collider, grab, parent, gravity, kinematic and collision settings. On cut, snap stays blocked until B is picked up. Code-driven assemblies can call `RearmSnap()` before a deliberate new placement.
7. Hook `completed` for weld completion and `weldBroken` for separation. Cutting resets all seam progress. Regrab the separated piece before the alignment aid becomes active again.

## Replace placeholder FX/audio

The PLS-T prefab uses `ThermalFXController` for three-layer plasma beams, separate white-hot contact flashes, and directional metallic sparks. CUT has a white/yellow core with an orange/red halo and broader sparks; WELD uses white/pale cyan/cyan/blue with a tighter spark pattern. Contact effects use the nozzle raycast's actual hit position and normal. `ThermalPathHeatView` draws a narrow ribbon directly over the authored cut/weld guide as progress advances: newly worked metal is white-hot, then cools through yellow, orange, and red over `coolingSeconds` (default 4 s). The old square heat marks are removed. Long stretched sparks mix white/yellow/orange with a few blue streaks. `enableContactLight` is off by default for Quest and, if enabled, uses a short-range shadowless point light. Tune ribbon widths and cooling duration on `ThermalPathHeatView`, particle rate/lifetime/speed in each ParticleSystem, and light and audio volumes on the controller and its child sources.

The provided `gas-burner.mp3` is split into `gas-burner-start.wav` (the audible ignition after leading silence) and `gas-burner-loop.wav`. Every trigger press plays the start once and then the burner loop in either mode. Valid metal contact layers `welding-continuous-loop.wav`; losing contact returns to burner only. Start, loop, contact, crackle, stop, mode-switch and completion clip slots remain exposed for final sound design. The controller gives a slightly stronger/slower contact pulse in CUT and a finer/faster one in WELD. `Tools > Junker Equipment > Thermal > Configure plasma effects and audio` reapplies the prefab VFX/audio setup.

`sparkBackOffset` on the tool FX pulls contact sparks toward the nozzle along its local -Z direction; the demo defaults to 0.1 m so sparks stay in front of the metal. The tool no longer reveals the red authored path. The scanner still does, while the tool's heated ribbon shows only actual cut/weld progress. The demo pipe begins welded: A is the grabbable Rigidbody, and B is a non-simulated child until cut. `Tools > Junker Equipment > Thermal > Repair board and pipe fixtures` restores the demo's board/pipe alignment without changing authored spark particles.

Assign ParticleSystems on `ThermalFXController`: cut/weld nozzle, cut/weld contact, optional metal glow, cut-complete burst, and weld-complete flash. Nozzle systems should be children of the nozzle. Contact and completion systems are repositioned to the actual hit. Use short-lived particles with Play On Awake disabled. Instances are reused; nothing is instantiated per frame.

Assign spatial AudioSources for loop, contact loop, and one-shots, then optional mode switch; cut/weld start, loop, stop; cut/weld contact; and completion clips. Missing clips stay silent. Loop sources change on state transitions rather than restarting each frame. The optional invalid clip is exposed for authored UI/event use and is not played repeatedly for ordinary lost contact. UnityEvents expose mode switch, start, stop, contact start, and target completion/break.

## Validation and limits

Use `Tools > Junker Equipment > Thermal > Validate assets and render preview` and `Run Play Mode validation`. Reports are written to `Temp/JunkerThermal.validation`; previews are in `Temp/Thermal-*.png`. Validation temporarily disables unrelated scene roots in Play mode, creates disposable fixtures, and exits Play mode. Unity restores the Edit mode scene. Do not run this command during a gameplay session you want to preserve.

This prototype uses authored polyline paths, prebuilt pieces, and a kinematic welded sub-piece; there is no runtime mesh cutting, fusion, crafting, inventory extension, repair UI, save-game persistence, or molten geometry. Partial progress lasts for the component's lifetime; it is not a save/load implementation. Pipe art is a simple capped cylinder placeholder. Only the current unfinished path is eligible for tool work. Guide reveal requires a nozzle ray to a relevant nearby physical surface.

Headset checks still required: both controller grips; actual nozzle placement; left/right click exactly once and no sprint/crouch change; trigger release/rearm; moving while aiming; comfortable path tolerance and seam reach; board dropping cleanly; snap and release; several weld/cut/reweld cycles; scanner/tool simultaneous visibility; repeated grab/drop/disable; haptic comfort; and an actual vehicle/floating-origin scene. Hardware comfort, tracking latency and joint behavior under extreme forces cannot be established by synthetic tests alone.


