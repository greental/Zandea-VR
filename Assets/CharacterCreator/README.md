# Male character creator POC

For the Inspector-first NPC upgrade, setup steps, art requirements, and compatibility limits, see `V2_REPORT.md` in this folder.

## Inspector preview

Add `CharacterAppearanceController` to an instance of `Models/male_creator.glb` in a scene. Set **Visual Root** to that model's transform (it defaults to the component's own transform), then edit **Live appearance preview** in the Inspector. Changes appear in the Scene view without entering Play Mode. Attach it to a scene instance, not the imported model asset. The custom Inspector disables controls whose morph targets or named materials are missing. **Use current size as 100% baseline** records the current size before previewing height changes. Leave Character Controller and VR Body empty for a standalone preview.

`male_creator.glb` is a derived copy with actual position morph targets. The source `male.glb` has no morph targets and cannot respond to anatomy sliders. Open `CharacterCreatorMorphPOC.unity` for a ready-to-test instance of the derived model. `Tools/build_creator_glb.py` regenerates the copy from the original GLB.

Open **Tools > Character Creator > Build POC Scene** in Unity. This creates `Assets/CharacterCreator/CharacterCreatorMorphPOC.unity` from an instance of the derived creator GLB without editing the source. Open the scene and press Play. Mouse input operates the world-space UI, including rotation buttons and a JSON preset at `Application.persistentDataPath/character-creator-preset.json`.

## Asset findings

- `male.glb` contains one skin with 102 joints, 14 meshes, and a `Calibration` animation. Seven body material meshes, paired eye and cornea meshes, upper and lower teeth, and boxers are separate.
- The original GLB has no morph targets. The derived creator copy adds paired POC morphs for weight, muscularity, nose width, jaw width, ear size, forehead shape, eye size, and eye spacing. Height (80–120%) changes only the visual model root; skin and eye colors use per-renderer material property blocks, including glTFast's `baseColorFactor` and `diffuseFactor` shader properties. Unity glTFast imports these morph frames at weight **1**, so the controller drives weights from 0 to 1. The body morphs share one displacement field across skin, nails, and boxers to preserve mesh seams. The generated morphs are geometric prototypes and need art review, especially eyelids and armor fit.
- The character has a CC_Base skeleton with twist and scale-compensation nodes. Eye and jaw bones exist, but moving them cannot reshape surrounding face topology. Do not scale limb, face, or torso bones for these sliders.
- The existing `Junker` game avatar uses Mixamo bones and a Generic Avatar. Its `VRBodyController` solves arms, legs, and head against tracked devices and saves/restores the animated pose. The male GLB is a distinct rig; it cannot be substituted into that prefab without remapping the avatar and IK bone references. The source GLB exposes only `Calibration`, so this POC displays its imported resting pose rather than a proven game idle animation.
- In a game integration, assign `visualRoot` to the model only, optionally assign the gameplay `CharacterController` and `VRBodyController`, and keep the XR Origin at scale one. Capsule height/center and the body controller's physical `playerHeight` follow height. The HMD standing-height calibration stays independent. Existing `JunkerCharacterControllerFit` updates its capsule every LateUpdate, so integrate height at that component's source configuration before using this controller on the Junker player.
- Clothes and armor must bind to the same skeleton and carry matching morphs, or they may clip as body shape changes. NPCs can use `CharacterAppearanceData` and the same controller without VR references.

## Blender work

For production art, replace or refine the generated shape keys while preserving these names: `WeightThin`, `WeightHeavy`, `MuscularitySlim`, `MuscularityMuscular`, `NoseNarrow`, `NoseWide`, `JawNarrow`, `JawWide`, `EarSmall`, `EarLarge`, `ForeheadLow`, `ForeheadHigh`, `EyeSizeSmall`, `EyeSizeLarge`, `EyeSpacingClose`, `EyeSpacingWide`. Each pair uses neutral at slider midpoint. Keep vertex order, skin weights, rest pose, and skeleton hierarchy stable. Eye-size and spacing morphs must be coordinated with the separate eye/cornea objects and eyelids. For actual animation compatibility, author a Humanoid mapping or retargeted clips and validate the VR IK solver on this skeleton.
