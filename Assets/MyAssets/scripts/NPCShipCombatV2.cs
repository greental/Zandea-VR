using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Core;
using MyAssets.Planet;

// Scene/prefab opt-in replacement for NPCShipAI. FlightController remains the sole physics driver.
[DefaultExecutionOrder(-60)]
[DisallowMultipleComponent]
public sealed class NPCShipCombatV2 : MonoBehaviour, IFloatingOriginListener
{
    public enum Tactic { Patrol, Intercept, AttackRun, TurnToTarget, Evade, Recover }
    [Header("Target and patrol")]
    [SerializeField] Transform target;
    [SerializeField] Transform[] patrolPoints;
    [SerializeField] string targetTag = "Player";
    [SerializeField] float sensorRange = 1200f, sensorHalfAngle = 85f, targetMemory = 6f;
    [Header("Pilot")]
    [SerializeField, Range(0f, 1f)] float aggression = .7f, maneuverability = .7f, accuracy = .8f;
    [SerializeField] float reactionInterval = .35f, preferredDistance = 340f, passDistance = 130f;
    [SerializeField, Range(.05f, 1f)] float steeringSensitivity = .15f;
    [SerializeField] float runCorrectionLimitDegrees = 13f, runCorrectionDegreesPerSecond = 2.5f;
    [SerializeField] float outboundSeconds = 1.25f, outboundSeparation = 280f, maximumInboundSeconds = 25f;
    [SerializeField] float outboundBoostMultiplier = 1.3f, outboundBoostSeconds = 1.5f;
    [SerializeField] float retreatShieldFraction = .25f, returnShieldFraction = .8f;
    [Header("Weapons")]
    [SerializeField] float fireRange = 850f, projectileSpeed = 320f;
    [SerializeField] float laserProjectileSpeed = 320f, kineticProjectileSpeed = 280f;
    [SerializeField] float maxMuzzleAimDegrees = 35f;
    [SerializeField] float burstSeconds = 2.2f, burstPause = .65f;
    [Header("Navigation")]
    [SerializeField] LayerMask obstacleLayers = ~0, terrainLayers = ~0;
    [SerializeField] float probeRadius = 9f, lookAheadSeconds = 2.5f, minimumTerrainClearance = 45f;
    [SerializeField] PlanetDefinition planet;
    [SerializeField] bool drawGizmos = true;

