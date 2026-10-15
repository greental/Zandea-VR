# Reusable screw-fastened panels

Use `Assets/MyAssets/prefabs/Tools/ScrewFastenedPanel.prefab`. It contains an open frame, a removable cover, four captive screws, two docking anchors, and a scanner compartment. `Assets/MyAssets/prefabs/Tools/CaptiveScrew.prefab` is the reusable individual screw.

## Make another panel

1. Drag **ScrewFastenedPanel** into the scene or make a prefab variant. Rotate its local +Z outward from the opening.
2. Replace/resize **Cover/Replace this cover mesh** and its collider. Keep the Cover root, Rigidbody and XR Grab Interactable. The frame uses four rails so removing the cover leaves a real opening.
3. Duplicate, delete or reposition **Captive Screw** children under **Cover**. No screwdriver target list needs editing. The panel collects its actual screws on startup; **Configure / refresh this panel** in the panel Inspector updates the authoring setup immediately. Zero screws means a freely removable cover.
4. Move **Storage Anchor - move me** to where the loose cover should be parked. Its position and rotation describe the **Cover root**, not a screw or a mesh corner. Keep this anchor outside Cover. It can also reference another appropriate transform on the machine.
5. **Closed Anchor** describes the cover's shut pose. Keep it outside Cover too. If changing the closed placement, move the anchor along with the cover. Tune **Snap Distance** and **Snap Angle** on `JunkerRepairPanel` for forgiving reassembly.

## Player interaction

* Loosen every screw. With the powered screwdriver, click the stick **of the hand holding it** to switch **Tighten / Loosen**; hold the trigger while contacting a screw head. The click is reserved for the tool while held so it does not also trigger crouch. A driver still in Tighten mode cannot further turn an already-tight screw. The manual driver's wrist rotation continues to work.
* The unfastened cover stays in place until grabbed. Screws remain captive on the cover.
* Grip and move the cover away, then release. With **Store On Release** enabled (default), it parks at Storage Anchor. It remains grabbable and follows the machine.
* Grab it from storage, bring it near the closed pose, align approximately and release. It snaps closed and stays there without being held. Tighten the screws with both hands free.
* Snapping does not fasten screws. The first partially tightened screw retains the cover; all screws can then be tightened fully. Loosen them again to repeat the cycle.

Turn **Store On Release** off if you prefer deliberate placement: release near Storage Anchor to park it, otherwise it becomes a loose physics object. A held cover is never pulled into either anchor. Screws cannot be worked while the cover is held or stored.

## Existing copied panels and scanner

The old screwdriver only checked its serialized scene-screw array. It now also uses a small nearby physics query, so scene copies and new prefabs work automatically. A screw creates its own small head trigger on startup, including legacy screws that had no collider. Travel is measured in world metres so scaled panels retain usable screw motion.

The old panel unlocked only once and treated missing screw-array entries as permanently fastened. The replacement supports reopening/reassembly and collects actual child screws. Its Inspector button also reparents old frame-mounted screws under the moving cover, creates anchors and clears an accidentally copied external instruction-label reference.

`HoverBikeRepairpanel` and the other copied panels in the currently open JunkerThermalDemo scene were upgraded in place, preserving their meshes and placements. The bike panels reference its existing `ServiceCompartment`: grabbing the cover opens inspection access, and snapping it closed closes access. The current scene already had unsaved edits, so these scene changes were left in memory; save the scene to keep them. The new prefab assets themselves are saved.

For a different vehicle, assign the appropriate **Compartment** field, or use `openChanged(bool)` to notify other systems. The Junk Hover Bike now has a removable TCU and requires a working module to drive; see the scanner README for replacement instructions.

`Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab` now includes the helmet scanner, headset HUD, profile, tracked head/hands and both existing trigger action references. New instances have the scanner automatically. Activation remains hand beside helmet + trigger.

## Validation and tools

**Tools > Junker Equipment > Panels > Install scanner and reusable panel prefabs** updates the player prefab, creates missing reusable prefabs, and upgrades panels in loaded scenes. Existing reusable prefab artwork is preserved on rerun. Dirty scenes are not automatically saved.

**Tools > Junker Equipment > Panels > Run panel Play Mode validation** exercises real XRI selection/activation/release with synthetic input and temporary fixtures. On 2026-10-04 all checks passed, including copied screw discovery, powered-driver operation, full remove/store/resnap/refasten cycles, moving-machine storage, added/deleted screws, scaled screw travel, four repeated scaled-cover storage/seat cycles and saved player-prefab scanner references. Results are in `Library/JunkerPanelWork/validation.txt`; in-headset ergonomic testing remains.

Runtime changes: `JunkerScrew.cs`, `JunkerScrewdriver.cs`, `JunkerRepairPanel.cs`. Editor tooling: `Assets/Editor/JunkerPanelAuthoring.cs`, `Assets/Editor/JunkerPanelValidation.cs`; the existing scanner setup exposes its rig-configuration helper for prefab use.
