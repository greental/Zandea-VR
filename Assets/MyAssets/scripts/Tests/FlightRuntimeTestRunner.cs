using UnityEngine;
using System.Collections;

/// <summary>
/// Optional: attach to a GameObject in the scene. While playing, press the test key to run
/// the same kind of checks as PlayMode tests (using simulated HOTAS input). Results are logged.
/// Ensure your ship has FlightController + HOTASController and enable "Use Simulated Input"
/// for the test, or this will use the ship's existing HOTAS and just run assertions.
/// </summary>
public class FlightRuntimeTestRunner : MonoBehaviour
{
    [Header("References (assign in editor)")]
    public FlightController flight;
    public HOTASController hotas;

    [Header("Trigger")]
    [Tooltip("Key to run tests while playing.")]
    public KeyCode testKey = KeyCode.F8;

    [Tooltip("If true, tests use simulated input and temporarily override stick/lever.")]
    public bool useSimulatedInputForTest = true;

    const int k_Steps = 80;
    bool _running;

    void Update()
    {
        if (_running || flight == null || hotas == null) return;
        if (!Input.GetKeyDown(testKey)) return;

        StartCoroutine(RunTests());
    }

    IEnumerator RunTests()
    {
        _running = true;
        bool wasSimulated = hotas.useSimulatedInput;
        float oldPitch = hotas.simulatedPitch;
        float oldRoll = hotas.simulatedRoll;
        float oldThrottle = hotas.simulatedThrottle;

        if (useSimulatedInputForTest)
            hotas.useSimulatedInput = true;

        Rigidbody rb = flight.rb;
        int passed = 0;
        int failed = 0;

        // 1) Pitch up raises nose
        rb.transform.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        hotas.simulatedPitch = 1f;
        hotas.simulatedRoll = 0f;
        hotas.simulatedThrottle = 0f;
        yield return null;
        float noseYBefore = rb.transform.forward.y;
        for (int i = 0; i < k_Steps; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        float noseYAfter = rb.transform.forward.y;
        if (noseYAfter > noseYBefore)
        {
            passed++; Debug.Log("[FlightTest] PASS: Pitch up raises nose.");
        }
        else
        {
            failed++; Debug.LogWarning($"[FlightTest] FAIL: Pitch up should raise nose. forward.y {noseYBefore} -> {noseYAfter}");
        }

        // 2) Stick neutral -> angular velocity decays
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = 0f;
        float spinMid = rb.angularVelocity.magnitude;
        for (int i = 0; i < 100; i++)
            yield return new WaitForFixedUpdate();
        float spinEnd = rb.angularVelocity.magnitude;
        if (spinEnd < spinMid * 0.6f || spinMid < 0.01f)
        {
            passed++; Debug.Log("[FlightTest] PASS: Stick neutral reduces spin.");
        }
        else
        {
            failed++; Debug.LogWarning($"[FlightTest] FAIL: Stick neutral should reduce spin. {spinMid} -> {spinEnd}");
        }

        // 3) Roll right changes bank
        rb.transform.rotation = Quaternion.identity;
        rb.angularVelocity = Vector3.zero;
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = 1f;
        yield return null;
        float rightYBefore = rb.transform.right.y;
        for (int i = 0; i < k_Steps; i++)
            yield return new WaitForFixedUpdate();
        float rightYAfter = rb.transform.right.y;
        if (Mathf.Abs(rightYAfter - rightYBefore) > 0.01f)
        {
            passed++; Debug.Log("[FlightTest] PASS: Roll right changes bank.");
        }
        else
        {
            failed++; Debug.LogWarning($"[FlightTest] FAIL: Roll right should change bank. right.y {rightYBefore} -> {rightYAfter}");
        }

        if (useSimulatedInputForTest)
        {
            hotas.useSimulatedInput = wasSimulated;
            hotas.simulatedPitch = oldPitch;
            hotas.simulatedRoll = oldRoll;
            hotas.simulatedThrottle = oldThrottle;
        }

        Debug.Log($"[FlightTest] Done. Passed={passed} Failed={failed}");
        _running = false;
    }
}
