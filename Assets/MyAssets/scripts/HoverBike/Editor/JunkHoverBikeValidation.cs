#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Runs actual control methods on the temporary prefab before it is saved.
public static class JunkHoverBikeValidation
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Call(object obj,string method)=>obj.GetType().GetMethod(method,Flags).Invoke(obj,null);
    static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,Flags).SetValue(obj,value);
    static void Check(bool condition,string message){if(!condition)throw new Exception("Junk bike validation: "+message);}

    public static void CheckControls(HoverBikeController bike)
    {
        var handObject=new GameObject("Temporary handlebar validation hand");
        handObject.SetActive(false);
        var hand=handObject.AddComponent<XRRayInteractor>();
        try
        {
            Call(bike,"Awake");
            var wheel=bike.drivingSticks;
            Vector3 rest=wheel.localPosition;
            Quaternion rotation=wheel.localRotation;
            foreach(var grip in new[]{bike.leftStick,bike.rightStick})
            {
                Call(grip,"Awake");
                Vector3 neutral=grip.transform.position;
                Set(grip,"interactor",hand);
                Set(grip,"neutralHand",Quaternion.Inverse(bike.transform.rotation)*(neutral-bike.transform.position));
                Set(grip,"neutralHandRotation",Quaternion.identity);
                foreach(float turn in new[]{-14f,14f})
                {
                    hand.transform.position=wheel.position+Quaternion.AngleAxis(turn,Vector3.up)*(neutral-wheel.position);
                    hand.transform.rotation=Quaternion.Euler(0,turn,0);
                    Call(grip,"Update");
                    Check(Mathf.Sign(grip.Steering)==Mathf.Sign(turn)&&Mathf.Abs(grip.Steering)>.3f,"steering direction");
                    Check(Mathf.Abs(grip.Throttle)<.001f,"steering must not apply throttle");
                }
                hand.transform.position=neutral;
                hand.transform.rotation=Quaternion.AngleAxis(-22.5f,Vector3.right);
                Call(grip,"Update");
                Check(grip==bike.rightStick?grip.Throttle<-.3f:Mathf.Abs(grip.Throttle)<.001f,"right-hand twist throttle");
                Check(Mathf.Abs(grip.Steering)<.001f,"wrist twist must not steer");
                hand.transform.position=wheel.position+Quaternion.AngleAxis(14f,Vector3.up)*(neutral-wheel.position);
                hand.transform.rotation=Quaternion.Euler(0,14,0)*Quaternion.AngleAxis(-22.5f,Vector3.right);
                Call(grip,"Update");
                for(int frame=0;frame<120;frame++)
                {
                    Call(bike,"ApplyDrivingSticksVisual");grip.SyncPose();
                    Check((wheel.localPosition-rest).sqrMagnitude<1e-10f,"pivot moved under throttle/steering");
                    Check(Vector3.Distance(grip.transform.position,wheel.TransformPoint(grip.wheelLocalAnchor))<.0001f,"grip detached");
                    float yaw=Mathf.DeltaAngle(0,wheel.localEulerAngles.y);
                    Check(yaw>0&&yaw<=bike.handlebarSteeringAngle,"wheel yaw direction or accumulated rotation");
                }
                Set(grip,"interactor",null);Set(grip,"<Throttle>k__BackingField",0f);Set(grip,"<Steering>k__BackingField",0f);
                wheel.localPosition=rest;wheel.localRotation=rotation;grip.SyncPose();
            }
        }
        finally
        {
            foreach(var grip in new[]{bike.leftStick,bike.rightStick})Set(grip,"interactor",null);
            UnityEngine.Object.DestroyImmediate(handObject);
        }
    }
}
#endif
