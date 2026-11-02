# Zandea opening validation report

Updated 2 November 2026. The project owner authorized end-of-feature testing after Slices 01–13 were written. Automated runs used the isolated Unity project at `D:\applications\Unity\OpeningHarnessValidationTemp`; headset checks still require a person wearing Quest 2.

## Integration

- `Assets/Main.unity` now contains the opening encounter and is the first enabled build scene. `Assets/Scenes/Zandea/OpeningCaptureTest.unity` is the saved test scene. The prior Main and build settings are preserved in `.codex-opening-before-main-install/`.
- Authored scene reference checks passed for both scenes: `OPENING_SCENE_WIRED` and `OPENING_AUTHORED_SCENES_PASS`.
- The 2 November placement repair switched the opening from the inactive legacy XR player to Main's active `VR player - Man Creator V2 (2)`. Main now saves all three slavers within 3.5 m of that player, with an Inspector-visible `OpeningTargetBinding` reference. `PuebloMissionTestStart` is disabled in Main because it teleported the active XR origin hundreds of metres on Play. The outpost and Pueblo streaming roots remain dormant in the opening scene; a later chapter can activate them through `OpeningDeferredWorldSystems.ActivateLaterWorld` when its terrain frame is ready. Opening navigation no longer repositions the player or slavers.
- `OpeningStaffBandit_V2` now has authored headwrap, rib vest, armored pants, and boots. V1 is preserved. The BanditGroup uses the three authored V2 instances, so it does not generate appearances at runtime.

## Automated Editor results

| Scenario | Result | Evidence |
| --- | --- | --- |
| Three knockout and recovery cycles, including movement state restoration | PASS | `OPENING_KNOCKOUT_CYCLES_PASS cycles=3` |
| Stationary-player real staff contact, after clothing repair | PASS | `contacts=2 overlaps=1074 playerHealth=82.0/100.0` |
| Nonlethal KO and ordinary lethal damage | PASS | `OPENING_NONLETHAL_LETHAL_PASS` |
| Capture and binding | PASS | `OPENING_CAPTURE_BIND_PASS` |
| Escort arrival | PASS | `OPENING_ESCORT_ARRIVAL_PASS` |
| One hand, two hands, and cancellation gesture cases | PASS | `OPENING_GESTURE_PASS` |
| Escape and victory routes | PASS | `OPENING_ESCAPE_ROUTE_PASS`, `OPENING_VICTORY_ROUTE_PASS` |
| Main saved player placement, target, and slaver navigation | PASS | `OPENING_MAIN_SMOKE phase=EncounterActive nav=True,True,True states=Circle,Circle,Circle`; active XR origin stayed within 0.3 m vertically of its saved position and slavers were 2.7–2.8 m from the HMD at five seconds |

## Remaining validation

The automated checks above do not establish stereo fade comfort, audible lines, held-object gesture rejection, punch and parry feel, rope visibility, natural escort behavior, branch presentation, replay safety, build startup, or Quest 2 performance. These require interactive Editor and headset runs. Voice recordings, final rope art, binding animation, and next-chapter destinations remain unassigned; subtitles, a LineRenderer rope, temporary binding pose, and serialized outcome events are placeholders.

Interactive headset checks above still prevent a full headset signoff. The deferred outpost and Pueblo systems need wiring and validation in the intended next chapter; they are intentionally inactive until that handoff is authored. The disposable validation project contains a previously blocked cleanup target; do not retry deleting it through another tool. Its path and size should be given to the owner for manual cleanup when no longer needed.
