using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Collider),typeof(XRGrabInteractable))]
public sealed class HoverBikeStick : MonoBehaviour
{
    public Transform handle,bikeFrame;
    public Transform wheelVisual;
    public Vector3 wheelLocalAnchor;
    [Range(1f,60f)] public float steeringAngle=31f;
    public float localYBack=-.004f,localYForward=-.003f;
    public bool applyVisual;
    [Min(.01f)] public float handTravel=.10f;
    [Range(0f,.5f)] public float deadZone=.08f;
    [Min(.01f), Tooltip("Hand-sized selection volume in world meters, independent of imported mesh scale.")]
    public float grabRadius=.06f;
    [Tooltip("Per-handle push/pull direction. Use -1 to reverse this handle only.")]
    public float throttleDirection=1f;
    [Tooltip("Per-handle vertical steering direction; opposite handles can be mirrored.")]
    public float steeringDirection=1f;
    [Header("Motorcycle grip")]
    public bool motorcycleGrip;
    public bool twistThrottle;
    [Range(10f,90f)] public float throttleTwistDegrees=45f;
    public float Throttle { get; private set; }
    public float Steering { get; private set; }
    public bool IsGrabbed => interactor!=null;
    XRGrabInteractable grab; IXRSelectInteractor interactor;
    IHoverBikeHandPose avatar;
    Vector3 neutralHand,restPosition; Quaternion restRotation;
    Quaternion wheelLocalRotation;
    Quaternion neutralHandRotation;
    Vector3 steeringPivot;
    void Awake()
    {
        grab=GetComponent<XRGrabInteractable>();
        var rb=GetComponent<Rigidbody>();if(rb){rb.isKinematic=true;rb.useGravity=false;}
        if(!handle)handle=transform;
        if(!bikeFrame)bikeFrame=GetComponentInParent<HoverBikeController>()?.transform;
        if(!wheelVisual && bikeFrame)
        {
            wheelVisual=bikeFrame.GetComponent<HoverBikeController>()?.drivingSticks;
            if(wheelVisual)wheelLocalAnchor=wheelVisual.InverseTransformPoint(transform.position);
        }
        if(wheelVisual)
        {
            steeringPivot=Quaternion.Inverse(bikeFrame.rotation)*(wheelVisual.position-bikeFrame.position);
            // Respect the grip placement authored in the scene, including rotation.
            wheelLocalAnchor=wheelVisual.InverseTransformPoint(transform.position);
            wheelLocalRotation=Quaternion.Inverse(wheelVisual.rotation)*transform.rotation;
        }
        // Migrate grips created under the tiny imported wheel mesh without changing their world pose.
        if(bikeFrame && transform.parent!=bikeFrame)
        {
            transform.SetParent(bikeFrame,true);
        }
        var sphere=GetComponent<SphereCollider>();
        if(sphere)
        {
            var scale=transform.lossyScale;
            sphere.radius=grabRadius/Mathf.Max(.0001f,Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            sphere.isTrigger=false; // Near/direct sphere casts commonly ignore triggers.
            if(bikeFrame)
                foreach(var other in bikeFrame.GetComponentsInChildren<Collider>())
                    if(other!=sphere)Physics.IgnoreCollision(sphere,other);
        }
        restPosition=handle.localPosition;restRotation=handle.localRotation;ConfigureGrab();
    }
    void Start()
    {
        // Older scenes had trigger-only grips and an empty serialized collider list.
        // XRI deliberately skips triggers during Awake, so rebuild its registration.
        var sphere=GetComponent<SphereCollider>();
        if(sphere && !grab.colliders.Contains(sphere))
        {
            bool wasEnabled=grab.enabled;
            grab.enabled=false;
            grab.colliders.Add(sphere);
            grab.enabled=wasEnabled;
        }
    }
    void ConfigureGrab(){grab.trackPosition=false;grab.trackRotation=false;grab.trackScale=false;grab.throwOnDetach=false;grab.retainTransformParent=true;grab.movementType=XRBaseInteractable.MovementType.Instantaneous;}
    void OnEnable(){grab=GetComponent<XRGrabInteractable>();ConfigureGrab();grab.selectEntered.AddListener(Grabbed);grab.selectExited.AddListener(Released);}
    void OnDisable(){if(grab){grab.selectEntered.RemoveListener(Grabbed);grab.selectExited.RemoveListener(Released);}ClearHand();interactor=null;}
    void Grabbed(SelectEnterEventArgs args)
    {
        interactor=args.interactorObject;
        // Near/far attach transforms may follow the selected object. Read the tracked
        // interactor itself so snapping the visual hand cannot cancel steering input.
        neutralHand=HandPosition();
        neutralHandRotation=Quaternion.Inverse(bikeFrame.rotation)*interactor.transform.rotation;
        transform.SetParent(bikeFrame,true);
        foreach(var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
            if(behaviour is IHoverBikeHandPose handPose){avatar=handPose;break;}
        avatar?.SetHoverBikeGrip(args.interactorObject.transform,transform);
    }
    void Released(SelectExitEventArgs args){ClearHand();interactor=null;Throttle=Steering=0f;ApplyVisual();}
    void ClearHand()
    {
        avatar?.ClearHoverBikeGrip(transform);
        avatar=null;
    }
    Vector3 HandPosition()=>Quaternion.Inverse(bikeFrame.rotation)*(interactor.transform.position-bikeFrame.position);
    void Update()
    {
        if(interactor!=null&&bikeFrame)
        {
            if(motorcycleGrip)
            {
                // Read tracked hands in the level bike frame, never the animated bar.
                // Both grips describe the same yaw, and grabbing starts at neutral.
                Vector3 from=Vector3.ProjectOnPlane(neutralHand-steeringPivot,Vector3.up);
                Vector3 to=Vector3.ProjectOnPlane(HandPosition()-steeringPivot,Vector3.up);
                Steering=Shape(steeringDirection*Vector3.SignedAngle(from,to,Vector3.up)/Mathf.Max(1f,steeringAngle));
                Quaternion delta=Quaternion.Inverse(bikeFrame.rotation)*interactor.transform.rotation*Quaternion.Inverse(neutralHandRotation);
                // Swing/twist decomposition about the lateral grip axis. Yaw does
                // not become throttle; rolling the wrist back drives forward.
                float twist=Mathf.DeltaAngle(0f,2f*Mathf.Atan2(delta.x,delta.w)*Mathf.Rad2Deg);
                Throttle=twistThrottle?Shape(throttleDirection*twist/throttleTwistDegrees):0f;
            }
            else
            {
                Vector3 delta=HandPosition()-neutralHand;
                Throttle=Shape(throttleDirection*delta.z/Mathf.Max(.01f,handTravel));
                Steering=Shape(steeringDirection*delta.y/Mathf.Max(.01f,handTravel));
            }
        }
        ApplyVisual();
    }
    void LateUpdate()=>SyncPose();
    public void SyncPose(){if(wheelVisual)transform.SetPositionAndRotation(wheelVisual.TransformPoint(wheelLocalAnchor),wheelVisual.rotation*wheelLocalRotation);}
    float Shape(float value){value=Mathf.Clamp(value,-1f,1f);float a=Mathf.Abs(value);return a<=deadZone?0f:Mathf.Sign(value)*Mathf.InverseLerp(deadZone,1f,a);}
    void ApplyVisual(){if(!handle||!applyVisual)return;var p=restPosition;p.y=Mathf.Lerp(localYBack,localYForward,(Throttle+1f)*.5f);handle.localPosition=p;handle.localRotation=restRotation*Quaternion.Euler(0f,Steering*steeringAngle,0f);}
}
