using MyAssets.Planet;
using UnityEngine;

// Opt-in ground clearance for V2 enemy ships. Their existing pilot still owns the flight path.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody), typeof(NPCShipCombatV2))]
public sealed class NPCShipTerrainRecoveryV3 : MonoBehaviour
{
    [SerializeField, Min(5f)] float targetClearance = 65f;
    [SerializeField, Min(1f)] float emergencyClearance = 18f;
    [SerializeField, Min(.1f)] float lookAheadSeconds = 2.5f;
    [SerializeField, Min(50f)] float groundSearchDistance = 550f;
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField, Min(0f)] float hoverSpring = 2.2f;
    [SerializeField, Min(0f)] float hoverDamping = 3f;
    [SerializeField, Min(1f)] float maxLiftAcceleration = 180f;
    [SerializeField] PlanetDefinition planet;

    readonly RaycastHit[] hits = new RaycastHit[32];
    Rigidbody body;
    BoxCollider hull;
    HOTASController hotas;
    ActivePlanetSelector selector;
    PlanetDefinition[] scenePlanets;
    float nextPlanetCheck, jammedSince;
    bool escape;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        hull = GetComponent<BoxCollider>();
        hotas = GetComponent<HOTASController>();
        selector = FindFirstObjectByType<ActivePlanetSelector>();
        scenePlanets = FindObjectsByType<PlanetDefinition>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    }

    void Update()
    {
        if (hotas == null || !hotas.useSimulatedInput) return;
        if (!escape) { hotas.simulatedVertical = 0f; return; }
        Vector3 up = planet.GetSurfaceUp(body.position);
        Vector3 climb = (Vector3.ProjectOnPlane(transform.forward, up) * .45f + up).normalized;
        Vector3 local = transform.InverseTransformDirection(climb);
        hotas.simulatedYaw = Mathf.Clamp(local.x * 1.2f, -1f, 1f);
        hotas.simulatedPitch = Mathf.Clamp(-local.y * 1.2f, -1f, 1f);
        hotas.simulatedRoll = 0f;
        hotas.simulatedVertical = Mathf.Sign(Vector3.Dot(transform.up, up));
        hotas.simulatedThrottle = Mathf.Min(hotas.simulatedThrottle, .3f);
    }

    void FixedUpdate()
    {
        if (body == null || body.isKinematic) return;
        SelectPlanet();
        if (planet == null) { escape = false; return; }
        Vector3 up = planet.GetSurfaceUp(body.position);
        float hullRadius = hull != null ? Vector3.Dot(hull.bounds.extents, Abs(up)) : 5f;
        Vector3 forward = Vector3.ProjectOnPlane(body.linearVelocity, up);
        Vector3 future = body.position + forward * lookAheadSeconds;
        bool currentFound = FindGround(body.position, up, hullRadius, out float currentClearance);
        bool futureFound = FindGround(future, up, hullRadius, out float futureClearance);
        if (!currentFound && !futureFound) { escape = false; jammedSince = 0f; return; }

        float clearance = currentFound ? currentClearance : float.PositiveInfinity;
        float predicted = futureFound ? futureClearance : float.PositiveInfinity;
        float verticalSpeed = Vector3.Dot(body.linearVelocity, up);
        float error = targetClearance - Mathf.Min(clearance, predicted);
        if (error > 0f)
        {
            float lift = Mathf.Clamp(error * hoverSpring - verticalSpeed * hoverDamping, 0f, maxLiftAcceleration);
            body.AddForce(up * lift, ForceMode.Acceleration);
        }

        escape = clearance < targetClearance * .7f || predicted < targetClearance * .7f;
        if (clearance < targetClearance * .6f)
            body.AddForce(-forward * 2f, ForceMode.Acceleration);
        if (clearance < emergencyClearance && verticalSpeed < 0f)
            body.linearVelocity -= up * verticalSpeed;

        bool jammed = clearance < -2f || clearance < emergencyClearance && body.linearVelocity.magnitude < 3f;
        if (!jammed) { jammedSince = 0f; return; }
        if (jammedSince <= 0f) jammedSince = Time.fixedTime;
        if (Time.fixedTime - jammedSince < .35f) return;
        // Contact resolution can hold a ship below a steep terrain triangle. Release it once wedged.
        body.position += up * Mathf.Clamp(emergencyClearance - clearance, 0f, 35f);
        body.linearVelocity += up * Mathf.Max(0f, 8f - Vector3.Dot(body.linearVelocity, up));
        jammedSince = 0f;
    }

    bool FindGround(Vector3 point, Vector3 up, float hullRadius, out float clearance)
    {
        clearance = float.PositiveInfinity;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, up).normalized;
        Vector3 fore = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        float x = hull != null ? hull.bounds.extents.magnitude * .55f : 5f;
        float z = x;
        bool found = false;
        for (int sample = 0; sample < 5; sample++)
        {
            Vector3 offset = sample == 0 ? Vector3.zero :
                right * (sample == 1 || sample == 2 ? -x : x) +
                fore * (sample == 1 || sample == 3 ? -z : z);
            Vector3 origin = point + offset + up * (groundSearchDistance * .5f);
            int count = Physics.RaycastNonAlloc(origin, -up, hits, groundSearchDistance,
                groundLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody == body ||
                    hit.collider.transform.IsChildOf(transform) || Vector3.Dot(hit.normal, up) < .35f)
                    continue;
                // A terrain or static surface is a valid floor. Moving ships are obstacles, not ground.
                if (hit.rigidbody != null) continue;
                clearance = Mathf.Min(clearance, Vector3.Dot(point - hit.point, up) - hullRadius);
                found = true;
            }
        }
        return found;
    }

    void SelectPlanet()
    {
        if (Time.fixedTime < nextPlanetCheck) return;
        nextPlanetCheck = Time.fixedTime + .5f;
        if (selector != null && selector.ActivePlanet != null)
        {
            planet = selector.ActivePlanet;
            return;
        }
        float nearest = 5000f;
        planet = null;
        for (int i = 0; i < scenePlanets.Length; i++)
        {
            PlanetDefinition candidate = scenePlanets[i];
            if (candidate == null) continue;
            float altitude = Mathf.Abs(candidate.GetAltitude(body.position));
            if (altitude < nearest) { nearest = altitude; planet = candidate; }
        }
    }

    static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
}
