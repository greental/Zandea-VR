# Customizable Man player prefab

`VR player - Man Creator.prefab` is copied from Main's `VR player - JunkerWeapon` instance. It keeps that player's XR origin, locomotion, equipment rig, backpack, shared item slots, grabbing, health, and wound treatment. The Junker `target_character` mesh and skeleton are removed; `male_creator.glb` supplies the visible body and `CC_Base` skeleton.

The copied `VRBodyController` points to the creator hips, chest, head, upper/lower arms, hands, thighs, calves, and feet. It still reads the existing HMD and tracked hands. `CreatorPlayerGait` adds simple two-bone foot motion while moving because the creator model has no retargeted player walk/run clips. The local HMD camera hides face, eyes, teeth, hair, and headwear; other cameras show them.

Edit **Character Appearance Controller** on the `JunkerBody` child for body shape, face, height, and color. **Character Cosmetics Controller** on the same child has the project catalog and slots for headwear, torso, legs, feet, hands, and outer armor. Its skinned garments bind to the creator skeleton. The Play check equips `armored_torso` and verifies its bones map to the player body.

Looted clothing uses its existing `NpcWorldItem` identity and the Junker backpack. A stored garment shows a compact roll; retrieving it restores the flat world cloth. Release it near the matching head, chest, or hip area to wear its `equippedAppearance` on this body. The physical item remains attached as a grab handle so taking it off restores the same loot object. A second item cannot replace an occupied clothing slot until the first is removed. Armor protection and movement weight are separate work under bug-plan item #13.

This is a separate prefab. Main still uses its original Junker player. The new prefab has not been tested with a tracked headset. The procedural gait is a first pass, player kick clips have not been retargeted to the creator skeleton, and there is no in-game appearance editor or looted-item-to-worn-armor flow yet. Those are subsequent parts of #9/#10/#13.

Use **Tools > Character Creator > Build Man player prefab from Main** to regenerate the prefab from the loaded Main player instance. **Tools > Character Creator > Validate Man player in Play Mode** checks runtime IK, wounds, backpack, item slots, appearance morphs, and skinned armor binding. Regenerating replaces this generated prefab, so make authored variants under separate names.
