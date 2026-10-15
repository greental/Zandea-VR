# Junker physical equipment

Runtime components for the JunkerBody scene. `JunkerEquipmentRig` creates stable body-relative holster anchors; `JunkerEquipmentItem` is the shared state/grab/holster layer; `JunkerPistol`, `JunkerBinoculars`, and `JunkerScrew` provide item-specific behavior.

The project already imports GLB through glTFast. Add the rig to the stable Junker body root (not the HMD), add `JunkerEquipmentItem` to each grabbable tool, and set `itemId` to `PKO`, `screwdriver`, or `binoculars`. Assign the existing PKO muzzle/attach transforms where available. The current animated hands remain untouched; their existing grip animation continues to be driven by `AnimateControllerHand`.

Controls: left stick click toggles sprint while moving (stopping clears it). Right stick click while holding the powered screwdriver toggles Tighten/Loosen. Trigger fires the PKO or runs the powered screwdriver.

Dropped items resting on a surface can also be picked up by pressing the hand grip within 0.55 m, even when they are just outside the direct hand collider. Release grip to drop as usual. `Floor Pickup Reach` on `JunkerEquipmentRig` adjusts this reach.

## Binoculars and reusable scope viewing

The binoculars in `Player/Junker` and `Player/JunkerWeapon` use `OpticalDevice`. Grab them and bring the eyepiece in front of your eyes, aligned with your gaze. Right trigger cycles 2x/4x/8x. Moving away or releasing disables the optical camera and removes the viewing window.

**Immersive Viewing** displays the same live optical image in both eyes, inside a large circular aperture with black everywhere outside it. **Viewing Radius** controls its size. The image keeps its proportions regardless of the headset or desktop viewport shape. The optical camera follows the assigned **Objective** transform's blue/Z axis; zoom changes its FOV. The HMD camera remains active throughout.

Both eye projections map onto a single virtual image plane, including each eye's off-center projection and position. **Viewing Focus Distance** sets the shared plane distance (default 2 metres), giving the eyes a coherent convergence target. Its angular image size is shared between the eyes rather than scaled separately to their viewports. The rendering validator includes unequal, asymmetric eye projections and checks that matching image features project onto that same plane.

The same component can be used on a sniper scope: assign its eyepiece, objective, grab interactable and optical camera, and enable Immersive Viewing. Set **Viewing Shader** to `JunkerEquipment/OpticalEyepiece`. Disable Immersive Viewing to use a physical lens renderer instead; its geometry should be square for the square optical texture. **Flip Physical Lens Horizontally** corrects the UVs of the standard rear-facing quad. **Activation Status** explains why an optic is inactive during Play Mode. `Tools > Junker Equipment > Validate binocular optics rendering` checks saved references, viewing lifecycle and rendered image orientation/aspect/black surround in an isolated preview scene.

Both binoculars and RustFang use a 1536 pixel source image with bilinear sampling. `OpticalDevice` matches the main camera's scene rendering settings and temporarily hides the held optic's own mesh from its source camera, preventing the near shell from obscuring the magnified view. The rifle's **Show Reticle** setting adds a fine amber crosshair and range circle to the viewing overlay; binoculars leave it off. Raise or lower **Texture Resolution** per device if headset performance needs tuning.

## Binocular scanning HUD

Binoculars stabilise the optical camera, suppressing small hand tremors more strongly at high zoom and responding faster to deliberate turns. Headset tracking is never filtered. Tune **Stabilise View**, **Rotation Smooth Time**, **Position Smooth Time**, **Angular Dead Zone**, and **Fast Turn Smooth Time** on `OpticalDevice`. Range, compass and scanning follow the stabilised camera. The larger distance readout sits nearer the centre; adjust **Range Position** and **Range Font Size** on `BinocularScannerHUD`, including during Play Mode.

Binoculars automatically scan while viewing. The shared optical image includes a top-left range marker, Razara's scrolling compass tape (world +Z is north), heading/zoom, and a compact target diagnostic readout. Only objects with `ScannableObject` provide scan data; line-of-sight and closed-compartment rules still apply. The binocular scanner is independent of the helmet scanner and switches off when the optic closes.

