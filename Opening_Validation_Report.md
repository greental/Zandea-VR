# Zandea opening validation report

Updated 2 November 2026. The project owner authorized end-of-feature testing after Slices 01–13 were written. Automated runs used the isolated Unity project at `D:\applications\Unity\OpeningHarnessValidationTemp`; headset checks still require a person wearing Quest 2.

## Integration

- `Assets/Main.unity` now contains the opening encounter and is the first enabled build scene. `Assets/Scenes/Zandea/OpeningCaptureTest.unity` is the saved test scene. The prior Main and build settings are preserved in `.codex-opening-before-main-install/`.
- Authored scene reference checks passed for both scenes: `OPENING_SCENE_WIRED` and `OPENING_AUTHORED_SCENES_PASS`.
- Main Play mode activates the opening director and three slavers. The opening surface anchor places them on terrain after the terrain shift, builds local navigation, and binds all three agents. The rerun passed. A separate preexisting `BanditOutpost` spawn error is still present in the Main log.

## Automated Editor results

| Scenario | Result | Evidence |
| --- | --- | --- |
| Three knockout and recovery cycles, including movement state restoration | PASS | `OPENING_KNOCKOUT_CYCLES_PASS cycles=3` |
| Stationary-player real staff contact | PASS | `contacts=2 overlaps=995 playerHealth=82.0/100.0` |
| Nonlethal KO and ordinary lethal damage | PASS | `OPENING_NONLETHAL_LETHAL_PASS` |
| Capture and binding | PASS | `OPENING_CAPTURE_BIND_PASS` |
| Escort arrival | PASS | `OPENING_ESCORT_ARRIVAL_PASS` |
| One hand, two hands, and cancellation gesture cases | PASS | `OPENING_GESTURE_PASS` |
| Escape and victory routes | PASS | `OPENING_ESCAPE_ROUTE_PASS`, `OPENING_VICTORY_ROUTE_PASS` |
| Main opening startup and slaver navigation | PASS | `OPENING_MAIN_SMOKE phase=EncounterActive nav=True,True,True states=Idle,Idle,Idle` |

## Remaining validation

The automated checks above do not establish stereo fade comfort, audible lines, held-object gesture rejection, punch and parry feel, rope visibility, natural escort behavior, branch presentation, replay safety, build startup, or Quest 2 performance. These require interactive Editor and headset runs. Voice recordings, final rope art, binding animation, and next-chapter destinations remain unassigned; subtitles, a LineRenderer rope, temporary binding pose, and serialized outcome events are placeholders.

The separate `BanditOutpost` spawn error and the interactive checks above prevent a full playable-Main or headset signoff. The disposable validation project contains a previously blocked cleanup target; do not retry deleting it through another tool. Its path and size should be given to the owner for manual cleanup when no longer needed.
