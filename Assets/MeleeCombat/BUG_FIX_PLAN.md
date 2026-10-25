# VR combat and loot bug fix plan

## ArmorFittings recovery (22 October 2026)

- `ChestArmorFittings.unity` is a separate seven-station Torso fitting scene. Each station has a character and an editable armor Transform; skinned items preview Position/Rotation/Scale edits, and **Capture Chest Armor Fits** saves them to the item assets.
- Preserved the user's saved `ArmorFittings.unity` as `ArmorFittings_UserFitBackup_20261022.unity` before capture. Edit Mode cosmetics refresh no longer overwrites fitting scene transforms on load.
- Captured fitted prefab transforms for linked headwear, beard, boots, skirt, coat, and armor entries. Chest, vest, paladin skirt, and three shoulder sets use the replacement prefab instances from the saved scene; distant spare chest/skirt copies are inactive in the resulting prefabs.
- Kept the replacement armor skinned with its creator bone mapping and four body morph declarations. Arm/hand armor and armored pants remain unmatched and need manual art work.
- The saved-scene capture completed in Unity. In-game animation and headset appearance of the replacement pieces remain to be checked before marking those items fully fixed.

## Headset pass (22 October 2026) — implementation

- Blood particles rendered as opaque red quads. Wound bleeding and impact sprays now use an alpha-masked droplet texture and transparent particle material.
- The Man player wrists now use the known-good controller wrist bases from the Junker player instead of the extra opposing Y corrections.
- `PlayerStartingItems` exposes all four ordinary item slots and their starting prefabs/items on the player root before Play, while retaining `JunkerEquipmentSlot` for every item type.
- `ArmorFittings.unity` is the capture source. Capture includes visual-child offsets for pieces aligned by moving their mesh, and the catalog collects every imported armor, hat, hair, and beard definition.
- Chest streaming re-enables the authored lid and renderers. A chest variant that lost its lid child creates a visible, grabbable, hinged fallback and storage volume.
- Daran starts his greeting at conversational distance, chest selection falls back to the main camera when needed, and he wanders locally while idle.
- Wildlife groups use `Strud 1` with its authored material, spawn 3–5 together, and contain baby, medium, and large sizes.
- Random bandit encounters retry every 90–180 seconds, use a packaged bandit when the settlement group is unloaded, and fall back to terrain ground when no NavMesh sample exists. Another group may spawn after the earlier encounter is removed.
- `PlayerWeaponGripAuthoring` lives on the actual Man player prefab. Its Inspector lists weapon prefabs, creates a hand-held scene preview, saves per-weapon right/left offsets plus the second-hand marker, and applies the fit when that weapon is grabbed.

## Creator-player headset pass (21 October 2026) — active

1. **Creator hand alignment — critical.** Correct the imported creator wrists: the right hand was turned 90 degrees outward and the left hand 90 degrees outward in the opposite direction.
2. **Tracked finger pose — critical.** Drive the creator hand bones from controller grip and trigger values so grabbing and pinching visibly close and reopen the hands.
3. **Full locomotion animation — critical.** Use the humanoid Junker walk/run animation blend and jump motion on the creator body; remove the small procedural foot shuffle that was overriding it.
4. **Remove worn armor — critical.** Helmet and torso clothing must expose a reachable pull volume after dressing and return to a physical loot object when removed.
5. **Wear skirt armor — critical.** Dressing reach must use the garment collider rather than the often distant imported skirt pivot, and fitted leg visuals must bind successfully.
6. **Inspector weapon fitting — critical.** Add a dedicated fitting station with a weapon dropdown, one/two-hand preview, editable primary and secondary position/rotation, and a capture action that writes the canonical weapon prefab.
7. **Random bandit encounters — feature.** Spawn one roaming encounter ahead of the player at a time, with a random group size from three through five and the authored weapon/appearance pools.
8. **Random wildlife — feature.** Maintain a small population selected from all wildlife prefabs, spawning on terrain ahead and to either side of the player.
9. **Helmet on severed head — critical.** Transfer the worn helmet and its pull handle to the detached head, suppress hair on the headless body, and allow one hand to hold the head while the other removes the helmet.
10. **Per-limb body grabbing — critical.** Give the head, arms, legs and torso separate physical grab interactables so two hands can hold different attached parts at their actual contact points.

Implementation started: creator wrist/finger driving, locomotion override removal, larger worn-item removal volumes, skirt collider-distance dressing, per-rigidbody corpse grabs, recurring wildlife, randomized 3–5 bandit encounters, and detached-head headwear transfer are in code. The Inspector weapon fitting station and tracked-controller acceptance remain open.

### Item-slot, fitting scene, rack and chest follow-up (21 October 2026)

