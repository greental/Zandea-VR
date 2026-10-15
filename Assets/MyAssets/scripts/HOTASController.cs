using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Content.Interaction;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Bridges stick/lever to FlightController. Same flight model as MinimalFlight; only difference is input source
/// (joystick vs keyboard). Enable Keyboard Override For Testing in VR to use W/S A/D; if ship then reacts correctly,
/// the issue is in HOTASJoystick (reference frame or invert).
/// </summary>
public class HOTASController : MonoBehaviour
{
    [Header("References")]
    public HOTASJoystick joystick;         // our joystick (pitch + roll)
    public EngineLever engineLever;        // your existing EngineLever
    public FlightController flight;        // your existing FlightController

    [Header("Testing: match simple scene input")]
    [Tooltip("When true, pitch/roll come from W/S and A/D (same as MinimalFlight). Use this in VR to verify ship reacts correctly; if it does, the issue is joystick mapping.")]
    public bool keyboardOverrideForTesting = false;

    [Header("Simulated input (for tests or sliders – used when Use Simulated Input is on)")]
    [Tooltip("When true, pitch/roll/yaw/throttle/strafe/vertical come from Simulated values below instead of joystick/lever.")]
    public bool useSimulatedInput = false;
    [Range(-1f, 1f)] public float simulatedPitch;
    [Range(-1f, 1f)] public float simulatedRoll;
    [Range(-1f, 1f)] public float simulatedYaw;
    [Range(-1f, 1f)] public float simulatedStrafe;
    [Range(-1f, 1f)] public float simulatedVertical;
    [Range(-1f, 1f)] public float simulatedThrottle = 0f;  // -1..1 (0 = no thrust)

    [Header("Pitch / Roll / Yaw strength")]
    [Tooltip("Scale applied to pitch (nose up/down). 1 = normal, 4 = 4x speed.")]
    [Range(0f, 4f)] public float pitchStrength = 1f;
    [Tooltip("Scale applied to roll (bank left/right). 1 = normal, 4 = 4x speed.")]
    [Range(0f, 4f)] public float rollStrength = 1f;
    [Tooltip("Scale applied to yaw (wrist twist). How fast the ship rotates when yawing. 2 = 2x rotation speed.")]
    [Range(0.5f, 4f)] public float yawStrength = 2f;
    [Tooltip("Scale applied to vertical (right stick Y, up/down). 1 = normal, 4 = 4x thrust.")]
    [Range(0f, 4f)] public float verticalStrength = 1f;
    [Tooltip("Scale applied to horizontal strafe (right stick X, left/right). 1 = normal, 4 = 4x thrust.")]
    [Range(0f, 4f)] public float horizontalStrength = 1f;

    [Header("Inertial Drift Joystick Sensitivity")]
    [Tooltip("Drift-mode left/right joystick sensitivity (roll). 0.25 = one quarter of the normal joystick response.")]
    [Range(0f, 2f)] public float driftLeftRightSensitivity = 0.25f;
    [Tooltip("Drift-mode up/down joystick sensitivity (pitch). 0.25 = one quarter of the normal joystick response.")]
    [Range(0f, 2f)] public float driftUpDownSensitivity = 0.25f;

    [Header("Ship speed")]
    [Tooltip("Scale applied to forward thrust and max speed. 1 = normal, 2 = twice as fast acceleration and top speed.")]
    [Range(0.25f, 4f)] public float speedStrength = 1f;

    [Header("Right controller stick (vertical + horizontal)")]
    [Tooltip("Deadzone for right stick. Push away = up, pull = down; left/right = strafe.")]
    [Range(0f, 0.4f)] public float rightStickDeadzone = 0.15f;

    [Header("Throttle snap to zero")]
    [Tooltip("When lever is within this range of center (-1..1), throttle is forced to 0 so the ship stops completely. Lever visual snap is on EngineLever (Snap To Zero Angle Deg).")]
    [Range(0.02f, 0.25f)] public float throttleSnapDeadzone = 0.12f;

    [Header("Cross-axis suppression (pitch/roll)")]
    [Tooltip("When stick is this far on one axis (e.g. 80% pitch), we treat that axis as dominant and can suppress the other.")]
    [Range(0.5f, 0.95f)] public float dominantAxisThreshold = 0.7f;
    [Tooltip("When one axis is dominant, the other axis is zeroed unless it exceeds this. Stops small unwanted roll when pulling pitch (and vice versa).")]
    [Range(0.1f, 0.5f)] public float crossAxisThreshold = 0.25f;

    [Header("Runtime Values (Read Only)")]
    [Range(-1f, 1f)] public float pitch;
    [Range(-1f, 1f)] public float roll;
    [Range(-1f, 1f)] public float yaw;
    [Range(0f, 1f)] public float throttle;
    [Range(-1f, 1f)] public float vertical;  // right stick Y: push away = up, pull = down
    [Range(-1f, 1f)] public float strafe;    // right stick X: left = -1, right = +1

