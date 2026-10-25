using UnityEngine;

namespace JunkerEquipment.Thermal
{
    // A thin, reusable ribbon over the authored guide. Progress and cooling are visual only.
    [RequireComponent(typeof(ThermalPathProgress))]
    public sealed class ThermalPathHeatView : MonoBehaviour
    {
        [Range(1,6)] public float coolingSeconds=4;
        [Range(.001f,.012f)] public float cutWidth=.006f;
        [Range(.001f,.012f)] public float weldWidth=.004f;
        ThermalPathProgress progress;
        Mesh mesh;
        MeshFilter filter;
        MeshRenderer meshRenderer;
        Vector3[] vertices;
        Color[] colors;
        float[] workedAt;
        int samples;
        float step,lastDistance;
        ThermalMode mode;
        Vector3 surfaceNormal=Vector3.forward;
        bool initialized;

        public void Initialize(Material material)
        {
            progress=GetComponent<ThermalPathProgress>();
            if(!progress || !progress.Valid || progress.Length<.002f || !material) return;
            if(!filter)filter=GetComponent<MeshFilter>();
            if(!filter)filter=gameObject.AddComponent<MeshFilter>();
            if(!meshRenderer)meshRenderer=GetComponent<MeshRenderer>();
            if(!meshRenderer)meshRenderer=gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial=material;
            meshRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows=false;
            float length=progress.Length;
            samples=Mathf.Clamp(Mathf.CeilToInt(length/.008f)+1,2,160);
            step=length/(samples-1);
            vertices=new Vector3[samples*2]; colors=new Color[samples*2]; workedAt=new float[samples];
            for(int i=0;i<samples;i++) workedAt[i]=-100;
            if(mesh) Destroy(mesh);
            mesh=new Mesh {name="Thermal path heat ribbon"}; mesh.MarkDynamic();
            mesh.vertices=vertices; mesh.colors=colors;
            var uv=new Vector2[samples*2];
            for(int i=0;i<uv.Length;i++) uv[i]=new Vector2(.5f,.5f);
            mesh.uv=uv;
            var triangles=new int[(samples-1)*6];
            // Face the surface normal; the previous winding pointed into the metal.
            for(int i=0;i<samples-1;i++) { int v=i*2,t=i*6; triangles[t]=v; triangles[t+1]=v+2; triangles[t+2]=v+1; triangles[t+3]=v+1; triangles[t+4]=v+2; triangles[t+5]=v+3; }
            mesh.triangles=triangles; filter.sharedMesh=mesh; meshRenderer.enabled=false; initialized=true;
        }
        public void Record(float distance,Vector3 normal,ThermalMode workMode)
        {
            if(!initialized) return;
            if(distance+step<lastDistance) Clear();
            mode=workMode; surfaceNormal=transform.InverseTransformDirection(normal).normalized;
            int from=Mathf.Clamp(Mathf.FloorToInt(lastDistance/step),0,samples-1);
            int to=Mathf.Clamp(Mathf.FloorToInt(distance/step),0,samples-1);
            for(int i=from;i<=to;i++) if(workedAt[i]<0) workedAt[i]=Time.time;
            workedAt[to]=Time.time;
            lastDistance=distance;
        }
        void Clear() { for(int i=0;i<samples;i++) workedAt[i]=-100; lastDistance=0; }
        void LateUpdate()
        {
            if(!initialized || !progress) return;
            if(progress.Distance+step<lastDistance) Clear();
            bool any=false;
            float width=mode==ThermalMode.Cut?cutWidth:weldWidth;
            for(int i=0;i<samples;i++)
            {
                float along=i*step;
                Vector3 p=transform.InverseTransformPoint(progress.PointAtDistance(along));
                Vector3 a=transform.InverseTransformPoint(progress.PointAtDistance(Mathf.Max(0,along-step*.5f)));
                Vector3 b=transform.InverseTransformPoint(progress.PointAtDistance(Mathf.Min(progress.Length,along+step*.5f)));
                Vector3 tangent=(b-a).normalized;
                Vector3 side=Vector3.Cross(surfaceNormal,tangent).normalized*width*.5f;
                vertices[i*2]=p+surfaceNormal*.002f-side; vertices[i*2+1]=p+surfaceNormal*.002f+side;
                float age=Time.time-workedAt[i];
                Color color=HeatColor(age);
                colors[i*2]=colors[i*2+1]=color;
                any|=color.a>0;
            }
            meshRenderer.enabled=any;
            if(!any) return;
            mesh.vertices=vertices; mesh.colors=colors; mesh.RecalculateBounds();
        }
        Color HeatColor(float age)
        {
            if(age<0 || age>=coolingSeconds)return Color.clear;
            if(age<.18f)return Color.Lerp(new Color(3,3,2.4f),new Color(3,2.2f,.5f),age/.18f);
            if(age<.75f)return Color.Lerp(new Color(3,2.2f,.5f),new Color(2.6f,.7f,.05f),(age-.18f)/.57f);
            if(age<2f)return Color.Lerp(new Color(2.6f,.7f,.05f),new Color(1.25f,.08f,.015f),(age-.75f)/1.25f);
            Color red=new Color(1.25f,.08f,.015f);
            return Color.Lerp(red,new Color(.22f,.008f,.003f,0),Mathf.Clamp01((age-2f)/Mathf.Max(.01f,coolingSeconds-2f)));
        }
        void OnDestroy() { if(mesh) Destroy(mesh); }
    }
}
