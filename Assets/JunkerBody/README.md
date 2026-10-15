# JunkerBody VR avatar

Scene: `Assets/Scenes/JunkerBody.unity`.
All new runtime code and generated assets belong to this folder. Shared XR prefabs, original model/animation imports, Packages and ProjectSettings are not edited by the setup tool.

## Current player settings

- On the Junker prefab, walk is 5 m/s, sprint is 8 m/s, and crouch movement is 2 m/s. Click the left stick while moving to toggle sprint; returning to neutral cancels it. Click the **right stick** to toggle crouch. Push the right stick up to stand, or jump when already standing.
- Uniform mesh scale: **1.4**, using your measured fit. Changing standing height no longer changes mesh scale.
- Physical player height: **1.78 m**. Standing HMD eye height starts at **1.66 m**, an estimate to calibrate while standing; it is not your total height.
- Arm Length Multiplier: **1.10**, increasing upper-arm and forearm reach 10% without enlarging hands. The adjustment is applied after animation without accumulation.

## Components

- `VRBodyController`: reads the existing HMD/controller transforms; moves only the avatar; solves arms after Animator evaluation using analytic two-bone IK and elbow hints. Existing XR Hands wrist transforms are used when their tracking events report a tracked hand. Head rotation/position offsets, body yaw dead zone, smoothing and physical crouch settings are exposed.
- `JunkerSprint`: changes the existing ContinuousMoveProvider speed before it updates. Left stick click while moving toggles sprint; right stick click toggles crouch. Press the right stick up to stand, or to jump if already standing. The same right-stick-up press that stands up cannot jump. **Crouch Speed**, **Crouch Depth**, and **Crouch Transition Speed** are exposed here. The existing JumpProvider receives this input instead of its former A-button action. Actual horizontal XR Origin displacement, gated by movement input, drives the animation blend; room-scale HMD movement is excluded.
- `JunkerTrackingCalibration`: optional scene-local vertical tracking correction. It offsets the common tracking parent so headset, controllers and optical wrists remain together. Button crouch adds a temporary offset to this calibrated position without changing the saved floor calibration.
- `JunkerLocalHead`: selects a headless mesh only while the local camera renders. A full-body shadow-only renderer preserves the head shadow. Other cameras receive the full mesh.
- `Editor/JunkerBodySetup`: scene-specific setup and validation menu commands. Explicitly opens/saves only JunkerBody. No automatic reconfiguration occurs; use the menu explicitly.
- `Editor/JunkerBodyPlayValidation`: opt-in synthetic Play Mode integration validation; only runs with JunkerBody as the sole open scene. Test objects, input devices, tracking overrides and input overrides exist only during Play Mode.

## Assets

`JunkerIdle.anim` uses the existing Alert clip as standing idle; `JunkerWalk.anim` uses Casual Walk; `JunkerRun.anim` uses Running. These are independent copies with looping enabled and horizontal root/hips trajectory removed. `JunkerLocomotion.controller` blends Speed 0/1/2 (idle/walk/run). Root motion is disabled.

The source GLB has a humanoid-shaped Mixamo skeleton but no Unity Humanoid Avatar. `JunkerAvatar.asset` is a valid Generic Avatar, preserving the existing bone-animation paths without retargeting or changing the original importer. Explicit bone references drive IK. Animation Rigging and third-party VRIK packages are not required.

`JunkerLocalBody.asset` is a scene-specific mesh copy with head/neck-weighted triangles removed. The original mesh remains unchanged.

## Tuning in the headset

Select `XR Origin Pos/XR Origin (VR) Variant/JunkerBody`.

1. Set Standing Height to your HMD height above the XR Origin floor, or invoke the standing-height calibration context menu during Play Mode while standing upright. Copy a calibrated value back to Edit Mode if you want it saved.
2. Adjust head position offset and each arm's wrist position/rotation offsets for the headset/controllers. Optical wrist rotation has a separate offset.
3. Adjust Walk Speed, Sprint Speed, Crouch Speed, Crouch Depth and Input Threshold on JunkerSprint.
4. Adjust Rotation Dead Zone, Body Rotation Smoothing and crouch controls to taste.

To change overall size, set **Mesh Scale** on VRBodyController, then use **Tools > JunkerBody > Apply avatar size and eye offset** and save. The command recalculates the eye-to-head offset for that size without using Standing Height to shrink the mesh. Tune **Arm Length Multiplier** independently for reach.

## Floor alignment

JunkerBody disables the old LockHeight component as a scene override. The Junker prefab now lets `JunkerCharacterControllerFit` lower the capsule during physical or stick-controlled crouch. The initial capsule is floor-relative, skin width is 2 cm, minimum movement distance is zero, and the existing gravity probe ends at the tracking floor instead of considering the player grounded about 4 cm early.

Test the floor using the **tracked controller position**, not only the avatar hand: an out-of-reach avatar hand can stop above the floor even when tracking is correct.

If a measured tracking-floor discrepancy remains, `JunkerTrackingCalibration` provides **Floor Offset** in metres. Negative lowers all tracked poses together. It defaults to **0**, because the reported 30?40 cm discrepancy has not yet been measured separately from avatar reach.

For an explicit measurement: during Play Mode, rest the left controller on the real floor and invoke that component's **Calibrate floor (left controller resting on physical floor)** context menu. **Controller Rest Height** is the tracking origin's physical height above the resting surface (5 cm initial estimate, adjust for the controller). Copy the resulting Floor Offset back to Edit Mode to save it, then calibrate Standing Height again while upright. Neither calibration rescales the world or changes the XR Origin's uniform scale.

## Limits

Feet follow their animated targets using a simple two-bone correction when the pelvis changes height. There is no terrain probing, step planning, seated mode or physical body collision. Very deep crouches and unreachable hand targets are limited by the configured limb lengths. Controller finger articulation is not added to the character's simplified hands. The existing XRI CharacterController now follows HMD height without the competing LockHeight override.

Quest hardware testing is required for final fit, visual comfort, optical wrist axes and grabbing. Synthetic input checks cannot establish those results.
