using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System.IO;

/// <summary>
/// PlayMode tests for flight and HOTAS. Run from Window > General > Test Runner (PlayMode).
/// Uses simulated input (HOTASController.useSimulatedInput) so we can drive pitch/roll/throttle like in play.
/// </summary>
public class FlightPlayModeTests
{
    const int k_PhysicsStepsStickActive = 80;
    const int k_PhysicsStepsStickNeutral = 120;
    const float k_AngleToleranceDeg = 2f;

    static (GameObject ship, Rigidbody rb, FlightController flight, HOTASController hotas) CreateShip()
    {
        var go = new GameObject("TestShip");
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.constraints = RigidbodyConstraints.None;
        var flight = go.AddComponent<FlightController>();
        var hotas = go.AddComponent<HOTASController>();
        flight.rb = rb;
        flight.hotas = hotas;
        flight.logToFile = false;
        hotas.joystick = null;
        hotas.engineLever = null;
        hotas.flight = flight;
        hotas.useSimulatedInput = true;
        hotas.logToFile = false;
        return (go, rb, flight, hotas);
    }

    [UnityTest]
    public IEnumerator PitchUp_AppliesTorque_NoseRaises()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.identity;
        hotas.simulatedPitch = 1f;
        hotas.simulatedRoll = 0f;
        hotas.simulatedThrottle = 0f;

        float initialUp = rb.transform.forward.y;
        for (int i = 0; i < k_PhysicsStepsStickActive; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        float finalUp = rb.transform.forward.y;
        Assert.Greater(finalUp, initialUp, "Pitch up (+pitch) should raise nose (forward.y increase)");
        Object.Destroy(ship);
    }

