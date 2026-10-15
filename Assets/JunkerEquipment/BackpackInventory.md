# Physical backpack inventory

The existing backpack root, shoulder slot, and `JunkerEquipmentItem` attachment flow are retained. The bag now grabs by its top handle, preserving its current orientation on pickup. No items are spawned or copied during storage/retrieval.

## Configured assets

- `Assets/MyAssets/prefabs/Player/Junker.prefab`
- `Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab`
- `Assets/MyAssets/prefabs/weapons/JKR-88 1.prefab`
- `Assets/MyAssets/prefabs/weapons/R4-PowerCell.prefab`
- `Assets/Scenes/JunkerBody.unity` (scene-owned powered screwdriver and PKO, plus the existing player tools)

The backpack's original `Visual` is the closed mesh. `Open backpack visual` instantiates `Assets/MyAssets/mesh/Armor/backpack/Backpack.glb`. `StorageVolume` is a box trigger; `StoredItemsRoot` owns stored poses. XRI uses only the forgiving `Top handle` collider to grab the bag, so reaching into the cavity selects contents. The shell remains a trigger and does not block insertion.

Release the bag in front of you to leave it floating at exactly its released world position and rotation. It does not turn toward the headset or follow the player after release. Pick up the handle again, bring it behind your shoulder, and release near the back slot to lock it on. The return check uses the larger of `returnRadius` (initially 0.35 m) and the slot's radius, without the old 0.18 m item-radius cap. Both root and handle distance are considered so wrist orientation does not make return unnecessarily precise. Other tools' holster rules are unchanged.

The handle starts at local `(0, 0.04, -0.14)` with a 0.075 m trigger sphere. Its dynamic attachment matches the controller's initial rotation while retaining the authored handle position, so pickup does not twist the bag; subsequent wrist motion controls it normally. Adjust `Top handle` if needed for reach comfort.

On `JunkerWeapon`, the backpack slot is at local `(-0.18, 0.054, -0.23)` under `PlayerEquipmentRig`. The backpack's `holsterEuler` is `(0, 180, 0)`, matching its authored mounted rotation. Set mounted rotation through `holsterEuler`: the equipment script reapplies that rotation during play, overriding a rotation set only on the bag Transform. The previous farther-back slot and zero-degree mount put the handle beyond normal shoulder reach. The validation now uses JunkerWeapon's actual slot, scale, and mounted rotation and checks direct-hand overlap and grip input before running the storage tests.

The open model uses a uniform 0.26 scale and local offset `(0, -0.07, 0)`. The initial forgiving cavity box is centered at `(0, -0.16, 0.04)` with size `(0.30, 0.24, 0.28)` in backpack-root units. Select `StorageVolume` to see its gizmo and tune the fit after a headset test. No Inspector assignments are required for the configured assets.

## Runtime scripts

- `VRInventoryItem`: anchor validation, XRI events, one storage release per grab, same-instance retrieval, reversible collision/render/interaction/rigidbody snapshots.
- `BackpackInventory`: collection, mounted/held/open state, closed/open visuals and access to contents.
- `BackpackStorageVolume`: registered box volumes and small valid-placement rim. Held items query only registered volumes; acceptance uses the anchor, not collider overlap.
- `JunkerEquipmentItem` has a small integration hook: valid inventory placement wins over its existing late holster check. Stored items cannot be mounted into equipment slots.

Held already means usable. A released floating bag remains open. Mounting disables stored collider queries and interaction layers, and optionally hides renderers. Retrieving restores the saved settings; XRI continues to own hand tracking. Trigger-only stored colliders allow direct hand selection without rigidbody contact response. Nested grabbables such as an installed pistol power cell are unavailable until the containing item is removed; their original interaction layers are restored with the item.

The release callback stores synchronously, suppresses XRI's deferred throw for that release, and keeps the exact relative pose. After retrieval, XRI's cached kinematic drop state and retained-parent policy are corrected so an ordinary world drop does not remain attached to the bag.

## Compatible items and extension

Manual screwdriver, powered screwdriver, binoculars, scene PKO, and JKR-88 use their existing functionality and XRI grab components. Add `VRInventoryItem` to another object's Rigidbody/XRGrabInteractable root. Assign an `InventoryAnchor` at the part of the object that should count as inside; omission falls back to its root. Turn off `CanStoreInBackpack` to reject an item. A container cannot store itself or an ancestor, and backpacks cannot nest.

R4 power cells are also configured, including cells nested in pistol prefabs. Loose full, partially used, and empty cells are accepted; storage preserves their charge. `JunkerPowerCell` yields to valid inventory storage before considering nearby pistol sockets, and suspends its loose-physics recovery while stored. `JunkerPistolBatterySocket` refuses stored cells and sockets inside stored pistols. Retrieval restores the normal battery reload flow; an installed cell must be removed through its existing latch first.

Storage requires an actual non-canceled release with the anchor still in a usable volume. Entering and leaving while held, touching with a collider edge, and dropping beside the bag do not store. The small mint rim appears for valid placement; compatible XRI input interactors also receive a single brief haptic pulse on entry.

No grid, packing, capacity, stacking, save/load, quick slots, cutter, or repair features were added. The item/volume/container separation leaves room for lockers and dedicated sockets later.

## Editor tools and validation

`Tools > Junker Equipment > Configure physical backpack inventory` updates the listed prefabs and the existing JunkerBody scene without rebuilding the rig. Rerunning preserves existing cavity dimensions and model placement. Setup report: `Temp/BackpackInventory.setup`.

`Tools > Junker Equipment > Validate physical backpack in Play Mode` runs actual XRI select/release operations against a clone of the configured backpack and isolated equipment/generic test objects, then exits Play Mode. It checks stable relative placement, multiple objects, mount/retrieve, original physics, parent policy, repeat storage, rejected anchors, canceled selection, and destroyed contents. Report: `Temp/BackpackInventory.validation`. Synthetic test objects exist only during that test, not in saved gameplay scenes.

## Headset acceptance test

1. Reach behind, grab the top handle, and pull the backpack forward. Confirm it does not twist on pickup. Release it: it should remain floating at that exact pose without auto-facing you.
2. Place the manual screwdriver diagonally in the cavity. Confirm the subtle rim/haptic, release, and verify it stays exactly there.
3. Place another tool beside it. Rotate and shake the bag; neither item should fall, jitter, or push the bag.
4. Grab the handle again, return it near the existing shoulder slot, and release. Confirm it locks onto your back and stored tools cannot be grabbed through the torso.
5. Pull the bag forward again. Confirm both placements, grab the same screwdriver, and use it on the existing repair panel.
6. Repeat with powered screwdriver, binoculars and pistol; confirm their existing trigger/optics/firing behavior after retrieval.
7. Move a held item in then out before releasing, release with only its collider edge inside, and drop beside the bag. None should store.
8. Store an R4 battery, leave it for a second, retrieve it, reload the pistol, remove it through the latch, and store it again. Check that its charge is preserved throughout.

Automated checks do not establish headset comfort, controller haptic delivery, or final visual cavity alignment. Those require this VR test.