`BinocularScannerHUD` is automatically added to existing binoculars at runtime. For saved Inspector settings, use **Tools > Junker Equipment > Configure binocular optics on player prefabs**, or add the component to a custom binocular object. **Scan While Viewing**, **Scan Distance** (default 100 m), **Scan Interval**, layer masks, font, and visual profile are adjustable. Range uses the binoculars' existing **Max Distance** (default 500 m). No new controller binding is needed; right trigger still changes zoom.

## Pistol kit and JKR-88

`JunkerPistolKit` can be attached to a pistol model in the scene or to its parent GameObject. It installs a Rigidbody, collider, XR grab interaction, `JunkerEquipmentItem`, `JunkerPistol`, muzzle flash, and spatial shot audio. It creates Grip and Muzzle transforms if absent. Put the Muzzle at the bore exit with its blue/Z axis pointing along the shot, then adjust Grip to fit the hand. The context menu **Install or refresh pistol components** reapplies the exposed settings after tuning. Assign a separate slide or compensator only if the model has one; assign a sight window and enable Add Reflex Dot only when there is a real optic window. New prefab instances bind to the scene's `JunkerEquipmentRig` at runtime.

The prepared [JKR-88 prefab](<../MyAssets/prefabs/weapons/JKR-88 1.prefab>) uses the one-piece JKR-88 model, whole-mesh recoil, and the same fire, damage, pickup, and holster behavior as PKO. One copy sits on the repair bench in JunkerBody. Both pistols are approved for the right-hip slot; remove PKO before holstering JKR-88. `Tools > Junker Equipment > Create JKR-88 pistol and scene pickup` installs or repairs the scene setup without duplicating an existing copy.

JKR-88 uses a physical R4 power cell. Hold the pistol and press right A once to unlock it (currently a switch click, with no latch animation). The cell remains seated until grabbed and pulled out; further A presses leave the latch open. Move the removed cell a short distance away, then return that same cell or use a spare. Release a cell near the socket to snap it in and lock automatically. The pistol only fires with a latched cell that has charge. Set **Shots Per Cell** on the pistol's `JunkerPistolBatterySocket` component to choose its magazine size; fresh cells receive that many shots, while used cells retain their spent charge when reinserted. Adjust **Insertion Radius** on the same component to tune the catch zone. The JKR-88 socket's **Mounted Euler** is currently -90° around the cell's local Y axis, rotating its face without tipping it over. A spare cell is on the repair bench, and the reusable [cell prefab](../MyAssets/prefabs/weapons/R4-PowerCell.prefab) can be placed elsewhere. `JunkerPistolKit` exposes the socket anchor, starting cell, insertion radius, and latch sound for future battery-powered pistols. PKO remains unchanged and does not require ammunition.

The cell keeps its world size when moved into or out of the socket, including on pistols with a non-unit transform scale. Repeated insertion no longer scales it up.

The R4 cell mounts with its blue full-meter face toward the pistol's left side. **Mounted Euler** on `JunkerPistolBatterySocket` controls this orientation. XRI can briefly restore the mounted Rigidbody state after a cell is released, so `JunkerPowerCell` restores gravity and dynamic physics after release.

JKR-88 bolts now travel and perform swept collision checks each frame. Damage and ragdoll start at projectile impact, not trigger pull. The impact pushes dynamic Rigidbodies and leaves a small dark scorch mark on the struck surface. **Laser Shot Speed**, **Impact Impulse**, and **Scorch Size** on `JunkerPistol` control these effects.

JKR-88 also fires a small visible red laser bolt using Razara's laser material. It is visual only: the pistol's existing raycast determines the hit and applies damage once. Laser speed, length, and width are adjustable on the pistol kit and pistol components; Razara and PKO are unchanged.

## Battery-powered tools

[Powered screwdriver battery](../MyAssets/prefabs/Tools/Powered%20screwdriver%20battery.prefab) is a new prefab using the Blender-split metal bit. The original screwdriver prefab and source GLB remain available. Its bit spins while the trigger is held, including when no screw is contacted. Tighten and Loosen reverse both the bit and screw movement. The PLS-T ScrapCutter prefab also has an R4 socket. Both tools start with a mounted R4 cell and use 600 charge units over 60 seconds of continuous operation by default. Press right A while holding a tool to open the latch; grab the cell to remove it, then release a compatible R4 cell near the socket to insert it. Change `shotsPerCell` and `toolSecondsPerCell` on the socket to tune capacity and runtime. The manual screwdriver does not use a battery.
