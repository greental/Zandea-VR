# Zandea opening — Slice 00 audit

Audited 2 November 2026. This is an implementation map, not a gameplay change. The worktree already contains many unrelated modified and untracked assets; preserve them during later slices.

## Scene and boot reality

- `ProjectSettings/EditorBuildSettings.asset` has one enabled scene: `Assets/Scenes/KenshiVR_Starter.unity`. It does **not** list `Assets/Main.unity`. The latter is the established authored world scene used by Pueblo, POI, and combat editor utilities. A standalone build will currently boot Starter, so opening integration into Main alone would not make it the build start.
- `Assets/MeleeCombat/RandomBanditEncounterDirector.cs` installs its encounter only when the active scene name is `Main`. Other world spawners use the same name check. The opening needs an explicit scene entry decision before production integration.
- Existing feature scenes include `Assets/Scenes/MeleeCombat/ThreeBanditVrParry.unity`, `BanditSurroundTest.unity`, and `BanditParryProof.unity`. Use a new `Assets/Scenes/Zandea/OpeningCaptureTest.unity` for Slice 01, then ask before promotion to Main. Reuse referenced prefabs; do not copy or overwrite them.

## Verified systems and extension points

| Need | Current implementation and API | Opening extension |
| --- | --- | --- |
| XR pose and body | `Assets/JunkerBody/VRBodyController.cs`: `xrOrigin`, `hmd`, `leftController`, `rightController`, optical wrists, IK targets; `SetMeleeGrip` / `ClearMeleeGrip`. `Assets/JunkerEquipment/JunkerGloveHand.cs`: `HandController`, `SetCurl`, `grip`, `trigger`. | Gesture reader should observe tracked controller/HMD transforms and grip/held state. Never move these transforms for restraint or KO. |
| Punch and grab | `Assets/MeleeCombat/PlayerMartialHand.cs`: `HandState`, `State`, `Velocity`, `TickTrackedImpact`, `TryDefend`, `TryIntercept`; references `XRDirectInteractor`. `PhysicalMeleeWeapon.Grab` exposes `XRGrabInteractable`. | Add action restrictions around interaction/combat in later slices; retain tracking. |
| Player health | `Assets/ScavengerNPC/ScavengerPlayerHealth.cs`: `IDamageable.ApplyDamage(in DamagePacket)`, `Current`, `IsDead`, `onDamaged`, `onDeath`, `onDirectHit`, `Restore`. No player knockout state or nonlethal packet route exists here. | Add reusable player KO and nonlethal policy before wiring slaver attacks. Keep lethal behavior. |
| NPC health | `Assets/ScavengerNPC/ScavengerHealth.cs`: `NpcConsciousness`, `StateChanged`, `onKnockout`, `onWakeUp`, `onDeath`, `KnockOut`, `WakeUp`, `ApplyDamage`. | Observe NPC state for victory/captor reassignment; do not confuse NPC KO with death. |
| Staff and hit | `Assets/MyAssets/prefabs/weapons/Bo Staff.prefab` contains `PhysicalMeleeWeapon`, `XRGrabInteractable`, grip and striking-tip children. `Assets/MeleeCombat/PhysicalMeleeWeapon.cs`: `HoldState`, `CombatReach`, `TryEquipNpc`, `Drop`, `Blocked`. `Assets/MeleeCombat/NpcGeneratedWeapon.cs`: `Instance`, `EnsureStartingWeapon`, `SetStartingWeapon`, `Drop`, `DropForKnockout`. | Use the shared staff via a distinct slaver V2 prefab. Verify actual contact and pickup after disarm. |
| Disarm | `Assets/MeleeCombat/WeaponDisarmResolver.cs`: `Active`, `Evaluate`, `NotifySeparation`, clash/roll counters; `IDisarmCombatant`. | Reuse collision-based disarm. No scripted betrayal success. |
| Bandit AI/navigation | `Assets/MeleeCombat/StarvingBandit.cs`: state enum, `target`, `agent`, `staff`, `health`, `groupControlled`, `groupDestination`, `groupCombatDestination`, `EffectivePreferredFightDistance`, `StrikeTargetPoint`. `Assets/MeleeCombat/BanditGroup.cs`: `authoredMembers`, `player`, sight/forget settings, surround radius, `TryTakeAttackSlot`, NavMesh setup. | Capture role coordinator should wrap/reuse group and agent behavior, with exclusive captor role. Existing group attack slot can coordinate combat. |
| Animation/contact | `Assets/MeleeCombat/MocapTest/BanditMocapNpcDriver.cs` drives staff attack animation, attack displacement and feet. `Assets/MeleeCombat/BanditDuelStrikeDamage.cs` uses `SphereCastNonAlloc` over moving weapon tips, checks committed target, applies `DamagePacket`. | Audit contact geometry in Slice 04 against a stationary player. Avoid fake proximity damage. |
| Dialogue/audio | `Assets/Zandea/FriendlyNPC/DialogueDefinition.cs` offers nodes, `AudioClip`, responses and actions; Daran voice assets are under `Assets/Audio/Dialogue/Daran/`. `Assets/MeleeCombat/BanditCombatAudio.cs` creates voice/metal `AudioSource`s. `Assets/Scenes/DesertAmbience.cs` owns desert ambience. | Opening line can be a dedicated assigned clip/subtitle; general merchant dialogue actions are not an appropriate surrender controller. |
| Reset and visual fade | `Assets/ScavengerNPC/ScavengerRestartSign.cs` reloads the active scene. `ScavengerPlayerHealth` creates a camera-child damage vignette. | Reuse scene reload pattern in the test harness. KO needs a separate stereo-safe black fade; damage vignette is not full blackout. |

