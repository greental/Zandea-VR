# Neutral gear fits from Fittings.unity

Captured from the saved `CombinedStarvingBandit (6)` through `(10)` scene instances on 2026-10-14. Each unique item's prefab now contains the transform and material of the first hand-placed instance listed here. All rotations are Unity quaternions `(x, y, z, w)`. Positions and scales are local to the named attachment bone. For rigid imported previews, the mesh transform is local to the item prefab root.

| Item | Scene | Slot and attachment | Authored fit |
| --- | --- | --- | --- |
| `black_guard_helmet` | 6 | Headwear, `CC_Base_Head_038` | Mesh P `(-0.5, 9, 1.8)`, R `(0.085591, 0, 0, 0.996330)`, S `(22, 23, 23)`; material `black_guard_helmet_material_0.mat` |
| `scavenger_vest` | 6 | Torso, `CC_Base_Spine02_035` | Root P `(7.7, -111.3, -19.6)`, R `(0.079677, 0, 0, 0.996821)`, S `(115, 90, 80)` |
| `spiked_helmet_a` | 7 | Headwear, `CC_Base_Head_038` | Mesh P `(-0.3, 7.06, 1.15)`, R `(0.070627, 0, 0, 0.997503)`, S `(17, 17, 23)` |
| `shoulder_harness_b` | 7 | Torso, `CC_Base_Spine02_035` | Root P `(-19.6, -98.5, -47.4)`, R `(0.190853, 0.015657, -0.080251, 0.978207)`, S `(110, 90, 75)` |
| `ragged_skirt_c` | 7 | Legs, `CC_Base_Hip_02` | Root P `(0, -120.6, 0)`, R identity, S `(100, 100, 70)` |
| `horned_hair_beard` | 8 | Beard, `CC_Base_Head_038` | Mesh P `(-0.3, -4.55, 8.88)`, R `(-0.073847, 0, 0, 0.997270)`, S `(12, 12, 15)` |
| `shoulder_harness_a` | 8 | Torso, `CC_Base_Spine02_035` | Root P `(13.3, -104.1, -2.6)`, R `(0, 0, 0.026439, 0.999651)`, S `(95, 80, 60)` |
| `ragged_skirt_b` | 8 | Legs, `CC_Base_Hip_02_scaleCompensation` | Root P `(0.4, 0, -125.8)`, R `(0.707107, 0, 0, 0.707107)`, S `(100, 100, 80)` |
| `wrapped_headgear` | 9 | Headwear, `CC_Base_Head_038` | Mesh P `(-0.4, 2.9, 0.4)`, R identity, S `(23, 28, 30)`; material `_ 1.mat` |
| `scarf_wrap_b` | 9, 10 | Torso, `CC_Base_Spine02_035` | Mesh P `(-0.4, 13.6, 0.2)`, R identity, S `(40, 25, 37)` |
| `ragged_skirt_a` | 9, 10 | Legs, `CC_Base_Pelvis_03_scaleCompensation` | Root P `(0, -110.6, 1.4)`, R identity, S `(90, 90, 70)` |
| `desert_hood_b` | 10 | Headwear, `CC_Base_Head_038` | Mesh P `(-0.5, 31.7, 7.5)`, R identity, S `(20, 20, 35)` |
| `wrapped_boot` | 9, 10 | Feet, `$CharacterRoot` | Paired rigid prefab. Boot A: P `(0.23, 0.144, 0.0061)`, R `(0.011776, -0.000349, 0.029664, 0.999491)`, S `(0.015, 0.018, 0.018)`. Boot B: P `(-0.1369, 0.1047, -0.2319)`, R `(0.048630, -0.113030, -0.108152, 0.986490)`, S `(0.015, 0.018, 0.018)` |

Character (6) has no saved Legs item. The offset duplicate of `ragged_skirt_c` on (7) and `shoulder_harness_a` on (8) remains in the scene for the authored display, but was excluded from the baseline prefab fit. The two loose boot copies on each of (9) and (10) were replaced by the selectable paired Feet item. `horned_hair_beard` is in the Full Beard picker, not Headwear.

The torso, skirt, and boot selections marked `skinned: false` remain rigid neutral-pose fits. Do not re-enable skinning until the mesh bones and body-size morphs are bound and tested across poses; doing so now would discard the hand-placed look.