    Rigidbody body, targetBody;
    HOTASController hotas;
    CombatDamageable damageable;
    ShieldSystem shield, targetShield;
    WeaponController[] weapons;
    Quaternion[] neutralMuzzleRotations;
    WeaponHardpoint[] hardpoints;
    ActivePlanetSelector planetSelector;
    PlanetDefinition[] scenePlanets;
    Vector3 patrolAnchor, desiredDirection, avoidanceDirection, lastSeen;
    Vector3 runHeading, runInitialHeading, outboundHeading, outboundStartPosition;
    [SerializeField] Tactic tactic;
    float nextDecision, stateSince, lastSeenTime, burstUntil, nextBurst, sensedShield = 1f, normalSpeedStrength;
    int patrolIndex, passSide = 1;
    bool sawTarget, obstacleAhead;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        hotas = GetComponent<HOTASController>();
        if (hotas != null) normalSpeedStrength = hotas.speedStrength;
        damageable = GetComponent<CombatDamageable>();
        shield = GetComponent<ShieldSystem>();
        weapons = GetComponentsInChildren<WeaponController>(true);
        neutralMuzzleRotations = new Quaternion[weapons.Length];
        for (int i = 0; i < weapons.Length; i++)
            if (weapons[i] != null && weapons[i].Muzzle != null)
                neutralMuzzleRotations[i] = weapons[i].Muzzle.localRotation;
        hardpoints = GetComponentsInChildren<WeaponHardpoint>(true);
        planetSelector = FindFirstObjectByType<ActivePlanetSelector>();
        if (planetSelector == null)
            scenePlanets = FindObjectsByType<PlanetDefinition>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (planet == null && planetSelector != null) planet = planetSelector.ActivePlanet;
        if (target != null) SetTarget(target);
        patrolAnchor = transform.position;
        desiredDirection = transform.forward;
        if (hotas != null) hotas.useSimulatedInput = true;
    }

    void Start()
    {
        ResolveTarget();
        nextDecision = Time.time;
    }

    void ResolveTarget()
    {
        if (target != null) return;
        ShipHUDManager playerHud = FindFirstObjectByType<ShipHUDManager>();
        Rigidbody playerBody = playerHud != null ? playerHud.GetRigidbody() : null;
        if (playerBody != null) { SetTarget(playerBody.transform); return; }
        if (!string.IsNullOrEmpty(targetTag))
        {
            GameObject found = GameObject.FindGameObjectWithTag(targetTag);
            if (found != null) { SetTarget(found.transform); return; }
        }
        if (planetSelector != null && planetSelector.TargetRigidbody != null)
            SetTarget(planetSelector.TargetRigidbody.transform);
    }

    public void SetTarget(Transform value)
    {
        target = value;
        Transform root = target != null ? target.root : null;
        targetBody = target != null ? target.GetComponentInParent<Rigidbody>() : null;
        targetShield = root != null ? root.GetComponentInChildren<ShieldSystem>(true) : null;
    }

    void Update()
    {
        if (body == null || hotas == null || (damageable != null && damageable.IsDestroyed)) return;
        if (Time.time >= nextDecision)
        {
            Decide();
            nextDecision = Time.time + Mathf.Max(.05f, reactionInterval);
        }
        Fly();
        Fire();
    }

    void Decide()
    {
        if (planetSelector != null) planet = planetSelector.ActivePlanet;
        else if (scenePlanets != null)
        {
            planet = null;
            float nearest = 5000f;
            for (int i = 0; i < scenePlanets.Length; i++)
            {
                PlanetDefinition candidate = scenePlanets[i];
                if (candidate == null) continue;
                float altitude = Mathf.Abs(candidate.GetAltitude(transform.position));
                if (altitude < nearest) { nearest = altitude; planet = candidate; }
            }
        }
        if (target == null) ResolveTarget();
        Vector3 toTarget = target != null ? target.position - transform.position : Vector3.zero;
        float distance = toTarget.magnitude;
        sawTarget = target != null && distance < sensorRange &&
            Vector3.Dot(transform.forward, toTarget / Mathf.Max(.01f, distance)) > Mathf.Cos(sensorHalfAngle * Mathf.Deg2Rad);
        if (sawTarget) { lastSeen = target.position; lastSeenTime = Time.time; }
        bool hasContact = target != null && (sawTarget || lastSeenTime > 0f && Time.time - lastSeenTime < targetMemory);
        float shieldLevel = ShieldFraction(shield);
        bool lowShield = shield != null && shieldLevel < retreatShieldFraction;
        if (tactic == Tactic.Recover)
        {
            if (shield == null || shieldLevel >= returnShieldFraction) SetTactic(hasContact ? Tactic.Intercept : Tactic.Patrol);
        }
        else if (lowShield) SetTactic(Tactic.Recover);
        else if (tactic == Tactic.AttackRun)
        {
            float elapsed = Time.time - stateSince;
            float traveled = Vector3.Distance(transform.position, outboundStartPosition);
            if (elapsed >= outboundSeconds && (traveled >= outboundSeparation || elapsed >= outboundSeconds + 3f))
                SetTactic(target != null ? Tactic.TurnToTarget : Tactic.Patrol);
        }
        else if (tactic == Tactic.TurnToTarget)
        {
            if (target == null) SetTactic(Tactic.Patrol);
            else if (Vector3.Angle(transform.forward, toTarget) <= 20f) SetTactic(Tactic.Intercept);
        }
        else if (!hasContact) SetTactic(Tactic.Patrol);
        else if (tactic != Tactic.Intercept)
        {
            if (hasContact) SetTactic(Tactic.Intercept);
        }
        else if (hasContact && Time.time - stateSince > .8f)
        {
            bool passed = Vector3.Dot(transform.forward, toTarget) < 0f && distance < preferredDistance;
            bool close = distance < passDistance;
            bool timedOut = Time.time - stateSince > maximumInboundSeconds;
            if (passed || close || timedOut) SetTactic(Tactic.AttackRun);
        }
        if (tactic == Tactic.Patrol)
        {
            if (patrolPoints != null && patrolPoints.Length > 0 && patrolPoints[patrolIndex] != null &&
                Vector3.Distance(transform.position, patrolPoints[patrolIndex].position) < preferredDistance * .4f)
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
        }
        PlanDirection(distance);
        ProbeObstacles();
    }

    void SetTactic(Tactic next)
    {
        if (next == tactic) return;
        tactic = next; stateSince = Time.time;
        if (next == Tactic.Intercept)
        {
            Vector3 predicted = target != null ? target.position +
                (targetBody != null ? targetBody.linearVelocity : Vector3.zero) *
                Mathf.Clamp(Vector3.Distance(transform.position, target.position) / Mathf.Max(40f, projectileSpeed), 0f, 4.5f)
                : transform.position + transform.forward * 500f;
            runInitialHeading = runHeading = (predicted - transform.position).normalized;
            if (runHeading.sqrMagnitude < .1f) runInitialHeading = runHeading = transform.forward;
        }
        if (next == Tactic.AttackRun)
        {
            passSide = -passSide;
            outboundStartPosition = transform.position;
            outboundHeading = transform.forward;
            if (target != null && Vector3.Dot(outboundHeading, target.position - transform.position) > .3f)
                outboundHeading = (outboundHeading + (transform.position - target.position).normalized * .3f).normalized;
        }
    }

    void PlanDirection(float distance)
    {
        Vector3 up = planet != null ? planet.GetSurfaceUp(transform.position) : Vector3.up;
        Vector3 aim = transform.position + transform.forward * 500f;
        if (tactic == Tactic.Patrol)
        {
            if (patrolPoints != null && patrolPoints.Length > 0 && patrolPoints[patrolIndex] != null)
                aim = patrolPoints[patrolIndex].position;
            else
            {
                Vector3 radial = Vector3.ProjectOnPlane(transform.position - patrolAnchor, up);
                aim = patrolAnchor + Quaternion.AngleAxis(Time.time * 1.5f, up) * transform.forward * preferredDistance * 2f;
                if (radial.magnitude > preferredDistance * 2f) aim = patrolAnchor - radial;
            }
        }
        else if (target != null)
        {
            Vector3 targetVelocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
            float lead = Mathf.Clamp(distance / Mathf.Max(40f, projectileSpeed), 0f, 4.5f) * accuracy;
            Vector3 predicted = target.position + targetVelocity * lead;
            switch (tactic)
            {
                case Tactic.Intercept:
                    Vector3 wanted = (predicted - transform.position).normalized;
                    Vector3 limited = Vector3.RotateTowards(runInitialHeading, wanted,
                        runCorrectionLimitDegrees * Mathf.Lerp(.8f, 1.2f, aggression) * Mathf.Deg2Rad, 0f);
                    runHeading = Vector3.RotateTowards(runHeading, limited,
                        runCorrectionDegreesPerSecond * reactionInterval * Mathf.Deg2Rad, 0f);
                    aim = transform.position + runHeading * 700f;
                    break;
                case Tactic.TurnToTarget: aim = target.position; break;
                case Tactic.AttackRun: aim = transform.position + outboundHeading * 700f; break;
                case Tactic.Evade: aim = transform.position + transform.forward * preferredDistance +
                    Vector3.Cross(up, transform.forward).normalized * passSide * preferredDistance + up * preferredDistance * .25f; break;
                case Tactic.Recover: aim = transform.position + (transform.position - target.position).normalized * preferredDistance * 2f + up * preferredDistance * .2f; break;
            }
        }
        desiredDirection = (aim - transform.position).normalized;
        if (desiredDirection.sqrMagnitude < .01f) desiredDirection = transform.forward;
    }

    void ProbeObstacles()
    {
        obstacleAhead = false;
        avoidanceDirection = Vector3.zero;
        float speed = body.linearVelocity.magnitude;
        float length = Mathf.Clamp(speed * lookAheadSeconds + probeRadius * 2f, 35f, 350f);
        Vector3 direction = Vector3.Slerp(transform.forward, body.linearVelocity.sqrMagnitude > 25f ? body.linearVelocity.normalized : transform.forward, .45f);
        if (Physics.SphereCast(transform.position, probeRadius, direction, out RaycastHit hit, length, obstacleLayers, QueryTriggerInteraction.Ignore) &&
            hit.rigidbody != body && !hit.transform.IsChildOf(transform))
        {
            obstacleAhead = true;
            Vector3 up = planet != null ? planet.GetSurfaceUp(transform.position) : transform.up;
            avoidanceDirection = (hit.normal + up * .6f + Vector3.Cross(up, direction) * passSide * .5f).normalized;
        }
        if (planet != null)
        {
            Vector3 up = planet.GetSurfaceUp(transform.position);
            Vector3 ahead = transform.position + direction * Mathf.Min(length, 150f) + up * minimumTerrainClearance;
            if (Physics.Raycast(ahead, -up, out RaycastHit ground, minimumTerrainClearance * 2.5f, terrainLayers, QueryTriggerInteraction.Ignore) &&
                !ground.transform.IsChildOf(transform) && Vector3.Dot(ground.normal, up) > .2f)
            {
                obstacleAhead = true;
                avoidanceDirection = (up * 2f + desiredDirection).normalized;
            }
        }
    }

    void Fly()
    {
        Vector3 direction = obstacleAhead ? Vector3.Slerp(desiredDirection, avoidanceDirection, .8f) : desiredDirection;
        Vector3 local = transform.InverseTransformDirection(direction);
        if (local.z < -.7f && Mathf.Abs(local.x) < .15f) local.x = passSide * .75f;
        float response = Mathf.Lerp(1.1f, 2.5f, maneuverability) *
            (tactic == Tactic.TurnToTarget ? .8f : steeringSensitivity);
        float yaw = Mathf.Clamp(local.x * response - transform.InverseTransformDirection(body.angularVelocity).y * .2f, -1f, 1f);
        float pitch = Mathf.Clamp(-local.y * response + transform.InverseTransformDirection(body.angularVelocity).x * .2f, -1f, 1f);
        float bank = Mathf.Clamp(-yaw * .7f - transform.InverseTransformDirection(body.angularVelocity).z * .2f, -1f, 1f);
        float blend = 1f - Mathf.Exp(-2.5f * Time.deltaTime);
        hotas.simulatedYaw = Mathf.Lerp(hotas.simulatedYaw, yaw, blend);
        hotas.simulatedPitch = Mathf.Lerp(hotas.simulatedPitch, pitch, blend);
        hotas.simulatedRoll = Mathf.Lerp(hotas.simulatedRoll, bank, blend);
        // FlightController.maxSpeed is the normal top-speed setting; the outbound
        // escape gets a short multiplier before the ship brakes to turn back.
        bool boosting = tactic == Tactic.AttackRun && Time.time - stateSince < outboundBoostSeconds;
        hotas.speedStrength = normalSpeedStrength * (boosting ? Mathf.Max(1f, outboundBoostMultiplier) : 1f);
        float throttle = tactic == Tactic.TurnToTarget ? 0f : obstacleAhead ? .7f : 1f;
        hotas.simulatedThrottle = Mathf.Lerp(hotas.simulatedThrottle, throttle, blend);
    }

    void Fire()
    {
        if (tactic == Tactic.Patrol || tactic == Tactic.Recover || tactic == Tactic.Evade || target == null)
        {
            ResetMuzzles();
            return;
        }
        for (int i = 0; i < hardpoints.Length; i++) hardpoints[i].TrySetDeployed(true);
        Vector3 targetPoint = targetBody != null ? targetBody.worldCenterOfMass : target.position;
        Vector3 relative = targetPoint - transform.position;
        float range = relative.magnitude;
        if (range > fireRange || range < 1f) return;
        Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
        if (targetShield != null) sensedShield = ShieldFraction(targetShield);
        DamageType wanted = sensedShield > .08f ? DamageType.Laser : DamageType.Kinetic;
        float shotSpeed = wanted == DamageType.Laser ? laserProjectileSpeed : kineticProjectileSpeed;
        bool aligned = false;
        for (int i = 0; i < weapons.Length; i++)
        {
            WeaponController candidate = weapons[i];
            if (candidate == null || candidate.WeaponDamageType != wanted || candidate.Muzzle == null) continue;
            Vector3 aim = InterceptPoint(candidate.Muzzle.position, targetPoint, velocity, shotSpeed);
            if (Vector3.Angle(transform.forward, aim - candidate.Muzzle.position) < maxMuzzleAimDegrees)
            {
                aligned = true;
                break;
            }
        }
        if (!aligned) return;
        if (Time.time >= nextBurst) { burstUntil = Time.time + burstSeconds; nextBurst = burstUntil + burstPause; }
        if (Time.time > burstUntil) return;
        for (int i = 0; i < weapons.Length; i++)
        {
            WeaponController weapon = weapons[i];
            if (weapon == null || weapon.WeaponDamageType != wanted || weapon.Muzzle == null) continue;
            Vector3 aim = InterceptPoint(weapon.Muzzle.position, targetPoint, velocity, shotSpeed);
            Vector3 direction = (aim - weapon.Muzzle.position).normalized;
            if (Vector3.Angle(transform.forward, direction) > maxMuzzleAimDegrees) continue;
            weapon.Muzzle.rotation = Quaternion.LookRotation(direction, transform.up);
            weapon.TryFire(); // WeaponController owns cooldown, reload, heat and ammo.
        }
    }

    static Vector3 InterceptPoint(Vector3 muzzle, Vector3 targetPoint, Vector3 targetVelocity, float shotSpeed)
    {
        Vector3 offset = targetPoint - muzzle;
        float speed = Mathf.Max(1f, shotSpeed);
        float a = targetVelocity.sqrMagnitude - speed * speed;
        float b = 2f * Vector3.Dot(offset, targetVelocity);
        float c = offset.sqrMagnitude;
        float time = Mathf.Sqrt(c) / speed;
        float discriminant = b * b - 4f * a * c;
        if (discriminant >= 0f && Mathf.Abs(a) > .0001f)
        {
            float root = Mathf.Sqrt(discriminant);
            float t1 = (-b - root) / (2f * a);
            float t2 = (-b + root) / (2f * a);
            if (t1 > 0f && t2 > 0f) time = Mathf.Min(t1, t2);
            else if (t1 > 0f) time = t1;
            else if (t2 > 0f) time = t2;
        }
        else if (Mathf.Abs(a) <= .0001f && Mathf.Abs(b) > .0001f)
        {
            float linearTime = -c / b;
            if (linearTime > 0f) time = linearTime;
        }
        return targetPoint + targetVelocity * Mathf.Min(time, 8f);
    }

    void ResetMuzzles()
    {
        for (int i = 0; i < weapons.Length; i++)
            if (weapons[i] != null && weapons[i].Muzzle != null)
                weapons[i].Muzzle.localRotation = neutralMuzzleRotations[i];
    }

    static float ShieldFraction(ShieldSystem value)
    {
        if (value == null) return 1f;
        return (value.GetArcNormalized(ShieldArc.Front) + value.GetArcNormalized(ShieldArc.Rear) +
            value.GetArcNormalized(ShieldArc.Left) + value.GetArcNormalized(ShieldArc.Right)) * .25f;
    }

    public void OnOriginShift(Vector3 shift) { patrolAnchor -= shift; lastSeen -= shift; outboundStartPosition -= shift; }

    void OnDisable()
    {
        if (hotas != null) hotas.speedStrength = normalSpeedStrength;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(transform.position, sensorRange);
        Gizmos.color = Color.yellow; Gizmos.DrawRay(transform.position, desiredDirection * 100f);
        if (obstacleAhead) { Gizmos.color = Color.red; Gizmos.DrawRay(transform.position, avoidanceDirection * 100f); }
        if (target != null)
        {
            Gizmos.color = Color.magenta; Gizmos.DrawLine(transform.position, target.position);
            if (Application.isPlaying && weapons != null)
            {
                Vector3 point = targetBody != null ? targetBody.worldCenterOfMass : target.position;
                Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
                float speed = sensedShield > .08f ? laserProjectileSpeed : kineticProjectileSpeed;
                for (int i = 0; i < weapons.Length; i++)
                {
                    if (weapons[i] == null || weapons[i].Muzzle == null) continue;
                    Vector3 aim = InterceptPoint(weapons[i].Muzzle.position, point, velocity, speed);
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(weapons[i].Muzzle.position, aim);
                    Gizmos.DrawWireSphere(aim, 2f);
                }
            }
        }
    }
}
