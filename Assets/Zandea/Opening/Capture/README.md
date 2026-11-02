# Capture, binding, restraint, escort components

`OpeningCaptureGroup` coordinates the existing `BanditGroup` and three authored `StarvingBandit` instances. `BeginCapture` requires `SetCompliance(true)`, chooses one living captor, and places two guards in side positions with weapons ready. `NotifyAggression` schedules a perception-based reaction. Lowering hands, stepping away, punching, or holding a grabbed object also interrupts the attempt. Captor loss causes reassignment or an explicit abort.

`OpeningBindingAction` begins only when the captor is close to the player's tracked wrist midpoint. It draws a temporary rope line, waits for the configured action duration, and applies `PlayerWristRestraints` once if all conditions remain valid. A captor hit, lost compliance, or reach loss interrupts. A binding animation clip and trigger are exposed; without an assigned clip the rope and reach action are a visible temporary placeholder and Unity logs a warning.

`PlayerWristRestraints` disables direct interaction and martial hand actions while leaving headset/controller tracking and locomotion untouched. The front rope is lightweight `LineRenderer` geometry with capped visual separation. `RemoveRestraints` restores previous enabled states, including coordination with an active `PlayerKnockout`.

`OpeningPrisonerEscort` directs only NPCs. The player walks voluntarily; a lagging player makes the leader stop and warn, then escalates after a configured refusal interval. Path failure, leader loss, broken restraints, aggression, escape, and destination arrival have explicit outcomes. Voice clips, destination, and geometry thresholds must be assigned by the opening scene builder/director. These components do not supply a production binding animation or rope mesh.
