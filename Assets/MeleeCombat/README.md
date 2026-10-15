# Starving Bandit staff combat

## Combined character

`CombinedStarvingBandit.prefab` is a third character built from a copy of the authored bandit in `MeleePOC.unity`. It retains that copy's saved guard, eight attack definitions, physical staff, ragdoll, health, navigation, and procedural motion. Its skinned meshes come from `male_creator.glb`, with the same bone order as the combat rig, so it also has the creator's body and face morphs. The root has `CharacterAppearanceController`, `CharacterCosmeticsController`, `NPCCharacterAppearance` (using the Starving Bandit archetype), and `NPCFacialExpressionController`.

Use **Tools > Melee Combat > Build combined bandit prefab** to refresh the prefab from the authored scene after combat changes. This command checks mesh paths and bone order before saving. The prefab keeps `guardAuthoringMode` enabled, matching the authored bandit; disable it on an instance to allow autonomous attacks.

## Armored bandit duel

Open `BanditDuel.unity` and press Play. Ash and Rust are separate instances of the combined prefab with fixed random appearance seeds, random helmets and gloves, armor torsos, and boots. They target each other and have autonomous attacks enabled. The screen shows both health bars and the winner. Their staff damage uses the existing Koombar `IDamageable` packet and `ScavengerHealth`; a fatal hit activates the added ragdoll. The armor in this scene is visual and does not reduce damage.

The duel adds one scored NPC body hit near the impact portion of a committed staff strike when the opponent is in range. This supplements the physical staff collision path, which can glance off another staff without touching the body. Each strike has an 80% clean-hit chance; dead fighters and dropped staffs cannot score further hits. When testing in the Editor, three snapshots and matching health/state logs are written to `Temp/BanditDuelSnapshots` at 2, 8, and 20 seconds.

Use **Tools > Melee Combat > Build bandit duel scene** to regenerate the arena, navigation mesh, armored archetype, and both fighters. The original authored `MeleePOC.unity` scene stays separate.

`MeleePOC.unity` contains the user's authored guard pose. Do not rebuild the scene or reset the rig to tune attacks. A copy of the guard scene from before this attack pass is in `Library/MeleePOC.AuthoredGuardBackup.unity`.

At runtime, `StarvingBandit` captures the staff's saved transform as the guard pose. `BanditRigMotion` captures each hand's position and rotation relative to the **actual staff** and the authored elbow bend directions. Attack definitions move the invisible `Animated grip goal`; the physical staff follows it. Arm IK reads the actual staff pose after the body pose, so a future block can deflect the weapon while the hands follow. The previous fixed hand spacing and wrist offset are no longer used for the grip.

The eight `StaffAttackDefinition` entries are ordered 12, 1:30, 3, 4:30, 6, 7:30, 9, 10:30. Each has windup, two Bezier strike control points, impact, follow-through, recovery, phase durations, easing curves, and torso yaw/lean/weight shift. All offsets are relative to the saved guard pose. Target yaw is frozen at windup and limited by `maximumAimYaw`.

## Preview and tune

The Bo staff Rigidbody is the `Bandit Bo staff` object. Its rendered GLB is a scaled, translated, and rotated child. `PositiveStrikingTip` and `NegativeStrikingTip` are measured from the rendered mesh and stored under the Rigidbody root. The solver uses their midpoint and longitudinal axis; the secondary axis points from the shaft toward the grip. The editor adds these markers to the open MeleePOC scene on script reload. **Tools > Melee Combat > Calibrate staff tip markers** can also create missing markers. Move a marker manually in the Scene view if the imported geometry needs a small correction.

Enter Play mode, select `Starving Bandit - male rig`, and use **Attack preview** in the Inspector. Choose a clock direction, choose a phase, press **Preview selected attack**, then scrub `Phase progress`. **Stop preview and restore guard** restores the staff and rig. The red dummy is 1.5 m in front. White joins the actual tips; green and blue mark positive and negative tips. Yellow traces the desired root, orange the selected tip, cyan the opposite tip, and the green trail records the actual tip during preview. Magenta shows the current desired staff and tip. The Inspector reports grip, reach, tip-position, and orientation errors.

Tune `windupTipLocal`, `tipControlA`, `impactTipLocal`, `impactApproachLocal`, and `followTipLocal` to shape the striking path. The offset and control fields move the visible shaft midpoint. Tune phase durations, easing curves, and torso yaw/lean/weight shift to coordinate the body. The old Euler fields are serialized for compatibility but are not used by the tip-driven solver. `reachMargin` reserves arm extension; `maximumAimYaw` controls target aiming. The scene keeps `guardAuthoringMode` enabled so Play mode remains stationary unless preview is active. Clear it when ready for autonomous attacks.

The current male GLB contains only a `Calibration` clip; locomotion and attacks are procedural. Collision, damage, and ranged combat systems were not changed in this pass. Visual quality and grip errors still need inspection in Unity Play mode and headset testing.
