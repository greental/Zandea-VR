using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class VehicleRouteDriver : MonoBehaviour, MyAssets.Planet.IFloatingOriginListener
{
    public void OnOriginShift(Vector3 shift)
    {
        _lastPosition -= shift;
        _lastGroundPoint -= shift;
    }
    [Serializable]
    public class WheelVisual
    {
        public string id = "Wheel";
        public Transform wheel;
        [Min(0.01f)] public float radius = 0.45f;
        public Vector3 axleLocalAxis = Vector3.right;
        [Tooltip("Optional local steering yaw angle visualized on this wheel.")]
        public bool applySteerYaw;
    }

    [Header("Route")]
    [Tooltip("Optional route root. If assigned and no waypoints are set manually, route uses all direct children in hierarchy order.")]
    public Transform waypointRoot;
    public List<Transform> waypoints = new List<Transform>();
    public bool loopRoute = true;
    [Min(0.1f)] public float waypointReachDistance = 2.5f;
    public bool autoStart = true;

    [Header("Movement")]
    [Min(0f)] public float cruiseSpeed = 12f;
    [Min(0f)] public float maxSpeed = 22f;
    [Min(0.01f)] public float acceleration = 8f;
    [Min(0.01f)] public float deceleration = 10f;
    [Min(1f)] public float turnRateDegrees = 120f;
    [Range(0f, 1f)] public float turnSlowdown = 0.35f;
    [Min(0f)] public float stopAtRouteEndDuration = 0f;

    [Header("Terrain Stick")]
    [Tooltip("Colliders used as drivable ground.")]
    public LayerMask groundLayers = ~0;
    [Min(0.5f)] public float terrainProbeHeight = 4f;
    [Min(0.5f)] public float terrainProbeDistance = 12f;
    [Min(0f)] public float groundOffset = 0.08f;
    [Min(0.01f)] public float stickToGroundSharpness = 10f;
    [Range(0f, 80f)] public float maxTraversableSlope = 45f;

    [Header("Uphill Handling")]
    [Tooltip("How much uphill slope reduces target speed.")]
    [Range(0f, 1f)] public float uphillSlowdownStrength = 0.55f;
    [Tooltip("Extra slowdown from very steep inclines near max traversable slope.")]
    [Range(0f, 1f)] public float steepClimbExtraSlowdown = 0.25f;

    [Header("Wheel Visuals")]
    public List<WheelVisual> wheelVisuals = new List<WheelVisual>();
    [Min(0f)] public float maxSteerVisualAngle = 24f;

    [Header("Audio")]
    public AudioSource engineSource;
    public AudioClip engineLoopClip;
    [Range(0f, 1f)] public float minEngineVolume = 0.1f;
    [Range(0f, 1f)] public float maxEngineVolume = 0.85f;
    [Range(0.3f, 3f)] public float minEnginePitch = 0.85f;
    [Range(0.3f, 3f)] public float maxEnginePitch = 1.5f;
    [Range(0f, 1f)] public float uphillPitchBoost = 0.12f;
    [Min(0.01f)] public float audioResponse = 6f;

    [Header("Debug")]
    public bool drawRouteGizmos = true;

    protected float CurrentSpeed => _currentSpeed;
    protected Vector3 GroundNormal => _groundNormal;
    protected int CurrentWaypointIndex => _currentWaypointIndex;

    float _currentSpeed;
    int _currentWaypointIndex;
    float _routeEndStopTimer;
    bool _isDriving;
    bool _isGrounded;
    Vector3 _groundNormal = Vector3.up;
    Vector3 _lastPosition;
    readonly Dictionary<Transform, Quaternion> _wheelBaseLocalRot = new Dictionary<Transform, Quaternion>();
    float _smoothedAudioNormSpeed;
    float _smoothedAudioUphill;

    protected virtual void Reset()
    {
        RefreshWaypointsFromRootIfEmpty();
    }

    protected virtual void Awake()
    {
        RefreshWaypointsFromRootIfEmpty();
        CacheWheelBaseRotations();
        _lastPosition = transform.position;
    }

    protected virtual void Start()
    {
        if (autoStart)
            BeginDriving();
    }

    protected virtual void OnValidate()
    {
        maxSpeed = Mathf.Max(maxSpeed, cruiseSpeed);
        RefreshWaypointsFromRootIfEmpty();
        CacheWheelBaseRotations();
    }

    protected virtual void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        if (_isDriving)
            TickDriving(dt);

        TickWheelVisuals(dt);
        TickAudio(dt);

        _lastPosition = transform.position;
    }

    public void BeginDriving()
    {
        _isDriving = waypoints != null && waypoints.Count > 0;
        if (_isDriving)
            _currentWaypointIndex = Mathf.Clamp(_currentWaypointIndex, 0, waypoints.Count - 1);
    }

    public void StopDriving()
    {
        _isDriving = false;
        _currentSpeed = 0f;
    }

    public void SetWaypoints(List<Transform> route, bool restart = true)
    {
        waypoints = route ?? new List<Transform>();
        _currentWaypointIndex = 0;
        if (restart)
            BeginDriving();
    }

    public void SetWaypointRoot(Transform root, bool rebuildNow = true)
    {
        waypointRoot = root;
        if (rebuildNow)
            RefreshWaypointsFromRootIfEmpty(force: true);
    }

    void TickDriving(float dt)
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            StopDriving();
            return;
        }

        Transform target = waypoints[_currentWaypointIndex];
        if (target == null)
        {
            AdvanceWaypoint();
            return;
        }

        ProbeGround();
        Vector3 targetPos = target.position;
        Vector3 toTarget = targetPos - transform.position;
        Vector3 planarToTarget = Vector3.ProjectOnPlane(toTarget, _groundNormal);
        float planarDistance = planarToTarget.magnitude;

        if (planarDistance <= waypointReachDistance)
        {
            AdvanceWaypoint();
            return;
        }

        Vector3 desiredForward = planarDistance > 0.001f ? planarToTarget / planarDistance : transform.forward;
        float turnDot = Mathf.Clamp01(Vector3.Dot(transform.forward, desiredForward));
        float cornerSlowdownFactor = Mathf.Lerp(turnSlowdown, 1f, turnDot);

        float slopeAngle = _isGrounded ? Vector3.Angle(_groundNormal, Vector3.up) : 0f;
        float uphill01 = Mathf.Clamp01(Vector3.Dot(desiredForward, Vector3.up));
        float slopeFactor = 1f - (uphill01 * uphillSlowdownStrength);
        float steepFactor = 1f - (Mathf.InverseLerp(0f, maxTraversableSlope, slopeAngle) * steepClimbExtraSlowdown);
        float inclineFactor = Mathf.Clamp01(slopeFactor * steepFactor);

        float desiredSpeed = Mathf.Min(cruiseSpeed, maxSpeed) * cornerSlowdownFactor * inclineFactor;
        float speedRate = desiredSpeed >= _currentSpeed ? acceleration : deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, desiredSpeed, speedRate * dt);

        Vector3 moveDir = Vector3.ProjectOnPlane(desiredForward, _groundNormal).normalized;
        if (moveDir.sqrMagnitude < 0.0001f)
            moveDir = transform.forward;

        Vector3 pos = transform.position;
        Vector3 nextPos = pos + moveDir * (_currentSpeed * dt);

        if (_isGrounded)
        {
            float targetY = (_lastGroundPoint + _groundNormal * groundOffset).y;
            nextPos.y = Mathf.Lerp(pos.y, targetY, 1f - Mathf.Exp(-stickToGroundSharpness * dt));
        }

        transform.position = nextPos;

        Quaternion desiredRot = Quaternion.LookRotation(moveDir, _groundNormal);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRot, turnRateDegrees * dt);

        _smoothedAudioUphill = Mathf.Lerp(_smoothedAudioUphill, uphill01, 1f - Mathf.Exp(-audioResponse * dt));
    }

    Vector3 _lastGroundPoint;
    void ProbeGround()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * terrainProbeHeight;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, terrainProbeHeight + terrainProbeDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            _isGrounded = true;
            _groundNormal = hit.normal;
            _lastGroundPoint = hit.point;

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope > maxTraversableSlope)
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, deceleration * Time.deltaTime);
        }
        else
        {
            _isGrounded = false;
            _groundNormal = Vector3.up;
        }
    }

    void AdvanceWaypoint()
    {
        if (waypoints == null || waypoints.Count == 0)
            return;

        if (_currentWaypointIndex < waypoints.Count - 1)
        {
            _currentWaypointIndex++;
            return;
        }

        if (loopRoute)
        {
            _currentWaypointIndex = 0;
            return;
        }

        if (stopAtRouteEndDuration <= 0f)
        {
            StopDriving();
            return;
        }

        _routeEndStopTimer += Time.deltaTime;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, deceleration * Time.deltaTime);
        if (_routeEndStopTimer >= stopAtRouteEndDuration)
        {
            _routeEndStopTimer = 0f;
            _currentWaypointIndex = 0;
            BeginDriving();
        }
    }

    void TickWheelVisuals(float dt)
    {
        if (wheelVisuals == null || wheelVisuals.Count == 0)
            return;

        float distanceMoved = Vector3.Distance(_lastPosition, transform.position);
        float sign = Vector3.Dot(transform.forward, transform.position - _lastPosition) >= 0f ? 1f : -1f;
        float steerYaw = 0f;

        if (_isDriving && waypoints != null && waypoints.Count > 0 && waypoints[_currentWaypointIndex] != null)
        {
            Vector3 toTarget = waypoints[_currentWaypointIndex].position - transform.position;
            Vector3 desiredDir = Vector3.ProjectOnPlane(toTarget, _groundNormal).normalized;
            Vector3 localDesired = transform.InverseTransformDirection(desiredDir);
            steerYaw = Mathf.Clamp(Mathf.Atan2(localDesired.x, Mathf.Max(0.0001f, localDesired.z)) * Mathf.Rad2Deg, -maxSteerVisualAngle, maxSteerVisualAngle);
        }

        for (int i = 0; i < wheelVisuals.Count; i++)
        {
            WheelVisual wheel = wheelVisuals[i];
            if (wheel == null || wheel.wheel == null || wheel.radius <= 0.001f)
                continue;

            if (!_wheelBaseLocalRot.TryGetValue(wheel.wheel, out Quaternion baseRot))
            {
                baseRot = wheel.wheel.localRotation;
                _wheelBaseLocalRot[wheel.wheel] = baseRot;
            }

            float deg = (distanceMoved / (2f * Mathf.PI * wheel.radius)) * 360f * sign;
            Vector3 axis = wheel.axleLocalAxis.sqrMagnitude > 0.0001f ? wheel.axleLocalAxis.normalized : Vector3.right;

            Quaternion steerRot = wheel.applySteerYaw ? Quaternion.Euler(0f, steerYaw, 0f) : Quaternion.identity;
            Quaternion rollRot = Quaternion.AngleAxis(deg, axis);
            wheel.wheel.localRotation = baseRot * steerRot * rollRot;
        }
    }

    void TickAudio(float dt)
    {
        if (engineSource == null)
            return;

        if (engineSource.clip == null && engineLoopClip != null)
        {
            engineSource.clip = engineLoopClip;
            engineSource.loop = true;
        }

        float normSpeed = maxSpeed > 0.01f ? Mathf.Clamp01(_currentSpeed / maxSpeed) : 0f;
        _smoothedAudioNormSpeed = Mathf.Lerp(_smoothedAudioNormSpeed, normSpeed, 1f - Mathf.Exp(-audioResponse * dt));

        float targetVolume = Mathf.Lerp(minEngineVolume, maxEngineVolume, _smoothedAudioNormSpeed);
        float targetPitch = Mathf.Lerp(minEnginePitch, maxEnginePitch, _smoothedAudioNormSpeed) + (_smoothedAudioUphill * uphillPitchBoost);

        engineSource.volume = targetVolume;
        engineSource.pitch = targetPitch;

        if (targetVolume > 0.01f)
        {
            if (!engineSource.isPlaying && engineSource.clip != null)
                engineSource.Play();
        }
        else if (engineSource.isPlaying)
        {
            engineSource.Stop();
        }
    }

    protected virtual void RefreshWaypointsFromRootIfEmpty(bool force = false)
    {
        if (waypointRoot == null)
            return;
        if (!force && waypoints != null && waypoints.Count > 0)
            return;

        if (waypoints == null)
            waypoints = new List<Transform>();
        else
            waypoints.Clear();

        for (int i = 0; i < waypointRoot.childCount; i++)
            waypoints.Add(waypointRoot.GetChild(i));
    }

    void CacheWheelBaseRotations()
    {
        _wheelBaseLocalRot.Clear();
        if (wheelVisuals == null)
            return;

        for (int i = 0; i < wheelVisuals.Count; i++)
        {
            WheelVisual wheel = wheelVisuals[i];
            if (wheel != null && wheel.wheel != null)
                _wheelBaseLocalRot[wheel.wheel] = wheel.wheel.localRotation;
        }
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (!drawRouteGizmos || waypoints == null || waypoints.Count == 0)
            return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < waypoints.Count; i++)
        {
            Transform wp = waypoints[i];
            if (wp == null)
                continue;
            Gizmos.DrawSphere(wp.position, 0.45f);

            int next = i + 1;
            if (next >= waypoints.Count)
            {
                if (!loopRoute)
                    continue;
                next = 0;
            }

            Transform wpNext = waypoints[next];
            if (wpNext != null)
                Gizmos.DrawLine(wp.position, wpNext.position);
        }
    }
}
