# Opening checkpoint decision

Project owner decision, 2 November 2026: write Slices 02–04 together on `codex/opening-slice-01`; do not run Unity tests or validation until the full checkpoint is written. Apply this cadence throughout the opening feature branch unless the owner changes it. Keep test and headset claims separate from implementation.

## Written scope of this checkpoint

- Slice 02: `PlayerKnockout` provides conscious, starting, unconscious, and recovering states; one-shot transition events; indefinite or timed KO; and a reversible action gate for movement, turning, jumping, sprint, equipment, melee hands and direct interactors. It leaves XR pose/body components active. A held player weapon rejects impact damage during KO.
- Slice 03: `KnockoutPresentation` subscribes to the state and uses a camera-child transparent quad for both eyes plus optional local impact/ringing sounds and an audio low-pass filter. It never moves the camera.
- Slice 04: the harness builder references three `OpeningStaffBandit_V2` prefabs with exclusive group attack slots. `OpeningStaffBandit_V1` is a distinct-GUID preservation copy. Tip-overlap scoring complements the existing physical tip sweep for stationary-target collisions; diagnostics expose contact and overlap counts.

This checkpoint is written but not validated in Unity or a headset under the owner's testing cadence. The scene builder has not yet been run, so `OpeningCaptureTest.unity` is not saved. A later checkpoint validation must first build it, verify prefab/scene references, repeat KO/recovery, and measure real staff contacts against a stationary player. No contact success or XR comfort claim is made here.