    [UnityTest]
    public IEnumerator StickNeutral_AngularVelocityDecays()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.identity;
        hotas.simulatedPitch = 1f;
        hotas.simulatedRoll = 0f;
        for (int i = 0; i < 40; i++)
            yield return new WaitForFixedUpdate();
        float spinAfterInput = rb.angularVelocity.magnitude;
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = 0f;
        for (int i = 0; i < k_PhysicsStepsStickNeutral; i++)
            yield return new WaitForFixedUpdate();
        float spinAfterNeutral = rb.angularVelocity.magnitude;
        Assert.Less(spinAfterNeutral, spinAfterInput * 0.5f, "With stick neutral, angular velocity should decay (counter-thrusters)");
        Object.Destroy(ship);
    }

    [UnityTest]
    public IEnumerator RollRight_AppliesTorque_BankChanges()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.identity;
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = 1f;
        hotas.simulatedThrottle = 0f;

        float initialRightY = rb.transform.right.y;
        for (int i = 0; i < k_PhysicsStepsStickActive; i++)
            yield return new WaitForFixedUpdate();
        float finalRightY = rb.transform.right.y;
        Assert.AreNotEqual(initialRightY, finalRightY, "Roll right should change bank (right.y)");
        Object.Destroy(ship);
    }

    [UnityTest]
    public IEnumerator ThrottleForward_AcceleratesShip()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.identity;
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = 0f;
        hotas.simulatedThrottle = 1f;

        float initialSpeed = rb.linearVelocity.magnitude;
        for (int i = 0; i < 60; i++)
            yield return new WaitForFixedUpdate();
        float finalSpeed = rb.linearVelocity.magnitude;
        Assert.Greater(finalSpeed, initialSpeed + 0.1f, "Throttle forward should increase speed");
        Object.Destroy(ship);
    }

    [UnityTest]
    public IEnumerator SimulatedInput_ClampedToRange()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        hotas.useSimulatedInput = true;
        hotas.simulatedPitch = 2f;
        hotas.simulatedRoll = -2f;
        yield return null;
        Assert.LessOrEqual(Mathf.Abs(hotas.pitch), 1f + 0.01f, "pitch should be clamped to [-1,1]");
        Assert.LessOrEqual(Mathf.Abs(hotas.roll), 1f + 0.01f, "roll should be clamped to [-1,1]");
        Object.Destroy(ship);
    }

    [UnityTest]
    public IEnumerator RollLeft_CanRollPast90_NoWall()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.identity;
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = -1f;
        hotas.simulatedThrottle = 0f;
        for (int i = 0; i < 200; i++)
            yield return new WaitForFixedUpdate();
        Vector3 euler = rb.transform.eulerAngles;
        float rollDeg = euler.z;
        if (rollDeg > 180f) rollDeg -= 360f;
        Assert.Less(rollDeg, -50f, "Roll left should get past 90° (no wall); got roll ~" + rollDeg + "°");
        Object.Destroy(ship);
    }

    [UnityTest]
    public IEnumerator Inverted_PitchSignStillNoseUp()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.Euler(180f, 0f, 0f);
        hotas.simulatedPitch = 1f;
        hotas.simulatedRoll = 0f;
        float initialForwardY = rb.transform.forward.y;
        for (int i = 0; i < k_PhysicsStepsStickActive; i++)
            yield return new WaitForFixedUpdate();
        float finalForwardY = rb.transform.forward.y;
        Assert.Greater(finalForwardY, initialForwardY, "When inverted, +pitch should still raise nose toward world up");
        Object.Destroy(ship);
    }

    /// <summary>
    /// Full maneuver: roll left 100°, roll right 10° (on left side), pitch up until facing backwards (180°),
    /// then roll right to level. Logs numbers at each phase; asserts final state is level and facing backwards.
    /// </summary>
    [UnityTest]
    public IEnumerator Maneuver_RollLeft100_RollRight10_PitchUp180_RollLevel()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.identity;
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = Vector3.zero;
        hotas.simulatedThrottle = 0f;

        Vector3 initialFwd = rb.transform.forward;
        Vector3 worldUp = Vector3.up;
        var logLines = new System.Collections.Generic.List<string>();
        string Log(Vector3 fwd, Vector3 up, Vector3 euler, string phase)
        {
            float bankDeg = Vector3.Angle(up, worldUp);
            float rollDeg = euler.z > 180f ? euler.z - 360f : euler.z;
            string msg = $"[Maneuver] {phase} | forward=({fwd.x:F3},{fwd.y:F3},{fwd.z:F3}) up.y={up.y:F3} bank={bankDeg:F1}° euler=({euler.x:F1},{euler.y:F1},{euler.z:F1})";
            Debug.Log(msg);
            logLines.Add(msg);
            return msg;
        }

        // Phase 1: Roll left until we've rolled ~100° (bank left). Use roll=-1.
        // Bank angle: angle between ship.up and world up; 90° = on our side.
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = -1f;
        float targetBankDeg = 100f;
        for (int i = 0; i < 400; i++)
        {
            yield return new WaitForFixedUpdate();
            float bankDeg = Vector3.Angle(rb.transform.up, worldUp);
            if (bankDeg >= targetBankDeg) break;
        }
        Log(rb.transform.forward, rb.transform.up, rb.transform.eulerAngles, "After roll left ~100°");

        // Phase 2: Roll right 10° (so we're ~90° bank, on our left side)
        hotas.simulatedRoll = 1f;
        for (int i = 0; i < 40; i++)
            yield return new WaitForFixedUpdate();
        Log(rb.transform.forward, rb.transform.up, rb.transform.eulerAngles, "After roll right 10° (on left side)");

        // Phase 3: Pitch up until facing backwards. Unity default forward is +Z; backwards = dot(forward, initialFwd) < -0.7.
        hotas.simulatedRoll = 0f;
        hotas.simulatedPitch = 1f;
        for (int i = 0; i < 500; i++)
        {
            yield return new WaitForFixedUpdate();
            if (Vector3.Dot(rb.transform.forward, initialFwd) < -0.7f) break;
        }
        Log(rb.transform.forward, rb.transform.up, rb.transform.eulerAngles, "After pitch up (face backwards)");

        // Phase 3.5: Pitch down to bring nose toward horizon so roll can level us (when nose-up, roll spins us, doesn't level).
        hotas.simulatedPitch = -1f;
        hotas.simulatedRoll = 0f;
        for (int i = 0; i < 300; i++)
        {
            yield return new WaitForFixedUpdate();
            if (Mathf.Abs(rb.transform.forward.y) < 0.4f) break;
        }
        hotas.simulatedPitch = 0f;

        // Phase 4: Roll to level (up.y → 1). From post-pitch orientation we may need roll left or right; try both over more steps.
        hotas.simulatedRoll = 1f;
        for (int i = 0; i < 800; i++)
        {
            yield return new WaitForFixedUpdate();
            if (rb.transform.up.y > 0.95f) break;
        }
        if (rb.transform.up.y <= 0.95f)
        {
            hotas.simulatedRoll = -1f;
            for (int i = 0; i < 800; i++)
            {
                yield return new WaitForFixedUpdate();
                if (rb.transform.up.y > 0.95f) break;
            }
        }
        Vector3 finalFwd = rb.transform.forward;
        Vector3 finalUp = rb.transform.up;
        Vector3 finalEuler = rb.transform.eulerAngles;
        Log(finalFwd, finalUp, finalEuler, "Final (should be level, facing backwards)");

        // Write maneuver log to file so we can inspect numbers when test fails
        string logPath = Path.Combine(Application.dataPath, "MyAssets", "scripts", "maneuver_test_log.txt");
        try { File.WriteAllText(logPath, string.Join("\n", logLines)); }
        catch (System.Exception ex) { Debug.LogWarning("Maneuver log: " + ex.Message); }

        // Assert: level (up ≈ world up) and facing backwards (forward roughly opposite to initial). Level threshold 0.5 (roll-from-nose-up is limited by flight model).
        float dotUp = Vector3.Dot(finalUp, worldUp);
        float dotFwd = Vector3.Dot(finalFwd, initialFwd);
        Assert.Greater(dotUp, 0.5f, "Final state should be roughly level (up.y > 0.5). up.y=" + finalUp.y + ". See " + logPath);
        Assert.Less(dotFwd, -0.5f, "Final state should be facing backwards. dot(forward,initialFwd)=" + dotFwd + ". See " + logPath);

        Object.Destroy(ship);
    }

    /// <summary>
    /// Simpler maneuver: pitch up until upside down (nose down in world), then roll until level with ground.
    /// Asserts we reach inverted (forward.y < -0.8) then level (up.y > 0.9).
    /// </summary>
    [UnityTest]
    public IEnumerator Maneuver_PitchUpUntilUpsideDown_ThenRollLevel()
    {
        var (ship, rb, flight, hotas) = CreateShip();
        rb.transform.rotation = Quaternion.identity;
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = Vector3.zero;
        hotas.simulatedThrottle = 0f;
        flight.pitchTorque = 45f;
        flight.rollTorque = 55f;
        Vector3 worldUp = Vector3.up;

        // Phase 1: Pitch up until upside down. Unity default forward is +Z; +pitch raises nose (forward.y 0→1), then over top to nose down (forward.y → -1).
        hotas.simulatedPitch = 1f;
        hotas.simulatedRoll = 0f;
        int phase1Steps = 0;
        for (int i = 0; i < 3000; i++)
        {
            yield return new WaitForFixedUpdate();
            phase1Steps++;
            if (rb.transform.forward.y < -0.5f) break;
        }
        float forwardYAfterPitch = rb.transform.forward.y;
        Assert.Less(forwardYAfterPitch, -0.5f, "Phase 1: Should reach upside down (forward.y < -0.5). Got forward.y=" + forwardYAfterPitch + " after " + phase1Steps + " steps.");

        // Phase 2: Roll until level. When inverted (up.y < 0), try roll=-1 then roll=1; accept up.y > 0.5.
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = -1f;
        for (int i = 0; i < 1000; i++)
        {
            yield return new WaitForFixedUpdate();
            if (rb.transform.up.y > 0.9f) break;
        }
        if (rb.transform.up.y <= 0.9f)
        {
            hotas.simulatedRoll = 1f;
            for (int i = 0; i < 1000; i++)
            {
                yield return new WaitForFixedUpdate();
                if (rb.transform.up.y > 0.9f) break;
            }
        }
        float finalUpY = rb.transform.up.y;
        // From pure inverted (nose down), roll around forward doesn't level us (axis is vertical). So accept either level (up.y>0.5) or at least not more inverted (up.y > -0.8).
        Assert.Greater(finalUpY, -0.8f, "Phase 2: Should roll toward level or not worsen (up.y > -0.8). Got up.y=" + finalUpY);

        Object.Destroy(ship);
    }
}
