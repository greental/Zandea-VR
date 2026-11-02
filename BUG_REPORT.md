# Zandea — Bug Backlog

**Playtest date:** October 30, 2026  
**Status:** Open — awaiting fixes and in-headset verification

## P0 — Critical

### BUG-004 — Bandit short saber attacks miss stationary targets

**Area:** NPC Combat / Hit Detection

Bandits wielding short sabers do not actually hit the player, even when the player remains stationary.

**Expected:** Attacks should connect with the player's body when in range, with proper collision detection and damage.

**Investigate:** Attack animations, weapon reach, IK, target positioning, and active hit windows.

### BUG-005 — Daran missing from merchant post

**Area:** NPC Spawning / POI

Daran does not appear at his assigned merchant post.

**Expected:** Daran consistently spawns at his designated location whenever the settlement is active.

**Investigate:** Scene references, spawn initialization, NPC activation, and POI streaming.

### BUG-006 — Windblown sand invisible

**Area:** Environment / VFX

The sand-in-the-wind particle effect cannot be seen during gameplay.

**Expected:** Sand should appear naturally around exposed dunes and mounds under appropriate wind conditions.

**Investigate:** Particle emission, visibility, materials, rendering layers, culling, and environmental activation.

### BUG-007 — Barrels spawn in square formation with dangling meshes

**Area:** Mission Props / Physics

Barrels appear arranged in an unnatural square formation, and their meshes dangle or move incorrectly relative to their physical objects.

**Expected:** Barrels spawn in intended positions, with stable meshes aligned to their colliders and rigidbodies.

**Investigate:** Spawn layout, mesh transforms, parenting, joints, and rigidbody setup.

## P1 — High Priority

### BUG-001 — Held clothing replaces already equipped clothing

**Area:** Equipment / Inventory

Bringing clothing held in the player's hand to an occupied body slot replaces the existing equipped item.

**Expected:** Clothing can only be equipped onto an empty compatible body slot. Occupied slots reject new clothing until the current item is explicitly removed.

### BUG-002 — Shoulder harness cannot be removed; replaced armor floats

**Area:** Equipment / Physics

Shoulder Harness A and B cannot be removed. Equipping a harness over existing armor can leave the previous armor floating in midair, sometimes at a reduced scale.

**Expected:** Both harness variants can be removed. No armor replacement occurs automatically, and removed items retain correct scale and physical behavior.

### BUG-003 — Short saber bandit elbow bends inward

**Area:** NPC Animation / IK

Bandits wielding short sabers have incorrectly positioned elbows that bend inward unnaturally.

**Expected:** Arm and elbow positioning should remain anatomically believable during idle, movement, attack, and defense.

### BUG-008 — Cannot initiate conversation with Daran

**Area:** NPC Dialogue / Scene Integration

During a previous run when Daran was present, the player could not initiate dialogue.

**Suspected cause:** Player object changes were implemented in another scene but not merged into the main scene.

**Expected:** The player can approach Daran and reliably initiate merchant dialogue.

### BUG-009 — Cannot remove helmets from corpses

**Area:** Corpse Looting / Scene Integration

Helmet removal functionality was reportedly implemented in a separate scene but is unavailable in the main playable scene.

**Expected:** Players can physically grab and remove equipped helmets from dead NPCs.

**Investigate:** Missing scene or prefab changes and corpse equipment interaction components.

### BUG-010 — Two-handed corpse grabbing unavailable

**Area:** Corpse Interaction / Scene Integration

Two-handed corpse grabbing was reportedly implemented in a separate scene but is unavailable in the main playable scene.

**Expected:** Players can grab and manipulate corpses using both hands simultaneously, with stable physics.

**Investigate:** Missing scene or prefab changes, grab interactables, and two-handed interaction configuration.

## Cross-Cutting Investigation — Scene Integration

Review the main playable scene against the scenes where Daran dialogue, helmet removal, and two-handed corpse grabbing were implemented.

Prefer synchronizing shared player/NPC prefabs and components rather than manually copying scene objects. Verify that prefab overrides and serialized references are preserved.

## Definition of Done

- Fix confirmed in the main playable scene.
- No regressions in related systems.
- Verified during an actual VR headset playtest.
- Relevant prefab and scene changes committed.
- Ticket closed only after successful playtest verification.
