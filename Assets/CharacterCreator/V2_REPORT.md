# Character appearance V2

## Implemented

- `male_creator.glb` remains a derived copy of the source male GLB. Its body morphs now create distinct skinny, fat, and muscular profiles. Weight and muscle displacement is shared across head/neck, torso, arms, legs, nails, and boxers so matching material-seam vertices move together. The original 102-joint skin and `Calibration` animation remain intact.
- Inspector controls are grouped as Body, Face, Colors, Hair, Beard, Expressions, and Clothing. Scene instances preview in Edit Mode; appearance data is serialized, Undo/Redo reapplies the preview, and the imported asset is not modified.
- Eye color recolors only the iris ring in a 256×256 texture override. Pupil and sclera retain their source pixels; the overlaid cornea map also receives the iris tint. Textures are shared by quantized color and assigned through renderer property blocks; the imported eye and cornea materials remain assigned.
- `NPCFacialExpressionController` provides neutral, happy, angry, sad, surprised, afraid, and pain mappings. Its Inspector previews available targets and its API supports transitions and temporary expressions. The derived model now includes all six expression morphs.
- `ScavengerFacialExpressionBridge` can listen to the existing Scavenger health events and combat state after a compatible facial rig is supplied; the existing AI and damage scripts were not edited.
- `CosmeticCatalog` and `AppearanceItemDefinition` provide hair, beard, headwear, torso, legs, feet, hands, and outer armor slots. Rigid items attach to named bones; skinned items remap by bone name and may carry matching body/face morphs. The catalog intentionally contains no fabricated meshes.
- `AppearancePreset`, `AppearanceArchetype`, and `NPCCharacterAppearance` separate saved appearance, generation rules, and a generated NPC instance. Starving Bandit, Heavy Guard, Merchant, and Wanderer archetypes are included. A seed gives reproducible appearance; different seeds vary supported traits. Generated appearance is applied once at spawn.

## Morph inventory and remaining art work

The source `male.glb` has no morphs. The derived `male_creator.glb` now includes body, nose, jaw, ear, forehead, eye size/spacing, brow height/thickness/angle/spacing, eye shape/tilt, mouth width, lip fullness, and `ExprHappy`, `ExprAngry`, `ExprSad`, `ExprSurprised`, `ExprAfraid`, and `ExprPain`. The original source is unchanged.

Heavy has a round protruding belly and fuller chest. Muscular has broader shoulders, chest, arms, and legs. These generated geometry targets preserve the existing skin and animation data. Matching material seam vertices receive the same body displacement.

Eyebrows are painted into the head texture; there is no brow hair mesh. The new `Brow*` morphs move the ridge tissue, but brow hair thickness still needs texture or mesh art. The six expression morphs are localized face deformations, suitable for a prototype. Expression preview also rotates the existing jaw bone to open the mouth. Close-up dialogue quality still calls for sculpted replacements under the same names, more detailed eyelids and lip shapes. The expression controller now rotates the existing jaw bone for mouth opening. Keep neutral at zero and avoid keying permanent appearance morphs in expression clips.

The separate upper and lower teeth meshes now have `TeethNarrow`; jaw width, low weight, and low muscularity drive it so the teeth fit narrower mouths. Eye color now recolors iris pixels in both the eye and the overlaid opaque cornea color map using renderer property blocks. The controller restores the imported eye materials if an older transient iris material was left on a scene instance. The sclera and pupil retain their source pixels.

## Assets needed

- Hair prefabs: short, medium, long/tied-back. Bald needs no asset.
- Beard prefabs: stubble, short, full. Clean-shaven needs no asset.
- Compatible headwear, torso, leg, foot, hand, and outer armor prefabs for the CC_Base skeleton. Provide matching clothing morphs or clearly mark a piece as normal-fit only.
- One complete outfit with corresponding body morphs is needed to validate fit at skinny, normal, fat, and muscular extremes. No such outfit exists in this project. The existing Junker glove rigs, backpack, and Scavenger visual assets use other rigs or purposes and were not silently reused.

## Compatibility limits

- The existing Scavenger NPC and Junker player visual rigs use Mixamo bones. The creator male uses CC_Base bones. Their existing animation clips, pose references, weapon attachment references, and IK targets cannot be directly swapped onto this model. Retargeting and explicit bone remapping are required before using it as those gameplay prefabs.
- Skinning remains on the original creator skeleton; no gameplay bones or XR Origin are rescaled. Height can update an assigned gameplay capsule and physical player height, but `JunkerCharacterControllerFit` may subsequently overwrite the capsule and needs integration at its source settings.
- Hair, beards, headwear, and clothing may clip under extreme morphs unless their meshes include corresponding shapes or fitted variants. The source body is split by material regions, so renderer-hiding names must be chosen deliberately; fine-grained skin hiding requires new body partitions or masks.

## Inspector workflow

1. Instantiate `Models/male_creator.glb` in a scene and add `CharacterAppearanceController` to its parent. Set **Visual Root** to the model instance. Edit Body, Face, and Colors directly.
2. Use **Add expression controller** and **Add clothing and cosmetics slots** in that Inspector. Set cosmetics **Skeleton Root** to the creator model and assign the `CosmeticCatalog` asset. Create item definitions with **Assets > Create > Kenshi VR > Appearance Item** and assign compatible prefabs to the catalog or clothing slots.
3. Add `NPCCharacterAppearance`, assign the appearance/cosmetics components and one of the four archetypes, then click **Generate with next seed** for Edit Mode variants. **Save current as preset** creates an asset; **Apply preset** restores it. With **Generate On Spawn** enabled, generation runs once in `Awake` using the stored seed.

## Inspector randomization

The appearance Inspector has Randomize All and separate Body, Face, Skin Tone, Eye Color, Hair, Beard, Expression, and clothing-slot buttons. Skin tones use a correlated human-tone palette. Hair and beard styles are selected only from catalog entries with prefabs; clothing is selected only from installed appearance item definitions with prefabs. Missing cosmetic assets remain unfilled.

Manual skin and eye colors use direct Inspector color pickers and commit each edit before randomization controls are drawn.

The appearance Inspector now edits a copied `CharacterAppearanceData` directly and applies each changed group immediately; random buttons no longer share its serialized-property edit cycle. The localized forward chest bulges in Heavy and Muscular were reduced to roughly one third of their previous coefficients, while the heavy belly remains unchanged.