- The four generic body locations are now serialized `JunkerEquipmentSlot` children in both creator-player prefabs. Mounted melee weapons become children of the chosen slot, so their hierarchy and transforms can be inspected during play like the original Koombar equipment. The player demo sword is an ordinary `JunkerEquipmentItem`/`VRInventoryItem` starting in the left hip slot; canonical weapons use the same components and no separate weapon-slot system.
- `Assets/MeleeCombat/WeaponGripFitting.unity` is the dedicated fitting scene. Its station Inspector lists canonical weapon prefabs, previews the creator character holding the chosen weapon, exposes the existing primary/left/right/secondary grip markers, and applies marker edits to the canonical prefab.
- The rack's single solid opening-sized box collider is removed. Rack sockets retain and return weapons without an invisible solid volume occupying the weapon space.
- Hinged chests now install an always-open physical storage volume. Held backpack-compatible items show a green box when valid, become accessible floating children of `StoredItemsRoot` when released, and can be taken out again through the existing inventory lifecycle.
- Settlement loading forces chest lids active and temporarily holds building LOD groups on their correctly aligned LOD0 geometry. The authored stair ramp direction was rotated 180 degrees to match the visible staircase and its navigation probes.

### Approved follow-up implementation (21 October 2026)

- Weapon racks now support four independent ordinary weapon sockets populated from canonical weapon prefabs, with matching generated `NpcItemDefinition` assets and no solid collider filling the rack opening.
- Completed bandages use a narrower 4 cm cloth. Ten seconds after treatment they stain dark red and become directly grabbable. Pulling one off turns it into a light physical discarded wrap; it fades and decomposes after thirty seconds on the ground.
- Chest storage preserves deposited `VRInventoryItem` instances across POI disable/destroy and restores the same objects into the corresponding stable chest ID. Prop validation now requires an active hinged lid plus the chest storage volume and green placement indicator.
- Building LOD groups no longer remain forced to LOD0. Lower-detail renderer bounds are realigned to the authored LOD0 center when the settlement loads, after which normal LOD switching resumes.
- The shared Junker/creator locomotion controller gains an unarmed jump state. `JunkerSprint` drives the same `Jumping` parameter for both bodies while retaining the existing walk/run blend.
- A helmet-hidden hairstyle is prepared on the detached head and revealed there when the helmet is pulled off. The headless source body remains hairless.
- Fallen-body rigidbodies become heavier and more damped, with joint projection and smoothed limb grabs to reduce flailing and backwards stretching. Existing combat severing remains the way to detach a pulled limb.
- Interior tissue mesh work and random-encounter lifecycle work remain intentionally on hold.

## Headset playthrough findings (20 October 2026) — open

These reports supersede automated validation. Reproduce each issue in the saved gameplay scene before changing it, then repeat the same headset action after the fix.

1. **Bo Staff NPC attacks miss — critical.** Bo Staff enemies perform attacks but do not hit their target in the gameplay settlement. Use the dedicated Bo Staff combat scene to confirm whether its authored attack reaches and damages the target, then compare that working setup with the runtime weapon/group variant, including weapon pose, animation profile, attack phase, hitbox, range, and target distance.
2. **Canonical weapon prefab coverage — critical.** Every available weapon must have an editable canonical prefab under `Assets/MyAssets/prefabs/weapons/`. Inventory the complete weapon catalog, create or move missing prefab variants without losing references, and make generated NPC/player setup consume those prefabs rather than hiding grip configuration in generated output.
3. **Editable player grip angles — critical.** Weapons held by the player, except the player sword, currently sit at incorrect angles. Each canonical weapon prefab must expose and persist its left- and right-hand grip pose so it can be tuned directly in the Inspector.
4. **Editable two-hand grips and hand assignment — critical.** Every two-handed weapon has the same role-assignment requirement, not only the Bo Staff: expose primary and secondary grip transforms on its canonical prefab, with independently editable hand positions and rotations. The first selecting hand must take the primary grip and the second selecting hand the secondary grip regardless of whether left or right grabs first. Current failure: left-first followed by right places both hands at the same staff location instead of assigning distinct grip points.
5. **Building LOD alignment — high.** Some settlement building LODs float or are offset from the full-detail mesh. Audit the affected LODGroup children and ensure every level preserves the authored building position, rotation, scale, and pivot through streaming.
6. **Chest lids absent on arrival — critical.** Both loot chests arrived with no visible lid in the gameplay settlement. Verify saved lid references, streaming hierarchy, active state, and runtime hinge setup; lids must exist before interaction and survive departure/return.
7. **Backpack form and armor acceptance — high.** Stored helmets must switch to a smaller stable backpack representation and must not roll around inside the backpack. Chest armor and skirts currently fail to enter storage; all wearable categories must be accepted, compacted while stored, and restored as usable/wearable objects when retrieved.
8. **Weapon-rack variety — high.** The settlement rack currently leaves the player with only the Bo Staff. Populate it with several usable weapon classes so combat, grip angles, two-hand behavior, blood, armor protection, and severing can be tested in one playthrough.
9. **POI stair collider orientation — low.** The current stair colliders are rotated 180 degrees relative to the visible stairs and do not match the stair dimensions. Rebuild them from the visible stair bounds with the correct ascent direction and size; verify player and NPC traversal.
10. **Left hip and backpack-side slot discoverability — medium.** The right hip slot worked in the headset. The left hip and both backpack-side slots were not found reliably. Verify their tracked-hand reach and activation volumes, then enlarge or reposition them if their intended locations are not discoverable without searching.

