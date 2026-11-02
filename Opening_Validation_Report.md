# Zandea opening validation report — pending end-of-feature test pass

Prepared 2 November 2026 for the complete opening implementation. Project owner explicitly deferred all Unity and headset testing until the writing phase ends. **No scenario below has been run.** This report is a test plan and an honest status record, not a signoff.

## Integration state before testing

- The code for Slices 01–13 is written on the opening branch. `Zandea/Opening/Build test scene` and `Zandea/Opening/Install encounter in Main` are authoring commands. Neither has been run, so the test scene is not saved and `Assets/Main.unity`/build scene order have not yet been changed by the opening work.
- The Main installer keeps the established player scene instance, composes three `OpeningStaffBandit_V2` slavers, and makes Main the first enabled build scene. `OpeningStaffBandit_V1` remains a distinct-GUID baseline. Installer execution and saved-reference review are required in the end-of-feature test pass.
- Voice recordings, a production rope mesh, binding animation, and next chapter destinations are unassigned. The current line/subtitle, LineRenderer rope, temporary binding pose, and serialized outcome events are intentional placeholders.

## Acceptance matrix

Every actual result is **NOT TESTED** by owner direction. Record the scene/build, date, result, console errors, and capture/video for each run. Headset rows require Quest 2 PCVR; an Editor simulation cannot pass them.

| ID | Method and expected result | Headset | Actual |
| --- | --- | --- | --- |
| A01 | Fresh Main/build load: unarmed player and three slavers start automatically | Yes | NOT TESTED |
| A02 | Wake: comfortable stereo fade and desert ambience | Yes | NOT TESTED |
| A03 | Demand: one audible warning or development subtitle | Yes | NOT TESTED |
| A04 | No gesture: coherent escalation; no scripted forced KO | Yes | NOT TESTED |
| A05 | Raise one hand: no surrender | Yes | NOT TESTED |
| A06 | Raise two open empty hands: confirm after hold | Yes | NOT TESTED |
| A07 | Hold item in one hand: no surrender | Yes | NOT TESTED |
| A08 | Ordinary overhead reach/climb: no false surrender | Yes | NOT TESTED |
| A09 | Raise/lower hands: capture attempt cancels | Yes | NOT TESTED |
| A10 | Surrender: exactly one captor; two guards remain ready | Yes | NOT TESTED |
| A11 | Punch captor during approach: ordinary physics hit/damage | Yes | NOT TESTED |
| A12 | Punch during binding: interrupt, no restraint | Yes | NOT TESTED |
| A13 | Miss punch: no scripted hit; guards react to intent | Yes | NOT TESTED |
| A14 | Complete binding: front rope visible; actions restricted | Yes | NOT TESTED |
| A15 | Separate tracked hands: controllers/head remain free | Yes | NOT TESTED |
| A16 | Escort: follow voluntarily; NPCs navigate | Yes | NOT TESTED |
| A17 | Refuse escort: warning then explicit escalation | Yes | NOT TESTED |
| A18 | Flee escort: pursuit/escape branch without teleport | Yes | NOT TESTED |
| A19 | Attack guard during escort: consistent interrupt | Yes | NOT TESTED |
| A20 | Stand still: real staff tip/collider contact damages player | Yes | NOT TESTED |
| A21 | Dodge/parry/block: existing combat mitigation remains | Yes | NOT TESTED |
| A22 | Disarm/pick up staff: references and physics remain valid | Yes | NOT TESTED |
| A23 | Nonlethal staff hits: player KO, not death/gore/loot | Editor + headset | NOT TESTED |
| A24 | Ordinary lethal attack elsewhere: death/gore unchanged | Editor | NOT TESTED |
| A25 | Debug KO/recover three times: exact state restoration | Editor + headset | NOT TESTED |
| A26 | Defeat all slavers: victory route; no forced defeat | Editor + headset | NOT TESTED |
| A27 | Escape unseen beyond distance/time: escape route | Editor + headset | NOT TESTED |
| A28 | Kill captor/leader: reassign or explicit abort | Editor + headset | NOT TESTED |
| A29 | Remove optional audio/rope/waypoint: warning and safe route | Editor | NOT TESTED |
| A30 | Each resolved branch: title policy, one title, one handoff | Editor + headset | NOT TESTED |
| A31 | Restart/replay: no duplicate characters/listeners/overlays | Editor | NOT TESTED |
| A32 | Shipping scene: Main opens without manual POI toggle | Editor + build | NOT TESTED |
| A33 | Tracking and comfort: no forced head rotation or walk | Yes | NOT TESTED |
| A34 | Quest 2 PCVR profiling: acceptable frame timing/stutters | Yes | NOT TESTED |

## End-of-feature execution order

1. Check D: free space (>20 GiB), close or coordinate the live Unity editor, run the Main installer and test-scene builder once, and inspect their serialized references. The previously blocked temporary validation copy at `D:\applications\Unity\OpeningHarnessValidationTemp` needs manual cleanup before any further large validation copy.
2. Compile/import and resolve all errors. Run the Editor cases above and inspect `BanditDuelStrikeDamage` contacts/overlaps with a stationary player. Fix any discovered blockers in scope.
3. Run the headset matrix on Quest 2 PCVR. Mark actual PASS/FAIL with evidence; do not infer stereo comfort or physics contact from Editor simulation.
4. Repeat fresh Main/build load and replay, then fill this report with actual results, remaining bugs, and signoff.

## Known open items before execution

- Main scene installation and build order are pending execution; no shipping integration claim yet.
- Binding animation and voice assets are absent; placeholders should be judged for usability, then replaced or assigned.
- No next-chapter scene was specified; serialized per-outcome handoff events are available but unassigned.
- All runtime behavior and XR presentation remain unverified until the deferred test pass.
