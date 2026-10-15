using System;
using System.Collections.Generic;
using MyAssets.Planet;
using UnityEngine;
using UnityEngine.XR.Content.Interaction;
/// <summary>
/// Add to the ship root (same GameObject as <see cref="Rigidbody"/> or a child of it). Wires flight, HOTAS, power, HUD, audio, seat, and joystick visuals.
/// Put <see cref="XRHandAttachAutoSetup"/> on the <b>XR Origin</b> so L/R <c>XRDirectInteractor</c> attach points (palm / fake hand) are set for stick + lever grabs.
/// Does not change flight math — only assigns references and adds missing behaviour components.
/// </summary>
[DefaultExecutionOrder(-500)]
public class ShipAutoSetup : MonoBehaviour
{
    [Tooltip("If set, used as the physics root; otherwise first Rigidbody on this object or in children.")]
    public Rigidbody rigidbodyOverride;

    [Tooltip("Where to place FlightController + HOTASController if missing. Defaults to Rigidbody's GameObject.")]
    public Transform flightSystemsHost;

    [Header("Optional name hints (leave empty to use defaults)")]
    public string[] seatAnchorNameHints = { "SeatAnchor", "PilotChairPos", "seatAnchor", "Seat", "ChairAnchor" };

    [Header("Unified tuning")]
    [Tooltip("Optional: apply the same HOTAS / flight gains everywhere. Assign after creating Assets → Create → MyAssets → Ship Flight Profile.")]
    public ShipFlightProfile flightProfile;

    [Header("Debug")]
    public bool logToConsole = true;

    static void Log(bool enabled, string msg)
    {
        if (enabled) Debug.Log("[ShipAutoSetup] " + msg);
    }

    void Awake()
    {
        RunSetup();
    }

    [ContextMenu("Run Setup Now")]
    void RunSetupContextMenu()
    {
        RunSetup();
    }

    public void RunSetup()
    {
        var rb = rigidbodyOverride != null ? rigidbodyOverride : GetComponent<Rigidbody>();
        if (rb == null)
        {
            // A loose tool/module under a static ship mesh is not the ship's hull.
            foreach(var candidate in GetComponentsInChildren<Rigidbody>(true))
                if(!candidate.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>())
                { rb=candidate; break; }
        }
        if(rb && rb.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>())
            return;
        if (rb == null)
        {
            Log(logToConsole, "No Rigidbody found — assign rigidbodyOverride or add a Rigidbody to the ship.");
            return;
        }

        Transform systemsRoot = flightSystemsHost != null ? flightSystemsHost : rb.transform;

        var fc = rb.GetComponentInChildren<FlightController>(true);
        if (fc == null)
            fc = systemsRoot.gameObject.AddComponent<FlightController>();
        if (fc.rb == null)
            fc.rb = rb;

        var hotas = rb.GetComponentInChildren<HOTASController>(true);
        if (hotas == null)
            hotas = systemsRoot.gameObject.AddComponent<HOTASController>();
        if (fc.hotas == null) fc.hotas = hotas;
        if (hotas.flight == null) hotas.flight = fc;

        var joysticks = rb.GetComponentsInChildren<HOTASJoystick>(true);
        if (joysticks.Length > 0 && hotas.joystick == null)
            hotas.joystick = PickPilotJoystick(joysticks, rb.transform);

        var levers = rb.GetComponentsInChildren<EngineLever>(true);
        if (levers.Length > 0 && hotas.engineLever == null)
            hotas.engineLever = PickClosest(levers, hotas.joystick != null ? hotas.joystick.transform : rb.transform);

        var hud = rb.GetComponentInChildren<ShipHUDManager>(true);
        if (hud == null)
            hud = systemsRoot.gameObject.AddComponent<ShipHUDManager>();
        if (hud.flight == null) hud.flight = fc;
        if (hud.hotas == null) hud.hotas = hotas;

        WireActivePlanetAltitude(rb, hud);

        var power = rb.GetComponentInChildren<ShipPowerController>(true);
        if (power == null)
            power = rb.gameObject.AddComponent<ShipPowerController>();
        if (power.hudManager == null) power.hudManager = hud;
        if (power.flightController == null) power.flightController = fc;
        if (power.hotasController == null) power.hotasController = hotas;
        AssignPowerSwitches(power, rb.GetComponentsInChildren<XRFlipSwitch>(true));

        var gear = rb.GetComponentInChildren<LandingGearController>(true);
        if (gear == null)
            gear = rb.gameObject.AddComponent<LandingGearController>();
        if (fc.landingGear == null)
            fc.landingGear = gear;

        var audio = rb.GetComponentInChildren<ShipAudioController>(true);
        if (audio == null)
            audio = rb.gameObject.AddComponent<ShipAudioController>();
        if (audio.powerController == null) audio.powerController = power;
        if (audio.hotasController == null) audio.hotasController = hotas;
        if (audio.flightController == null) audio.flightController = fc;
        if (audio.landingGearController == null) audio.landingGearController = gear;

        WireJoystickVisuals(rb, hotas.joystick);
        WireSitInCockpits(rb, rb.transform);

        if (flightProfile != null)
            ShipFlightProfileBinder.ApplyToHierarchy(rb.gameObject, flightProfile);

        Log(logToConsole, $"Wired ship on '{rb.name}': FC, HOTAS, HUD, power, gear, audio.");
    }

