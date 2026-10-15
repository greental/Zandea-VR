using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(-106)] // Update moving cockpit colliders before XRI's manager (-105).
public sealed class HoverBikeController : MonoBehaviour
{
    public Rigidbody body;
    public HoverBikeStick leftStick, rightStick;
    [Tooltip("Connected two-hand driving wheel/stick mesh.")]
    public Transform drivingSticks;
    public XRFlipSwitch ignition;
    public SitInCockpit seat;
    public Transform hoverProbe;
    public LayerMask groundMask=~0;
    [Header("Required installed systems")]
    [Tooltip("Drive, hover lift, boost, and engine audio need every listed slot to contain a working part. Empty list preserves older bikes.")]
    public MonoBehaviour[] requiredDriveSlots = System.Array.Empty<MonoBehaviour>();
    [Range(0.01f,100f)] public float minimumDriveCondition = 10f;
    public bool CanPowerDrive
    {
        get
        {
            foreach(var slot in requiredDriveSlots)
                if(!slot || slot is not IHoverBikeDriveRequirement requirement || !requirement.AllowsDrive(minimumDriveCondition)) return false;
            return true;
        }
    }

    [Header("Hover")]
    [Range(.5f,2f)] public float targetHeight=1f;
    public float minimumHeight=.5f, maximumHeight=2f;
    public float heightAdjustSpeed=.7f;
    [Min(0f)] public float hoverSpring=24f, hoverDamping=10f;
    [Min(0f)] public float maxHoverAcceleration=35f;
    [Min(0f)] public float groundProbeLift=1.5f;
    [Min(0f)] public float uprightTorque=18f, uprightDamping=10f;
    [Min(5f)] public float groundSearchDistance=40f;
    [Header("Runtime diagnostics")]
    public bool groundDetected;
    [Tooltip("Distance from the bottom of the hull collider to the highest ground under it.")]
    public float measuredGroundHeight;

    [Header("Drive")]
    public float forwardSpeed=13f, reverseSpeed=4f, acceleration=16f, braking=22f;
    public float steeringDegreesPerSecond=75f;
    public float lateralGrip=7f;
    [Tooltip("Horizontal handlebar steering and right-hand twist throttle instead of Pyxol push/pull controls.")]
    public bool motorcycleHandlebar;
    [Range(5f,45f)] public float handlebarSteeringAngle=28f;

    [Header("Engine audio")]
    public AudioClip engineLoop;
    public AudioSource engineSource;
    [Range(0f,1f)] public float engineIdleVolume=.22f, engineFastVolume=.8f;
    [Range(.1f,3f)] public float engineIdlePitch=.85f, engineFastPitch=1.3f;
    [Min(.01f)] public float engineAudioResponse=5f;

    [Header("Boost")]
    public float boostImpulse=9f, boostDuration=.65f, boostCooldown=4f;

    public InputActionProperty enterAction=new InputActionProperty(new InputAction("Enter hover bike",InputActionType.Button,"<XRController>{RightHand}/primaryButton"));
    public InputActionProperty boostAction=new InputActionProperty(new InputAction("Hover bike boost",InputActionType.Button,"<XRController>{RightHand}/secondaryButton"));
    public InputActionProperty heightAction=new InputActionProperty(new InputAction("Hover height",InputActionType.Value,"<XRController>{RightHand}/primary2DAxis"));

    bool ownsEnter,ownsBoost,ownsHeight;
    float boostUntil,nextBoost;
    Camera viewer;
    Vector3 drivingSticksRestPosition;
    Quaternion drivingSticksRestRotation;
    readonly RaycastHit[] groundHits=new RaycastHit[32];
    Collider[] vehicleColliders;
    HoverBikeCosmeticLean cosmeticLean;
    readonly HoverBikeHeldItemCollisions heldItemCollisions=new HoverBikeHeldItemCollisions();
    static int seatToggleFrame=-1;