    [Header("In-game metrics (WASD equivalent)")]
    [Tooltip("Show current pitch/roll on screen as W/S and A/D equivalent so you can see what the joystick is sending. Off by default — enable only when debugging input.")]
    public bool showInputMetrics = false;
    public int metricsFontSize = 24;

    [Header("Debug log")]
    [Tooltip("Log raw joystick and final pitch/roll to file. Keep disabled in play – file I/O causes frame hitches when stick is active.")]
    public bool logToFile = false;
    public int logMaxLines = 500;

    static string LogPath => Path.Combine(Application.dataPath, "MyAssets", "scripts", "hotas_log.txt");
    static List<string> s_logLines = new List<string>();
    static float s_nextLogTime;

    void Awake()
    {
        if (flight == null)
        {
            flight = GetComponent<FlightController>() ?? GetComponentInParent<FlightController>();
            if (flight == null && transform.root != null)
                flight = transform.root.GetComponentInChildren<FlightController>(true);
        }
    }

    void Update()
    {
        // ---- STICK INPUT: keyboard override (same as MinimalFlight) > simulated > joystick ----
        if (keyboardOverrideForTesting)
        {
            float kPitch = 0f;
            if (Input.GetKey(KeyCode.W)) kPitch += 1f;
            if (Input.GetKey(KeyCode.S)) kPitch -= 1f;
            float kRoll = 0f;
            if (Input.GetKey(KeyCode.D)) kRoll += 1f;
            if (Input.GetKey(KeyCode.A)) kRoll -= 1f;
            pitch = Mathf.Clamp(kPitch, -1f, 1f);
            roll  = Mathf.Clamp(kRoll,  -1f, 1f);
        }
        else if (useSimulatedInput)
        {
            pitch = Mathf.Clamp(simulatedPitch, -1f, 1f);
            roll  = Mathf.Clamp(simulatedRoll,  -1f, 1f);
            yaw   = Mathf.Clamp(simulatedYaw,   -1f, 1f);
            vertical = Mathf.Clamp(simulatedVertical * verticalStrength, -1f, 1f);
            strafe   = Mathf.Clamp(simulatedStrafe * horizontalStrength, -1f, 1f);
        }
        else if (joystick != null)
        {
            if (!joystick.IsGrabbed)
            {
                pitch = 0f;
                roll = 0f;
                yaw = 0f;
            }
            else
            {
                bool driftMode = flight != null && flight.flightModelMode == FlightModelMode.InertialDrift;
                float pitchModeSensitivity = driftMode ? driftUpDownSensitivity : 1f;
                float rollModeSensitivity = driftMode ? driftLeftRightSensitivity : 1f;
                pitch = Mathf.Clamp(joystick.Pitch * pitchStrength * pitchModeSensitivity, -1f, 1f);
                roll  = Mathf.Clamp(joystick.Roll  * rollStrength * rollModeSensitivity,  -1f, 1f);
                yaw   = Mathf.Clamp(joystick.Yaw, -1f, 1f);
            }
        }
        else
        {
            pitch = 0f;
            roll = 0f;
            yaw = 0f;
        }

        // ---- Cross-axis suppression: when mostly on one axis, ignore small deflection on the other ----
        if (joystick != null && joystick.IsGrabbed && !keyboardOverrideForTesting && !useSimulatedInput)
            ApplyCrossAxisSuppression(ref pitch, ref roll);

        // ---- VERTICAL + STRAFE: right controller thumbstick only when ship joystick is grabbed ----
        if (useSimulatedInput)
        {
            // vertical/strafe already set from simulatedVertical / simulatedStrafe above
        }
        else if (joystick != null && joystick.IsGrabbed)
        {
            ReadRightStick(out float rawVert, out float rawStrafe);
            vertical = Mathf.Clamp(rawVert * verticalStrength, -1f, 1f);
            strafe   = Mathf.Clamp(rawStrafe * horizontalStrength, -1f, 1f);
        }
        else
            vertical = strafe = 0f;

        // ---- LOG to file (raw from joystick + final) ----
        if (logToFile && Time.time >= s_nextLogTime)
        {
            s_nextLogTime = Time.time + 0.1f;
            float rawP = joystick != null ? joystick.Pitch : float.NaN;
            float rawR = joystick != null ? joystick.Roll : float.NaN;
            float rawY = joystick != null ? joystick.Yaw : float.NaN;
            string line = $"{Time.time:F2} | rawPitch={rawP:F3} rawRoll={rawR:F3} rawYaw={rawY:F3} -> pitch={pitch:F3} roll={roll:F3} yaw={yaw:F3} throttle={throttle:F3}";
            s_logLines.Add(line);
            if (s_logLines.Count > logMaxLines) s_logLines.RemoveAt(0);
            try { File.WriteAllText(LogPath, string.Join("\n", s_logLines)); }
            catch (System.Exception ex) { Debug.LogWarning("HOTASController log: " + ex.Message); }
        }

        // ---- THROTTLE INPUT (real lever or simulated) ----
        if (useSimulatedInput)
        {
            float t = Mathf.InverseLerp(-1f, 1f, simulatedThrottle);
            if (t > 0.4f && t < 0.6f) throttle = 0f;
            else if (t <= 0.4f) throttle = -Mathf.InverseLerp(0.4f, 0f, t) * 0.2f;
            else throttle = Mathf.InverseLerp(0.6f, 1f, t);
        }
        else if (engineLever != null)
        {
            float lever = engineLever.normalizedValue;  // -1..1
            if (Mathf.Abs(lever) <= throttleSnapDeadzone)
            {
                throttle = 0f;  // snap to zero so ship stops completely when lever is near center
            }
            else
            {
                float t = Mathf.InverseLerp(-1f, 1f, lever);
                if (t > 0.4f && t < 0.6f) throttle = 0f;
                else if (t <= 0.4f) throttle = -Mathf.InverseLerp(0.4f, 0f, t) * 0.2f;
                else throttle = Mathf.InverseLerp(0.6f, 1f, t);
            }
        }
        else
        {
            throttle = 0f;
        }


        // ---- SEND TO FLIGHT CONTROLLER ----
        if (flight != null)
        {
            // FlightController internally uses:
            //   float throttleInput = hotas.Throttle;
            //   float thrust = throttleInput;   // 0..1
            //   float pitch = hotas.pitch;
            //   float roll  = hotas.roll;
            //   float yaw   = 0f;
            //
            // So we just give it updated self-reference.
            flight.hotas = this;
        }

    }