### Retest coverage for this pass

- Bo Staff arena strike reaches and damages its target; the same weapon and attack profile works on a settlement enemy.
- Every weapon is represented by an editable canonical prefab, and both one- and two-hand grip edits survive play mode.
- Affected buildings transition through every LOD without visible movement or floating parts, before and after POI streaming.
- Both chest lids are present, attached, supported, grabbable, and still present after leaving and returning.
- Helmet, torso armor, and skirt each store, remain stable in compact form, retrieve, and can be worn again.

### Critical weapon pass implementation

- Bo Staff NPC damage now sweeps the two real animated staff-tip paths during the Strike phase. This closes kinematic tunneling against a moving VR target while retaining the committed-target, attack-phase, speed, block, and one-hit-per-attack rules used by physical collision damage.
- The complete generated NPC weapon set now has canonical editable prefabs under `Assets/MyAssets/prefabs/weapons/`; all 15 generated bandit variants reference those canonical prefabs at runtime. The three imported armor-pack weapons are present in the same folder.
- Canonical combat weapons expose independent `Primary Grip Right` and `Primary Grip Left` markers. Supported two-hand weapons additionally expose common, right-hand, and left-hand secondary markers.
- Two-hand selection now assigns roles by selection order for every supported two-handed weapon: the first selector remains primary and the second selector uses the secondary marker regardless of handedness. A bad secondary reference that aliases either primary marker is rebuilt as a distinct point. Releasing the primary promotes the remaining hand at its existing secondary point without dropping or snapping the weapon.

### Headset results recorded in this pass

- NPC injury balance passed: combat was challenging without becoming long or exhausting.
- Breakable barrels passed body contact, deliberate breaking, and loot behavior.
- Held weapons did not lift the player or permit walking on air.
- Bandaging passed and felt good.
- Dead-bandit headgear removal passed; simultaneous body grabs at separate locations remain unchecked.
- Right hip storage passed. Left hip and backpack-side storage need a focused reach check.
- POI departure and return remain unchecked.
- Blood and severed-detail checks remain blocked by the rack providing only a Bo Staff.
- The chest is a loot source, not an inventory deposit volume. Persistence means storing chest loot in the backpack, leaving the POI, returning, and retrieving it from the backpack.

## Zandea P0 playable demo (20 October 2026)

The current acceptance path is spawn, discover the settlement without Inspector changes, fight bandits, treat wounds, open a chest, store and retrieve loot, leave, and return. The demo blockers below are being verified in that order; headset acceptance remains open until the whole route passes.

