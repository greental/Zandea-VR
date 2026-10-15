FLIGHT & HOTAS TESTS
===================

1) PlayMode tests (Test Runner)
   - Open: Window > General > Test Runner
   - Select the PlayMode tab
   - Run All, or run "FlightPlayModeTests" only
   - Tests create a temporary ship (Rigidbody + FlightController + HOTASController),
     use simulated input (pitch/roll/throttle), step physics, and assert behaviour.

2) Simulated input (same as when you play)
   - On HOTASController, enable "Use Simulated Input" and set:
     Simulated Pitch, Simulated Roll (-1..1), Simulated Throttle (-1..1)
   - The ship will use these instead of the joystick/lever. Lets you test flight
     from the Inspector or from scripts.

3) Runtime test runner (while playing)
   - Add the "FlightRuntimeTestRunner" component to a GameObject in the scene
   - Assign its Flight Controller and HOTAS Controller references
   - In Play mode, press F8 (or the key you set) to run the same checks as the
     PlayMode tests. Results are logged to the Console (Pass/Fail).