    void WireActivePlanetAltitude(Rigidbody rb, ShipHUDManager hud)
    {
        if (rb == null || hud == null)
            return;

        ActivePlanetSelector selector = FindFirstObjectByType<ActivePlanetSelector>();
        if (selector != null)
        {
            selector.ConfigureTarget(rb.transform, rb);

            PlanetSystemRig[] rigs = selector.PlanetRigs;
            if (rigs == null || rigs.Length == 0)
                selector.RefreshPlanetRigs();
        }

        AltitudeIndicatorHUD altitudeHud = hud.GetComponent<AltitudeIndicatorHUD>();
        if (altitudeHud == null)
            altitudeHud = hud.gameObject.AddComponent<AltitudeIndicatorHUD>();

        if (altitudeHud.hudManager == null)
            altitudeHud.hudManager = hud;
        if (altitudeHud.positionSource == null)
            altitudeHud.positionSource = rb.transform;
        if (altitudeHud.activePlanetSelector == null)
            altitudeHud.activePlanetSelector = selector;
    }

    static HOTASJoystick PickPilotJoystick(HOTASJoystick[] joysticks, Transform shipRoot)
    {
        if (joysticks.Length == 1)
            return joysticks[0];
        HOTASJoystick best = null;
        float bestScore = float.NegativeInfinity;
        foreach (var j in joysticks)
        {
            if (j == null) continue;
            string n = j.gameObject.name.ToLowerInvariant();
            float score = 0f;
            if (n.Contains("copilot") || n.Contains("co-pilot") || n.Contains("right seat"))
                score -= 100f;
            if (n.Contains("pilot") || n.Contains("left") || n.Contains("captain"))
                score += 50f;
            if (score > bestScore)
            {
                bestScore = score;
                best = j;
            }
        }
        return best != null ? best : joysticks[0];
    }

    static EngineLever PickClosest(EngineLever[] levers, Transform from)
    {
        EngineLever best = levers[0];
        float bestD = float.MaxValue;
        foreach (var l in levers)
        {
            if (l == null) continue;
            float d = (l.transform.position - from.position).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = l;
            }
        }
        return best;
    }

    static void AssignPowerSwitches(ShipPowerController power, XRFlipSwitch[] all)
    {
        if (all == null || all.Length == 0)
            return;

        var list = new List<XRFlipSwitch>(all);
        XRFlipSwitch master = null;
        foreach (var s in list)
        {
            if (s == null) continue;
            string n = s.gameObject.name.ToLowerInvariant();
            if (n.Contains("24") || n.Contains("24v") || (n.Contains("master") && n.Contains("hud")))
            {
                master = s;
                break;
            }
        }

        var engines = new List<XRFlipSwitch>();
        foreach (var s in list)
        {
            if (s == null || s == master) continue;
            string n = s.gameObject.name.ToLowerInvariant();
            if (n.Contains("engine") || n.Contains("arm") || n.Contains("propulsion") || n.Contains("motor"))
                engines.Add(s);
        }

        if (master == null && list.Count > 0)
            master = list[0];

        if (engines.Count >= 2)
        {
            engines.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.OrdinalIgnoreCase));
            if (power.master24VSwitch == null) power.master24VSwitch = master;
            if (power.engineSwitchA == null) power.engineSwitchA = engines[0];
            if (power.engineSwitchB == null) power.engineSwitchB = engines[1];
            return;
        }

        if (engines.Count == 1)
        {
            if (power.master24VSwitch == null) power.master24VSwitch = master;
            if (power.engineSwitchA == null) power.engineSwitchA = engines[0];
            if (power.engineSwitchB == null) power.engineSwitchB = engines[0];
            return;
        }

        var remainder = new List<XRFlipSwitch>();
        foreach (var s in list)
        {
            if (s != null && s != master)
                remainder.Add(s);
        }

        if (power.master24VSwitch == null) power.master24VSwitch = master;
        if (remainder.Count >= 2)
        {
            remainder.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.OrdinalIgnoreCase));
            if (power.engineSwitchA == null) power.engineSwitchA = remainder[0];
            if (power.engineSwitchB == null) power.engineSwitchB = remainder[1];
        }
        else if (remainder.Count == 1)
        {
            if (power.engineSwitchA == null) power.engineSwitchA = remainder[0];
            if (power.engineSwitchB == null) power.engineSwitchB = remainder[0];
        }
    }

    void WireJoystickVisuals(Rigidbody rb, HOTASJoystick primaryJoystick)
    {
        var mines = rb.GetComponentsInChildren<XRJoystickMine>(true);
        foreach (var mine in mines)
        {
            if (mine == null) continue;
            mine.EnsureReferenceFrame(rb.transform);
            if (mine.hotasJoystick == null)
            {
                if (primaryJoystick != null)
                    mine.hotasJoystick = primaryJoystick;
                else
                {
                    var hj = mine.GetComponent<HOTASJoystick>() ?? mine.GetComponentInChildren<HOTASJoystick>(true);
                    if (hj != null)
                        mine.hotasJoystick = hj;
                }
            }
        }
    }

    void WireSitInCockpits(Rigidbody rb, Transform shipRoot)
    {
        var seats = rb.GetComponentsInChildren<SitInCockpit>(true);
        foreach (var seat in seats)
        {
            if (seat == null) continue;
            if (seat.seatAnchor == null)
                seat.seatAnchor = FindSeatAnchor(shipRoot);
        }
    }

    Transform FindSeatAnchor(Transform shipRoot)
    {
        if (seatAnchorNameHints == null || seatAnchorNameHints.Length == 0)
            seatAnchorNameHints = new[] { "SeatAnchor", "PilotChairPos", "seatAnchor" };
        foreach (var hint in seatAnchorNameHints)
        {
            if (string.IsNullOrEmpty(hint)) continue;
            var t = FindChildRecursive(shipRoot, hint);
            if (t != null) return t;
        }
        return null;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var f = FindChildRecursive(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }
}