1. **Two-hand weapons (#6):** The equipment rig now considers an already-held multiple-select weapon at its secondary grip, and supported long blades also receive a secondary grip. Verify both hands selecting, steering, either-hand release, and re-grab with tracked controllers.
2. **POI streaming (#1):** Main's terrain treadmill component is enabled in the saved scene and the outpost enables it defensively on startup. Walking away from the ship transfers the treadmill target to the player. The outpost now loads as soon as its authored terrain tile is active, even before the player approaches, and unloads only when that tile streams away. The Main Play check needs to verify this at startup and on departure/return.
3. **NPC injury (#12):** Generated bandits have 95 health, higher trauma thresholds, lower head-knockout chance and reduced sharp/blunt trauma. Living-limb cuts require higher speed and damage. Compare ordinary and powerful hits in the headset.
4. **Loot chest (#4/#5):** Runtime lid setup aligns a real HingeJoint with the chest rim, uses velocity-driven grabbing, expands pickup collision, and filters player contact while held. The container excludes the lid from its own grab colliders. Check the saved chest and crate in physics and in VR.
5. **Backpack looting (#7):** Physical NPC/chest world loot now joins the existing `VRInventoryItem` lifecycle on creation, retaining its `NpcItemInstance` through store and retrieval. Check chest and corpse loot with the backpack volume.
6. **POI stairs (#11):** Authored stairs receive one continuous sloped collider; the tall imported step blockers are disabled before navigation is built. Check player and NPC traversal in both directions.
7. **Breakable barrels (#3):** Body collisions cannot break the barrel. A weapon damage packet can still break it and spill the same loot instance once. Check a body brush, a deliberate strike, fragments and loot.
8. **Per-bandit weapon choice:** The authored group Inspector has one dropdown per member, covering the generated weapon variants and standalone physical weapon prefabs. At startup the choice replaces that member's saved weapon, binds its grip, and copies the matching variant's attack/hand profile when available. Validate a mixed authored group and check each attack style in the headset.
9. **Standalone Main terrain height:** Main starts the player far below the old Koombar terrain reveal offset and has no planet transition controller. On startup, align the treadmill's tile height to the player's ground plane while keeping the authored POI and terrain together. Verify the player stands on terrain and the settlement appears at the matching height.

Next backlog pass: the player equipment rig now creates four additional `JunkerEquipmentSlot` mounts: left and right hip, plus left and right backpack side. These are ordinary item slots that accept weapons and other equipment through the same item-ID allow/block lists; they are not a separate weapon inventory. Existing tool and backpack slots stay registered, and authored child slots auto-register so the count is not capped at four. `GroundLootPlayValidation` checks four separate swords, an ordinary item, and an arbitrary fifth slot. Headset placement, reach, and grab-to-draw still need checking.

Applied bandages now use a fitted visual radius (80% of the treatment-zone radius) while keeping the same three-turn gesture and bleeding treatment, and the completed cloth is removed after 180 seconds. `BandagePlayValidation` passed the treatment, visual-size, consumption, and immediate re-grab checks. Dead/unconscious ragdolls now permit two simultaneous XRI selections across their existing head, arm, leg, and torso colliders. `CorpseGrabPlayValidation` passed two-hand selection, independent release, and collider cleanup after head/arm severing. Tracked-hand movement and placement remain headset checks.

Man player body (#9) first pass: `Assets/CharacterCreator/Player/VR player - Man Creator.prefab` copies the Main Junker Weapons player and replaces its Junker visual subtree with the morphable `male_creator.glb` body. The existing XR, equipment, backpack, and wound systems remain; HMD, two arm chains, torso, and legs are mapped to `CC_Base` bones. It has appearance and armor slots, local-camera head hiding, and simple procedural leg gait while moving. The separate prefab passed an Edit asset audit and a Main Play check for head/leg IK, reachable arm IK, wounds, backpack, four additional shared item slots, heavy body morph, and skinned armored torso binding. Main still uses the original player. Tracked VR fit, retargeted kick/walk animation, in-game appearance editing, and wearing looted armor remain open.

Unity Play validation resumed on 20 October after D: recovered to 343.68 GiB free. The Main treadmill Play check now passes terrain at player height, POI already spawned with its visible tile, four authored bandits and all authored structures present, complete NPC navigation from stair foot to landing, tile departure and return, and preservation of death and authored hierarchy. A short navigation join bridges the terrain/ramp seam. The authored-group Play sweep equips all 15 generated weapon variants on one member without per-weapon transform edits. The prop Play check passes a connected/grabbable chest lid, actual chest loot selection, storage in the existing backpack and retrieval as the same world object and item identity, chest claiming/open-close, intentional barrel breaking and one-time spill. These are automated checks; tracked VR acceptance of grip feel, stair traversal, chest handling, and the full demo route remains open.


## Playthrough backlog (19 October 2026) — open

These are fresh headset reports and feature requests. Earlier implementation notes and automated passes below do not close an item here; close each one only after its behavior is checked in the actual playthrough. Keep this section as the ongoing backlog as new reports arrive.

1. **POI activation and streaming — bug.** The POI appears unchecked/disabled when the game starts and does not appear on approach. Manually unchecking it in-game made the visible settlement look good. Determine which checkbox or enabled state changes, keep the POI active for normal approach, and verify arrival, departure, and return without manual Inspector changes.
2. **Per-bandit weapon selection — feature.** Give each authored member of a Starving Bandit group a dropdown of available weapons. Choosing one should configure the weapon, grips, hand pose, and related anchors automatically, with no per-transform setup. Every bandit should be able to equip and use any listed weapon, including its attack behavior.
3. **Breakable barrels — bug.** Touching a breakable barrel with the player's body makes it disappear. Body contact should not silently destroy or despawn it; breaking should require the intended impact and leave the expected pieces/loot. Other breakables felt good in this playthrough.
4. **Simple loot chest — bug.** Its lid cannot be grabbed, its hinge does not work, and the lid falls through the chest at rest. Make the lid selectable, constrained by its hinge, and supported in the closed position. A grabbed lid must not collide with the player or satisfy locomotion ground queries.
5. **Other loot chest — bug.** Its lid is not connected to the hinge: the player can carry it away, it snaps back oddly, and it falls through the box at rest. Keep it physically attached with sensible limits, rest contact, and held-player collision filtering. Chest contents are partly usable already; preserve that behavior.
6. **Player two-hand weapons — bug.** The player cannot grip a weapon with both hands despite earlier setup work. Both tracked hands must select the same supported weapon, the secondary hand must steer it, and either hand must release cleanly without dropping or locking the other.
7. **Backpack lootables — feature.** Lootable world items should enter the existing backpack/storage system and be retrievable as usable items.
8. **Two-hand body grabbing — feature.** Allow grabbing a dead or unconscious body at separate head, arm, and leg locations, including a second grab with the other hand. The held body should follow both grips without lifting the player or breaking ragdoll/cut-off limb behavior.
9. **Man player body and IK — feature.** Replace the Junker player body with the character `Man`; configure tracked head and hand IK plus the existing interaction, equipment, wound, and locomotion references on that body.
10. **Looted clothing storage and wear — feature.** Store looted cloth/armor in the backpack as a rolled-up version of the flat world cloth, then allow the player to take it out and wear it. Preserve identity, appearance, and equipment slot behavior across storage and wear.

   Implemented on the separate Man player prefab: `NpcWorldItem` adds `WearableLoot` to clothing with an equipped appearance. The existing `VRInventoryItem` backpack lifecycle switches the same object's flat loot mesh to a compact grabbable roll while stored, then restores the flat form when retrieved. Release near the matching head, chest, or hip anchor to fit the catalog garment through `CharacterCosmeticsController`; grab its body handle to take it off. The world object and `NpcItemInstance` GUID remain unchanged, and occupied clothing slots reject a second item. `ClothingWearPlayValidation` passes shirt and pants storage, retrieval, simultaneous wear, slot conflict, and removal. Main still spawns the original Junker player; use the Man player prefab for this feature until that player conversion is adopted in Main. Tracked VR gesture and garment fit remain to check.
11. **POI stairs collision — bug.** Correct the stair colliders so the player and NPCs can walk the visible steps without catching, falling through, or hitting invisible barriers.
12. **NPC injury tuning — bug.** Bandits faint too quickly and lose limbs on roughly every second hit. Raise ordinary combat endurance and severing thresholds while keeping severe hits effective; compare several representative weapons in a normal fight.
13. **Armor protection and weight — feature.** Worn armor should reduce incoming damage, especially cutting and bleeding, according to the covered body part. Its weight should also slow movement proportionally. Test armored versus unarmored hits and speed with representative gear.
14. **Bandage persistence and fit — bug/polish.** A completed bandage stays forever and looks wider than the hand or leg. Give applied wraps an appropriate lifetime or removal path, and size the cloth to the limb while preserving the now-working three-loop treatment, bleeding stop, and attachment.
15. **Additional player item slots — feature.** Use the existing item-slot system for two hip slots and one on each side of the backpack. They must also hold weapons, without a separate weapon-slot type or a four-slot cap. Each should support storing and drawing a weapon without accidental slot changes or player collision.
16. **Severed-limb finish — visual feature.** Replace the current spherical cut cap with convincing interior tissue and severed detail, and smooth jagged detached mesh edges. Check several limb sizes, skin tones, armor states, and cut locations.

## Bug pass (19 October 2026) — reopened from headset testing

The following reports supersede earlier automated passes. Implementation and validation results will be recorded here; tracked-controller acceptance is still required.

1. Holding a weapon lets the player walk on air. Disable weapon/player collision throughout the hold, including locomotion ground queries and collider state changes.
2. Weapons picked up from NPCs need a 180-degree roll around their long axis in the player grip.
3. The player sword currently extends along a palm-down arm with its edge up. It should extend perpendicular to the hand with its edge forward.
4. Authored bandit groups must preserve edited members, gear, appearance and placement instead of replacing them from group Inspector spawn options.
5. Wound treatment works, but takes 6–12 passes to consume a bandage and stop bleeding. Make the configured three turns count reliably during natural wrapping.
6. The handcrafted settlement moves elsewhere at runtime and leaves pieces behind. Preserve the authored layout and transform through streaming.
7. Dead bandit headgear cannot be grabbed. Keep worn visuals, inventory and pull handles synchronized.
8. Bandage capsule colliders are too small and roll away on slopes. Use a larger stable pickup/contact shape.
9. After bandaging, the right hand sometimes cannot grab. Finish manual selection before consuming the roll and verify a subsequent grab.

### Implementation

- Held loot now stays outside the XRI gravity probe mask while held or overlapping the player. Collision ignores are reapplied after collider state changes; release waits for clearance instead of restoring after a fixed timeout. Weapon/world combat contact remains active.
- NPC grip fallback applies a player-only 180-degree rotation around the measured long axis. The two practice sword prefabs remove the extra -90-degree controller offset, returning the blade to the perpendicular authored pose.
- Authored groups retain their members' positions, weapon selection, appearance seed and clothing. Group spawn choices apply to new procedural members. Inventory adopts authored clothing, including headwear; pull handles rebuild when visuals change.
- Bandage wrapping accepts the wider orbit of a controller outside the roll, while retaining direction, jump and oscillation checks. The roll uses a 14 × 10 × 14 cm box with higher angular damping. Consumption ends manual interaction before canceling selection; assisted grabs also clear stale manual state.
- Main's `BanditOutpost` reuses the edited `Layout Preview`, maps its placement through the scene terrain tile, preserves rotation, and includes nearby authored props in the same streaming hierarchy. It no longer replaces that layout with a seed-generated copy. Authored NPCs inside the layout are retained. A scene audit then found `AdobeRound`, `AdobeStairs`, `Adobe2`, both `GateSide` pieces, and the four-member authored bandit group outside the preview hierarchy; these now stream with it. The authored tile's sloped terrain is preserved rather than flattened to one plateau, which had buried the downhill walls and displaced elevated details.

### Validation and remaining gate

`BugPassValidation.RunBatch` compiled and completed with `SUITE PASS` on 19 October. The report at `Logs/BugPass_20261019/report.txt` covers the larger bandage collider, both practice sword prefab angles, held weapon/player collision and gravity filtering, Main's authored settlement location, authored bandit settings, removable dead-bandit headwear, a wider three-turn bandage gesture, bleeding stopped, roll consumption, and an immediate new grab with the same hand. The generated-weapon sweep passed all 15 variants. After the scene audit, Main loaded with its four authored bandits on navigation; the test verified the five formerly detached structures and bandit group now travel with the layout, that the terrain still slopes beneath its walls, and that the authored hierarchy and a dead bandit survive unload and reload. D: held above 5 GiB free during these authorized low-space reruns.

Tracked-controller acceptance remains: check both sword angles, locomotion while holding each weapon, headgear pulling, bandaging on a slope, and the settlement in the headset before marking the reports closed.

## Playthrough follow-up (17 October 2026)

1. **Bandit detection and group gear.** Nearby player detection now interrupts patrol even when a held prop or facing animation blocks the sight ray. The editable members in `Main.unity` now use four actual armed variants in the group gear options: Bo Staff, SawCleaver, RustedDoubleAxe, and PoleBlade. At startup, authored members receive their selected weapon, a new appearance seed, body and face morphs, and a headwear choice. A delayed refresh keeps headwear visible after inventory startup. Play validation passes four distinct weapons, varied weight and face values, rendered headgear, following, shared engagement, and a leader attack.
2. **Fixed player grip and two hands.** Physical weapons snap to the authored hand pose when selected. Grip position and rotation are solved relative to the weapon root, so a nested grip marker and pickup angle cannot change the blade orientation. Right and left position and Euler offsets are exposed in the Inspector; both practice sword prefabs use a forward blade angle. Held bodies extrapolate with stronger damped joint drives and carry the player rig's translation while clear of world contact; collision still arrests the dynamic blade. Bo Staff, SawCleaver, RustedDoubleAxe, and PoleBlade now create a secondary grip and accept a second XR selector. The second controller steers the weapon axis. Play validation passes secondary grip setup on all four types; tracked-hand feel still needs a headset pass.
3. **Held loot collision.** Weapons and severed body parts ignore player body colliders while selected and restore collision after release once clear. This removes the physics feedback that could lift the player. Headset pickup and drop are still needed to confirm the feel.
4. **Blood hits.** Every flesh hit explicitly emits visible blood particles. Particle size and tint are increased, and hit marks raycast down to static floor surfaces only. Follow-up amputation pulses track the moving wound bone instead of staying at the original world point. Play validation emitted 36 rendered particles and placed its mark on terrain. A headset pass should check repeated hits and amputation sprays.

Unity compiled these changes. The generated weapon Play sweep passes all 15 variants, including two-hand setup on the four named weapons. The terrain check passes sword and clothing contact plus holster and unmount through the player equipment rig. The bandit group and hit-blood checks pass. Remaining work is a tracked VR pass for grip angle, walking smoothness, second-hand release, player lift, and blood visibility.

## Playthrough follow-up (16 October 2026, second pass)

The latest headset pass confirmed metal clash audio and rendered severed limbs. It found these issues and the changes below:

1. **Dropped weapons and loot interaction/terrain contact.** Loose melee weapons now get a shallow box collider sized from their visible mesh, in addition to their strike collider. World loot receives the same contact shape and continuous collision detection when bound. The equipment rig searches more nearby colliders, reaches at least 0.75 m, and ignores dead or unconscious NPC colliders when checking a clear path to loot. Play mode confirms all 15 bandit weapons drop with active grab shapes and mount in a normal equipment slot; a separate terrain simulation confirms the practice sword and scarf loot land with solid boxes. Re-test hand selection and terrain contact in the headset.
2. **Hand pose, fixed angle, and held bob.** Player hand identity is resolved against the left/right controller hierarchy. Right and left grip Euler offsets are editable on `PhysicalMeleeWeapon`, with an active hand-pose marker for avatar IK. The physical joint now receives the latest tracked pose in the same physics step, without adding alternating locomotion velocity impulses. Re-test both hands, running, turns, and weapon contacts in the headset.
3. **Sword holster.** Physical melee weapons now use `JunkerEquipmentItem`, `JunkerEquipmentSlot`, and `JunkerEquipmentRig.TryHolster`. The rig adds four ordinary item slots at the hips and backpack sides, and authored child slots can expand the set. Every slot applies the same item-ID filtering to weapons and other equipment. Re-test release near the hip, drawing, backpack storage, and collision behavior in the headset.
4. **Bandit leader and followers.** Scene-authored unarmed followers now equip their configured generated weapon when the group starts. The group's weapon options appear below armor options, and new group spawns skip unarmed profiles when a weapon is required. Shared sight ignores members' separately spawned held weapons. NPC-held weapons only apply damage during the Strike phase. Two Play checks passed for four armed members, follow destinations, shared engagement, and a leader attack. Re-test the actual group patrol and combat in the headset.
5. **Severed hand size and hair tint.** Wrist selection includes blended cuff/palm triangles; the Play sweep still passes all 11 cuts and wrist bounds increased slightly. Rigid hair/accessory clones explicitly copy the source renderer's per-material color overrides. Re-test detached hand size and varied hair colors in the headset.

The user still needs to verify physical pickup, terrain contact, sword motion, sheathing, and appearance with VR input. Automated Play checks cannot establish how those interactions feel through a tracked controller.

## Playthrough follow-up (16 October 2026)

The latest headset pass confirmed NPC clothing loot, terrain blood marks, and removal of the sword's walking lag. It found these remaining issues, ordered by play impact:

1. **Head geometry and hit blood.** The character prefab has 0.01 scale: detached flesh had baked transform scale and renderer scale applied together. Both head and clothing bakes now use unscaled source coordinates, with world scale applied once. Unity Play checks now pass at all 11 cut points: each produced flesh triangles with tight bounds, the head persisted after its NPC was destroyed, and isolated head, arm, and leg materials rendered pixels. Head volume fallback is limited to small facial meshes, and detached bounds use only vertices referenced by cut triangles. Floating wound quads were removed; impact and amputation squirts use the pooled particle spray. Re-test a helmeted head, varied skin/body morphs, and repeated impacts in the headset.
2. **Bandit weapon pickup and sword sheath.** Legacy generated weapons lacked `XRGrabInteractable`; `PhysicalMeleeWeapon` now supplies one with physical colliders at runtime. Death now drops a bandit's generated weapon before ragdoll activation, as knockout already did. The Shield and Saber prefab had its physical weapon and collider disabled; both are enabled now. A Play check passes for all 15 generated variants: each begins equipped and drops on death with an active grab collider, dynamic rigidbody, and body storage component. A melee sheath anchor on `JunkerEquipmentRig` accepts a released weapon near the hip and lets it be drawn again. Re-test physical pickup for every weapon and sheath use with both sword hands in the headset.
3. **Clothing scale.** The cached scarf loot mesh was baked in centimeter coordinates, leaving a 71 × 49 unit collider. Its saved prefab now has 0.01 root scale, and the clothing baker detects oversized imported meshes so rebakes preserve that normalization. Cached loot keeps its prefab scale when pulled off. Re-test `scarf_wrap_b` and normal sized garments.
4. **Combat contact.** Bandit weapons no longer deal damage to other `StarvingBandit` actors during ordinary physics contact, including bandits from other groups; intentional ally hits during a real swing remain a future pass. Significant weapon clashes now play the existing metal parry sounds. Re-test patrol collisions, attacks, and player parries.
5. **Held sword bob.** The player joint uses higher solver precision, a short projection tolerance, and mass-aware damping while remaining physical. Root translation and turn velocity are now smoothed before their momentum correction is applied, avoiding alternating physics samples during steady locomotion. Re-test walking, running, turning, and blade contact.
6. **Editable groups.** `BanditGroup` accepts scene-authored members. Its Inspector button spawns editable prefab instances and switches the group to those members at runtime. Populate the desired group in the scene, then verify patrol and engagement.
7. **Animation polish.** Keep the current imported attacks for now. Review each weapon and every directional animation in a later focused pass.

Priority is based on how often the issue blocks normal play and how much it affects combat readability. Validate each item in the actual VR arena before marking it complete.

## P0 — core interaction

1. **Held sword tracks the hand while walking and running.** Remove the player target speed clamp, keep contact driven by the physical joint, and validate locomotion, sharp turns, impacts, and release. Code change started in `PhysicalMeleeWeapon.cs`; headset play check pending.
2. **All weapons have a larger, reliable pickup region.** Assisted grip reach is now 0.75 m and includes equipment, melee weapons, and NPC loot. A single proximity comparison chooses the closest valid item, and scenery blocks assisted grabs through walls. Test both hands and dense loot clusters in VR.
3. **Fixed grip orientation.** Physical melee grabs disable XRI dynamic attach; the generated melee builder creates explicit left and right grip markers. The saved practice sword and its builder now bind both hands to its authored grip. Existing tuned prefabs and actual blade direction still need an orientation audit in VR.
4. **Every weapon stores on the body.** Physical melee weapons now receive `VRInventoryItem` at runtime for the existing backpack storage volume. Test store and retrieval for all melee classes and confirm oversized models fit visually.

## P1 — loot and gore

5. **NPC items can be picked up.** Assisted grip reaches dropped `NpcWorldItem` objects and clothing pull handles. The root cause on generated bandits was missing loot components: 14 saved `StarvingBandit_*.prefab` variants now have `NpcInventory` with the Starving Bandit loot table and `NpcClothingLoot`; the unified Bo Staff already had both. The variant builder now configures these for future prefabs. Test stripping, pocket spill, transfer, and persistence on dead and unconscious NPCs.
6. **Severed flesh appears with clothing and helmet.** All 11 cut locations pass Play mode flesh geometry and tight bounds checks. Head, arm, and leg material captures render correctly; the head persists after its NPC is destroyed. Validate helmeted heads and appearance variants in the headset.
7. **Severed parts preserve appearance.** Detached meshes now copy effective per-material property blocks from the source skin; `BakeMesh` already captures the active body morph. Test body size and skin tone variants.
8. **Blood stays on the blade.** The floating blade quads are removed. Blade renderers now generate light, heavy, and soaked albedo variants from their source texture and switch stages as coverage changes; materials without an albedo property retain a color fallback. Inspect blade-only renderer assignments so handles do not stain.
9. **Surface blood faces upward and stays on terrain.** Ground hits reject downward-facing surfaces. Marks and pools now use small meshes sampled against the ground at each vertex; pools resample as they grow. Validate rendering, slope contact, and visual cost with multiple corpses in the arena.

## P2 — animation quality

10. **Generated bandit attack pass.** The 12 saved weapon variants already use imported combat attacks. Their one-hand AttackDirection5–7 slots now repeat single strikes instead of long multi-hit combos, and all eight attack phase timings are capped at 1.7 seconds with matching Animator playback speeds. Two-hand variants retain their existing Great Sword Slash mix with the same timing cap. `BoStaff_Unified` uses its separate directional system. `ImportedCombatAnimationInstaller` now reproduces the saved setup, and the older variant builder refuses to overwrite installed imported attacks. Check attack arc, grip alignment, hit timing, recovery, and foot sliding for each weapon in the arena.

## Fallen body interaction (17 October 2026)

Dead and unconscious ragdolls now expose an XR grab across their active bone colliders, anchored on the dynamic hips body. Held bodies ignore player collision through the existing loot collision guard, and cut-off bones leave the grab collider list. Sword damage against a dead NPC now reaches the severing system; corpse-only speed and damage multipliers are editable in `DismemberableCharacter`. Recovery waits while an unconscious body is held. Unity Play mode passed a synthetic hand select/release after the body fell, followed by ordinary-threshold head and shoulder cuts, visible head flesh geometry, and grab-collider cleanup. Test lifting, dragging, and chopping fallen bodies with tracked hands in the headset.

## Validation gate

Serialized asset checks pass for all 15 generated bandit loot prefabs and all 12 imported attack variants. Unity compiled the latest fixes. `Temp/SeveredLimbPlayValidation.result` passes all 11 cut geometry checks, head persistence, and isolated head/arm/leg material rendering. `Temp/CombatInteractionPlayValidation.result` passes all 15 generated weapon variants for runtime equip, death drop, active grab collider, dynamic rigidbody, body storage, ground box, and standard equipment slot mounting. `Temp/BanditGroupPlayValidation.result` twice passed four armed members, follow destinations, shared engagement, and a leader attack. `Temp/GroundLootPlayValidation.result` confirms the practice sword and scarf loot settle on active terrain with solid box colliders. Test tracked-hand pickup, sheathing, clothing scale, body storage, NPC loot, dismemberment appearance, blood, and attacks in the headset. Record prefab/scene coverage before claiming any item fully fixed.
