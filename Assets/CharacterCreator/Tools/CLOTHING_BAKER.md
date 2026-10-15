# Clothing morph baker

The reference is `../Models/male_creator.glb`, already derived from the original male GLB with its 102-joint `CC_Base` skeleton and body shape targets. Its source proportions use roughly centimetre-scale coordinates; Blender's GLB importer handles the transform. The Unity appearance sliders drive `WeightThin`/`WeightHeavy` and `MuscularitySlim`/`MuscularityMuscular` independently, from zero at the midpoint to one at each extreme. Height scales the visual root and needs no garment shape key.

Run Blender 4.1 or newer:

```text
blender --background --python clothing_baker.py -- --config baker_config.example.json
```

Paths in the JSON are relative to the config file. Point `input` at a garment file or directory; `recursive` includes subdirectories. Supported inputs are GLB, glTF, FBX, and OBJ. The garment must already be oriented and roughly positioned around the neutral character. Start with a single simple shirt or tunic, inspect the output, then use a directory. The original model and garments are only read.

The baker uses nearest body triangles and barycentric interpolation for skin weights and shape deltas. `fit_strength` applies bounded normal clearance correction to likely penetration. `loose_fabric` reduces morph movement to preserve drape. Keep it low for fitted garments and higher for loose clothes. Large correspondence distances are rejected because a nearby surface might be the wrong limb or body region. Set `body_mesh` to an exact imported Blender object name if automatic selection picks the wrong body mesh.

Each item exports one GLB with the source material slots, armature, weights, neutral mesh, and four blend shapes. Blender may not preserve every source shader or embedded texture; inspect the Unity material import. `baker_report.json` has per-item `status`, `generated_morphs`, `validation` counts, `warnings`, and `requires_manual_adjustment`. A failed item does not stop the batch. Any failed item makes the Blender process exit with code 2. Validation uses nearest-surface normals and is a heuristic; open garments, hems, and folds can produce false positives. Animation pose validation and corrective combination shapes are not yet automatic.

In Unity, import the baked GLB and select **Assets > Character Creator > Create Clothing Item From Model**. Set the generated item's slot, then assign it to `CharacterCosmeticsController` on the NPC. The existing controller rebinds the garment's bones by name to `skeletonRoot` and updates its blend shapes whenever the Inspector sliders change. For a rigid accessory, create an `AppearanceItemDefinition` manually with `skinned` off and `attachBoneName` set to an existing bone. The baker is for elastic garments.

Manual fitting is required when source alignment is wrong, a garment spans several unrelated body surfaces, long loose fabric should retain its silhouette, the report flags clipping, or animation poses cause intersections. Review shoulders, elbows, hips, knees, waist, hems, and mixed slider values in Unity. No garment sample was available in this repository, so the single-item and directory acceptance runs remain pending.
