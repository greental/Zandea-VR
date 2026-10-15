# Clothing rigging and fit plan

## Implemented in the creator

`Editor/RiggedWardrobeBaker.cs` transfers the creator body's CC_Base weights and
four body-size morphs to eleven body-worn pieces. The item definitions and
matcher use the resulting skinned prefabs. Both gloves and boots are paired;
boot wraps are upright with calf/foot weights and fitted foot shells. The
skirt prefabs have thigh and shin plates. The six-character showcase captures
front, side, head, and lowered-arm pose views across different body sizes.

The remaining steps below are manual quality work for gameplay poses, loose
fabric, and garments whose source topology does not fit by nearest-body
transfer. `Tools/clothing_baker.py` remains an alternate Blender pipeline for
those manual corrections.

## Current state

The 30 downloaded pieces are rigid preview attachments. The importer in
`Editor/DownloadedWardrobeImporter.cs` now sizes each type separately. This is
useful for checking silhouette, but it cannot keep cloth aligned with bending
elbows, knees, hips, or changes to weight and muscularity. Keep the source GLBs
unchanged and use the preview prefabs as alignment references.

## Production path

1. **Normalize a garment.** In Blender, import `Models/male_creator.glb` and one
   source mesh. Apply the preview prefab's position, rotation, and scale to a
   copy of the garment vertices so the copy surrounds the neutral body in the
   reference rest pose. Inspect inside and outside surfaces. Split combined
   outfits by anatomical region where weight transfer would confuse nearby
   limbs. Make separate left and right gloves and boots.
2. **Bake weights and body shapes.** Run `Tools/clothing_baker.py` with a per-item
   config based on `Tools/baker_config.example.json`. It transfers weights from
   the `CC_Base` skeleton and creates `WeightThin`, `WeightHeavy`,
   `MuscularitySlim`, and `MuscularityMuscular`. Use low `loose_fabric` for snug
   shirts and armor, higher values for skirts and hanging cloth. Check
   `baker_report.json` for correspondence warnings and penetration at the
   neutral, extreme, and mixed sizes. The baker needs Blender 4.1 or later;
   the currently installed Blender 2.93 cannot run this pipeline as documented.
3. **Correct the result by hand.** Paint weights at shoulders, elbows, waist,
   hips, knees, and hems. Repair reported intersections and inspect the four
   shape extremes plus thin/muscular and heavy/slim combinations. Add garment
   specific corrective shapes if those mixes still clip. Preserve slack on
   skirts, capes, straps, and other loose parts. These need pose correctives or
   cloth/secondary motion after basic skinning.
4. **Import into Unity.** Import the baked GLB and use **Assets > Character
   Creator > Create Clothing Item From Model**. Set its slot and replace the
   matching preview item in the catalog. `CharacterCosmeticsController.BindSkin`
   maps bones to the live skeleton, while `ApplyClothingMorphs` follows the
   body sliders. Keep the rigid version until its skinned replacement passes
   review.
5. **Validate on the characters.** Run **Tools > Character Creator > Validate
   Downloaded Wardrobe**, then rebuild `Showcase/RandomCharacters.unity` and
   its front, side, and head snapshots. Add an animation pose pass with arms
   down/raised/forward, torso twist, crouch, walking, and VR reach. Check each
   pose at neutral, thin, heavy, slim, muscular, and mixed body settings.

## Suggested order

| Group | Pieces | Main fit risk |
| --- | --- | --- |
| 1 | `scavenger_vest`, `armored_torso` | shoulder and elbow deformation; torso girth |
| 2 | `ragged_skirt_a/b/c`, `scavenger_outfit` | hips, legs, hems, mixed body sizes |
| 3 | `leather_glove_a/b`, `wrapped_boot` | separate left/right pieces and hand/foot poses |
| 4 | `shoulder_harness_a/b`, scarves and hoods | layered clearance and head rotation |

Hair and beards should follow the head. Rigid attachment is suitable for short
styles; long strands need secondary motion and a collision pass around the
neck and shoulders. Helmets should be reviewed against every hair style and
face shape, with the catalog's `hideHair` rule used when needed.

## Acceptance criteria

- The garment has the expected `CC_Base` bone mapping, four body shapes, and
  correct materials in Unity.
- No major skin exposure or mesh intersection appears in front, side, and back
  views at the body extremes or the listed poses.
- Paired gloves and boots track the correct limb independently.
- The matcher can equip the item without covering an incompatible torso or
  skirt, and the preview catalog points to the approved skinned prefab.

`Tools/CLOTHING_BAKER.md` documents the baker and its current limits. Its
nearest-surface penetration checks are a first pass; visual pose review is
still required.