    void Awake()
    {
        if(!body) body=GetComponent<Rigidbody>();
        cosmeticLean=GetComponent<HoverBikeCosmeticLean>();
        if(!hoverProbe) hoverProbe=transform;
        vehicleColliders=GetComponentsInChildren<Collider>(true);
        viewer=Camera.main;
        if(drivingSticks){drivingSticksRestPosition=drivingSticks.localPosition;drivingSticksRestRotation=drivingSticks.localRotation;}
        if(engineLoop && !engineSource)
        {
            var audioObject=new GameObject("Pyxol Engine Audio");
            audioObject.transform.SetParent(transform,false);
            engineSource=audioObject.AddComponent<AudioSource>();
            engineSource.spatialBlend=1f;
            engineSource.rolloffMode=AudioRolloffMode.Logarithmic;
            engineSource.minDistance=2f;
            engineSource.maxDistance=30f;
        }
        if(engineSource){engineSource.playOnAwake=false;engineSource.loop=true;engineSource.clip=engineLoop;engineSource.volume=0f;}
    }
    void Start()
    {
        // Kinematic control bodies overlap the hull. Without these exclusions the
        // ignition switch continuously pushes its own parent bike into the ground.
        for(int i=0;i<vehicleColliders.Length;i++)
            for(int j=i+1;j<vehicleColliders.Length;j++)
                if(vehicleColliders[i]&&vehicleColliders[j])
                    Physics.IgnoreCollision(vehicleColliders[i],vehicleColliders[j]);
    }
    void OnEnable()
    {
        Enable(enterAction,out ownsEnter); Enable(boostAction,out ownsBoost); Enable(heightAction,out ownsHeight);
    }
    void OnDisable()
    {
        heldItemCollisions.Restore();
        Disable(enterAction,ownsEnter);Disable(boostAction,ownsBoost);Disable(heightAction,ownsHeight);
        if(engineSource)engineSource.Stop();
    }
    static void Enable(InputActionProperty p,out bool owned) { owned=p.action!=null&&!p.action.enabled;if(owned)p.action.Enable(); }
    static void Disable(InputActionProperty p,bool owned) { if(owned)p.action?.Disable(); }

