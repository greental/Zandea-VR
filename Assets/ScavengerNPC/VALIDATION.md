# Scavenger NPC validation - 2026-09-27

Unity 6000.2.8f1. Generated assets and play-mode checks ran in an isolated project using the same installed navigation/glTF packages. Existing gameplay scenes were not modified.

- Runtime and Editor C# compiled against the main project's Unity assembly references (compiler exit 0), including the JunkerPistol damage integration.
- All five repaired GLBs preserve every byte outside joint-index and skin-weight buffers. Each repairs 201 right-thumb vertices; skin weights remain normalized.
- Both generated prefabs have valid scripts, animation bindings, weapon/eye references, 13 ragdoll bodies, and 12 joints.

Actual headless Unity play-mode results:

```text
PASS spawn on baked NavMesh
PASS health initialized
PASS wall blocks sight from cover
PASS peek point sees target
PASS sees player, runs to cover, hides, peeks
PASS aimed burst fires from peek
PASS NPC shots damage player collider
PASS nonlethal hit deducts health
PASS lethal hit transitions to dead
PASS death enables physics and disables animation/navigation
PASS death releases cover
PASS dead NPC cannot fire or die twice
PASS ragdoll remains finite and above floor
PASS unalerted patrol follows waypoint
PASS standing stops navigation without distant-target aggro
PASS sitting lowers pelvis
PASS casual idle remains unalerted
```

Visual hand/grip alignment and VR feel still need in-headset review. Tests exercise NPC hitscan damage and the existing damage contract, but do not simulate an XR trigger pulling the player's pistol. Seating and aiming are procedural; there are no individual finger joints. Navigation assumes Y-up and uses authored cover/peek markers.

## Follow-up attachment fix

Both prefabs were rebuilt through the main project's Unity importer. Their guns are now children of `mixamorig:RightHand`. All thirteen animated ragdoll bodies use `RigidbodyInterpolation.None`; interpolation activates only at death. Unity Play Mode additionally passed checks that the weapon follows the hand, the animated mesh stays within human-sized bounds while aiming, and ragdoll joint anchors remain within 0.35 m after impact. Patrol, standing, sitting, and casual checks still pass. A visual rig diagnostic menu was added for inspecting scene instances and prefab overrides.

## Ammo gun and cover update

The isolated Unity play run passed 24 checks, including crouching while hidden, alternating both side peek markers, shooting from an over-cover peek without stepping sideways, and the earlier animation, damage, ragdoll, and idle behavior checks. The run used a lightweight stub for the project's XR pistol classes, so it verifies the enemy state machine but does not verify real R4 charge consumption. The actual `JunkerPistol` and NPC integration compiled against the main project's assemblies. The main editor generated `Scavenger_JKR88_Ammo.prefab`; its subsequent demo-scene regeneration hit a Play-mode transition error, so the complete build validation did not finish.

## Hit reaction update

The isolated Unity play run passed 26 checks after adding the upper-body flinch. A nonlethal damage packet starts the reaction, the reaction weight rises and returns to zero, and lethal damage still activates ragdoll immediately. The procedural flinch uses the existing rig and does not require a new imported clip.

## Projectile and restart update

Runtime and Editor code compiled against the main project's Unity assemblies. An isolated Unity Play Mode test passed delayed projectile damage, physical Rigidbody impulse, and scorch-mark creation. The shootable restart sign prefab was built through Unity and placed in the Scavenger demo scene. The battery's blue face was identified on the GLB's +Z mesh side; the prefab visual's -90-degree Y rotation already points it toward the pistol's local left, so the socket uses zero additional Y rotation. Physical battery release and full scene reload still need in-headset/play-scene verification.
