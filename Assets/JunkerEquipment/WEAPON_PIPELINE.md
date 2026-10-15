# Mesh to Junker primary weapon

The existing `JunkerPistolKit` is the shared installer for handheld guns. It adds the Rigidbody, collider, XR grab interaction, `JunkerEquipmentItem`, firing component, muzzle flash, and spatial audio. `JunkerEquipmentSlot` accepts items by `itemId`. `OpticalDevice` supplies binocular-style viewing for any held optic.

## RustFang

`Tools > Junker Equipment > Build RustFang primary rifle` imports the gun and scope GLBs from `Assets/MyAssets/mesh/weapons/SniperRifles/RustFang`, combines them in their shared source coordinate frame, builds `Assets/MyAssets/prefabs/weapons/RustFang.prefab`, and assigns an instance as the starting item in the `primary` shoulder slot of `Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab`. The first build also runs automatically on editor script reload if the prefab is missing. The command can be rerun to refresh scripts and slot wiring without duplicating the rifle.

The rifle uses `JunkerPistol` for trigger firing and damage. The scope uses `OpticalDevice` with the project's binocular viewing shader. Hold the rifle, bring the scope eyepiece near the HMD, and align it with your gaze. Press the right thumbstick while viewing to cycle 2x, 4x, and 8x. The trigger remains available for firing.

The right shoulder slot and rifle each allow a 0.38 metre return distance. Bring the rifle root or Grip near the shoulder and release. On grab the rifle detaches from the moving player hierarchy, so a released rifle remains in the world. Rifle/player collider pairs stay ignored while the rifle is held or loose.

## New weapon checklist

1. Import the visual mesh. Put separately exported attachments under one Visual transform with matching source origins. Orient the visual so the bore points along the root's blue/Z axis and scale it in metres.
2. Add `JunkerPistolKit` to the root. Set a unique `itemId`, Visual, Grip, and Muzzle. Place Muzzle at the bore exit with blue/Z along the shot. Run **Install or refresh pistol components**. Set fire rate, damage, range, audio clips, and optional ammunition on the kit.
3. For a magnified sight, add `OpticalDevice`, a dedicated disabled camera, eyepiece and objective anchors, and `JunkerScopeZoom`. Assign the grab interactable, viewing shader, and zoom levels. Keep the optical camera separate from the HMD camera.
4. Add the item ID to a `JunkerEquipmentSlot.approvedItemIds` list. Place a prefab instance in that slot and set `startingItem` to its `JunkerEquipmentItem` if it should spawn equipped.
5. In Play Mode, check grip pose, muzzle direction, collider fit, shoulder clearance, projectile impact, scope alignment, and zoom. Adjust the saved prefab anchors for the specific model.

RustFang's anchors are estimated from renderer bounds. Inspect the saved prefab in Unity and fine tune Grip, Muzzle, Scope eyepiece, and Scope objective against the model before relying on aiming accuracy.

## Edit a weapon's hand grip

Open the weapon prefab in Prefab Mode, select its root `JunkerEquipmentItem`, and click **Select Grip and frame it**. Move and rotate the `Grip` child in the Scene view to place the hand on the handle. The blue axis is forward and green is up. Weapons with **Use Grip Transform Directly** enabled use this transform exactly; the runtime does not overwrite its pose with cached offsets. The root's Scene view also shows position and rotation handles for the Grip. Save the prefab and test a grab. For RustFang, its Grip, XR Grab Interactable attach transform, and Pistol Kit grip pose all reference the same child.
