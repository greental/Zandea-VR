# Armed bandit visual review

Open `ArmedBanditReview.unity` and press Play. The scene contains all 15 generated armed bandit variants in a labelled 5-by-3 grid. Their real Animator, grip solver, physical weapons, and fitted weapon colliders remain active.

The bandits loop through an in-place walking guard followed by attack directions 1–8. The Game view controls let you pause, turn off automatic cycling, select one attack direction, switch walking on or off, and focus the camera on any weapon. A label above each bandit shows the current attack number and clip name; focus view also shows the full clip name. In focus view, report the weapon name, direction number, and whether the problem occurs during walking guard, windup, strike, follow-through, or recovery. A screenshot is useful for a specific pose; a short recording is useful for timing or weapon lag. The two low Bo Staff directions now use side strikes because the old floor sweep crossed the body and stepped backward.

The scene is isolated from `Main.unity` and does not save changes to it. The review controller is under `Review Controls` if you need to adjust the guard and gap durations.
