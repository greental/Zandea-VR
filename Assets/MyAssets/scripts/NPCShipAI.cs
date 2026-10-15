using System.Collections.Generic;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Core;

/// <summary>
/// How direct children under <see cref="NPCShipAI.fleeRouteWaypointRoot"/> are ordered when building the flee polyline.
/// HierarchyOrder = sibling order in the Hierarchy. NameAscending = alphabetical (use zero-padded names if needed, e.g. wp (01), wp (02)).
/// </summary>
public enum NPCShipFleeRouteChildOrder
{
    HierarchyOrder,
    NameAscending
}

/// <summary>
/// Inspector-tuned finite-state AI for a single enemy ship. Drives <see cref="HOTASController"/> simulated
/// input and calls <see cref="WeaponController.TryFire"/> in bursts. Integrates with existing flight, weapons,
/// and damage; does not replace destruction logic.
/// </summary>
/// <remarks>
/// <b>Setup (vertical slice)</b>
/// <list type="number">
/// <item>Duplicate your player ship prefab as the enemy; keep <see cref="Rigidbody"/>, <see cref="FlightController"/>, <see cref="HOTASController"/>.</item>
/// <item>On HOTAS: enable <b>Use Simulated Input</b> (or leave off — this component forces it at runtime).</item>
/// <item>Add this script; assign <b>Rigidbody</b>, <b>HOTASController</b>, optional <b>FlightController</b> (for gizmos only),
/// one or more <see cref="WeaponController"/>, optional <see cref="CombatDamageable"/> or <see cref="ExplodableDamageable"/> for flee/death,
/// optional <see cref="WeaponHardpoint"/> (auto-deploys when engaging).</item>
/// <item>Enemy weapons: set <b>Aim From Head</b> off on <see cref="WeaponController"/> so shots follow the nose/muzzle.</item>
/// <item>If the ship has <see cref="ShipPowerController"/>, ensure weapons are allowed (e.g. HUD powered) or remove power from the AI prefab.</item>
/// <item>Tag your player <c>Player</c> (or set <see cref="targetTag"/> / manual <see cref="manualTarget"/>).</item>
/// <item><b>First test:</b> detection 1200, cone 80°, fire range 700, attack band 100–350 m, breakaway 45 m, dot 0.94,
/// burst 0.45 s / cooldown 1.2 s, flee hull 0.28, lost target 6 s.</item>
/// </list>
/// </remarks>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public class NPCShipAI : MonoBehaviour, MyAssets.Planet.IFloatingOriginListener
{
    public void OnOriginShift(Vector3 shift)
    {
        if (_patrolAnchorInitialized) _patrolAnchorPosition -= shift;
        if (_currentPatrolPoint != Vector3.zero) _currentPatrolPoint -= shift;
        if (currentFleeLookaheadPoint != Vector3.zero) currentFleeLookaheadPoint -= shift;
        if (currentDownProbeHit) currentGroundHeightBelow -= shift.y;
        if (currentForwardDownProbeHit) currentForwardGroundHeight -= shift.y;
    }
    public enum NPCShipState
    {
        Patrol,
        Chase,
        Attack,
        BreakAway,
        Flee,
        Dead
    }

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private HOTASController hotas;
    [SerializeField] private Transform aimOrigin;
    [SerializeField] private CombatDamageable combatDamageable;
    [SerializeField] private ExplodableDamageable explodableDamageable;
    [SerializeField] private WeaponController[] weapons;
    [SerializeField] private WeaponHardpoint weaponHardpoint;
    [Tooltip("Optional explicit hardpoints to deploy for AI firing. If empty, all child hardpoints are auto-used.")]
    [SerializeField] private WeaponHardpoint[] weaponHardpoints;
    [Tooltip("Optional; used for flee shield % and engagement damage detection. Auto-found if unset.")]
    [SerializeField] private ShieldSystem shieldSystem;

    [Header("Target")]
    [SerializeField] private Transform manualTarget;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private bool findTargetByTagOnStart = true;
    [Tooltip("If assigned, prefer this transform's Rigidbody root or parent ship root instead of chasing a child transform directly.")]
    [SerializeField] private bool resolveManualTargetToRigidbodyRoot = true;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 1200f;
    [SerializeField] [Range(10f, 170f)] private float viewConeAngleDegrees = 80f;
    [SerializeField] private bool useLineOfSightRaycast = true;
    [SerializeField] private LayerMask lineOfSightMask = ~0;
    [SerializeField] private float lostTargetTimeoutSeconds = 6f;
    [Tooltip("After entering combat or taking damage, keep the target 'sticky' for this long even if LOS flickers.")]
    [SerializeField] private float combatTargetMemorySeconds = 4f;

    [Header("Engagement (defer chase until close or hit)")]
    [Tooltip("If true, Patrol does not chase on long-range sight alone. Chase begins when the player enters Patrol Engagement Range, or after hull/shield takes damage (any source).")]
    [SerializeField] private bool deferEngagementUntilProximityOrDamage;
    [Tooltip("Distance to player (omnidirectional) to start combat when deferring engagement.")]
    [SerializeField] private float patrolEngagementRange = 350f;
    [Tooltip("When returning to Patrol after lost target, clear damage latch so the ship can wait again.")]
    [SerializeField] private bool clearAggroLatchOnLostTarget = true;

    [Header("Combat ranges (meters)")]
    [SerializeField] private float fireRange = 700f;
    [SerializeField] private float preferredDistanceMin = 100f;
    [SerializeField] private float preferredDistanceMax = 350f;
    [SerializeField] private float breakawayDistance = 45f;
    [Tooltip("Must stay in Attack this long before BreakAway can trigger (reduces flicker).")]
    [SerializeField] private float minTimeInAttackBeforeBreakAway = 0.35f;
    [Tooltip("After BreakAway, wait this long before Attack can resume.")]
    [SerializeField] private float attackReentryCooldownSeconds = 0.75f;
    [Tooltip("After entering Chase, wait before switching to Attack.")]
    [SerializeField] private float minTimeInChaseBeforeAttack = 0.4f;

    [Header("Aim")]
    [SerializeField] [Range(0.5f, 0.999f)] private float aimDotThreshold = 0.94f;
    [SerializeField] private float aimNoiseAmplitude = 0.025f;
    [SerializeField] private float aimNoiseFrequency = 0.35f;

    [Header("Burst fire")]
    [SerializeField] private float burstDurationSeconds = 0.45f;
    [SerializeField] private float burstCooldownSeconds = 1.2f;
    [SerializeField] private float gatlingBurstDurationSeconds = 1.6f;
    [SerializeField] private float gatlingBurstCooldownSeconds = 0.9f;
    [SerializeField] [Range(0f, 1f)] private float targetShieldDownThreshold = 0.05f;

    [Header("Attack maneuver (mild wobble)")]
    [SerializeField] private float attackLateralOffsetMeters = 22f;
    [SerializeField] private float attackWobbleFrequency = 0.55f;

    [Header("Movement — speed strength (FlightController uses hotas.speedStrength for cap)")]
    [SerializeField] private float patrolSpeedStrength = 0.45f;
    [SerializeField] private float chaseSpeedStrength = 1f;
    [SerializeField] private float attackSpeedStrength = 0.85f;
    [SerializeField] private float breakAwaySpeedStrength = 1.35f;
    [SerializeField] private float fleeSpeedStrength = 1.25f;
    [Tooltip("Scales local aim error into pitch/yaw stick -1..1.")]
    [SerializeField] private float turnResponsiveness = 2.2f;
    [SerializeField] private float throttleSmoothing = 8f;
    [Tooltip("Clamp pitch command to avoid violent spin with sensitive flight setups.")]
    [SerializeField] [Range(0.1f, 1f)] private float maxPitchInput = 0.6f;
    [Tooltip("Clamp yaw command to avoid violent spin with sensitive flight setups.")]
    [SerializeField] [Range(0.1f, 1f)] private float maxYawInput = 0.55f;
    [Tooltip("Input smoothing for pitch/yaw/roll commands.")]
    [SerializeField] private float steeringInputSmoothing = 7f;
    [Tooltip("Ignore tiny steering errors to avoid micro-oscillation around center.")]
    [SerializeField] private float steeringDeadzoneDegrees = 1.5f;
    [Tooltip("Damps yaw command when already rotating in yaw (reduces pendulum wobble).")]
    [SerializeField] private float yawRateDamping = 0.22f;
    [Tooltip("Damps pitch command when already rotating in pitch.")]
    [SerializeField] private float pitchRateDamping = 0.12f;
    [Tooltip("Damps roll command when already rotating in roll.")]
    [SerializeField] private float rollRateDamping = 0.2f;
    [Tooltip("Scales yaw response after smoothing (lower = gentler, less overshoot).")]
    [SerializeField] [Range(0.2f, 1.5f)] private float yawResponseScale = 0.8f;
    [Tooltip("Scales roll response after smoothing (lower = less wobble while banking).")]
    [SerializeField] [Range(0.2f, 1.5f)] private float rollResponseScale = 0.72f;
    [Tooltip("Do not apply roll-level correction for tiny bank errors.")]
    [SerializeField] private float rollLevelDeadzoneDegrees = 2.5f;
    [Tooltip("Maximum absolute roll-level command so level correction cannot dominate turn banking.")]
    [SerializeField] [Range(0f, 1f)] private float maxRollLevelCommand = 0.35f;
    [Tooltip("Suppress roll-level correction while actively yaw-turning so bank is visible.")]
    [SerializeField] [Range(0f, 1f)] private float rollLevelSuppressionFromYaw = 0.85f;
    [Tooltip("When ship up points downward (inverted), add extra roll command to recover upright orientation.")]
    [SerializeField] [Range(0f, 1f)] private float inversionRecoveryRollStrength = 0.75f;
    [Header("Turn-speed control")]
    [Tooltip("Start reducing throttle when nav target direction differs from forward by this angle.")]
    [SerializeField] private float turnSlowdownStartAngleDegrees = 12f;
    [Tooltip("At or above this heading error angle, throttle reaches minimum slowdown scale.")]
    [SerializeField] private float turnSlowdownFullAngleDegrees = 55f;
    [Tooltip("Minimum throttle scale at full turn slowdown (0.3 = keep 30% of requested forward throttle).")]
    [SerializeField] [Range(0.02f, 1f)] private float turnSlowdownMinThrottleScale = 0.06f;
    [Tooltip("After leaving recovery/stabilize, keep turn-based slowdown stronger for this duration.")]
    [SerializeField] private float postRecoveryThrottleEaseSeconds = 1.75f;
    [SerializeField] [Range(1f, 2.5f)] private float postRecoveryTurnSlowdownBoost = 1.35f;
    [Tooltip("Extra speed-cap reduction while turn-slowdown is active. Helps when throttle floor keeps speed too high.")]
    [SerializeField] [Range(0.1f, 1f)] private float turnSlowdownMinSpeedStrengthScale = 0.25f;
    [Tooltip("When heading error is this high, begin hard-brake behavior (beyond normal slowdown).")]
    [SerializeField] private float turnHardBrakeStartAngleDegrees = 55f;
    [Tooltip("At or above this heading error, hard-brake reaches full strength.")]
    [SerializeField] private float turnHardBrakeFullAngleDegrees = 95f;
    [Tooltip("Max reverse throttle applied at full hard-brake demand.")]
    [SerializeField] [Range(0f, 1f)] private float turnHardBrakeReverseThrottleAtFull = 0.75f;
    [Tooltip("Do not engage strong braking below this speed (keeps low-speed patrol motion natural).")]
    [SerializeField] private float brakeMinSpeedForEngage = 40f;
    [Tooltip("Additional speed range above min where braking ramps to full authority.")]
    [SerializeField] private float brakeFullAuthoritySpeedRange = 35f;
    [Tooltip("Scale braking while in Patrol. Lower values preserve gentle loiter behavior.")]
    [SerializeField] [Range(0f, 1f)] private float patrolBrakeScale = 0.2f;
    [Tooltip("Never drop below this forward throttle while patrolling turns, to avoid full stop between waypoints.")]
    [SerializeField] [Range(0f, 1f)] private float patrolTurnThrottleFloor = 0.28f;
    [Tooltip("Absolute reverse-throttle cap used by turn hard-brake so ship slows without unnatural midair full-stop.")]
    [SerializeField] [Range(0f, 1f)] private float brakeReverseThrottleClamp = 0.45f;
    [Tooltip("If direct path clearance drops below this, apply extra collision-imminent braking.")]
    [SerializeField] private float imminentCollisionBrakeDistance = 90f;
    [Tooltip("Strength of extra braking when flying toward imminent obstacle/terrain.")]
    [SerializeField] [Range(0f, 1f)] private float imminentCollisionBrakeStrength = 0.32f;
    [Tooltip("How fast brake demand rises (higher = snappier).")]
    [SerializeField] private float brakeEngageRate = 2.1f;
    [Tooltip("How fast brake demand releases (higher = faster acceleration return).")]
    [SerializeField] private float brakeReleaseRate = 1.15f;
    [Tooltip("Reverse hard-brake starts only above this normalized heading-brake demand.")]
    [SerializeField] [Range(0f, 1f)] private float reverseBrakeStartDemand = 0.82f;
    [Tooltip("Do not allow reverse hard-brake in these calmer states; keeps non-flee turns natural.")]
    [SerializeField] private bool reverseBrakeOnlyInHighUrgencyStates = true;
    [Tooltip("Additional wider cast radius used for forward obstacle detection (ship-width safety).")]
    [SerializeField] private float forwardProbeRadius = 12f;
    [Tooltip("How much the ship banks while yawing/turning. 0 = no bank.")]
    [SerializeField] [Range(0f, 1f)] private float rollFromTurn = 0.35f;
    [Tooltip("Extra visual bank added from yaw demand so turns don't look flat.")]
    [SerializeField] [Range(0f, 1f)] private float turnVisualBankBoost = 0.28f;
    [Tooltip("Small coordinated turn pitch-up while banking, for more natural arc turns.")]
    [SerializeField] [Range(0f, 0.4f)] private float coordinatedTurnPitchBoost = 0.08f;
    [Tooltip("World-up roll leveling. Higher values keep wings more level with the horizon.")]
    [SerializeField] [Range(0f, 0.08f)] private float rollLevelStrength = 0.018f;

    [Header("Patrol")]
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private float waypointReachRadius = 40f;
    [SerializeField] private float patrolThrottle = 0.35f;
    [Tooltip("Optional center for free patrol when no explicit patrol waypoints are assigned.")]
    [SerializeField] private Transform patrolCenter;
    [SerializeField] private float patrolRadius = 160f;
    [SerializeField] private float patrolPointReachRadius = 35f;
    [SerializeField] private float patrolPointRefreshSeconds = 6f;
    [SerializeField] private float loiterStrafeAmplitude = 0.18f;
    [SerializeField] private float loiterVerticalAmplitude = 0.12f;
    [SerializeField] private float loiterFrequency = 0.2f;

    [Header("BreakAway / Flee")]
    [SerializeField] private float breakAwayDurationSeconds = 1.6f;
    [SerializeField] [Range(0f, 1f)] private float fleeHullFractionThreshold = 0.3f;
    [SerializeField] private bool fleeWhenShieldsLow = true;
    [SerializeField] [Range(0f, 1f)] private float fleeShieldFractionThreshold = 0.1f;
    [SerializeField] private float fleeSteerNoiseAmplitude = 0.08f;

    [Header("Flee route (canyon / authored corridor)")]
    [Tooltip("Optional manual list. If Flee Route Waypoint Root is assigned and builds a valid path, the root/children are used instead.")]
    [SerializeField] private Transform[] fleeRouteWaypoints;
    [Tooltip("Parent object whose position is the route start, with waypoint children underneath (e.g. CanyonWaypoints → wp (1)…wp (26)).")]
    [SerializeField] private Transform fleeRouteWaypointRoot;
    [Tooltip("Include the root transform as the first point, then all direct children.")]
    [SerializeField] private bool fleeRouteIncludeRootAsFirstWaypoint = true;
    [Tooltip("Order of direct children when building the polyline from Flee Route Waypoint Root.")]
    [SerializeField] private NPCShipFleeRouteChildOrder fleeRouteChildOrder = NPCShipFleeRouteChildOrder.HierarchyOrder;
    [Tooltip("When entering Flee, set progress from the polyline segment (not just a vertex) closest to the ship. Turn off to always start at waypoint 0.")]
    [SerializeField] private bool fleeRouteStartFromNearestWaypoint = true;
    [Tooltip("Advance to next waypoint when within this distance of the current one.")]
    [SerializeField] private float fleeRouteAdvanceRadius = 45f;
    [Tooltip("Aim target is this many meters ahead along the polyline from the ship (smoother than aiming at raw waypoints).")]
    [SerializeField] private float fleeRouteLookaheadMeters = 120f;
    [Tooltip("At route end, loop to waypoint 0 instead of falling back to run-away.")]
    [SerializeField] private bool fleeRouteLoop;
    [Tooltip("When the route ends (and not looping), switch to classic flee-away steering.")]
    [SerializeField] private bool fleeRouteFallbackWhenComplete = true;
    [Tooltip("If true, never use generic random flee while an authored flee route exists. If fallback is disabled, the ship will hold/continue on route heading instead.")]
    [SerializeField] private bool authoredFleeRouteIsAuthoritative = true;
    [SerializeField] private bool drawFleeRouteGizmo = true;
    [SerializeField] [Range(0f, 1f)] private float fleeRouteRollFromTurnScale = 0.2f;

    [Header("Stuck recovery")]
    [SerializeField] private bool enableStuckRecovery = true;
    [SerializeField] private float stuckSpeedThreshold = 2.5f;
    [SerializeField] private float stuckDurationSeconds = 1.5f;
    [SerializeField] private float groundCheckDistance = 12f;
    [SerializeField] private float recoverDurationSeconds = 1.2f;
    [SerializeField] [Range(0f, 1f)] private float recoverThrottle01 = 0.8f;
    [SerializeField] [Range(0f, 1f)] private float recoverVerticalUp = 0.8f;
    [Tooltip("After backing away from collision, hold level (pitch/roll neutral) and climb vertically for this long before resuming route/nav.")]
    [SerializeField] private float recoverStabilizeDurationSeconds = 6f;
    [SerializeField] [Range(0f, 1f)] private float recoverStabilizeVerticalUp = 1f;
    [SerializeField] private float terrainCollisionRecoveryDurationSeconds = 1.4f;
    [SerializeField] [Range(0f, 1f)] private float terrainCollisionEscapeThrottle01 = 0.6f;
    [SerializeField] [Range(0f, 1f)] private float terrainCollisionEscapeVertical = 1f;

    [Header("Ground probing")]
    [Tooltip("Desired clearance above ground along world down ray. Soft hold engages when below this.")]
    [SerializeField] private float minAltitudeAboveGround = 30f;
    [SerializeField] private float altitudeHoldRaycastMaxDistance = 4000f;
    [Tooltip("Starts the downward ground probe this many meters above the ship to improve terrain hits on steep canyon walls / large colliders.")]
    [SerializeField] private float altitudeProbeStartHeight = 120f;
    [Tooltip("Layers counted as ground/terrain for altitude raycasts.")]
    [SerializeField] private LayerMask altitudeGroundLayers = ~0;
    [Tooltip("Blend steering direction toward world up when low (0 = off).")]
    [SerializeField] [Range(0f, 0.5f)] private float altitudeSteerUpBlend = 0.22f;
    [Tooltip("Extra nose-up pitch input at full hold.")]
    [SerializeField] [Range(0f, 0.6f)] private float altitudePitchAddMax = 0.28f;

    [Header("Collision recovery memory")]
    [Tooltip("Layers treated as solid obstacles for collision recovery memory (terrain + canyon meshes + props).")]
    [SerializeField] private LayerMask terrainObstacleLayers = ~0;
    [SerializeField] private float terrainProbeRadius = 8f;
    [SerializeField] private float terrainProbeDistance = 80f;
    [SerializeField] private float terrainProbeUpOffset = 18f;
    [Tooltip("Extra probe distance added based on current speed.")]
    [SerializeField] private float terrainProbeDistancePerSpeed = 1.2f;
    [Tooltip("Horizontal fan angle used for side terrain probes.")]
    [SerializeField] [Range(10f, 80f)] private float terrainProbeSideAngleDegrees = 32f;
    [Tooltip("Vertical fan angle used for upward terrain probes.")]
    [SerializeField] [Range(10f, 75f)] private float terrainProbeUpAngleDegrees = 26f;
    [SerializeField] [Range(0f, 1f)] private float terrainAvoidanceVerticalAddMax = 0.85f;
    [SerializeField] [Range(0.1f, 1f)] private float terrainAvoidanceThrottleScale = 0.45f;
    [SerializeField] private float terrainEscapeHoldSeconds = 0.9f;
    [SerializeField] private float terrainEscapeReacquireDelaySeconds = 0.35f;
    [SerializeField] private float terrainRecoverUnstickAcceleration = 38f;
    [SerializeField] private float maxContinuousRouteAltitudeEmergencySeconds = 1.15f;

    [Header("Raycast-fan navigation (new)")]
    [SerializeField] private float baseProbeDistance = 55f;
    [SerializeField] private float probeLookaheadSeconds = 1.1f;
    [SerializeField] [Range(0.25f, 1f)] private float turningProbeDistanceScale = 0.55f;
    [SerializeField] private float maxProbeDistance = 180f;
    [SerializeField] [Range(10f, 45f)] private float yawProbeAngleDegrees = 28f;
    [SerializeField] [Range(8f, 35f)] private float upwardProbeAngleDegrees = 18f;
    [SerializeField] private float directPathSafetyFraction = 0.92f;
    [SerializeField] private float immediateCollisionDistance = 12f;
    [SerializeField] private float clearanceScoreWeight = 2.2f;
    [SerializeField] private float alignmentScoreWeight = 1f;
    [SerializeField] private float waypointSidePreferenceWeight = 0.55f;
    [SerializeField] private float climbPenaltyWeight = 0.35f;
    [SerializeField] private float lowAltitudePenaltyWeight = 3f;
    [SerializeField] private float immediateCollisionPenaltyWeight = 4f;

    [Header("Ground clearance band (new)")]
    [SerializeField] private float desiredMinGroundClearance = 28f;
    [SerializeField] private float desiredMaxGroundClearance = 90f;
    [SerializeField] private float groundProbeDistance = 4000f;
    [SerializeField] private float groundClearancePitchWeight = 0.28f;
    [SerializeField] private float groundClearanceVerticalWeight = 0.7f;
    [SerializeField] private float downProbeOriginUpOffset = 2f;
    [SerializeField] private float forwardDownProbeForwardDistance = 35f;
    [SerializeField] private float forwardDownProbeOriginUpOffset = 6f;
    [SerializeField] [Range(5f, 80f)] private float forwardDownProbeAngleDegrees = 28f;
    [SerializeField] private float upcomingTerrainEmergencyClearance = 24f;
    [SerializeField] private float upcomingTerrainUpBiasWeight = 0.65f;
    [SerializeField] [Range(0f, 1f)] private float minGroundProbeNormalY = 0.2f;

    [Header("Navigation debug (new)")]
    [SerializeField] private bool drawNavigationDebugRays = true;
    [SerializeField] private bool drawGroundClearanceDebug = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;
    [Tooltip("Inspector test toggle: when enabled, this AI is forced into Flee state (if alive).")]
    [SerializeField] private bool forceFleeForTesting;
    [Tooltip("Diagnostic only: invert AI pitch command sign (up/down). Leave OFF for normal behavior.")]
    [SerializeField] private bool invertAIPitchDebug;
    [SerializeField] private bool showStateOnGui;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawAltitudeHoldGizmo;

    [Header("Runtime Status (read-only)")]
    [SerializeField] private float currentHullFraction = 1f;
    [SerializeField] private float currentMinShieldFraction = 1f;
    [SerializeField] private float currentFrontShieldFraction = 1f;
    [SerializeField] private float currentLeftShieldFraction = 1f;
    [SerializeField] private float currentRightShieldFraction = 1f;
    [SerializeField] private float currentRearShieldFraction = 1f;
    [SerializeField] private bool targetCurrentlyVisible;
    [SerializeField] private bool hasDamageAggro;
    [SerializeField] private bool hasCombatTargetMemory;
    [SerializeField] private bool hasActiveFleeRoute;
    [SerializeField] private float currentGroundDistance = -1f;
    [SerializeField] private int activeFleeRouteIndex;
    [SerializeField] private int currentClosestFleeSegmentIndex;
    [SerializeField] private Vector3 currentFleeLookaheadPoint;
    [SerializeField] private bool routeSteeringActive;
    [SerializeField] private bool routeSteeringOverriddenByEmergency;
    [SerializeField] private bool routeReacquireAttempted;
    [SerializeField] private bool altitudeEmergencyActive;
    [SerializeField] private float altitudeEmergencyContinuousSeconds;
    [SerializeField] private bool terrainEscapeActive;
    [SerializeField] private bool ignoredCombatCollisionAsSteeringHit;
    [SerializeField] private string currentTargetName;
    [SerializeField] private string lastWeaponBlockReason = "";
    [SerializeField] private string activeWeaponMode = "Laser";
    [SerializeField] private bool touchingTerrainCollider;
    [SerializeField] private bool directPathAccepted;
    [SerializeField] private string selectedAvoidanceMode = "Direct";
    [SerializeField] private bool currentDownProbeHit;
    [SerializeField] private bool currentForwardDownProbeHit;
    [SerializeField] private float currentDownProbeClearance = -1f;
    [SerializeField] private float currentForwardDownProbeClearance = -1f;
    [SerializeField] private float currentGroundHeightBelow;
    [SerializeField] private float currentForwardGroundHeight;
    [SerializeField] private bool altitudeEmergencyFromCurrentLow;
    [SerializeField] private bool altitudeEmergencyFromUpcomingTerrain;

    NPCShipState _state = NPCShipState.Patrol;
    Transform _target;
    float _stateEnterTime;
    float _lostTargetTimer;
    float _combatTargetMemoryUntil;
    float _breakAwayEndTime;
    float _attackReentryLockUntil;
    bool _burstActive;
    float _burstEndTime;
    float _nextBurstAllowedTime;
    float _smoothedForwardThrottle01;
    int _waypointIndex;
    Vector3 _currentPatrolPoint;
    float _nextPatrolPointRefreshTime;
    bool _patrolAnchorInitialized;
    Vector3 _patrolAnchorPosition;
    bool _hardpointDeployed;
    float _stuckTimer;
    float _recoverUntilTime;
    float _recoverStabilizeUntilTime;
    bool _recoverStabilizePending;
    bool _wasInRecoveryLikeLastFrame;
    float _postRecoverySlowUntilTime;
    float _inputPitch;
    float _inputYaw;
    float _inputRoll;
    float _inversionRecoveryRollSign = 1f;
    float _prevHullFraction = 1f;
    float _prevMinShieldNormalized = 1f;
    bool _aggroLatchFromDamage;
    bool _wantsImmediateCombatFire;
    Transform[] _fleeRoutePendingOverride;
    Transform[] _fleeRouteActive;
    int _fleeRouteIndex;
    bool _fleeRouteCompleted;
    Collider _lastTerrainCollisionCollider;
    Vector3 _lastTerrainCollisionNormal = Vector3.up;
    float _terrainEscapeUntilTime;
    float _terrainEscapeReacquireUntilTime;
    Vector3 _terrainEscapeDirection = Vector3.forward;
    float _ignoredCombatCollisionUntilTime;
    float _routeReacquireUntilTime;
    float _altitudeEmergencyStartTime = -1f;
    float _nextCollisionRecoveryAllowedTime;
    bool _altitudeEmergencyBoundedThisFrame;
    float _smoothedBrakeDemand;

    Vector3 _dbgDesiredDirection;
    Vector3 _dbgFinalDirection;
    Vector3 _dbgDirectRayDirection;
    float _dbgDirectRayDistance;
    int _dbgCandidateCount;
    int _dbgChosenCandidateIndex = -1;
    readonly Vector3[] _dbgCandidateDirections = new Vector3[8];
    readonly float[] _dbgCandidateClearances = new float[8];
    readonly bool[] _dbgCandidateBlocked = new bool[8];

    public bool ShowDebugLogs => showDebugLogs;

    // Represents a projected position on a specific flee-route segment.
    // segmentIndex, segmentT, and point must always describe the same segment sample.
    struct FleeRouteProgress
    {
        public int segmentIndex;
        public float segmentT;
        public Vector3 point;
    }

    struct TerrainProbeSample
    {
        public Vector3 direction;
        public float clearance;
        public Vector3 normal;
        public bool blocked;
    }

    struct GroundProbeInfo
    {
        public bool hit;
        public float clearance;
        public float groundY;
        public Vector3 point;
        public Vector3 normal;
    }

    /// <summary>Current AI state (for HUD / debugging).</summary>
    public NPCShipState CurrentState => _state;

    void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (hotas == null)
            hotas = GetComponentInChildren<HOTASController>(true);
        if (aimOrigin == null && rb != null)
            aimOrigin = rb.transform;
        if (combatDamageable == null)
            combatDamageable = GetComponentInChildren<CombatDamageable>(true);
        if (explodableDamageable == null)
            explodableDamageable = GetComponentInChildren<ExplodableDamageable>(true);
        if (weapons == null || weapons.Length == 0)
        {
            var found = GetComponentsInChildren<WeaponController>(true);
            if (found != null && found.Length > 0)
                weapons = found;
        }
        if (weaponHardpoint == null)
            weaponHardpoint = GetComponentInChildren<WeaponHardpoint>(true);
        if (weaponHardpoints == null || weaponHardpoints.Length == 0)
            weaponHardpoints = GetComponentsInChildren<WeaponHardpoint>(true);
        if (shieldSystem == null)
            shieldSystem = GetComponentInChildren<ShieldSystem>(true);

        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    void Start()
    {
        if (manualTarget != null)
            _target = ResolvePreferredTargetTransform(manualTarget);
        else if (findTargetByTagOnStart && !string.IsNullOrEmpty(targetTag))
        {
            var go = GameObject.FindGameObjectWithTag(targetTag);
            if (go != null)
                _target = ResolvePreferredTargetTransform(go.transform);
        }

        if (hotas != null)
            hotas.useSimulatedInput = true;

        _prevHullFraction = GetHullFraction();
        _prevMinShieldNormalized = GetMinShieldNormalized();
    }

    void OnDisable()
    {
        ClearHotas();
    }

    void OnCollisionEnter(Collision collision)
    {
        HandleTerrainCollision(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        HandleTerrainCollision(collision);
    }

    void Update()
    {
        if (!EnsureRefs())
            return;

        if (CheckDead())
            return;

        ResolveTarget();

        TrackDamageAggro();
        ResetFrameRuntimeFlags();

        bool inRecoveryLikeNow = Time.time < _recoverUntilTime || _recoverStabilizePending || Time.time < _recoverStabilizeUntilTime;
        if (_wasInRecoveryLikeLastFrame && !inRecoveryLikeNow)
            _postRecoverySlowUntilTime = Time.time + Mathf.Max(0f, postRecoveryThrottleEaseSeconds);
        _wasInRecoveryLikeLastFrame = inRecoveryLikeNow;

        if (enableStuckRecovery && _state != NPCShipState.Dead)
            UpdateStuckRecovery(Time.deltaTime);

        if (Time.time < _recoverUntilTime)
        {
            selectedAvoidanceMode = "Recovery";
            TickRecover(Time.deltaTime);
            UpdateRuntimeStatus();
            return;
        }

        if (_recoverStabilizePending)
        {
            _recoverStabilizePending = false;
            _recoverStabilizeUntilTime = Time.time + Mathf.Max(0f, recoverStabilizeDurationSeconds);
        }

        if (Time.time < _recoverStabilizeUntilTime)
        {
            selectedAvoidanceMode = "RecoveryStabilize";
            TickRecoverStabilize(Time.deltaTime);
            UpdateRuntimeStatus();
            return;
        }

        float dt = Time.deltaTime;
        if (forceFleeForTesting && _state != NPCShipState.Flee && _state != NPCShipState.Dead)
            EnterState(NPCShipState.Flee);

        if (ShouldFleeByHealth() && _state != NPCShipState.Flee && _state != NPCShipState.Dead)
            EnterState(NPCShipState.Flee);

        switch (_state)
        {
            case NPCShipState.Patrol:
                TickPatrol(dt);
                break;
            case NPCShipState.Chase:
                TickChase(dt);
                break;
            case NPCShipState.Attack:
                TickAttack(dt);
                break;
            case NPCShipState.BreakAway:
                TickBreakAway(dt);
                break;
            case NPCShipState.Flee:
                TickFlee(dt);
                break;
            case NPCShipState.Dead:
                break;
        }

        if (_state != NPCShipState.Dead && hotas != null)
            hotas.speedStrength = Mathf.Min(hotas.speedStrength, GetSpeedStrengthForState());

        UpdateRuntimeStatus();
    }

    void ResetFrameRuntimeFlags()
    {
        routeSteeringActive = false;
        routeSteeringOverriddenByEmergency = false;
        altitudeEmergencyActive = false;
        altitudeEmergencyFromCurrentLow = false;
        altitudeEmergencyFromUpcomingTerrain = false;
        directPathAccepted = false;
        selectedAvoidanceMode = Time.time < _recoverUntilTime ? "Recovery" : "Idle";
        _dbgCandidateCount = 0;
        _dbgChosenCandidateIndex = -1;
        _altitudeEmergencyBoundedThisFrame = false;
    }

    bool EnsureRefs()
    {
        if (rb == null || hotas == null)
            return false;
        return true;
    }

    bool CheckDead()
    {
        if (_state == NPCShipState.Dead)
        {
            ClearHotas();
            return true;
        }

        if (combatDamageable != null && combatDamageable.IsDestroyed)
        {
            EnterDead();
            return true;
        }

        if (explodableDamageable != null && explodableDamageable.IsDestroyed)
        {
            EnterDead();
            return true;
        }

        return false;
    }

    void EnterDead()
    {
        if (_state == NPCShipState.Dead)
            return;
        EnterState(NPCShipState.Dead);
        ClearHotas();
        _burstActive = false;
        enabled = false;
    }

    void ResolveTarget()
    {
        if (manualTarget != null)
            _target = ResolvePreferredTargetTransform(manualTarget);
        else if (_target == null && findTargetByTagOnStart && !string.IsNullOrEmpty(targetTag))
        {
            var go = GameObject.FindGameObjectWithTag(targetTag);
            if (go != null)
                _target = ResolvePreferredTargetTransform(go.transform);
        }

        bool seen = _target != null && CanDetectTarget();
        targetCurrentlyVisible = seen;
        if (seen)
        {
            _lostTargetTimer = 0f;
            _combatTargetMemoryUntil = Mathf.Max(_combatTargetMemoryUntil, Time.time + Mathf.Max(0.25f, combatTargetMemorySeconds * 0.35f));
        }
        else if (_target != null)
            _lostTargetTimer += Time.deltaTime;

        if (_state != NPCShipState.Patrol && _state != NPCShipState.Flee && _state != NPCShipState.Dead)
        {
            float effectiveLostTimeout = Mathf.Max(0.5f, lostTargetTimeoutSeconds);
            bool keepCombatLock = _aggroLatchFromDamage || Time.time < _combatTargetMemoryUntil;
            if ((_target == null && !keepCombatLock) || (!keepCombatLock && _lostTargetTimer >= effectiveLostTimeout))
            {
                if (showDebugLogs && _lostTargetTimer >= effectiveLostTimeout)
                    Debug.Log($"[NPCShipAI] Lost target → Patrol", this);
                if (clearAggroLatchOnLostTarget)
                    _aggroLatchFromDamage = false;
                EnterState(NPCShipState.Patrol);
            }
        }
    }

    void TrackDamageAggro()
    {
        float h = GetHullFraction();
        float sMin = GetMinShieldNormalized();
        const float eps = 0.0002f;
        if (h < _prevHullFraction - eps || sMin < _prevMinShieldNormalized - eps)
        {
            _aggroLatchFromDamage = true;
            _combatTargetMemoryUntil = Mathf.Max(_combatTargetMemoryUntil, Time.time + Mathf.Max(0.5f, combatTargetMemorySeconds));
        }
        _prevHullFraction = h;
        _prevMinShieldNormalized = sMin;
    }

    void UpdateRuntimeStatus()
    {
        currentHullFraction = GetHullFraction();
        currentFrontShieldFraction = shieldSystem != null ? shieldSystem.GetArcNormalized(ShieldArc.Front) : 1f;
        currentLeftShieldFraction = shieldSystem != null ? shieldSystem.GetArcNormalized(ShieldArc.Left) : 1f;
        currentRightShieldFraction = shieldSystem != null ? shieldSystem.GetArcNormalized(ShieldArc.Right) : 1f;
        currentRearShieldFraction = shieldSystem != null ? shieldSystem.GetArcNormalized(ShieldArc.Rear) : 1f;
        currentMinShieldFraction = Mathf.Min(Mathf.Min(currentFrontShieldFraction, currentLeftShieldFraction), Mathf.Min(currentRightShieldFraction, currentRearShieldFraction));
        hasDamageAggro = _aggroLatchFromDamage;
        hasCombatTargetMemory = Time.time < _combatTargetMemoryUntil;
        hasActiveFleeRoute = _fleeRouteActive != null && _fleeRouteActive.Length > 0 && !_fleeRouteCompleted;
        if (TryProbeGroundBelow(out GroundProbeInfo downInfo))
        {
            currentGroundDistance = downInfo.clearance;
            currentDownProbeHit = true;
            currentDownProbeClearance = downInfo.clearance;
            currentGroundHeightBelow = downInfo.groundY;
        }
        else
        {
            currentGroundDistance = -1f;
            currentDownProbeHit = false;
            currentDownProbeClearance = -1f;
            currentGroundHeightBelow = 0f;
        }

        if (TryProbeGroundAhead(rb != null ? rb.transform.forward : transform.forward, out GroundProbeInfo forwardInfo))
        {
            currentForwardDownProbeHit = true;
            currentForwardDownProbeClearance = forwardInfo.clearance;
            currentForwardGroundHeight = forwardInfo.groundY;
        }
        else
        {
            currentForwardDownProbeHit = false;
            currentForwardDownProbeClearance = -1f;
            currentForwardGroundHeight = 0f;
        }

        activeFleeRouteIndex = _fleeRouteIndex;
        terrainEscapeActive = Time.time < _terrainEscapeUntilTime;
        routeReacquireAttempted = Time.time < _routeReacquireUntilTime;
        ignoredCombatCollisionAsSteeringHit = Time.time < _ignoredCombatCollisionUntilTime;
        altitudeEmergencyContinuousSeconds = altitudeEmergencyActive && _altitudeEmergencyStartTime > 0f
            ? Mathf.Max(0f, Time.time - _altitudeEmergencyStartTime)
            : 0f;
        currentTargetName = _target != null ? _target.name : "(none)";
        touchingTerrainCollider = _lastTerrainCollisionCollider != null && Time.time < _recoverUntilTime + 0.25f;
    }

    bool ShouldFleeByHealth()
    {
        if (GetHullFraction() <= fleeHullFractionThreshold)
            return true;
        if (fleeWhenShieldsLow && shieldSystem != null && GetMinShieldNormalized() <= fleeShieldFractionThreshold)
            return true;
        return false;
    }

    float GetHullFraction()
    {
        if (combatDamageable != null)
            return combatDamageable.HullFraction;
        if (explodableDamageable != null)
            return explodableDamageable.HullFraction;
        return 1f;
    }

    float GetMinShieldNormalized()
    {
        if (shieldSystem == null)
            return 1f;
        float min = 1f;
        min = Mathf.Min(min, shieldSystem.GetArcNormalized(ShieldArc.Front));
        min = Mathf.Min(min, shieldSystem.GetArcNormalized(ShieldArc.Left));
        min = Mathf.Min(min, shieldSystem.GetArcNormalized(ShieldArc.Right));
        min = Mathf.Min(min, shieldSystem.GetArcNormalized(ShieldArc.Rear));
        return min;
    }

    bool PlayerWithinPatrolEngagementRange()
    {
        if (_target == null || rb == null)
            return false;
        return Vector3.Distance(rb.position, _target.position) <= patrolEngagementRange;
    }

    bool ShouldPatrolBeginChase()
    {
        if (_target == null)
            return false;
        if (_aggroLatchFromDamage)
            return true;
        if (PlayerWithinPatrolEngagementRange())
            return true;
        if (!deferEngagementUntilProximityOrDamage)
            return CanDetectTarget();
        return false;
    }

    bool CanEngageWeapons()
    {
        if (_target == null)
            return false;
        if (CanDetectTarget())
            return true;
        if (_aggroLatchFromDamage && Vector3.Distance(rb.position, _target.position) <= fireRange)
            return true;
        if (deferEngagementUntilProximityOrDamage && PlayerWithinPatrolEngagementRange())
            return true;
        return false;
    }

    void TickPatrol(float dt)
    {
        if (ShouldPatrolBeginChase())
        {
            _wantsImmediateCombatFire = deferEngagementUntilProximityOrDamage;
            _combatTargetMemoryUntil = Mathf.Max(_combatTargetMemoryUntil, Time.time + Mathf.Max(0.5f, combatTargetMemorySeconds));
            DeployHardpointIfNeeded();
            EnterState(NPCShipState.Chase);
            return;
        }

        bool loiter = waypoints == null || waypoints.Length == 0;
        Vector3 patrolPoint = GetPatrolSteerPoint(out float forward01);
        ApplySteeringTowardPoint(patrolPoint, forward01, patrolSpeedStrength, dt);
        if (loiter)
            ApplyLoiterStrafe();
        ClearBurst();
    }

    Vector3 GetPatrolSteerPoint(out float forward01)
    {
        forward01 = patrolThrottle;
        if (waypoints != null && waypoints.Length > 0)
        {
            Transform wp = waypoints[_waypointIndex % waypoints.Length];
            if (wp == null)
            {
                forward01 = patrolThrottle * 0.5f;
                return rb.position + rb.transform.forward * 50f;
            }

            Vector3 toWp = wp.position - rb.position;
            float d = toWp.magnitude;
            if (d < waypointReachRadius)
                _waypointIndex = (_waypointIndex + 1) % waypoints.Length;

            return wp.position;
        }

        EnsurePatrolPoint();
        if ((_currentPatrolPoint - rb.position).sqrMagnitude <= patrolPointReachRadius * patrolPointReachRadius)
            PickNewPatrolPoint();
        if (Time.time >= _nextPatrolPointRefreshTime)
            PickNewPatrolPoint();

        return _currentPatrolPoint;
    }

    Vector3 GetLocalPatrolPointDirection()
    {
        EnsurePatrolPoint();
        Vector3 toPoint = _currentPatrolPoint - rb.position;
        if (toPoint.sqrMagnitude <= patrolPointReachRadius * patrolPointReachRadius)
        {
            PickNewPatrolPoint();
            toPoint = _currentPatrolPoint - rb.position;
        }

        if (Time.time >= _nextPatrolPointRefreshTime)
            PickNewPatrolPoint();

        if (toPoint.sqrMagnitude < 1f)
            return rb.transform.forward;

        return toPoint.normalized;
    }

    void ApplyLoiterStrafe()
    {
        float t = Time.time * loiterFrequency;
        hotas.simulatedStrafe = Mathf.Clamp(Mathf.Sin(t * Mathf.PI * 2f) * loiterStrafeAmplitude, -1f, 1f);
        hotas.simulatedVertical = Mathf.Clamp(Mathf.Cos(t * 1.3f * Mathf.PI * 2f) * loiterVerticalAmplitude, -1f, 1f);
    }

    void TickChase(float dt)
    {
        if (_target == null)
            return;

        float dist = Vector3.Distance(rb.position, _target.position);
        if (dist < breakawayDistance && Time.time >= _attackReentryLockUntil)
        {
            StartBreakAway();
            return;
        }

        float chaseDelay = _wantsImmediateCombatFire ? 0f : minTimeInChaseBeforeAttack;
        if (dist >= preferredDistanceMin && dist <= preferredDistanceMax
            && CanEngageWeapons()
            && Time.time - _stateEnterTime >= chaseDelay
            && Time.time >= _attackReentryLockUntil)
        {
            EnterState(NPCShipState.Attack);
            return;
        }

        Vector3 chasePoint = GetLiveTargetPosition();
        float forward01 = GetChaseThrottle(dist);
        ApplySteeringTowardPoint(chasePoint, forward01, chaseSpeedStrength, dt);
        ClearBurst();
    }

    float GetChaseThrottle(float dist)
    {
        if (dist > preferredDistanceMax)
            return Mathf.Clamp01((dist - preferredDistanceMax) / Mathf.Max(50f, detectionRange * 0.25f) + 0.45f);
        if (dist < preferredDistanceMin)
            return 0.28f;
        return 0.55f;
    }

    void TickAttack(float dt)
    {
        if (_target == null)
            return;

        float dist = Vector3.Distance(rb.position, _target.position);
        if (dist < breakawayDistance && Time.time - _stateEnterTime >= minTimeInAttackBeforeBreakAway)
        {
            StartBreakAway();
            return;
        }

        if (dist < preferredDistanceMin || dist > preferredDistanceMax || !CanEngageWeapons())
        {
            EnterState(NPCShipState.Chase);
            return;
        }

        Vector3 attackAim = GetAttackAimPoint(1f);
        ApplySteeringTowardPoint(attackAim, 0.62f, attackSpeedStrength, dt);

        TryBurstFire(dist);
    }

    void TickBreakAway(float dt)
    {
        if (_target == null)
        {
            EnterState(NPCShipState.Chase);
            return;
        }

        Vector3 away = (rb.position - _target.position);
        if (away.sqrMagnitude < 1f)
            away = -_target.forward;
        away.Normalize();

        ApplySteering(away, 1f, breakAwaySpeedStrength, dt);

        if (Time.time >= _breakAwayEndTime)
        {
            _attackReentryLockUntil = Time.time + attackReentryCooldownSeconds;
            EnterState(NPCShipState.Chase);
        }

        ClearBurst();
    }

    void TickFlee(float dt)
    {
        if (TryTickFleeAlongRoute(dt))
            return;

        if (authoredFleeRouteIsAuthoritative && ResolveFleeRouteWaypoints() is Transform[] route && route != null && route.Length > 0)
        {
            if (TryReacquireAuthoritativeFleeRoute(route, dt))
            {
                ClearBurst();
                return;
            }

            // If an authored route exists, stay in nav-safe route fallback (never raw-forward bypass).
            FleeRouteProgress fallbackProgress = GetClosestProgressOnFleePolyline(route, rb.position);
            currentClosestFleeSegmentIndex = fallbackProgress.segmentIndex;
            currentFleeLookaheadPoint = GetPointAheadOnFleePolyline(
                route,
                fallbackProgress.segmentIndex,
                fallbackProgress.segmentT,
                Mathf.Max(30f, fleeRouteLookaheadMeters * 0.55f),
                fleeRouteLoop);

            Vector3 fallbackSteer = currentFleeLookaheadPoint - fallbackProgress.point;
            if (fallbackSteer.sqrMagnitude < 1e-6f)
                fallbackSteer = rb.transform.forward;
            else
                fallbackSteer.Normalize();

            if (showDebugLogs && Time.frameCount % 30 == 0)
                Debug.Log($"[NPCShipAI] Flee route fallback(nav-safe) -> seg={fallbackProgress.segmentIndex} lookahead={currentFleeLookaheadPoint}", this);

            ApplyRouteSteering(fallbackSteer, currentFleeLookaheadPoint, 0.45f, fleeSpeedStrength, dt);
            ClearBurst();
            return;
        }

        if (_target == null)
        {
            ApplySteering(rb.transform.forward, 0.4f, fleeSpeedStrength, dt);
            return;
        }

        Vector3 targetPos = GetLiveTargetPosition();
        Vector3 away = (rb.position - targetPos).normalized;
        float n = Mathf.PerlinNoise(Time.time * 1.7f, 2.3f) * 2f - 1f;
        Vector3 perp = Vector3.Cross(away, Vector3.up);
        if (perp.sqrMagnitude < 0.01f)
            perp = Vector3.Cross(away, Vector3.forward);
        perp.Normalize();
        away = (away + perp * (n * fleeSteerNoiseAmplitude)).normalized;

        ApplySteering(away, 1f, fleeSpeedStrength, dt);
        ClearBurst();
    }

    /// <summary>Optional: set from a trigger volume so the next Flee uses this polyline (e.g. canyon mouth).</summary>
    public void SetFleeRouteOverride(Transform[] route)
    {
        if (route == null || route.Length == 0)
            return;
        _fleeRoutePendingOverride = route;
    }

    bool TryTickFleeAlongRoute(float dt)
    {
        if (_fleeRouteActive == null || _fleeRouteActive.Length == 0)
            return false;

        if (_fleeRouteCompleted)
        {
            if (fleeRouteLoop)
            {
                _fleeRouteCompleted = false;
                _fleeRouteIndex = 0;
            }
            else if (fleeRouteFallbackWhenComplete)
            {
                _fleeRouteActive = null;
                return false;
            }
            else
            {
                Vector3 fwd = rb.transform.forward;
                ApplySteering(fwd, 0.5f, fleeSpeedStrength, dt);
                ClearBurst();
                return true;
            }
        }

        FleeRouteProgress progress = UpdateFleeRouteIndex();
        if (_fleeRouteActive == null || _fleeRouteActive.Length < 2)
            return false;

        Vector3 aim = GetPointAheadOnFleePolyline(_fleeRouteActive, progress.segmentIndex, progress.segmentT, fleeRouteLookaheadMeters, fleeRouteLoop);
        currentFleeLookaheadPoint = aim;
        Vector3 steer = aim - progress.point;
        if (steer.sqrMagnitude < 1e-6f)
            steer = rb.transform.forward;
        else
            steer.Normalize();

        if (showDebugLogs && Time.frameCount % 20 == 0)
        {
            string waypointName = "(none)";
            int nextWaypointIndex = Mathf.Clamp(progress.segmentIndex + 1, 0, _fleeRouteActive.Length - 1);
            if (_fleeRouteActive[nextWaypointIndex] != null)
                waypointName = _fleeRouteActive[nextWaypointIndex].name;

            Debug.Log(
                $"[NPCShipAI] Flee route follow -> seg={progress.segmentIndex} nextWp={nextWaypointIndex} '{waypointName}' lookahead={currentFleeLookaheadPoint}",
                this);
        }

        // Route desired direction still goes through the new raycast-fan resolver.
        ApplyRouteSteering(steer, currentFleeLookaheadPoint, 1f, fleeSpeedStrength, dt);
        ClearBurst();
        return true;
    }

    bool TryReacquireAuthoritativeFleeRoute(Transform[] route, float dt)
    {
        if (route == null || route.Length < 2)
            return false;

        FleeRouteProgress progress;
        if (fleeRouteStartFromNearestWaypoint)
            progress = GetClosestProgressOnFleePolyline(route, rb.position);
        else
            progress = GetProjectedProgressOnSegment(route, 0, rb != null ? rb.position : transform.position);

        _fleeRouteActive = route;
        _fleeRouteCompleted = false;
        _fleeRouteIndex = Mathf.Clamp(progress.segmentIndex, 0, Mathf.Max(0, route.Length - 2));
        _routeReacquireUntilTime = fleeRouteStartFromNearestWaypoint ? Time.time + 0.25f : 0f;

        Vector3 aim = GetPointAheadOnFleePolyline(route, progress.segmentIndex, progress.segmentT, Mathf.Max(30f, fleeRouteLookaheadMeters * 0.5f), fleeRouteLoop);
        currentClosestFleeSegmentIndex = progress.segmentIndex;
        currentFleeLookaheadPoint = aim;
        Vector3 steer = aim - progress.point;
        if (steer.sqrMagnitude < 1e-6f)
            steer = rb.transform.forward;
        else
            steer.Normalize();

        if (showDebugLogs && Time.frameCount % 30 == 0)
        {
            int nextWaypointIndex = Mathf.Clamp(progress.segmentIndex + 1, 0, route.Length - 1);
            string waypointName = route[nextWaypointIndex] != null ? route[nextWaypointIndex].name : "(none)";
            Debug.Log($"[NPCShipAI] Flee route reacquire -> seg={progress.segmentIndex} nextWp={nextWaypointIndex} '{waypointName}' lookahead={aim}", this);
        }

        ApplyRouteSteering(steer, currentFleeLookaheadPoint, 0.8f, fleeSpeedStrength, dt);
        return true;
    }

    FleeRouteProgress UpdateFleeRouteIndex()
    {
        if (_fleeRouteActive == null || _fleeRouteActive.Length == 0)
            return default;

        if (!fleeRouteStartFromNearestWaypoint)
        {
            int sequentialLastSegment = Mathf.Max(0, _fleeRouteActive.Length - 2);
            _fleeRouteIndex = Mathf.Clamp(_fleeRouteIndex, 0, sequentialLastSegment);

            FleeRouteProgress progressSequential = GetProjectedProgressOnSegment(_fleeRouteActive, _fleeRouteIndex, rb.position);
            currentClosestFleeSegmentIndex = _fleeRouteIndex;

            int nextWaypointIndex = Mathf.Min(_fleeRouteIndex + 1, _fleeRouteActive.Length - 1);
            Transform nextWaypoint = _fleeRouteActive[nextWaypointIndex];
            bool reachedNext = nextWaypoint != null && Vector3.Distance(rb.position, nextWaypoint.position) <= fleeRouteAdvanceRadius;
            bool progressedAlongSegment = progressSequential.segmentT >= 0.96f;

            if (reachedNext || progressedAlongSegment)
            {
                if (_fleeRouteIndex < sequentialLastSegment)
                    _fleeRouteIndex++;
                else if (fleeRouteLoop)
                    _fleeRouteIndex = 0;
                else
                    _fleeRouteCompleted = true;

                progressSequential = GetProjectedProgressOnSegment(_fleeRouteActive, _fleeRouteIndex, rb.position);
                currentClosestFleeSegmentIndex = _fleeRouteIndex;
            }

            return progressSequential;
        }

        FleeRouteProgress closest = GetClosestProgressOnFleePolyline(_fleeRouteActive, rb.position);
        currentClosestFleeSegmentIndex = closest.segmentIndex;

        if (Time.time < _routeReacquireUntilTime && fleeRouteStartFromNearestWaypoint && rb != null)
        {
            // Deterministic route reacquire window: project to nearest segment progress and continue authored route.
            _fleeRouteIndex = closest.segmentIndex;
            _fleeRouteCompleted = false;
            return GetProjectedProgressOnSegment(_fleeRouteActive, _fleeRouteIndex, rb.position);
        }

        if (Time.time < _terrainEscapeReacquireUntilTime && fleeRouteStartFromNearestWaypoint && rb != null)
        {
            // Emergency reacquire is allowed to snap to the nearest segment.
            _fleeRouteIndex = closest.segmentIndex;
            _fleeRouteCompleted = false;
            return GetProjectedProgressOnSegment(_fleeRouteActive, _fleeRouteIndex, rb.position);
        }

        int lastSegment = Mathf.Max(0, _fleeRouteActive.Length - 2);
        if (fleeRouteLoop)
        {
            if (_fleeRouteCompleted)
                _fleeRouteCompleted = false;

            Transform lastWp = _fleeRouteActive[_fleeRouteActive.Length - 1];
            if (lastWp != null && closest.segmentIndex >= lastSegment && Vector3.Distance(rb.position, lastWp.position) <= fleeRouteAdvanceRadius)
                _fleeRouteIndex = 0;
            else
                // Normal authored route-follow never moves backward; it only advances along the corridor.
                _fleeRouteIndex = Mathf.Clamp(Mathf.Max(_fleeRouteIndex, closest.segmentIndex), 0, lastSegment);

            _fleeRouteCompleted = false;
        }
        else
        {
            // Normal authored route-follow never moves backward; it only advances along the corridor.
            _fleeRouteIndex = Mathf.Clamp(Mathf.Max(_fleeRouteIndex, closest.segmentIndex), 0, lastSegment);
            Transform lastWp = _fleeRouteActive[_fleeRouteActive.Length - 1];
            if (lastWp != null && closest.segmentIndex >= lastSegment && Vector3.Distance(rb.position, lastWp.position) <= fleeRouteAdvanceRadius)
                _fleeRouteCompleted = true;
        }

        return GetProjectedProgressOnSegment(_fleeRouteActive, _fleeRouteIndex, rb.position);
    }

    /// <summary>Start index of segment (vertex <paramref name="i"/> for segment i→i+1) closest to <paramref name="p"/>.</summary>
    static int ClosestSegmentIndexOnFleePolyline(Transform[] wp, Vector3 p)
    {
        if (wp == null || wp.Length < 2)
            return 0;

        int best = 0;
        float bestSq = float.MaxValue;

        for (int s = 0; s < wp.Length - 1; s++)
        {
            if (wp[s] == null || wp[s + 1] == null)
                continue;

            Vector3 a = wp[s].position;
            Vector3 b = wp[s + 1].position;
            Vector3 ab = b - a;
            float lab = ab.sqrMagnitude;
            if (lab < 1e-8f)
                continue;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lab);
            Vector3 proj = a + ab * t;
            float d = (p - proj).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = s;
            }
        }

        return best;
    }

    static FleeRouteProgress GetClosestProgressOnFleePolyline(Transform[] wp, Vector3 p)
    {
        FleeRouteProgress best = default;
        best.point = p;

        if (wp == null || wp.Length < 2)
            return best;

        float bestSq = float.MaxValue;
        for (int s = 0; s < wp.Length - 1; s++)
        {
            if (wp[s] == null || wp[s + 1] == null)
                continue;

            Vector3 a = wp[s].position;
            Vector3 b = wp[s + 1].position;
            Vector3 ab = b - a;
            float lab = ab.sqrMagnitude;
            if (lab < 1e-8f)
                continue;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lab);
            Vector3 proj = a + ab * t;
            float d = (p - proj).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best.segmentIndex = s;
                best.segmentT = t;
                best.point = proj;
            }
        }

        return best;
    }

    static Vector3 GetPointAheadOnFleePolyline(Transform[] wp, int startSegIndex, float startSegmentT, float aheadMeters, bool loop)
    {
        if (wp == null || wp.Length == 0)
            return Vector3.zero;
        if (wp.Length == 1)
            return wp[0] != null ? wp[0].position : Vector3.zero;

        float remaining = Mathf.Max(0.01f, aheadMeters);
        int seg = Mathf.Clamp(startSegIndex, 0, wp.Length - 2);
        float tOnSegment = Mathf.Clamp01(startSegmentT);

        for (int guard = 0; guard < 128; guard++)
        {
            if (seg >= wp.Length - 1)
            {
                if (!loop)
                    return wp[wp.Length - 1] != null ? wp[wp.Length - 1].position : Vector3.zero;
                seg = 0;
                tOnSegment = 0f;
            }

            Transform ta = wp[seg];
            Transform tb = wp[seg + 1];
            if (ta == null || tb == null)
            {
                seg++;
                continue;
            }

            Vector3 a = ta.position;
            Vector3 b = tb.position;
            Vector3 ab = b - a;
            float lab = ab.magnitude;
            if (lab < 1e-4f)
            {
                seg++;
                continue;
            }

            Vector3 dir = ab / lab;
            Vector3 proj = a + ab * tOnSegment;
            float distAlongToB = lab * (1f - tOnSegment);

            if (remaining <= distAlongToB)
                return proj + dir * remaining;

            remaining -= distAlongToB;
            seg++;
            tOnSegment = 0f;
        }

        return wp[wp.Length - 1] != null ? wp[wp.Length - 1].position : Vector3.zero;
    }

    void OnEnterFleeRouteSetup()
    {
        _fleeRouteActive = _fleeRoutePendingOverride != null && _fleeRoutePendingOverride.Length > 0
            ? _fleeRoutePendingOverride
            : ResolveFleeRouteWaypoints();

        _fleeRoutePendingOverride = null;
        _fleeRouteCompleted = false;
        _fleeRouteIndex = 0;

        if (_fleeRouteActive == null || _fleeRouteActive.Length == 0)
            return;

        if (fleeRouteStartFromNearestWaypoint && rb != null)
            _fleeRouteIndex = ClosestSegmentIndexOnFleePolyline(_fleeRouteActive, rb.position);
        else
            _fleeRouteIndex = 0;

        _routeReacquireUntilTime = 0f;

        currentClosestFleeSegmentIndex = _fleeRouteIndex;
        currentFleeLookaheadPoint = rb != null ? rb.position : transform.position;

        if (showDebugLogs && _fleeRouteActive.Length > 0)
            Debug.Log($"[NPCShipAI] Flee route: {_fleeRouteActive.Length} pts, start index {_fleeRouteIndex}", this);
    }

    /// <summary>Manual array, or transforms built from <see cref="fleeRouteWaypointRoot"/> (root + direct children).</summary>
    Transform[] ResolveFleeRouteWaypoints()
    {
        if (fleeRouteWaypointRoot != null)
        {
            Transform[] built = BuildFleeRouteFromRoot();
            if (built != null && built.Length > 0)
                return built;
        }

        return fleeRouteWaypoints;
    }

    Transform[] BuildFleeRouteFromRoot()
    {
        if (fleeRouteWaypointRoot == null)
            return null;

        var list = new List<Transform>(32);
        if (fleeRouteIncludeRootAsFirstWaypoint)
            list.Add(fleeRouteWaypointRoot);

        var children = new List<Transform>(fleeRouteWaypointRoot.childCount);
        for (int i = 0; i < fleeRouteWaypointRoot.childCount; i++)
        {
            Transform c = fleeRouteWaypointRoot.GetChild(i);
            if (c != null)
                children.Add(c);
        }

        if (fleeRouteChildOrder == NPCShipFleeRouteChildOrder.NameAscending)
            children.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        list.AddRange(children);
        return list.Count > 0 ? list.ToArray() : null;
    }

    void StartBreakAway()
    {
        _breakAwayEndTime = Time.time + breakAwayDurationSeconds;
        EnterState(NPCShipState.BreakAway);
    }

    bool CanDetectTarget()
    {
        if (_target == null || aimOrigin == null)
            return false;

        Vector3 to = _target.position - aimOrigin.position;
        float dist = to.magnitude;
        if (dist > detectionRange + 1f)
            return false;

        Vector3 fwd = aimOrigin.forward;
        float halfCone = viewConeAngleDegrees * 0.5f;
        if (Vector3.Angle(fwd, to) > halfCone + 0.5f)
            return false;

        if (useLineOfSightRaycast && dist > 1f)
        {
            Vector3 dir = to / dist;
            if (Physics.Raycast(aimOrigin.position, dir, out RaycastHit hit, dist, lineOfSightMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(_target))
                    return false;
            }
        }

        return true;
    }

    Vector3 GetSteerTowardTarget(float forwardOffset)
    {
        Vector3 aim = GetAttackAimPoint(forwardOffset);
        Vector3 dir = (aim - rb.position).normalized;
        return dir;
    }

    Vector3 GetAttackAimPoint(float forwardOffset)
    {
        if (_target == null)
            return rb.position + rb.transform.forward * 100f;

        Vector3 basePt = GetLiveTargetPosition();
        if (forwardOffset > 0.01f && attackLateralOffsetMeters > 0.01f)
        {
            Vector3 los = (basePt - rb.position).normalized;
            Vector3 right = Vector3.Cross(los, Vector3.up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.Cross(los, Vector3.forward);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, los).normalized;
            float w = Time.time * attackWobbleFrequency * Mathf.PI * 2f;
            basePt += (right * Mathf.Sin(w) + up * Mathf.Cos(w * 0.73f)) * attackLateralOffsetMeters;
        }

        return basePt;
    }

    Vector3 GetLiveTargetPosition()
    {
        if (_target == null)
            return rb.position + rb.transform.forward * 100f;

        Vector3 p = _target.position;
        Rigidbody trb = _target.GetComponentInParent<Rigidbody>();
        if (trb != null)
        {
            // Small lead improves responsiveness against fast targets.
            p += trb.linearVelocity * 0.2f;
        }
        return p;
    }

    Transform ResolvePreferredTargetTransform(Transform candidate)
    {
        if (candidate == null)
            return null;

        if (!resolveManualTargetToRigidbodyRoot)
            return candidate;

        Rigidbody targetRb = candidate.GetComponentInParent<Rigidbody>();
        if (targetRb != null)
            return targetRb.transform;

        return candidate.root != null ? candidate.root : candidate;
    }

    void ApplySteering(Vector3 worldDir, float forwardThrottle01, float speedStrengthTier, float dt)
    {
        Vector3 desiredDir = worldDir.sqrMagnitude > 1e-8f ? worldDir.normalized : rb.transform.forward;
        Vector3 syntheticPoint = rb.position + desiredDir * Mathf.Max(25f, GetDynamicTerrainProbeDistance());
        ApplySteeringInternal(desiredDir, true, syntheticPoint, forwardThrottle01, speedStrengthTier, dt, 1f, false);
    }

    void ApplySteeringTowardPoint(Vector3 desiredPoint, float forwardThrottle01, float speedStrengthTier, float dt)
    {
        Vector3 desiredDir = desiredPoint - rb.position;
        if (desiredDir.sqrMagnitude < 1e-6f)
            desiredDir = rb.transform.forward;
        else
            desiredDir.Normalize();

        ApplySteeringInternal(desiredDir, true, desiredPoint, forwardThrottle01, speedStrengthTier, dt, 1f, false);
    }

    void EnsurePatrolPoint()
    {
        if (_currentPatrolPoint == Vector3.zero)
            PickNewPatrolPoint();
    }

    void PickNewPatrolPoint()
    {
        if (!_patrolAnchorInitialized)
        {
            _patrolAnchorPosition = rb != null ? rb.position : transform.position;
            _patrolAnchorInitialized = true;
        }

        Vector3 center = patrolCenter != null ? patrolCenter.position : _patrolAnchorPosition;
        Vector2 circle = Random.insideUnitCircle * Mathf.Max(10f, patrolRadius);
        float verticalOffset = Random.Range(-patrolRadius * 0.2f, patrolRadius * 0.2f);
        _currentPatrolPoint = center + new Vector3(circle.x, verticalOffset, circle.y);
        _nextPatrolPointRefreshTime = Time.time + Mathf.Max(1f, patrolPointRefreshSeconds);
    }

    void ApplyRawSteeringInputs(Vector3 worldDir, float forwardThrottle01, float speedStrengthTier, float dt, float bankScale = 1f)
    {
        worldDir = worldDir.sqrMagnitude > 1e-8f ? worldDir.normalized : rb.transform.forward;

        Vector3 local = rb.transform.InverseTransformDirection(worldDir);
        float horiz = Mathf.Sqrt(local.x * local.x + local.z * local.z);
        // HOTAS/flight sign convention: +simulatedPitch should raise the nose.
        // Therefore, when desired local direction is above the nose (local.y > 0), pitchErr should be positive.
        float pitchErr = Mathf.Atan2(local.y, Mathf.Max(1e-4f, horiz));
        float yawErr = Mathf.Atan2(local.x, local.z);

        float deadzoneRad = Mathf.Deg2Rad * Mathf.Max(0f, steeringDeadzoneDegrees);
        if (Mathf.Abs(pitchErr) < deadzoneRad)
            pitchErr = 0f;
        if (Mathf.Abs(yawErr) < deadzoneRad)
            yawErr = 0f;

        float targetPBeforeInvert = Mathf.Clamp(pitchErr * turnResponsiveness, -maxPitchInput, maxPitchInput);
        float targetP = invertAIPitchDebug ? -targetPBeforeInvert : targetPBeforeInvert;
        float targetY = Mathf.Clamp(yawErr * turnResponsiveness, -maxYawInput, maxYawInput);

        // Angular-rate feedback damping: if already rotating in an axis, reduce extra command on that axis.
        Vector3 localAngular = rb != null ? rb.transform.InverseTransformDirection(rb.angularVelocity) : Vector3.zero; // rad/s
        targetP = Mathf.Clamp(targetP - localAngular.x * pitchRateDamping, -maxPitchInput, maxPitchInput);
        targetY = Mathf.Clamp(targetY - localAngular.y * yawRateDamping, -maxYawInput, maxYawInput);

        // Stable bank-angle estimate without 180° wrap flip: right.y ~= sin(bankAngle).
        float bankAngleDeg = Mathf.Asin(Mathf.Clamp(rb.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg;
        float levelBankErrorDeg = Mathf.Abs(bankAngleDeg) <= rollLevelDeadzoneDegrees ? 0f : bankAngleDeg;
        float rollLevelCmd = Mathf.Clamp(-levelBankErrorDeg * rollLevelStrength, -maxRollLevelCommand, maxRollLevelCommand);
        float rollLevelSuppression = 1f - Mathf.Clamp01(Mathf.Abs(targetY)) * Mathf.Clamp01(rollLevelSuppressionFromYaw);
        rollLevelCmd *= Mathf.Clamp01(rollLevelSuppression);

        // Explicit anti-inversion recovery: Asin(right.y) cannot distinguish perfectly level inverted from upright.
        // If ship up points down, push a consistent roll direction until upright again.
        float uprightDot = Vector3.Dot(rb.transform.up, Vector3.up);
        if (uprightDot < -0.05f)
        {
            float signedRollError = Vector3.SignedAngle(rb.transform.up, Vector3.up, rb.transform.forward);
            if (Mathf.Abs(signedRollError) > 2f)
                _inversionRecoveryRollSign = -Mathf.Sign(signedRollError);

            float inversion01 = Mathf.InverseLerp(-0.05f, -0.95f, uprightDot);
            float inversionCmd = _inversionRecoveryRollSign * Mathf.Lerp(0.22f, 1f, inversion01) * Mathf.Clamp01(inversionRecoveryRollStrength);
            rollLevelCmd = Mathf.Clamp(rollLevelCmd + inversionCmd, -1f, 1f);
        }
        else if (uprightDot > 0.2f)
        {
            _inversionRecoveryRollSign = 1f;
        }

        float visualBank = -Mathf.Sign(targetY) * Mathf.Pow(Mathf.Abs(targetY), 0.85f) * turnVisualBankBoost * Mathf.Clamp01(bankScale);
        float targetR = Mathf.Clamp((-targetY * rollFromTurn * Mathf.Clamp01(bankScale)) + visualBank + rollLevelCmd, -1f, 1f);
        targetR = Mathf.Clamp(targetR - localAngular.z * rollRateDamping, -1f, 1f);

        // Coordinated-turn pitch adds a slight arc in turns (reduces flat yaw-only look).
        float coordinatedPitch = Mathf.Abs(targetY) * coordinatedTurnPitchBoost;
        targetP = Mathf.Clamp(targetP + coordinatedPitch, -maxPitchInput, maxPitchInput);

        float smooth = Mathf.Max(0.1f, steeringInputSmoothing);
        _inputPitch = Mathf.MoveTowards(_inputPitch, targetP, dt * smooth);
        _inputYaw = Mathf.MoveTowards(_inputYaw, targetY, dt * smooth * yawResponseScale);
        _inputRoll = Mathf.MoveTowards(_inputRoll, targetR, dt * smooth * rollResponseScale);
        _smoothedForwardThrottle01 = Mathf.MoveTowards(_smoothedForwardThrottle01, Mathf.Clamp01(forwardThrottle01), dt * throttleSmoothing * 0.5f);

        hotas.simulatedPitch = _inputPitch;
        hotas.simulatedRoll = _inputRoll;
        hotas.simulatedYaw = _inputYaw;
        hotas.simulatedThrottle = Forward01ToSimulatedThrottle(_smoothedForwardThrottle01);
        hotas.speedStrength = speedStrengthTier;

        bool inFleeOrTerrainContext = _state == NPCShipState.Flee || altitudeEmergencyActive || routeSteeringOverriddenByEmergency || Time.time < _terrainEscapeUntilTime;
        bool inRecoveryContext = Time.time < _recoverUntilTime;
        if (inFleeOrTerrainContext || inRecoveryContext)
        {
            LogVerticalSteeringDebug(
                inRecoveryContext ? "recover-steer" : "steer",
                local,
                pitchErr,
                targetPBeforeInvert,
                targetP);
        }
    }

    void ApplyRouteSteering(Vector3 routeDir, Vector3 routeLookaheadPoint, float forwardThrottle01, float speedStrengthTier, float dt)
    {
        routeSteeringActive = true;
        Vector3 routeDirNormalized = routeDir.sqrMagnitude > 1e-8f ? routeDir.normalized : rb.transform.forward;
        ApplySteeringInternal(routeDirNormalized, true, routeLookaheadPoint, forwardThrottle01, speedStrengthTier, dt, fleeRouteRollFromTurnScale, true);
    }

    void ApplyRouteSteering(Vector3 routeDir, float forwardThrottle01, float speedStrengthTier, float dt)
    {
        Vector3 routeDirNormalized = routeDir.sqrMagnitude > 1e-8f ? routeDir.normalized : rb.transform.forward;
        Vector3 fallbackPoint = rb.position + routeDirNormalized * Mathf.Max(25f, GetDynamicTerrainProbeDistance());
        ApplyRouteSteering(routeDirNormalized, fallbackPoint, forwardThrottle01, speedStrengthTier, dt);
    }

    void ApplySteeringInternal(
        Vector3 desiredDir,
        bool hasDesiredPoint,
        Vector3 desiredPoint,
        float forwardThrottle01,
        float speedStrengthTier,
        float dt,
        float bankScale,
        bool isRouteSteering)
    {
        hotas.simulatedStrafe = 0f;
        hotas.simulatedVertical = 0f;
        routeSteeringOverriddenByEmergency = false;

        desiredDir = desiredDir.sqrMagnitude > 1e-8f ? desiredDir.normalized : rb.transform.forward;
        _dbgDesiredDirection = desiredDir;

        float probeDistance = GetDynamicTerrainProbeDistance();
        float turnIntensity = GetTurnIntensity(desiredDir);
        probeDistance *= Mathf.Lerp(1f, turningProbeDistanceScale, turnIntensity);
        probeDistance = Mathf.Clamp(probeDistance, Mathf.Max(10f, terrainProbeRadius * 2f), Mathf.Max(terrainProbeRadius * 2f, maxProbeDistance));

        float targetDistance = hasDesiredPoint
            ? Mathf.Clamp(Vector3.Distance(rb.position, desiredPoint), Mathf.Max(8f, terrainProbeRadius * 1.5f), probeDistance)
            : probeDistance;

        _dbgDirectRayDirection = desiredDir;
        _dbgDirectRayDistance = targetDistance;

        bool directPathClear = IsPathClear(desiredDir, targetDistance * directPathSafetyFraction, out float directClearance);
        directPathAccepted = directPathClear;

        Vector3 finalDir = desiredDir;
        float throttleScale = 1f;
        selectedAvoidanceMode = "Direct";
        _dbgCandidateCount = 0;
        _dbgChosenCandidateIndex = -1;

        if (!directPathClear)
        {
            Vector3 right = Vector3.Cross(Vector3.up, desiredDir);
            if (right.sqrMagnitude < 1e-4f)
                right = rb.transform.right;
            right.Normalize();

            Vector3[] candidates = BuildProbeCandidates(desiredDir, right);
            _dbgCandidateCount = Mathf.Min(candidates.Length, _dbgCandidateDirections.Length);

            float bestScore = float.NegativeInfinity;
            int bestIdx = 0;

            for (int i = 0; i < candidates.Length; i++)
            {
                Vector3 c = candidates[i].sqrMagnitude > 1e-8f ? candidates[i].normalized : desiredDir;
                TerrainProbeSample sample = SampleTerrainProbe(c, probeDistance);
                bool immediateCollision = sample.blocked && sample.clearance <= Mathf.Max(immediateCollisionDistance, terrainProbeRadius * 1.2f);

                float groundAtSample = GetProjectedGroundClearance(c, Mathf.Min(probeDistance, Mathf.Max(sample.clearance, terrainProbeRadius * 1.25f)));
                bool lowAltitudeUnsafe = groundAtSample >= 0f && groundAtSample < desiredMinGroundClearance * 0.72f;

                float clearance01 = Mathf.Clamp01(sample.clearance / Mathf.Max(1f, probeDistance));
                float alignment01 = Mathf.Clamp01((Vector3.Dot(c, desiredDir) + 1f) * 0.5f);
                float sidePreference = hasDesiredPoint ? GetRouteSidePreference(c, desiredPoint) : 0f;
                float climbPenalty = Mathf.Max(0f, c.y);

                float score =
                    clearance01 * clearanceScoreWeight +
                    alignment01 * alignmentScoreWeight +
                    sidePreference * waypointSidePreferenceWeight -
                    climbPenalty * climbPenaltyWeight;

                if (lowAltitudeUnsafe)
                    score -= lowAltitudePenaltyWeight;
                if (immediateCollision)
                    score -= immediateCollisionPenaltyWeight;

                if (i < _dbgCandidateDirections.Length)
                {
                    _dbgCandidateDirections[i] = c;
                    _dbgCandidateClearances[i] = sample.clearance;
                    _dbgCandidateBlocked[i] = sample.blocked || lowAltitudeUnsafe;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                    finalDir = c;
                }
            }

            _dbgChosenCandidateIndex = bestIdx;
            selectedAvoidanceMode = "ProbeCandidate";
        }

        float groundVerticalAdd = 0f;
        if (TryApplyGroundClearanceBias(ref finalDir, out groundVerticalAdd) && groundVerticalAdd > 0.01f)
            selectedAvoidanceMode = directPathAccepted ? "Direct+GroundBand" : "Probe+GroundBand";

        bool escapeMemoryActive = false;
        // Collision recovery memory is a last-resort post-contact assist.
        // It can bias steering briefly, but normal raycast-fan navigation remains primary.
        if (Time.time < _terrainEscapeUntilTime)
        {
            finalDir = Vector3.Slerp(finalDir, _terrainEscapeDirection.sqrMagnitude > 1e-5f ? _terrainEscapeDirection.normalized : finalDir, 0.42f).normalized;
            throttleScale *= terrainAvoidanceThrottleScale;
            routeSteeringOverriddenByEmergency = true;
            selectedAvoidanceMode = "CollisionRecoveryMemory";
            escapeMemoryActive = true;
        }

        if (_altitudeEmergencyBoundedThisFrame)
        {
            routeSteeringOverriddenByEmergency = false;
            escapeMemoryActive = false;
            selectedAvoidanceMode = directPathAccepted ? "Direct+GroundBand(Bounded)" : "Probe+GroundBand(Bounded)";
        }

        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        float headingAngleToFinalDir = Vector3.Angle(rb.transform.forward, finalDir);
        float turnThrottleScale = ComputeTurnThrottleScale(finalDir);
        throttleScale *= turnThrottleScale;
        if (turnThrottleScale < 0.995f)
            selectedAvoidanceMode = selectedAvoidanceMode + "+TurnSlow";

        float brakeAuthRange = Mathf.Max(1f, brakeFullAuthoritySpeedRange);
        float speedBrakeAuthority01 = Mathf.InverseLerp(Mathf.Max(0f, brakeMinSpeedForEngage), Mathf.Max(0f, brakeMinSpeedForEngage) + brakeAuthRange, speed);
        if (_state == NPCShipState.Patrol)
            speedBrakeAuthority01 *= Mathf.Clamp01(patrolBrakeScale);

        float hardBrakeStart = Mathf.Max(0f, turnHardBrakeStartAngleDegrees);
        float hardBrakeFull = Mathf.Max(hardBrakeStart + 1f, turnHardBrakeFullAngleDegrees);
        float headingHardBrake01 = Mathf.InverseLerp(hardBrakeStart, hardBrakeFull, headingAngleToFinalDir) * speedBrakeAuthority01;

        // Avoid harsh stop-turn-go in low-urgency states. Keep strongest braking for breakaway/flee.
        float stateBrakeScale = 1f;
        switch (_state)
        {
            case NPCShipState.Patrol:
                stateBrakeScale = 0.01f;
                break;
            case NPCShipState.Chase:
                stateBrakeScale = 0.45f;
                break;
            case NPCShipState.Attack:
                stateBrakeScale = 0.35f;
                break;
            case NPCShipState.BreakAway:
            case NPCShipState.Flee:
                stateBrakeScale = 1f;
                break;
        }
        headingHardBrake01 *= stateBrakeScale;

        float collisionBrake01 = 0f;
        if (!directPathClear)
        {
            float collisionDistance = Mathf.Max(5f, imminentCollisionBrakeDistance);
            collisionBrake01 = Mathf.Clamp01((collisionDistance - directClearance) / collisionDistance) * Mathf.Clamp01(imminentCollisionBrakeStrength) * speedBrakeAuthority01;
            collisionBrake01 *= Mathf.Lerp(0.45f, 1f, stateBrakeScale);
        }

        float rawBrakeDemand = Mathf.Max(headingHardBrake01, collisionBrake01);
        float brakeRate = rawBrakeDemand > _smoothedBrakeDemand
            ? Mathf.Max(0.05f, brakeEngageRate)
            : Mathf.Max(0.05f, brakeReleaseRate);
        _smoothedBrakeDemand = Mathf.MoveTowards(_smoothedBrakeDemand, rawBrakeDemand, dt * brakeRate);

        float hardBrake01 = _smoothedBrakeDemand;
        float reverseDemand01 = Mathf.InverseLerp(Mathf.Clamp01(reverseBrakeStartDemand), 1f, headingHardBrake01) * speedBrakeAuthority01;
        bool allowReverseBrake = !reverseBrakeOnlyInHighUrgencyStates || _state == NPCShipState.BreakAway || _state == NPCShipState.Flee;
        float hardBrakeReverse01 = allowReverseBrake
            ? Mathf.Min(
                Mathf.Clamp01(turnHardBrakeReverseThrottleAtFull) * reverseDemand01,
                Mathf.Clamp01(brakeReverseThrottleClamp))
            : 0f;

        if (hardBrake01 > 0.001f)
            selectedAvoidanceMode = selectedAvoidanceMode + "+HardBrake";
        if (collisionBrake01 > 0.001f)
            selectedAvoidanceMode = selectedAvoidanceMode + "+CollisionBrake";

        _dbgFinalDirection = finalDir;
        float requestedForwardThrottle01 = Mathf.Clamp01(forwardThrottle01 * throttleScale);
        if (hardBrake01 > 0.001f)
            requestedForwardThrottle01 *= (1f - hardBrake01);

        if (_state == NPCShipState.Patrol)
            requestedForwardThrottle01 = Mathf.Max(requestedForwardThrottle01, patrolTurnThrottleFloor);

        ApplyRawSteeringInputs(finalDir, requestedForwardThrottle01, speedStrengthTier, dt, bankScale);

        if (hardBrakeReverse01 > 0.001f)
            hotas.simulatedThrottle = Mathf.Min(hotas.simulatedThrottle, -hardBrakeReverse01);

        // Practical slowdown: throttle floor maps 0..1 to 0.62..1, so also cap speedStrength during sharp turns.
        float turnSlowAmount = Mathf.InverseLerp(1f, Mathf.Clamp01(turnSlowdownMinThrottleScale), turnThrottleScale);
        float speedCapScale = Mathf.Lerp(1f, Mathf.Clamp01(turnSlowdownMinSpeedStrengthScale), Mathf.Max(turnSlowAmount, hardBrake01));
        hotas.speedStrength = Mathf.Min(hotas.speedStrength, speedStrengthTier * speedCapScale);

        if (Mathf.Abs(groundVerticalAdd) > 0.001f)
            hotas.simulatedVertical = Mathf.Clamp(hotas.simulatedVertical + groundVerticalAdd, -1f, 1f);

        if (escapeMemoryActive)
        {
            float escapeVerticalAdd = Mathf.Clamp01(terrainAvoidanceVerticalAddMax) * 0.25f;
            hotas.simulatedVertical = Mathf.Clamp(hotas.simulatedVertical + escapeVerticalAdd, -1f, 1f);
        }

        if (showDebugLogs && Time.frameCount % 20 == 0)
        {
            float shipY = rb != null ? rb.position.y : transform.position.y;
            float desiredY = hasDesiredPoint ? desiredPoint.y : shipY;
            Debug.Log($"[NPCShipAI][Nav] mode={selectedAvoidanceMode} speed={speed:F1} brakeAuth={speedBrakeAuthority01:F2} headingAngle={headingAngleToFinalDir:F1} turnScale={turnThrottleScale:F2} headingBrake={headingHardBrake01:F2} collisionBrake={collisionBrake01:F2} hardBrake01={hardBrake01:F2} rawBrake={rawBrakeDemand:F2} reverse01={hardBrakeReverse01:F2} reqThrottle={requestedForwardThrottle01:F2} smoothedThrottle={_smoothedForwardThrottle01:F2} simThrottle={hotas.simulatedThrottle:F2} speedStrength={hotas.speedStrength:F2} direct={directPathAccepted} directClear={directClearance:F1} probeDist={probeDistance:F1} turn={turnIntensity:F2} desired={_dbgDesiredDirection} final={_dbgFinalDirection} emergency={altitudeEmergencyActive} emergencySec={altitudeEmergencyContinuousSeconds:F2} shipY={shipY:F1} desiredY={desiredY:F1} groundY={currentGroundHeightBelow:F1} downHit={currentDownProbeHit} downClr={currentDownProbeClearance:F1} fwdHit={currentForwardDownProbeHit} fwdClr={currentForwardDownProbeClearance:F1} emergencyLow={altitudeEmergencyFromCurrentLow} emergencyAhead={altitudeEmergencyFromUpcomingTerrain}", this);
        }

        if (!isRouteSteering)
            routeSteeringActive = false;
    }

    Vector3[] BuildProbeCandidates(Vector3 desiredDir, Vector3 right)
    {
        float yaw = yawProbeAngleDegrees > 0.01f ? yawProbeAngleDegrees : terrainProbeSideAngleDegrees;
        float up = upwardProbeAngleDegrees > 0.01f ? upwardProbeAngleDegrees : terrainProbeUpAngleDegrees;

        Vector3 left = Quaternion.AngleAxis(-yaw, Vector3.up) * desiredDir;
        Vector3 rightDir = Quaternion.AngleAxis(yaw, Vector3.up) * desiredDir;
        Vector3 upDir = Quaternion.AngleAxis(-up, right) * desiredDir;
        Vector3 upLeft = Quaternion.AngleAxis(-up * 0.7f, right) * left;
        Vector3 upRight = Quaternion.AngleAxis(-up * 0.7f, right) * rightDir;

        bool allowDownCandidate = TryGetGroundDistance(out float gd) && gd > desiredMaxGroundClearance + 25f;
        if (allowDownCandidate)
        {
            Vector3 downDir = Quaternion.AngleAxis(up * 0.45f, right) * desiredDir;
            return new[] { desiredDir, left, rightDir, upDir, upLeft, upRight, downDir };
        }

        return new[] { desiredDir, left, rightDir, upDir, upLeft, upRight };
    }

    float GetTurnIntensity(Vector3 desiredDir)
    {
        Vector3 local = rb.transform.InverseTransformDirection(desiredDir);
        float horiz = Mathf.Atan2(local.x, Mathf.Max(1e-4f, local.z));
        float pitch = Mathf.Atan2(local.y, Mathf.Max(1e-4f, Mathf.Sqrt(local.x * local.x + local.z * local.z)));
        float demand = Mathf.Clamp01((Mathf.Abs(horiz) + Mathf.Abs(pitch)) / (Mathf.PI * 0.45f));
        float angular = rb != null ? Mathf.Clamp01(rb.angularVelocity.magnitude / 1.5f) : 0f;
        return Mathf.Clamp01(Mathf.Max(demand, angular));
    }

    float ComputeTurnThrottleScale(Vector3 navDirection)
    {
        Vector3 navDir = navDirection.sqrMagnitude > 1e-8f ? navDirection.normalized : rb.transform.forward;
        float headingAngle = Vector3.Angle(rb.transform.forward, navDir);
        float startAngle = Mathf.Max(0f, turnSlowdownStartAngleDegrees);
        float fullAngle = Mathf.Max(startAngle + 1f, turnSlowdownFullAngleDegrees);
        float minScale = Mathf.Clamp01(turnSlowdownMinThrottleScale);

        float turn01 = Mathf.InverseLerp(startAngle, fullAngle, headingAngle);
        if (Time.time < _postRecoverySlowUntilTime)
            turn01 = Mathf.Clamp01(turn01 * Mathf.Max(1f, postRecoveryTurnSlowdownBoost));

        return Mathf.Lerp(1f, minScale, turn01);
    }

    bool IsPathClear(Vector3 direction, float distance, out float clearance)
    {
        clearance = distance;
        if (rb == null)
            return true;

        Vector3 origin = rb.position + Vector3.up * Mathf.Max(1f, terrainProbeUpOffset * 0.6f);
        float directProbeRadius = Mathf.Max(0.1f, terrainProbeRadius, forwardProbeRadius);
        if (Physics.SphereCast(origin, directProbeRadius, direction.normalized, out RaycastHit hit, distance, terrainObstacleLayers, QueryTriggerInteraction.Ignore))
        {
            if (!ShouldIgnoreSteeringCollision(hit.collider))
            {
                clearance = Mathf.Max(0f, hit.distance);
                return false;
            }
        }

        float projectedGround = GetProjectedGroundClearance(direction, distance);
        if (projectedGround >= 0f && projectedGround < desiredMinGroundClearance * 0.72f)
        {
            clearance = Mathf.Min(clearance, Mathf.Lerp(distance, 0f, Mathf.Clamp01((desiredMinGroundClearance - projectedGround) / Mathf.Max(1f, desiredMinGroundClearance))));
            return false;
        }

        return true;
    }

    float GetProjectedGroundClearance(Vector3 direction, float distance)
    {
        Vector3 point = rb.position + direction.normalized * Mathf.Max(0f, distance);
        if (TryProbeGroundAtPoint(point, downProbeOriginUpOffset, out GroundProbeInfo ground))
            return ground.clearance;
        return -1f;
    }

    float GetRouteSidePreference(Vector3 candidateDir, Vector3 desiredPoint)
    {
        Vector3 targetLocal = rb.transform.InverseTransformPoint(desiredPoint);
        if (Mathf.Abs(targetLocal.x) < 0.5f)
            return 0f;

        float sideSign = Mathf.Sign(targetLocal.x);
        Vector3 candidateLocal = rb.transform.InverseTransformDirection(candidateDir);
        return Mathf.Sign(candidateLocal.x) == sideSign ? 1f : -1f;
    }

    bool TryApplyGroundClearanceBias(ref Vector3 direction, out float verticalAdd)
    {
        verticalAdd = 0f;
        if (rb == null)
            return false;

        direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : rb.transform.forward;

        bool hasDown = TryProbeGroundBelow(out GroundProbeInfo downInfo);
        bool hasAhead = TryProbeGroundAhead(direction, out GroundProbeInfo aheadInfo);

        currentDownProbeHit = hasDown;
        currentForwardDownProbeHit = hasAhead;
        currentDownProbeClearance = hasDown ? downInfo.clearance : -1f;
        currentForwardDownProbeClearance = hasAhead ? aheadInfo.clearance : -1f;
        currentGroundHeightBelow = hasDown ? downInfo.groundY : 0f;
        currentForwardGroundHeight = hasAhead ? aheadInfo.groundY : 0f;
        currentGroundDistance = currentDownProbeClearance;

        if (!hasDown && !hasAhead)
        {
            altitudeEmergencyActive = false;
            altitudeEmergencyFromCurrentLow = false;
            altitudeEmergencyFromUpcomingTerrain = false;
            _altitudeEmergencyStartTime = -1f;
            return false;
        }

        float upBias = 0f;
        float downBias = 0f;
        if (hasDown)
        {
            if (downInfo.clearance < desiredMinGroundClearance)
                upBias = Mathf.Clamp01((desiredMinGroundClearance - downInfo.clearance) / Mathf.Max(1f, desiredMinGroundClearance));
            else if (downInfo.clearance > desiredMaxGroundClearance)
                downBias = Mathf.Clamp01((downInfo.clearance - desiredMaxGroundClearance) / Mathf.Max(1f, desiredMaxGroundClearance));
        }

        float upcomingUpBias = 0f;
        if (hasAhead && aheadInfo.clearance < upcomingTerrainEmergencyClearance)
        {
            upcomingUpBias = Mathf.Clamp01((upcomingTerrainEmergencyClearance - aheadInfo.clearance) / Mathf.Max(1f, upcomingTerrainEmergencyClearance));
            upcomingUpBias *= Mathf.Clamp01(upcomingTerrainUpBiasWeight);
        }

        altitudeEmergencyFromCurrentLow = hasDown && downInfo.clearance < desiredMinGroundClearance * 0.55f;
        altitudeEmergencyFromUpcomingTerrain = hasAhead && aheadInfo.clearance < upcomingTerrainEmergencyClearance;
        altitudeEmergencyActive = altitudeEmergencyFromCurrentLow || altitudeEmergencyFromUpcomingTerrain;

        float net = (upBias + upcomingUpBias) - downBias;
        if (Mathf.Abs(net) >= 0.001f)
        {
            direction = (direction + Vector3.up * net * groundClearancePitchWeight).normalized;
            if (altitudeSteerUpBlend > 0.001f && net > 0f)
                direction = Vector3.Slerp(direction, Vector3.up, Mathf.Clamp01(net) * altitudeSteerUpBlend).normalized;

            verticalAdd = Mathf.Clamp(net * groundClearanceVerticalWeight, -0.75f, 0.95f);
        }

        if (hasDown && downInfo.clearance < desiredMinGroundClearance && direction.y < 0.02f)
            direction = new Vector3(direction.x, Mathf.Max(0.02f, direction.y), direction.z).normalized;

        if (!altitudeEmergencyActive)
            _altitudeEmergencyStartTime = -1f;
        else if (_altitudeEmergencyStartTime < 0f)
            _altitudeEmergencyStartTime = Time.time;

        if (altitudeEmergencyFromCurrentLow && _altitudeEmergencyStartTime > 0f)
        {
            float emergencyTime = Time.time - _altitudeEmergencyStartTime;
            if (emergencyTime > maxContinuousRouteAltitudeEmergencySeconds)
            {
                verticalAdd = Mathf.Min(verticalAdd, 0.2f);

                _altitudeEmergencyBoundedThisFrame = true;
                _terrainEscapeUntilTime = Mathf.Min(_terrainEscapeUntilTime, Time.time);
                _terrainEscapeReacquireUntilTime = Mathf.Max(_terrainEscapeReacquireUntilTime, Time.time + terrainEscapeReacquireDelaySeconds);
                _routeReacquireUntilTime = Mathf.Max(_routeReacquireUntilTime, Time.time + 0.3f);

                if (showDebugLogs && Time.frameCount % 25 == 0)
                    Debug.Log($"[NPCShipAI] Altitude emergency bounded after {emergencyTime:F2}s; forcing route/nav reacquire.", this);
            }
        }

        return true;
    }

    void UpdateStuckRecovery(float dt)
    {
        bool nearGround = TryGetGroundDistance(out float groundDistance) && groundDistance <= groundCheckDistance;
        bool lowSpeed = rb.linearVelocity.magnitude < stuckSpeedThreshold;
        if (nearGround && lowSpeed)
            _stuckTimer += dt;
        else
            _stuckTimer = 0f;

        if (_stuckTimer >= stuckDurationSeconds)
        {
            _stuckTimer = 0f;
            _recoverUntilTime = Time.time + recoverDurationSeconds;
            if (showDebugLogs)
                Debug.Log("[NPCShipAI] Stuck near ground: recovery burst", this);
        }
    }

    void TickRecover(float dt)
    {
        Vector3 recoverDir = _terrainEscapeDirection.sqrMagnitude > 1e-5f
            ? _terrainEscapeDirection
            : (_lastTerrainCollisionNormal + Vector3.up * 1.25f + rb.transform.forward * 0.35f).normalized;

        // Last-resort collision recovery path (post-contact only):
        // start from normal nav steering, then temporarily hard-bias controls to separate from terrain.
        ApplySteering(recoverDir, Mathf.Max(recoverThrottle01, terrainCollisionEscapeThrottle01), fleeSpeedStrength, dt);

        Vector3 n = _lastTerrainCollisionNormal.sqrMagnitude > 0.01f ? _lastTerrainCollisionNormal.normalized : Vector3.up;
        Vector3 localAway = rb.transform.InverseTransformDirection(n);
        hotas.simulatedStrafe = Mathf.Clamp(localAway.x * 0.9f, -1f, 1f);
        hotas.simulatedVertical = Mathf.Clamp(Mathf.Max(recoverVerticalUp, terrainCollisionEscapeVertical), 0f, 1f);
        ApplyWorldLevelingInputs(dt, 1.35f, out float levelPitchCmd, out float levelRollCmd);
        hotas.simulatedPitch = Mathf.Clamp(Mathf.Max(hotas.simulatedPitch, Mathf.Max(levelPitchCmd, altitudePitchAddMax * 1.75f)), -1f, 1f);
        hotas.simulatedRoll = Mathf.Clamp(levelRollCmd, -1f, 1f);

        // During recovery, do not keep driving into obstacles. Force neutral/reverse throttle.
        float intoObstacle = Vector3.Dot(rb.transform.forward, -n);
        hotas.simulatedThrottle = intoObstacle > 0.15f ? -1f : Mathf.Min(hotas.simulatedThrottle, 0f);

        if (rb != null && terrainRecoverUnstickAcceleration > 0f)
            rb.AddForce((n + Vector3.up * 0.35f).normalized * terrainRecoverUnstickAcceleration, ForceMode.Acceleration);

        // If still close to obstruction, hold recovery a bit longer.
        if (Physics.SphereCast(rb.position + Vector3.up * Mathf.Max(1f, terrainProbeUpOffset), Mathf.Max(0.1f, terrainProbeRadius * 0.85f), rb.transform.forward,
            out RaycastHit _, Mathf.Max(6f, terrainProbeRadius * 2.2f), terrainObstacleLayers, QueryTriggerInteraction.Ignore))
        {
            _recoverUntilTime = Mathf.Max(_recoverUntilTime, Time.time + 0.25f);
            _recoverStabilizePending = true;
        }

        Vector3 recoverLocal = rb.transform.InverseTransformDirection(recoverDir.sqrMagnitude > 1e-8f ? recoverDir.normalized : rb.transform.forward);
        float recoverHoriz = Mathf.Sqrt(recoverLocal.x * recoverLocal.x + recoverLocal.z * recoverLocal.z);
        float recoverPitchErr = Mathf.Atan2(recoverLocal.y, Mathf.Max(1e-4f, recoverHoriz));
        float recoverTargetPBeforeInvert = Mathf.Clamp(recoverPitchErr * turnResponsiveness, -maxPitchInput, maxPitchInput);
        float recoverTargetPAfterInvert = invertAIPitchDebug ? -recoverTargetPBeforeInvert : recoverTargetPBeforeInvert;
        LogVerticalSteeringDebug("recover-final", recoverLocal, recoverPitchErr, recoverTargetPBeforeInvert, recoverTargetPAfterInvert);
    }

    void TickRecoverStabilize(float dt)
    {
        if (hotas == null)
            return;

        float smooth = Mathf.Max(0.1f, steeringInputSmoothing);
        ApplyWorldLevelingInputs(dt, 1.85f, out float levelPitchCmd, out float levelRollCmd);
        hotas.simulatedPitch = levelPitchCmd;
        hotas.simulatedRoll = levelRollCmd;

        // While leveling, keep yawing toward intended nav direction (especially useful for flee waypoints).
        Vector3 recoverYawDir = GetRecoveryStabilizeYawDirection();
        Vector3 localYawDir = rb.transform.InverseTransformDirection(recoverYawDir);
        float yawErr = Mathf.Atan2(localYawDir.x, Mathf.Max(1e-4f, localYawDir.z));
        float yawCmd = Mathf.Clamp(yawErr * (turnResponsiveness * 0.9f), -maxYawInput, maxYawInput);
        hotas.simulatedYaw = Mathf.MoveTowards(hotas.simulatedYaw, yawCmd, dt * smooth * 1.25f);

        hotas.simulatedStrafe = 0f;
        hotas.simulatedVertical = Mathf.Clamp01(recoverStabilizeVerticalUp);
        hotas.simulatedThrottle = Mathf.Min(hotas.simulatedThrottle, 0f);
        hotas.speedStrength = fleeSpeedStrength;
    }

    Vector3 GetRecoveryStabilizeYawDirection()
    {
        Vector3 desired = rb != null ? rb.transform.forward : transform.forward;

        if (_state == NPCShipState.Flee)
        {
            if (currentFleeLookaheadPoint != Vector3.zero)
            {
                Vector3 toLookahead = currentFleeLookaheadPoint - rb.position;
                if (toLookahead.sqrMagnitude > 4f)
                    desired = toLookahead.normalized;
            }
            else if (_target != null)
            {
                Vector3 away = rb.position - GetLiveTargetPosition();
                if (away.sqrMagnitude > 4f)
                    desired = away.normalized;
            }
        }
        else if ((_state == NPCShipState.Chase || _state == NPCShipState.Attack) && _target != null)
        {
            Vector3 toTarget = GetLiveTargetPosition() - rb.position;
            if (toTarget.sqrMagnitude > 4f)
                desired = toTarget.normalized;
        }
        else if (_state == NPCShipState.Patrol)
        {
            if (waypoints != null && waypoints.Length > 0)
            {
                Transform wp = waypoints[_waypointIndex % waypoints.Length];
                if (wp != null)
                {
                    Vector3 toWp = wp.position - rb.position;
                    if (toWp.sqrMagnitude > 4f)
                        desired = toWp.normalized;
                }
            }
            else
            {
                Vector3 toPatrol = _currentPatrolPoint - rb.position;
                if (toPatrol.sqrMagnitude > 4f)
                    desired = toPatrol.normalized;
            }
        }

        desired = Vector3.ProjectOnPlane(desired, Vector3.up);
        if (desired.sqrMagnitude < 1e-6f)
            return Vector3.ProjectOnPlane(rb.transform.forward, Vector3.up).normalized;

        return desired.normalized;
    }

    void ApplyWorldLevelingInputs(float dt, float gain, out float pitchCmd, out float rollCmd)
    {
        // Level attitude to world: ship up -> world up, and nose toward horizon.
        float smooth = Mathf.Max(0.1f, steeringInputSmoothing);
        float forwardY = Mathf.Clamp(rb.transform.forward.y, -1f, 1f);
        float rollAngleDeg = Vector3.SignedAngle(rb.transform.up, Vector3.up, rb.transform.forward);

        float targetPitch = Mathf.Clamp((forwardY) * gain, -maxPitchInput, maxPitchInput);
        float targetRoll = Mathf.Clamp((-rollAngleDeg / 45f) * gain, -1f, 1f);

        pitchCmd = Mathf.MoveTowards(hotas.simulatedPitch, targetPitch, dt * smooth * 1.5f);
        rollCmd = Mathf.MoveTowards(hotas.simulatedRoll, targetRoll, dt * smooth * 1.5f);
    }

    void LogVerticalSteeringDebug(string context, Vector3 localTargetDirection, float pitchErr, float targetPBeforeInvert, float targetPAfterInvert)
    {
        if (!showDebugLogs)
            return;

        if (Time.frameCount % 10 != 0)
            return;

        float groundDistance;
        bool hasGroundDistance = TryGetGroundDistance(out groundDistance);
        string groundText = hasGroundDistance ? groundDistance.ToString("F1") : "n/a";

        Debug.Log(
            $"[NPCShipAI][Vertical] ctx={context} state={_state} localDir={localTargetDirection} localY={localTargetDirection.y:F3} " +
            $"pitchErr={pitchErr:F3} targetP={targetPBeforeInvert:F3} finalTargetP={targetPAfterInvert:F3} " +
            $"invertAIPitchDebug={invertAIPitchDebug} " +
            $"finalPitch={hotas.simulatedPitch:F3} finalVertical={hotas.simulatedVertical:F3} altitudeEmergencyActive={altitudeEmergencyActive} " +
            $"routeSteeringOverriddenByEmergency={routeSteeringOverriddenByEmergency} groundDistance={groundText} shipY={rb.position.y:F1} " +
            $"targetY={(_target != null ? _target.position.y : 0f):F1} lookaheadY={currentFleeLookaheadPoint.y:F1} groundY={currentGroundHeightBelow:F1} " +
            $"downHit={currentDownProbeHit} downClr={currentDownProbeClearance:F1} fwdHit={currentForwardDownProbeHit} fwdClr={currentForwardDownProbeClearance:F1} " +
            $"emergencyLow={altitudeEmergencyFromCurrentLow} emergencyAhead={altitudeEmergencyFromUpcomingTerrain}",
            this);
    }

    static FleeRouteProgress GetProjectedProgressOnSegment(Transform[] wp, int segmentIndex, Vector3 p)
    {
        FleeRouteProgress progress = default;
        progress.point = p;

        if (wp == null || wp.Length < 2)
            return progress;

        int seg = Mathf.Clamp(segmentIndex, 0, wp.Length - 2);
        Transform ta = wp[seg];
        Transform tb = wp[seg + 1];
        if (ta == null || tb == null)
        {
            progress.segmentIndex = seg;
            return progress;
        }

        Vector3 a = ta.position;
        Vector3 b = tb.position;
        Vector3 ab = b - a;
        float lab = ab.sqrMagnitude;
        float t = lab > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / lab) : 0f;

        progress.segmentIndex = seg;
        progress.segmentT = t;
        progress.point = a + ab * t;
        return progress;
    }

    float GetDynamicTerrainProbeDistance()
    {
        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        float baseDistance = Mathf.Max(baseProbeDistance, terrainProbeDistance);
        float lookaheadFactor = Mathf.Max(0.05f, probeLookaheadSeconds, terrainProbeDistancePerSpeed * 0.35f);
        float dynamicDistance = baseDistance + speed * lookaheadFactor;
        dynamicDistance = Mathf.Min(dynamicDistance, Mathf.Max(baseProbeDistance, maxProbeDistance));
        return Mathf.Max(terrainProbeRadius * 2f, dynamicDistance);
    }

    TerrainProbeSample SampleTerrainProbe(Vector3 dir, float probeDistance)
    {
        TerrainProbeSample sample = new TerrainProbeSample
        {
            direction = dir.sqrMagnitude > 1e-5f ? dir.normalized : Vector3.forward,
            clearance = probeDistance,
            normal = Vector3.up,
            blocked = false,
        };

        Vector3 origin = rb.position + Vector3.up * Mathf.Max(1f, terrainProbeUpOffset);
        float sampleProbeRadius = Mathf.Max(0.1f, terrainProbeRadius, forwardProbeRadius * 0.8f);
        if (Physics.SphereCast(origin, sampleProbeRadius, sample.direction, out RaycastHit hit, probeDistance, terrainObstacleLayers, QueryTriggerInteraction.Ignore)
            && !ShouldIgnoreSteeringCollision(hit.collider)
            && (hit.transform == null || !hit.transform.IsChildOf(transform)))
        {
            sample.blocked = true;
            sample.clearance = Mathf.Max(0f, hit.distance);
            sample.normal = hit.normal;
        }

        float lookahead = Mathf.Min(probeDistance, Mathf.Max(terrainProbeRadius * 2f, sample.clearance));
        Vector3 samplePoint = rb.position + sample.direction * lookahead;
        if (TryProbeGroundAtPoint(samplePoint, downProbeOriginUpOffset, out GroundProbeInfo groundInfo))
        {
            float terrainClearance = groundInfo.clearance;
            float desiredVerticalClearance = minAltitudeAboveGround + terrainProbeUpOffset * 0.5f;
            if (terrainClearance < desiredVerticalClearance)
            {
                float pseudoClearance = Mathf.Lerp(probeDistance, 0f, Mathf.Clamp01((desiredVerticalClearance - terrainClearance) / Mathf.Max(1f, desiredVerticalClearance)));
                if (!sample.blocked || pseudoClearance < sample.clearance)
                {
                    sample.blocked = true;
                    sample.clearance = pseudoClearance;
                    sample.normal = Vector3.up;
                }
            }
        }

        return sample;
    }

    void BeginTerrainEscape(Vector3 escapeDir, float urgency)
    {
        Vector3 resolved = escapeDir.sqrMagnitude > 1e-5f ? escapeDir.normalized : Vector3.up;
        if (_lastTerrainCollisionNormal.sqrMagnitude > 0.1f)
            resolved = Vector3.Slerp(resolved, (_lastTerrainCollisionNormal + Vector3.up).normalized, 0.35f).normalized;

        _terrainEscapeDirection = resolved;
        _terrainEscapeUntilTime = Mathf.Max(_terrainEscapeUntilTime, Time.time + Mathf.Lerp(terrainEscapeHoldSeconds * 0.6f, terrainEscapeHoldSeconds * 1.4f, Mathf.Clamp01(urgency)));
        _terrainEscapeReacquireUntilTime = Mathf.Max(_terrainEscapeReacquireUntilTime, Time.time + terrainEscapeReacquireDelaySeconds);
    }

    void HandleTerrainCollision(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;

        if (ShouldIgnoreSteeringCollision(collision.collider))
        {
            _ignoredCombatCollisionUntilTime = Time.time + 0.35f;
            return;
        }

        if (collision.collider.transform != null && collision.collider.transform.IsChildOf(transform))
            return;

        if (((1 << collision.collider.gameObject.layer) & terrainObstacleLayers.value) == 0)
            return;

        if (Time.time < _nextCollisionRecoveryAllowedTime)
            return;

        _lastTerrainCollisionCollider = collision.collider;
        if (collision.contactCount > 0)
            _lastTerrainCollisionNormal = collision.GetContact(0).normal;
        else
            _lastTerrainCollisionNormal = Vector3.up;

        _recoverUntilTime = Mathf.Max(_recoverUntilTime, Time.time + terrainCollisionRecoveryDurationSeconds);
        _recoverStabilizePending = true;
        _recoverStabilizeUntilTime = Mathf.Max(_recoverStabilizeUntilTime, 0f);
        _nextCollisionRecoveryAllowedTime = Time.time + 0.35f;
        BeginTerrainEscape((_lastTerrainCollisionNormal + Vector3.up * 1.2f + rb.transform.forward * 0.2f).normalized, 1f);

        if (rb != null)
        {
            Vector3 vel = rb.linearVelocity;
            float intoTerrain = Vector3.Dot(vel, -_lastTerrainCollisionNormal);
            if (intoTerrain > 0f)
                rb.linearVelocity += _lastTerrainCollisionNormal * (intoTerrain + 4f);
        }

        if (showDebugLogs && Time.frameCount % 15 == 0)
            Debug.Log($"[NPCShipAI] Terrain collision recovery against '{collision.collider.name}' normal={_lastTerrainCollisionNormal}", this);
    }

    bool ShouldIgnoreSteeringCollision(Collider collider)
    {
        if (collider == null)
            return true;
        if (collider.transform != null && collider.transform.IsChildOf(transform))
            return true;
        if (collider.GetComponentInParent<WeaponProjectile>() != null)
            return true;

        Rigidbody hitRb = collider.attachedRigidbody;
        if (hitRb != null && hitRb.GetComponentInParent<WeaponProjectile>() != null)
            return true;

        string layerName = LayerMask.LayerToName(collider.gameObject.layer);
        if (!string.IsNullOrEmpty(layerName))
        {
            string lowerLayer = layerName.ToLowerInvariant();
            if (lowerLayer.Contains("projectile") || lowerLayer.Contains("weapon") || lowerLayer.Contains("vfx"))
                return true;
        }

        return false;
    }

    bool TryGetGroundDistance(out float groundDistance)
    {
        groundDistance = -1f;
        if (TryProbeGroundBelow(out GroundProbeInfo info))
        {
            groundDistance = info.clearance;
            return true;
        }

        return false;
    }

    bool TryProbeGroundBelow(out GroundProbeInfo info)
    {
        return TryProbeGroundAtPoint(rb != null ? rb.position : transform.position, downProbeOriginUpOffset, out info);
    }

    bool TryProbeGroundAhead(Vector3 flightDirection, out GroundProbeInfo info)
    {
        info = default;
        info.clearance = -1f;

        Vector3 fwd = Vector3.ProjectOnPlane(flightDirection.sqrMagnitude > 1e-8f ? flightDirection.normalized : (rb != null ? rb.transform.forward : transform.forward), Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f)
            fwd = rb != null ? rb.transform.forward : transform.forward;
        fwd.Normalize();

        Vector3 originBase = rb != null ? rb.position : transform.position;
        Vector3 origin = originBase + Vector3.up * Mathf.Max(0.25f, forwardDownProbeOriginUpOffset);

        float downFactor = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(forwardDownProbeAngleDegrees, 5f, 85f));
        Vector3 rayDir = (fwd + Vector3.down * downFactor).normalized;

        float horizDot = Mathf.Max(0.15f, Vector3.Dot(rayDir, fwd));
        float rayDistance = Mathf.Max(8f, Mathf.Max(groundProbeDistance, forwardDownProbeForwardDistance / horizDot));
        if (!TryGetFirstValidGroundHit(origin, rayDir, rayDistance, out RaycastHit hit))
            return false;

        info.hit = true;
        info.point = hit.point;
        info.normal = hit.normal;
        info.groundY = hit.point.y;
        info.clearance = Mathf.Max(0f, originBase.y - hit.point.y);
        return true;
    }

    bool TryProbeGroundAtPoint(Vector3 point, float originUpOffset, out GroundProbeInfo info)
    {
        info = default;
        info.clearance = -1f;

        float effectiveOriginUp = Mathf.Max(0.25f, originUpOffset, altitudeProbeStartHeight * 0.1f);
        Vector3 origin = point + Vector3.up * effectiveOriginUp;
        float maxDistance = Mathf.Max(10f, groundProbeDistance + effectiveOriginUp);
        if (!TryGetFirstValidGroundHit(origin, Vector3.down, maxDistance, out RaycastHit hit))
            return false;

        info.hit = true;
        info.point = hit.point;
        info.normal = hit.normal;
        info.groundY = hit.point.y;
        info.clearance = Mathf.Max(0f, point.y - hit.point.y);
        return true;
    }

    bool ShouldIgnoreGroundProbeHit(Collider collider)
    {
        if (collider == null)
            return true;

        if (collider.transform != null && collider.transform.IsChildOf(transform))
            return true;

        if (ShouldIgnoreSteeringCollision(collider))
            return true;

        return false;
    }

    bool TryGetFirstValidGroundHit(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit bestHit)
    {
        bestHit = default;
        RaycastHit[] hits = Physics.RaycastAll(origin, direction.normalized, maxDistance, altitudeGroundLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        bool found = false;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (ShouldIgnoreGroundProbeHit(hit.collider))
                continue;

            float normalY = hit.normal.sqrMagnitude > 1e-8f ? hit.normal.normalized.y : 1f;
            if (normalY < minGroundProbeNormalY)
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        return found;
    }

    bool TryGetGroundHeightAt(Vector3 worldPos, out float groundY)
    {
        groundY = 0f;

        Terrain activeTerrain = Terrain.activeTerrain;
        if (activeTerrain == null || activeTerrain.terrainData == null)
            return false;

        Vector3 terrainPos = activeTerrain.transform.position;
        Vector3 size = activeTerrain.terrainData.size;
        if (worldPos.x < terrainPos.x || worldPos.x > terrainPos.x + size.x ||
            worldPos.z < terrainPos.z || worldPos.z > terrainPos.z + size.z)
            return false;

        groundY = activeTerrain.SampleHeight(worldPos) + terrainPos.y;
        return true;
    }

    static float Forward01ToSimulatedThrottle(float forward01)
    {
        forward01 = Mathf.Clamp01(forward01);
        return Mathf.Lerp(0.62f, 1f, forward01);
    }

    float GetSpeedStrengthForState()
    {
        switch (_state)
        {
            case NPCShipState.Patrol: return patrolSpeedStrength;
            case NPCShipState.Chase: return chaseSpeedStrength;
            case NPCShipState.Attack: return attackSpeedStrength;
            case NPCShipState.BreakAway: return breakAwaySpeedStrength;
            case NPCShipState.Flee: return fleeSpeedStrength;
            default: return patrolSpeedStrength;
        }
    }

    void TryBurstFire(float dist)
    {
        if (weapons == null)
            return;

        float desiredBurstDuration = GetDesiredBurstDuration();
        float desiredBurstCooldown = GetDesiredBurstCooldown();

        if (_burstActive && Time.time >= _burstEndTime)
        {
            _burstActive = false;
            _nextBurstAllowedTime = Time.time + desiredBurstCooldown;
            return;
        }

        if (dist > fireRange || !CanEngageWeapons())
            return;

        Vector3 aimPoint = GetAttackAimPoint(1f);
        Vector3 toAim = (aimPoint - rb.position).normalized;
        float dot = Vector3.Dot(rb.transform.forward, toAim);
        float noise = (Mathf.PerlinNoise(Time.time * aimNoiseFrequency, 0.7f) - 0.5f) * 2f * aimNoiseAmplitude;
        float threshold = Mathf.Clamp(aimDotThreshold - noise, 0.5f, 0.999f);
        if (dot < threshold)
            return;

        if (!_burstActive && Time.time < _nextBurstAllowedTime)
            return;

        if (!_burstActive)
        {
            _burstActive = true;
            _burstEndTime = Time.time + desiredBurstDuration;
        }

        DeployAllHardpointsIfNeeded();

        bool anyFired = false;
        lastWeaponBlockReason = string.Empty;
        bool useKinetic = ShouldPreferKineticWeapons();
        activeWeaponMode = useKinetic ? "Kinetic" : "Laser";

        if (showDebugLogs && Time.frameCount % 20 == 0)
            Debug.Log($"[NPCShipAI] Weapon mode selected -> {activeWeaponMode}", this);

        for (int i = 0; i < weapons.Length; i++)
        {
            WeaponController w = weapons[i];
            if (w == null)
                continue;

            if (useKinetic)
            {
                if (w.WeaponDamageType != DamageType.Kinetic)
                    continue;
            }
            else
            {
                if (w.WeaponDamageType == DamageType.Kinetic)
                    continue;
            }

            if (w.TryFire())
            {
                anyFired = true;
                continue;
            }

            if (string.IsNullOrEmpty(lastWeaponBlockReason))
                lastWeaponBlockReason = $"{w.name}: {w.GetShootBlockReason()}";
        }

        if (!anyFired && showDebugLogs && !string.IsNullOrEmpty(lastWeaponBlockReason) && Time.frameCount % 20 == 0)
            Debug.Log($"[NPCShipAI] Fire blocked -> {lastWeaponBlockReason}", this);
    }

    bool ShouldPreferKineticWeapons()
    {
        if (_target == null)
            return false;

        ShieldSystem targetShields = _target.GetComponentInChildren<ShieldSystem>(true);
        if (targetShields == null)
            return false;

        float minShield = 1f;
        minShield = Mathf.Min(minShield, targetShields.GetArcNormalized(ShieldArc.Front));
        minShield = Mathf.Min(minShield, targetShields.GetArcNormalized(ShieldArc.Left));
        minShield = Mathf.Min(minShield, targetShields.GetArcNormalized(ShieldArc.Right));
        minShield = Mathf.Min(minShield, targetShields.GetArcNormalized(ShieldArc.Rear));
        return minShield <= targetShieldDownThreshold;
    }

    float GetDesiredBurstDuration()
    {
        return ShouldPreferKineticWeapons() ? Mathf.Max(burstDurationSeconds, gatlingBurstDurationSeconds) : burstDurationSeconds;
    }

    float GetDesiredBurstCooldown()
    {
        return ShouldPreferKineticWeapons() ? gatlingBurstCooldownSeconds : burstCooldownSeconds;
    }

    void ClearBurst()
    {
        _burstActive = false;
        _nextBurstAllowedTime = Time.time;
    }

    void DeployHardpointIfNeeded()
    {
        if (_hardpointDeployed || weaponHardpoint == null)
            return;
        weaponHardpoint.TrySetDeployed(true);
        _hardpointDeployed = true;
    }

    void DeployAllHardpointsIfNeeded()
    {
        bool deployedAny = false;

        if (weaponHardpoints != null)
        {
            for (int i = 0; i < weaponHardpoints.Length; i++)
            {
                WeaponHardpoint hp = weaponHardpoints[i];
                if (hp == null)
                    continue;
                hp.TrySetDeployed(true);
                deployedAny = true;
            }
        }

        if (!deployedAny)
            DeployHardpointIfNeeded();

        _hardpointDeployed = true;
    }

    void EnterState(NPCShipState s)
    {
        if (_state == NPCShipState.Dead)
            return;
        if (_state == s)
            return;

        _state = s;
        _stateEnterTime = Time.time;
        if (s == NPCShipState.Chase || s == NPCShipState.Attack)
        {
            _lostTargetTimer = 0f;
            _combatTargetMemoryUntil = Mathf.Max(_combatTargetMemoryUntil, Time.time + Mathf.Max(0.5f, combatTargetMemorySeconds));
        }
        if (s == NPCShipState.Patrol)
        {
            if (!_patrolAnchorInitialized)
            {
                _patrolAnchorPosition = rb != null ? rb.position : transform.position;
                _patrolAnchorInitialized = true;
            }
            PickNewPatrolPoint();
        }

        if (s == NPCShipState.Flee)
            OnEnterFleeRouteSetup();

        if (s == NPCShipState.Attack && _wantsImmediateCombatFire)
        {
            _nextBurstAllowedTime = Time.time;
            _burstActive = false;
            _wantsImmediateCombatFire = false;
        }

        if (showDebugLogs)
            Debug.Log($"[NPCShipAI] {name} → {s}", this);
    }

    void ClearHotas()
    {
        if (hotas == null)
            return;
        hotas.simulatedPitch = 0f;
        hotas.simulatedRoll = 0f;
        hotas.simulatedYaw = 0f;
        hotas.simulatedStrafe = 0f;
        hotas.simulatedVertical = 0f;
        hotas.simulatedThrottle = 0f;
    }

    public float CurrentHullFraction => currentHullFraction;
    public float CurrentMinShieldFraction => currentMinShieldFraction;
    public float CurrentFrontShieldFraction => currentFrontShieldFraction;
    public float CurrentLeftShieldFraction => currentLeftShieldFraction;
    public float CurrentRightShieldFraction => currentRightShieldFraction;
    public float CurrentRearShieldFraction => currentRearShieldFraction;

    void OnGUI()
    {
        if (!showStateOnGui)
            return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = Color.yellow } };
        GUI.Label(new Rect(12, Screen.height - 36, 420, 30), $"NPC AI: {_state}", style);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Transform origin = aimOrigin != null ? aimOrigin : (rb != null ? rb.transform : transform);
        Vector3 o = origin.position;
        Vector3 fwd = origin.forward;

        Gizmos.color = new Color(0.2f, 0.85f, 0.3f, 0.35f);
        Gizmos.DrawWireSphere(o, detectionRange);

        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.4f);
        Gizmos.DrawWireSphere(o, fireRange);

        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.35f);
        Gizmos.DrawWireSphere(o, preferredDistanceMin);
        Gizmos.DrawWireSphere(o, preferredDistanceMax);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(o, breakawayDistance);

        if (deferEngagementUntilProximityOrDamage)
        {
            Gizmos.color = new Color(0.85f, 0.5f, 1f, 0.35f);
            Gizmos.DrawWireSphere(o, patrolEngagementRange);
        }

        float halfConeRad = viewConeAngleDegrees * 0.5f * Mathf.Deg2Rad;
        float coneLength = detectionRange;
        float coneRadiusAtEnd = coneLength * Mathf.Tan(halfConeRad);

        Vector3 right = Vector3.Cross(fwd, Vector3.up);
        if (right.sqrMagnitude < 1e-4f)
            right = Vector3.Cross(fwd, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, fwd).normalized;

        Vector3 coneCenter = o + fwd * coneLength;
        int segments = 32;
        Vector3 firstEdgePoint = Vector3.zero;
        Vector3 prevEdgePoint = Vector3.zero;
        Gizmos.color = new Color(0.9f, 0.9f, 0.2f, 0.45f);
        for (int i = 0; i <= segments; i++)
        {
            float t = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 ringOffset = (Mathf.Cos(t) * right + Mathf.Sin(t) * up) * coneRadiusAtEnd;
            Vector3 edgePoint = coneCenter + ringOffset;

            if (i == 0)
            {
                firstEdgePoint = edgePoint;
            }
            else
            {
                Gizmos.DrawLine(prevEdgePoint, edgePoint);
            }

            if (i % 4 == 0)
            {
                Gizmos.DrawLine(o, edgePoint);
            }

            prevEdgePoint = edgePoint;
        }

        Gizmos.DrawLine(prevEdgePoint, firstEdgePoint);

        if (drawAltitudeHoldGizmo && rb != null)
        {
            if (Physics.Raycast(rb.position, Vector3.down, out RaycastHit gh, altitudeHoldRaycastMaxDistance, altitudeGroundLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 ground = gh.point;
                Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.7f);
                Gizmos.DrawLine(rb.position, ground);
                Vector3 minClear = ground + Vector3.up * minAltitudeAboveGround;
                Gizmos.DrawWireSphere(minClear, 2.5f);
            }
        }

        Transform[] fleePath = ResolveFleeRouteWaypoints();
        if (drawFleeRouteGizmo && fleePath != null && fleePath.Length > 0)
        {
            Gizmos.color = new Color(0.95f, 0.45f, 0.95f, 0.9f);
            for (int i = 0; i < fleePath.Length - 1; i++)
            {
                if (fleePath[i] == null || fleePath[i + 1] == null)
                    continue;
                Gizmos.DrawLine(fleePath[i].position, fleePath[i + 1].position);
            }

            for (int i = 0; i < fleePath.Length; i++)
            {
                if (fleePath[i] == null)
                    continue;
                Gizmos.DrawWireSphere(fleePath[i].position, 4f);
            }

            if (routeSteeringActive)
            {
                Gizmos.color = routeSteeringOverriddenByEmergency
                    ? new Color(1f, 0.35f, 0.2f, 0.95f)
                    : new Color(0.2f, 1f, 0.9f, 0.95f);
                Gizmos.DrawWireSphere(currentFleeLookaheadPoint, 6f);
            }
        }

        if (drawNavigationDebugRays && Application.isPlaying && rb != null)
        {
            Vector3 p = rb.position;
            float rayLen = Mathf.Max(12f, _dbgDirectRayDistance);

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(p, p + _dbgDesiredDirection.normalized * rayLen);

            Gizmos.color = directPathAccepted ? new Color(0.2f, 1f, 0.3f, 0.9f) : new Color(1f, 0.25f, 0.25f, 0.95f);
            Gizmos.DrawLine(p + Vector3.up * 1.5f, p + Vector3.up * 1.5f + _dbgDirectRayDirection.normalized * _dbgDirectRayDistance);

            for (int i = 0; i < _dbgCandidateCount && i < _dbgCandidateDirections.Length; i++)
            {
                bool blocked = _dbgCandidateBlocked[i];
                Gizmos.color = blocked ? new Color(1f, 0.2f, 0.2f, 0.8f) : new Color(0.2f, 1f, 0.75f, 0.8f);
                float len = Mathf.Max(8f, _dbgCandidateClearances[i]);
                Vector3 start = p + Vector3.up * 3f;
                Gizmos.DrawLine(start, start + _dbgCandidateDirections[i].normalized * len);
            }

            if (_dbgChosenCandidateIndex >= 0 && _dbgChosenCandidateIndex < _dbgCandidateDirections.Length)
            {
                Gizmos.color = new Color(1f, 1f, 0.2f, 1f);
                Vector3 start = p + Vector3.up * 5f;
                Gizmos.DrawLine(start, start + _dbgCandidateDirections[_dbgChosenCandidateIndex].normalized * rayLen * 0.9f);
            }

            Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.95f);
            Gizmos.DrawLine(p + Vector3.up * 0.5f, p + Vector3.up * 0.5f + _dbgFinalDirection.normalized * rayLen);
        }

        if (drawGroundClearanceDebug && Application.isPlaying && rb != null)
        {
            if (TryGetGroundDistance(out float gd))
            {
                Vector3 start = rb.position;
                Vector3 end = start + Vector3.down * Mathf.Min(gd, groundProbeDistance);
                Gizmos.color = gd < desiredMinGroundClearance
                    ? new Color(1f, 0.25f, 0.25f, 0.95f)
                    : (gd > desiredMaxGroundClearance ? new Color(0.8f, 0.8f, 1f, 0.9f) : new Color(0.25f, 1f, 0.25f, 0.9f));
                Gizmos.DrawLine(start, end);
                Gizmos.DrawWireSphere(end, 1.4f);
            }
        }
    }
}