    // Public accessors (used by FlightController)
    public float Throttle => throttle;
    public float Vertical => vertical;
    public float Strafe => strafe;
    public Vector2 Stick => new Vector2(roll, pitch);

    void ApplyCrossAxisSuppression(ref float p, ref float r)
    {
        float absP = Mathf.Abs(p);
        float absR = Mathf.Abs(r);
        if (absP >= dominantAxisThreshold && absR < crossAxisThreshold)
            r = 0f;
        if (absR >= dominantAxisThreshold && absP < crossAxisThreshold)
            p = 0f;
    }

    void ReadRightStick(out float verticalOut, out float strafeOut)
    {
        verticalOut = 0f;
        strafeOut = 0f;
        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return;
        if (!device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick)) return;
        float dz = rightStickDeadzone;
        // Y: push away from you = up (+), pull toward = down (-)
        float vy = Mathf.Clamp(stick.y, -1f, 1f);
        if (Mathf.Abs(vy) >= dz) { float t = Mathf.InverseLerp(dz, 1f, Mathf.Abs(vy)); verticalOut = t * Mathf.Sign(vy); }
        // X: left = -1, right = +1
        float vx = Mathf.Clamp(stick.x, -1f, 1f);
        if (Mathf.Abs(vx) >= dz) { float t = Mathf.InverseLerp(dz, 1f, Mathf.Abs(vx)); strafeOut = t * Mathf.Sign(vx); }
    }

    void OnGUI()
    {
        if (!showInputMetrics) return;
        int fontSize = Mathf.Clamp(metricsFontSize, 10, 72);
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperLeft;
        float x = 20f, y = 20f, lineH = fontSize * 1.4f;

        string source = keyboardOverrideForTesting ? "Keyboard" : (useSimulatedInput ? "Simulated" : (joystick != null && joystick.IsGrabbed ? "Joystick" : "Joystick (released)"));
        GUI.Label(new Rect(x, y, 400, lineH * 2), $"Input: {source}", style);
        y += lineH * 2;

        // Pitch = W/S. +pitch = W (nose up), -pitch = S (nose down).
        string ws = pitch >= 0.01f ? $"W {pitch:F2}" : (pitch <= -0.01f ? $"S {-pitch:F2}" : "—");
        GUI.Label(new Rect(x, y, 400, lineH), $"W/S (pitch): {ws}   value={pitch:F2}", style);
        y += lineH;

        // Roll = A/D. +roll = D (bank right), -roll = A (bank left).
        string ad = roll >= 0.01f ? $"D {roll:F2}" : (roll <= -0.01f ? $"A {-roll:F2}" : "—");
        GUI.Label(new Rect(x, y, 400, lineH), $"A/D (roll):  {ad}   value={roll:F2}", style);
        y += lineH;

        // Yaw = wrist twist (nose left/right).
        string yawStr = Mathf.Abs(yaw) >= 0.01f ? $"yaw={yaw:F2}" : "—";
        GUI.Label(new Rect(x, y, 400, lineH), $"Yaw (twist): {yawStr}", style);
        y += lineH;

        // Right stick: vertical (up/down) + strafe (left/right).
        string vert = vertical >= 0.01f ? $"Up {vertical:F2}" : (vertical <= -0.01f ? $"Dn {-vertical:F2}" : "—");
        string strf = strafe >= 0.01f ? $"R {strafe:F2}" : (strafe <= -0.01f ? $"L {-strafe:F2}" : "—");
        GUI.Label(new Rect(x, y, 400, lineH), $"R stick: vert={vert}  strafe={strf}", style);
        y += lineH;

        GUI.Label(new Rect(x, y, 500, lineH), "Same values as keyboard → same flight response.", style);
    }
}