    void Update()
    {
        if(!viewer) viewer=Camera.main;
        if(seatToggleFrame!=Time.frameCount && (enterAction.action?.WasPressedThisFrame()??false))
        {
            if(seat && seat.IsSeated){seatToggleFrame=Time.frameCount;seat.StandUp();}
            else if(IsNearestAvailableSeat() && seat.StartSeated())seatToggleFrame=Time.frameCount;
        }
        ApplyDrivingSticksVisual();
        // Compose lean after the wheel's authored control pose, before grip/XRI sync.
        if(cosmeticLean && cosmeticLean.isActiveAndEnabled)cosmeticLean.ApplyVisuals();
        heldItemCollisions.Refresh(seat,vehicleColliders);
        if(leftStick)leftStick.SyncPose();
        if(rightStick)rightStick.SyncPose();
        Physics.SyncTransforms();
        UpdateEngineAudio();
        if(!ignition || !ignition.isOn || !CanPowerDrive || !seat || !seat.IsSeated) return;
        float vertical=heightAction.action!=null?heightAction.action.ReadValue<Vector2>().y:0f;
        targetHeight=Mathf.Clamp(targetHeight+vertical*heightAdjustSpeed*Time.deltaTime,minimumHeight,maximumHeight);
        if((boostAction.action?.WasPressedThisFrame()??false)&&ignition&&ignition.isOn&&Time.time>=nextBoost)
        { boostUntil=Time.time+boostDuration;nextBoost=Time.time+boostCooldown; }
    }
    bool IsNearestAvailableSeat()
    {
        if(!seat || !seat.seatAnchor || !viewer)return false;
        float distance=Vector3.Distance(viewer.transform.position,seat.seatAnchor.position);
        if(distance>seat.sitDistance+1f)return false;
        foreach(var other in FindObjectsByType<HoverBikeController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
        {
            if(other==this || !other.isActiveAndEnabled || !other.seat || !other.seat.seatAnchor)continue;
            if(other.seat.IsSeated)return false;
            float otherDistance=Vector3.Distance(viewer.transform.position,other.seat.seatAnchor.position);
            if(otherDistance<=other.seat.sitDistance+1f &&
                (otherDistance<distance || (Mathf.Approximately(otherDistance,distance) && other.GetInstanceID()<GetInstanceID())))return false;
        }
        return true;
    }
    void UpdateEngineAudio()
    {
        if(!engineSource || !engineLoop)return;
        bool running=ignition && ignition.isOn && CanPowerDrive;
        float speed=body ? Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up).magnitude : 0f;
        float intensity=Mathf.Clamp01(speed/Mathf.Max(.01f,forwardSpeed));
        float targetVolume=running ? Mathf.Lerp(engineIdleVolume,engineFastVolume,intensity) : 0f;
        float targetPitch=Mathf.Lerp(engineIdlePitch,engineFastPitch,intensity);
        float blend=1f-Mathf.Exp(-engineAudioResponse*Time.deltaTime);
        engineSource.volume=Mathf.Lerp(engineSource.volume,targetVolume,blend);
        engineSource.pitch=Mathf.Lerp(engineSource.pitch,targetPitch,blend);
        if(running && !engineSource.isPlaying)engineSource.Play();
        if(!running && engineSource.isPlaying && engineSource.volume<.005f)engineSource.Stop();
    }
    internal void ApplyDrivingSticksVisual()
    {
        if(!drivingSticks)return;
        float throttle=ThrottleInput(),steering=SteeringInput();
        var p=drivingSticksRestPosition;
        if(!motorcycleHandlebar)
            p.y=throttle>=0?Mathf.Lerp(drivingSticksRestPosition.y,-.003f,throttle):Mathf.Lerp(drivingSticksRestPosition.y,-.004f,-throttle);
        drivingSticks.localPosition=p;
        drivingSticks.localRotation=drivingSticksRestRotation*Quaternion.Euler(0f,steering*(motorcycleHandlebar?handlebarSteeringAngle:-31f),0f);
    }
    void FixedUpdate()
    {
        // Catch grabs/releases before the next physics simulation, including items
        // already in either hand when the player sits down.
        heldItemCollisions.Refresh(seat,vehicleColliders);
        Bounds hull=HullBounds();
        groundDetected=TryFindGround(hull,out var hit);
        if(groundDetected && CanPowerDrive)
        {
            float heightAboveGround=hull.min.y-hit.point.y;
            measuredGroundHeight=heightAboveGround;
            float error=targetHeight-heightAboveGround;
            float verticalVelocity=Vector3.Dot(body.linearVelocity,Vector3.up);
            float liftAcceleration=-Vector3.Dot(Physics.gravity,Vector3.up)+error*hoverSpring-verticalVelocity*hoverDamping;
            body.AddForce(Vector3.up*Mathf.Clamp(liftAcceleration,0f,maxHoverAcceleration),ForceMode.Acceleration);
        }
        // Hold the vehicle level to world horizon. Preserve yaw so steering can turn it.
        Vector3 currentUp=body.rotation*Vector3.up;
        Quaternion levelCorrection=Quaternion.FromToRotation(currentUp,Vector3.up);
        levelCorrection.ToAngleAxis(out float levelAngle,out Vector3 levelAxis);
        if(levelAngle>180f)levelAngle-=360f;
        Vector3 levelError=levelAxis*(levelAngle*Mathf.Deg2Rad);
        Vector3 tiltAngularVelocity=Vector3.ProjectOnPlane(body.angularVelocity,Vector3.up);
        if(CanPowerDrive)
            body.AddTorque(levelError*uprightTorque-tiltAngularVelocity*uprightDamping,ForceMode.Acceleration);
        if(!ignition||!ignition.isOn || !CanPowerDrive) return;
        float throttle=ThrottleInput();
        float steering=SteeringInput();
        // The wheel's local push and steering signs oppose the bike's travel axes.
        float drive=-throttle;
        float desired=drive>=0?drive*forwardSpeed:drive*reverseSpeed;
        Vector3 planar=Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up);
        Vector3 forward=Vector3.ProjectOnPlane(body.rotation*Vector3.forward,Vector3.up).normalized;
        if(forward.sqrMagnitude<.001f)forward=Vector3.forward;
        Vector3 right=Vector3.Cross(Vector3.up,forward);
        float forwardNow=Vector3.Dot(planar,forward);
        float rate=Mathf.Abs(desired)<Mathf.Abs(forwardNow)?braking:acceleration;
        body.AddForce(forward*Mathf.Clamp(desired-forwardNow,-rate*Time.fixedDeltaTime,rate*Time.fixedDeltaTime),ForceMode.VelocityChange);
        body.AddForce(-right*Vector3.Dot(planar,right)*lateralGrip,ForceMode.Acceleration);
        float desiredYaw=steering*steeringDegreesPerSecond*Mathf.Deg2Rad;
        float currentYaw=Vector3.Dot(body.angularVelocity,Vector3.up);
        body.AddTorque(Vector3.up*(desiredYaw-currentYaw)*3f,ForceMode.Acceleration);
        if(Time.time<boostUntil) body.AddForce(forward*boostImpulse,ForceMode.Acceleration);
    }
    Bounds HullBounds()
    {
        var bounds=new Bounds(body.position,Vector3.zero);
        bool found=false;
        foreach(var c in vehicleColliders)
        {
            if(!c||!c.enabled||c.isTrigger||c.attachedRigidbody!=body)continue;
            if(found)bounds.Encapsulate(c.bounds);else{bounds=c.bounds;found=true;}
        }
        return bounds;
    }
    bool TryFindGround(Bounds hull,out RaycastHit nearest)
    {
        nearest=default;
        bool found=false;
        float highest=float.NegativeInfinity;
        // Centre plus four footprint corners keep the level hull clear on slopes.
        for(int sample=0;sample<5;sample++)
        {
            var origin=new Vector3(hull.center.x,hull.max.y+groundProbeLift,hull.center.z);
            if(sample>0)
            {
                origin.x+=hull.extents.x*.85f*((sample==1||sample==2)?-1f:1f);
                origin.z+=hull.extents.z*.85f*((sample==1||sample==3)?-1f:1f);
            }
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,groundHits,groundSearchDistance+groundProbeLift+hull.size.y,groundMask,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var candidate=groundHits[i];
                if(!candidate.collider||candidate.collider.transform.IsChildOf(transform))continue;
                if(seat&&seat.xrOrigin&&candidate.collider.transform.IsChildOf(seat.xrOrigin))continue;
                if(candidate.normal.y<.2f||candidate.point.y<=highest)continue;
                highest=candidate.point.y;nearest=candidate;found=true;
            }
        }
        return found;
    }
    float SteeringInput()
    {
        if(motorcycleHandlebar)return Average(s=>s.Steering);
        if(rightStick&&rightStick.IsGrabbed)return rightStick.Steering;
        return leftStick&&leftStick.IsGrabbed?leftStick.Steering:0f;
    }
    float ThrottleInput()=>motorcycleHandlebar ? (rightStick&&rightStick.IsGrabbed?rightStick.Throttle:0f) : Average(s=>s.Throttle);
    float Average(System.Func<HoverBikeStick,float> read)
    {
        float sum=0;int count=0;
        if(leftStick&&leftStick.IsGrabbed){sum+=read(leftStick);count++;}
        if(rightStick&&rightStick.IsGrabbed){sum+=read(rightStick);count++;}
        return count>0?sum/count:0f;
    }
}
