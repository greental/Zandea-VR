using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.Bandaging
{
    // A healed wrap stains red, becomes a normal grabbable object, and decays after disposal.
    [RequireComponent(typeof(BandageRibbon))]
    public sealed class AppliedBandage : MonoBehaviour
    {
        [Min(1f)] public float groundLifetime = 30f;
        XRGrabInteractable grab;
        Rigidbody body;
        BoxCollider shape;
        bool ready, discarded;
        public void MarkReady()
        {
            if (ready) return; ready = true;
            var renderer=GetComponent<MeshRenderer>();
            if(renderer&&renderer.material)renderer.material.color=new Color(.48f,.025f,.02f,1f);
            shape=gameObject.AddComponent<BoxCollider>();
            shape.isTrigger=true;
            var bounds=renderer?renderer.localBounds:new Bounds(Vector3.zero,new Vector3(.18f,.14f,.18f));
            shape.center=bounds.center;shape.size=Vector3.Max(bounds.size,new Vector3(.12f,.10f,.12f));
            body=gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.mass=.05f;
            grab=gameObject.AddComponent<XRGrabInteractable>();grab.useDynamicAttach=true;grab.throwOnDetach=true;
            grab.movementType=XRBaseInteractable.MovementType.VelocityTracking;grab.selectEntered.AddListener(Remove);
        }
        void Remove(SelectEnterEventArgs _)
        {
            if(discarded)return;discarded=true;transform.SetParent(null,true);shape.isTrigger=false;body.isKinematic=false;body.useGravity=true;
            StartCoroutine(Decay());
        }
        IEnumerator Decay()
        {
            yield return new WaitForSeconds(groundLifetime*.75f);
            var renderer=GetComponent<Renderer>();float start=Time.time;
            while(this&&Time.time-start<groundLifetime*.25f)
            { if(renderer)renderer.material.color=Color.Lerp(new Color(.48f,.025f,.02f,1f),new Color(.12f,.07f,.04f,0f),(Time.time-start)/(groundLifetime*.25f));yield return null; }
            if(this)Destroy(gameObject);
        }
    }
}