## Contact risk to measure

`StarvingBandit` derives fight spacing from `CombatReach`, authored attack distances and `StrikeTargetPoint`; `BanditMocapNpcDriver` moves the body/weapon during strikes; `BanditDuelStrikeDamage` sweeps the tip between frames. A stationary target miss can result from spacing or animated tip trajectory never crossing the player's collider, even when the swing looks close. The existing sweep already mitigates tunneling. Slice 04 should record root-to-target distance, chosen attack, tip path, collider sweep, and accepted contact in `ThreeBanditVrParry`/the opening test scene before changing reach. No claim of a reproduced or fixed miss is made here.

## Wiring and risks for subsequent slices

1. Test scene: reference the existing player prefab (`Assets/CharacterCreator/Player/VR player - Man Creator.prefab` or its intended V2 after verifying current scene use), three staff bandit V2 prefabs, a local NavMesh, an encounter root, and a restart/diagnostics object. Save under `Assets/Scenes/Zandea/`.
2. Keep previous shared player/bandit prefabs as separate V1 assets with distinct `.meta` GUIDs before V2 changes. Existing `Assets/CharacterCreator/Player/Versions/` and combat feature scenes are untracked user work; inspect rather than replace them.
3. Add reusable KO/compliance/restraint components without story dependencies. Opening director later subscribes to those and NPC health/group events and owns outcome/title routing.
4. Verify whether a test scene plays in Editor and headset, then explicitly decide build startup and Main promotion. `Assets/Main.unity` is not in build settings, and scene name based spawners make an opening scene behave differently from Main.
5. Main scene is large and already modified. No Main or prefab edits belong in this audit. Avoid concurrent Unity batch operations while Unity processes have this project open.

## Validation performed

- Read source, prefab YAML, scene list, package manifest, and git status. Unity 6000.2.8f1 is running in multiple processes. Free D: space was about 267.6 GiB.
- No Unity compile or play test was run: batch opening this live project risks lock/conflict and disk-heavy import. No gameplay code changed; this audit has no compile surface.
- Headset behavior, stationary staff contact, and scene object references still require live verification in later slices.
