using UnityEngine;
using JunkerScanner;

namespace JunkerEquipment.Thermal
{
    [RequireComponent(typeof(AuthoredCutPath))]
    public sealed class ThermalPathProgress : MonoBehaviour
    {
        public AuthoredCutPath path;
        [Min(.005f)] public float tolerance = .035f;
        [Min(.001f)] public float metresPerSecond = .18f;
        [Min(.01f)] public float pointWeldSeconds = 1;
        [SerializeField] float distance;
        public float Length
        {
            get { float length=0; for(int i=0;i<Segments;i++) length+=Vector3.Distance(Point(i),Point(i+1)); return length; }
        }
        int Count => path ? path.controlPoints.Length : 0;
        int Segments => Count < 2 ? 0 : Count - (path.loop ? 0 : 1);
        Vector3 Point(int index) => path.controlPoints[index % Count].position;
        public bool Valid { get { if(!path || Count==0) return false; foreach(var p in path.controlPoints) if(!p) return false; return true; } }
        public float Fraction => !Valid ? 0 : Mathf.Clamp01(distance / (Count==1 ? 1 : Mathf.Max(.0001f,Length)));
        public float Distance => distance;
        public Vector3 PointAtDistance(float along) => Valid ? At(Mathf.Clamp(along,0,Length)) : transform.position;
        public bool Complete => Fraction>=1;
        public Vector3 WorkLocation => Valid ? At(distance) : transform.position;
        void Reset() { path=GetComponent<AuthoredCutPath>(); }
        public void Clear() { distance=0; }
        Vector3 At(float along)
        {
            for(int i=0;i<Segments;i++) { float length=Vector3.Distance(Point(i),Point(i+1)); if(along<=length) return Vector3.Lerp(Point(i),Point(i+1),length>0?along/length:0); along-=length; }
            return Point(Count-1);
        }
        public bool Near(Vector3 hit,float radius)
        {
            if(!Valid) return false;
            if(Count==1) return Vector3.Distance(hit,Point(0))<=radius;
            for(int i=0;i<Segments;i++) { var a=Point(i); var d=Point(i+1)-a; float t=Mathf.Clamp01(Vector3.Dot(hit-a,d)/Mathf.Max(.000001f,d.sqrMagnitude)); if(Vector3.Distance(hit,a+d*t)<=radius) return true; }
            return false;
        }
        public bool Advance(Vector3 hit,float seconds,float speed=1)
        {
            if(!Valid || Complete || seconds<=0 || speed<=0) return false;
            if(Count==1) { if(!Near(hit,tolerance)) return false; distance=Mathf.Min(1,distance+seconds*speed/Mathf.Max(.01f,pointWeldSeconds)); return true; }
            // Only project onto the CURRENT segment. A stationary hit can advance to its
            // projection plus tolerance, but never consume the remaining polyline over time.
            float start=0;
            for(int i=0;i<Segments;i++)
            {
                var a=Point(i); var delta=Point(i+1)-a; float length=delta.magnitude;
                if(length<.00001f) continue;
                if(distance>start+length-.00001f) { start+=length; continue; }
                float projected=Mathf.Clamp(Vector3.Dot(hit-a,delta/length),0,length);
                if(Vector3.Distance(hit,a+delta/length*projected)>tolerance || Mathf.Abs(start+projected-distance)>tolerance*2) return false;
                float next=Mathf.Min(start+length,start+projected+tolerance*.5f,distance+metresPerSecond*seconds*speed);
                if(next<=distance) return false;
                distance=next; return true;
            }
            return false;
        }
        void OnDrawGizmosSelected() { if(!Valid) return; Gizmos.color=Color.green; Gizmos.DrawWireSphere(WorkLocation,tolerance); }
    }
}
