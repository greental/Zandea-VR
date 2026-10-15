# Minimal Flight Test Scene

This scene proves **world-referential pitch** and **body roll** in isolation (no VR, no HOTAS).  
If behavior is correct here but wrong in VR, the bug is in HOTAS input mapping.

---

## 1. New scene

- **File → New Scene** (Basic or Empty).
- **File → Save As** → e.g. `Scenes/MinimalFlightTest.unity`.

---

## 2. Ship (cube)

- **GameObject → 3D Object → Cube**.
- Rename to `MinimalShip`.
- **Add Component → Rigidbody**:
  - **Use Gravity**: off.
  - **Constraints**: None.
  - **Linear / Angular Drag**: 0 (script sets damping).
- **Add Component → Scripts → Minimal Flight** (the script you just added).
- Leave **MinimalFlight** defaults (or set Pitch Torque 25, Roll Torque 38).
- Position: e.g. (0, 2, 0) so it’s in view.

---

## 3. Camera (optional follow)

- **Main Camera** is already in the scene.
- **Add Component → Scripts → Minimal Flight Camera**.
- In **Minimal Flight Camera**:
  - **Target** = drag `MinimalShip` from the Hierarchy.
  - **Distance** = 8, **Height** = 2, **Smooth Time** = 0.15.

If you don’t use the follow script, move the camera so it looks at the cube and you can see it pitch/roll.

---

## 4. Play and test

- **Press Play**.
- **W / S** = pitch (pull back = nose toward sky).
- **A / D** = roll (left / right).
- **E / Q** = throttle forward / back (optional).

**Checks:**

1. **Level:** Hold **W** → nose pitches up (no yaw). Release → rotation damps.
2. **Bank 90°:** Hold **A** until ship is on its left side (wing toward camera). Hold **W** → nose should still pitch **toward the sky** (same “pull up” feel), not yaw sideways.
3. **Over the top:** Keep **W** until nose is straight up (~90°). Keep holding **W** → nose should continue over the top and point down (no hard stop at 90°).

If (2) yaws instead of pitching, or (3) stops at 90°, the **flight math** is wrong and we fix `MinimalFlight` / `FlightController`.  
If (2) and (3) are correct here but wrong in VR, the bug is in **HOTAS** (stick → pitch/roll mapping).

---

## 5. Optional: ground plane

- **GameObject → 3D Object → Plane** (scale 5,0.1,5), position (0,0,0) so you have a horizon reference.

---

## Flight model mode (Inspector)

On **MinimalFlight** (and **FlightController** in the main game) use the **Flight Model Mode** dropdown to compare:

- **Direct Torque** – current axis logic (orient axis so +pitch = nose toward up; persist at singularity).
- **Band Delta** – when `fwd.y` is inside a band (e.g. 0.42–0.58), the pitch axis is locked to the value at band entry so it doesn’t flip. Tune with **Pitch Band Delta**.
- **Target Orientation** – an invisible “FlightTarget” transform is driven by integrated pitch/roll; the ship applies torque toward that target (no axis singularity). Optional **Flight Target** assign in inspector; if null, a child is created.

---

## Diagnostics when pitch "stops"

- **On-screen:** With **Show On Screen Debug** checked on `MinimalFlight`, the top-left shows nose up angle, pitch axis, pitch torque magnitude, angular velocity, and whether the **near vertical** branch is active. When pitch freezes, note if `[NEAR VERTICAL]` appears and whether pitch torque or "along pitch axis" goes to zero.
- **File log:** With **Log Pitch To File** checked, holding W/S writes lines to  
  `Assets/MyAssets/scripts/minimal_flight_pitch_log.txt`  
  (path under your project). When pitch seems caught, stop, open that file and share the last 30–50 lines (or the whole file). That shows noseUp°, fwd.y, nearVert, pitchAxis, torque, and angular velocity so we can see why it stopped.
