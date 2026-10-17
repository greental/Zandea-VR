# VR Kenshi gore implementation

## Entry points

- `PhysicalMeleeWeapon.Impact` uses the existing collision, damage and parry path. Only cutting strikes can sever. Strike velocity is measured at the contact point and subtracts tracked player root motion for the sever check.
- `ScavengerHealth` remains the source of health, knockout, death and blood loss. Starving Bandits get `GoreImpactSystem` and `DismemberableCharacter` if a legacy variant lacks them.
- `CombinedStarvingBandit.prefab` has both components explicitly, so their thresholds can be tuned in the Inspector.
- `GoreCombatVRArena.unity` is an isolated copy of the existing imported VR combat arena. It includes the VR rig, a practice cutting sword and an Axe bandit.

## Severing

Supported bone boundaries: head, left/right shoulder, elbow, wrist, hip and knee. A proximal cut excludes its distal descendants from future cuts on the original body. The system partitions each readable skinned mesh by bone influence on the instance and bakes a pose snapshot for the detached visual. This preserves current blend shapes and clothing that uses the same skeleton, but the boundary can be jagged on meshes whose triangles bridge a joint. Source mesh assets are never modified.

The current bandit uses a Generic Animator. Joint lookup supports this model's `CC_Base_*` bone names and Inspector `joints` overrides for other Generic rigs; Humanoid rigs use `Animator.GetBoneTransform`. Confirm all 11 mappings with **Tools > Melee Combat > Validate gore setup**.

The source `male_creator.glb` has one shared skin with 14 mesh nodes. Head skin, body skin, both arms, both legs, nails, eyelashes, eyes, teeth and boxers are separate renderers/meshes. Both arms share one arm mesh and both legs share one leg mesh; a Transform-only detach would remove too much. The head mesh has 40 morph targets and the body/arm/leg meshes each have four. Rigid hair/headwear attached under a severed bone is cloned onto the part; skinned clothing is included in the bone mask pass.

Detached parts receive a rigidbody, box collider, XR grab interactable, wound cap and `SeveredBodyPartIdentity`. Identity fields are NPC ID, name, faction, bounty ID/value, appearance seed and sever point. Starving Bandits default to the `starving_bandits` faction ID; bounty fields can be set on the component because no bounty system was found on this bandit. Heads are protected from cleanup. Other parts expire after 240 seconds when not grabbed.

The current caps are small dark red spheres aligned to the bone boundaries. They are a stable fallback, not authored anatomy-matched cap meshes. Bone masking logs and skips unreadable imported meshes; such clothing may remain visible after a cut until its import has Read/Write enabled. Run **Tools > Melee Combat > Validate gore setup** to check the current import. No arbitrary freeform cutting is used.

## Injury and blood

- Head loss calls the existing death/ragdoll path.
- Arm/hand loss drops a right-hand weapon or a two-handed staff and drives the existing bandit AI into a persistent retreat. This is the stable fallback because the current combat animations do not contain one-armed attack variants.
- Leg loss invokes the existing knockout ragdoll and prevents standing recovery. The project has no crawl animation/behavior.
- Bleeding uses `ScavengerHealth.ApplyBleeding` and can cause death over time.
- Cutting hits produce pooled directional droplets, wound marks, bounded surface marks and trails while a wounded NPC moves. Death grows a single blood pool. Blunt staff hits do not trigger cutting blood effects. Global runtime limits are 24 particle systems, 256 surface marks and 32 pools.
- `WeaponBloodSurface` uses a material property block on blade-only renderers. Assign `bladeRenderers` on weapon prefabs whose geometry is not named `blade` or `metal`. With no authored blade renderer it reuses small stain quads at actual weapon contacts. The renderer tint is not a detailed spatial mask.

## Validation and VR test

1. In Unity, open `GoreCombatVRArena.unity`, then run **Tools > Melee Combat > Validate gore setup**. All relevant skinned meshes must report `readable=true`.
2. Enter Play mode with the XR headset/controllers connected. Pick up the practice sword from the table.
3. Tap the bandit's torso, then slash it. Confirm only the slash damages and makes blood, with a surface mark and a localized wound mark.
4. Strike near the right shoulder/elbow/wrist with a fast, aligned cutting swing. Confirm the weapon drops, the part detaches once, is grabbable and can be thrown, and the bandit retreats.
5. Strike a knee/hip on a fresh bandit. Confirm permanent ragdoll collapse without standing movement.
6. Strike the neck/head on a fresh bandit. Confirm one death event, a detached grabbable head and a growing pool. Inspect the head's `SeveredBodyPartIdentity` and repeat grabs/throws.
7. Repeat weak strikes and Bo Staff hits. Confirm neither severs. Confirm repeated contact does not duplicate a part.
8. Inspect clothing, body morphs, wound caps, ground decals and frame time on the target headset. Tune the limits on `GoreImpactSystem` and `DismemberableCharacter` for the device.

The runtime and editor C# assemblies compile through Unity's Roslyn response files. The last Unity setup check found 14/14 skinned meshes readable and a Generic Animator; joint lookup was adjusted afterward and requires a new setup check. Headset appearance, collision behavior and performance still require the play-mode checks above.
