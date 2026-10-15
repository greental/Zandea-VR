# Validation ? 2026-09-23

Unity 6000.2.8f1; JunkerBody scene only. Updated runtime and Editor scripts compiled without C# errors. The saved scene was reopened and its references validated after Play Mode.

## Current fit and floor changes

- Uniform mesh scale 1.4, preserving the user's measured size independently of standing-height calibration.
- Physical player height 1.78 m; standing HMD eye height 1.66 m is an initial estimate, pending upright headset calibration.
- Arm segment length multiplier 1.10. Hands are not independently enlarged, and lengths do not accumulate across frames.
- LockHeight disabled only on the scene instance. The original CharacterController and XRI body manipulator remain in use; XRI now owns capsule height/center without the competing LateUpdate writer.
- Capsule initialized floor-relative; skin width 0.02 m and minimum movement distance zero. The existing gravity sphere probe's distance buffer is minus its radius, removing its previous approximately 4 cm early-grounded margin.
- Optional common tracking-parent Floor Offset starts at zero. No guessed 30?40 cm offset was applied. An explicit controller-resting-on-floor calibration is available if the tracked controller itself shows a residual discrepancy.

## Saved-scene checks

```text
PASS: references, bone chains, target parenting, valid Generic avatar, Animator, no missing scripts, one continuous movement provider.
JunkerIdle: 4.08s, legacy=False, curves=196
JunkerWalk: 4.25s, legacy=False, curves=196
JunkerRun: 0.71s, legacy=False, curves=196
```

## Actual synthetic Play Mode checks

The validator temporarily disables tracked pose drivers, supplies synthetic head/controller poses, drives the existing movement provider's manual input readers, and presses the actual scene-local sprint action using a synthetic left XR controller. It samples the authored idle clip for an unmodified arm-length baseline. Physics tests wait for simulated time rather than counting XR startup wall-clock stalls as gravity evaluation.

```text
Unity Play Mode validation using synthetic tracking and movement input.
PASS uniform 1.4 scale, arm length adjustment, XRI capsule ownership, ground alignment error=0.0000 m.
PASS idle, head and both arm/wrist IK.
PASS torso yaw dead zone.
PASS room-scale alignment without locomotion.
PASS walking: 3.00 m/s.
PASS held click sprint and run blend: 5.40 m/s.
PASS release returns to walking.
PASS neutral stick prevents sprint and returns to idle.
PASS crouch pelvis lowering and grounded foot targets.
PASS overhead arm IK.
PASS crossed/chest arm IK.
PASS changed tracking pose/recenter alignment.
PASS local camera head removal, full-mesh restoration and first-person capture.
PASS optional floor correction moves HMD and controllers together.
PASS floor correction restores without accumulated offset.
PASS all synthetic Play Mode checks. Quest hardware fit, grabbing and visual comfort still require headset testing.
```

Standing, crouching and first-person captures were generated; the final first-person capture was inspected. Temporary captures are in Temp/JunkerBody-standing.png, Temp/JunkerBody-crouching.png and Temp/JunkerBody-firstperson.png. Test objects and input/tracking overrides were discarded on exiting Play Mode. Unity was left in Edit Mode.

## Limits and scope

The measured near-zero ground error is the XR Origin floor relative to the Unity scene collider under synthetic tracking. It does not establish that the headset runtime's real-floor calibration is correct, nor that an unreachable avatar hand indicates a tracking-floor error. Physical controller contact, standing eye height, reach, optical tracking, grabbing and comfort still require a headset check.

The Editor/OpenXR loader reported device/session lifecycle errors during development; these did not prevent the recorded synthetic checks. This is not a clean Quest hardware validation.

Authored changes are restricted to Assets/JunkerBody and Assets/Scenes/JunkerBody.unity. Shared XR prefabs, the original LockHeight script, source model/animation imports, global input assets, Packages and ProjectSettings were not edited. All new serialized references are assigned.
