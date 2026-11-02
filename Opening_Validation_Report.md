# Zandea opening validation report

Updated 2 November 2026. The project owner authorized end-of-feature testing after Slices 01–13 were written. Automated runs used the isolated Unity project at `D:\applications\Unity\OpeningHarnessValidationTemp`; headset checks still require a person wearing Quest 2.

## Integration

- `Assets/Main.unity` now contains the opening encounter and is the first enabled build scene. `Assets/Scenes/Zandea/OpeningCaptureTest.unity` is the saved test scene. The prior Main and build settings are preserved in `.codex-opening-before-main-install/`.
- Authored scene reference checks passed for both scenes: `OPENING_SCENE_WIRED` and `OPENING_AUTHORED_SCENES_PASS`.
- The placement repair switched the opening from the inactive legacy XR player to Main's active `VR player - Man Creator V2 (2)`. Main saves all three slavers within 3.5 m of that player, with an Inspector-visible `OpeningTargetBinding` reference. `PuebloMissionTestStart` is disabled because it teleported the XR origin on Play. Opening navigation does not reposition the player or slavers.
- Kareth is the active authored city preview, with its scene root at the city's saved terrain position. The POI's streaming and resident NPCs stay dormant so terrain and buildings do not shift during the opening. `OpeningDeferredWorldSystems.ActivateLaterWorld` is available for a later chapter handoff.
- Both blackout presentations use an always-on-top stereo overlay, and the welcome title renders above it. The title fades back to the scene after each route. A held captured knockout recovers at handoff while the next chapter destination remains unassigned.
- The active player's left and right rig arm lengths are equal (0.533 m in the saved pose). The body now prefers a tracked XR controller over simultaneous optical hand tracking for each arm. Controller reach still needs a headset check.
- `OpeningStaffBandit_V2` now has authored headwrap, rib vest, armored pants, and boots. V1 is preserved. The BanditGroup uses the three authored V2 instances, so it does not generate appearances at runtime.

## Automated Editor results

| Scenario | Result | Evidence |
| --- | --- | --- |
| Three knockout and recovery cycles, including movement state restoration | PASS | `OPENING_KNOCKOUT_CYCLES_PASS cycles=3` |
| Stationary-player real staff contact, after clothing repair | PASS | `contacts=2 overlaps=1074 playerHealth=82.0/100.0` |
| Nonlethal KO and ordinary lethal damage | PASS | `OPENING_NONLETHAL_LETHAL_PASS` |
| Capture and binding | PASS | `OPENING_CAPTURE_BIND_PASS` |
| Escort arrival | PASS | `OPENING_ESCORT_ARRIVAL_PASS` |
| One hand, fully overhead two hands, and cancellation | PASS | `OPENING_GESTURE_PASS oneHand/overheadTwoHands/cancel` |
| Hit during ultimatum releases all slavers into combat | PASS | `OPENING_AGGRESSION_COMBAT_PASS` |
| Victory title and return to the scene | PASS | `OPENING_VICTORY_ROUTE_PASS` with visible title and fade-back assertions |
| Captured title and recovery from held knockout | PASS | `OPENING_CAPTURED_TITLE_RECOVERY_PASS` |
| Escape route | PASS | `OPENING_ESCAPE_ROUTE_PASS` from the prior test pass |
| Main saved player placement, target, and slaver navigation | PASS | `OPENING_MAIN_SMOKE phase=EncounterActive nav=True,True,True states=Circle,Circle,Circle`; active XR origin stayed within 0.3 m vertically of its saved position and slavers were 2.7–2.8 m from the HMD at five seconds |
| Kareth visible at its saved terrain position | PASS | `OPENING_KARETH_STATIC_PASS renderers=258 position=(-3836.40, -3208.83, 2704.95)` |

## Remaining validation

The automated checks above do not establish stereo fade comfort, left-hand reach with a Quest controller, audible lines, held-object gesture rejection, punch and parry feel, rope visibility, natural escort behavior, replay safety, build startup, or Quest 2 performance. These require interactive Editor and headset runs. Voice recordings, final rope art, binding animation, and next-chapter destinations remain unassigned; subtitles, a LineRenderer rope, temporary binding pose, and serialized outcome events are placeholders.

Interactive headset checks above still prevent a full headset signoff. Kareth's residents and POI streaming need wiring and validation in the intended next chapter; only the authored city geometry is active during the opening. The disposable validation project contains a previously blocked cleanup target; do not retry deleting it through another tool. Its path and size should be given to the owner for manual cleanup when no longer needed.
