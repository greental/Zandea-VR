using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JunkerEquipment.Thermal.Editor
{
    [InitializeOnLoad]
    public static class ThermalFXSetup
    {
        const string Request="Temp/JunkerThermal.fx-setup";
        const string Report="Temp/JunkerThermal.fx-result";
        const string Folder="Assets/JunkerEquipment/Thermal/Generated";
        const string Audio="Assets/MyAssets/Audio/tools/PLS-Welder/";
        static ThermalFXSetup() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Configure(); File.WriteAllText(Report,"PASS: thermal VFX and audio configured on tool prefabs."); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Zandea/Junker Equipment/Thermal/Configure plasma effects and audio")]
        public static void Configure()
        {
            foreach(string path in new[]{"Assets/MyAssets/prefabs/Tools/PLS-T ScrapCutter.prefab",Folder+"/ScrapCutter.prefab"})
            {
                if(!AssetDatabase.LoadAssetAtPath<GameObject>(path)) continue;
                var root=PrefabUtility.LoadPrefabContents(path);
                try { var fx=root.GetComponent<ThermalFXController>(); if(fx) { Setup(fx); PrefabUtility.SaveAsPrefabAsset(root,path); } }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        static Material Material(string name)
        {
            string path=Folder+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat)
            {
                var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if(!shader) throw new InvalidOperationException("URP particle shader unavailable");
                mat=new Material(shader); mat.SetFloat("_Surface",1); mat.SetFloat("_Blend",1);
                mat.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_ZWrite",0); mat.renderQueue=3000;
                AssetDatabase.CreateAsset(mat,path);
            }
            string texPath=Folder+"/Plasma soft disc.asset";
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if(!tex)
            {
                tex=new Texture2D(32,32,TextureFormat.RGBA32,false) {name="Plasma soft disc",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                var pixels=new Color[32*32];
                for(int y=0;y<32;y++)for(int x=0;x<32;x++)
                {
                    float radius=Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))/15.5f;
                    pixels[y*32+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),1.6f));
                }
                tex.SetPixels(pixels); tex.Apply(); AssetDatabase.CreateAsset(tex,texPath);
            }
            mat.SetTexture("_BaseMap",tex); mat.SetTexture("_MainTex",tex);
            return mat;
        }
        static Transform Child(Transform parent,string name)
        {
            var t=parent.Find(name); if(t)return t;
            t=new GameObject(name).transform; t.SetParent(parent,false); return t;
        }
        static LineRenderer Line(Transform parent,string name,Material mat,float start,float end)
        {
            var t=Child(parent,name); var line=t.GetComponent<LineRenderer>(); if(!line)line=t.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial=mat; line.startWidth=start; line.endWidth=end; line.numCapVertices=2; line.enabled=false;
            line.textureMode=LineTextureMode.Stretch; line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows=false; return line;
        }
        static ParticleSystem Particles(Transform parent,string name,Material mat,Color color,float life,float size,float speed,float rate,int max,float cone,bool blue=false)
        {
            var t=Child(parent,name); var ps=t.GetComponent<ParticleSystem>(); if(!ps)ps=t.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main; main.playOnAwake=false; main.loop=true; main.startLifetime=life; main.startSize=size;
            main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.7f,speed*1.3f);
            main.startSize=new ParticleSystem.MinMaxCurve(size*.7f,size*1.3f);
            main.startColor=blue ? new ParticleSystem.MinMaxGradient(new Color(.25f,.5f,2.5f),new Color(.8f,1.5f,3f)) :
                new ParticleSystem.MinMaxGradient(new Color(2.5f,1.5f,.25f),new Color(3,3,2.2f));
            main.maxParticles=max;
            main.simulationSpace=ParticleSystemSimulationSpace.World; main.gravityModifier=.25f;
            var emission=ps.emission; emission.enabled=true; emission.rateOverTime=rate;
            var shape=ps.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Cone; shape.radius=.002f; shape.angle=cone;
            var over=ps.colorOverLifetime; over.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(blue ? new[]{
                new GradientColorKey(new Color(.6f,.85f,1),0),new GradientColorKey(new Color(.25f,.45f,1),.5f),new GradientColorKey(new Color(.04f,.07f,.3f),1)} : new[]{
                new GradientColorKey(new Color(1,1,.7f),0),new GradientColorKey(new Color(1,.48f,.06f),.45f),new GradientColorKey(new Color(.45f,.035f,.005f),1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.55f),new GradientAlphaKey(0,1)});
            over.color=gradient;
            var renderer=ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=mat;
            renderer.renderMode=ParticleSystemRenderMode.Stretch; renderer.lengthScale=4; renderer.velocityScale=.18f;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows=false;
            return ps;
        }
        static AudioSource Source(Transform parent,string name,float volume)
        {
            var t=Child(parent,name); var src=t.GetComponent<AudioSource>(); if(!src)src=t.gameObject.AddComponent<AudioSource>();
            src.playOnAwake=false; src.spatialBlend=.35f; src.volume=volume; src.minDistance=.3f; src.maxDistance=6;
            src.rolloffMode=AudioRolloffMode.Linear; src.dopplerLevel=0; return src;
        }
        static AudioClip Clip(string name)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Audio+name);
            if(!clip)throw new InvalidOperationException("Missing thermal audio: "+name);
            return clip;
        }
        static void Setup(ThermalFXController fx)
        {
            var mat=Material("Plasma additive");
            for(int i=fx.transform.childCount-1;i>=0;i--)
            {
                var child=fx.transform.GetChild(i);
                if(child.name=="Cut cooling metal" || child.name=="Weld cooling seam" ||
                   child.name.StartsWith("Cut cooled trace ") || child.name.StartsWith("Weld cooled trace "))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            fx.beamCore=Line(fx.transform,"White-hot beam core",mat,.0025f,.001f);
            fx.beamHalo=Line(fx.transform,"Plasma outer halo",mat,.014f,.007f);
            if(fx.beam) { fx.beam.sharedMaterial=mat; fx.beam.startWidth=.007f; fx.beam.endWidth=.003f; }
            fx.cutContact=Particles(fx.transform,"Cut metallic sparks",mat,Color.white,.3f,.0017f,1.15f,50,48,58);
            fx.weldContact=Particles(fx.transform,"Weld metallic sparks",mat,Color.white,.2f,.0014f,.75f,32,30,34);
            fx.cutMicroSpatter=Particles(fx.transform,"Cut micro-spatter",mat,Color.white,.2f,.0011f,1.25f,13,15,60,true);
            fx.weldMicroSpatter=Particles(fx.transform,"Weld micro-spatter",mat,Color.white,.14f,.001f,.8f,8,12,38,true);
            fx.cutContactFlash=Particles(fx.transform,"Cut white-hot flash",mat,new Color(3,2.5f,1),.045f,.018f,.01f,26,8,8);
            fx.weldContactFlash=Particles(fx.transform,"Weld white-hot flash",mat,new Color(2,3,3.5f),.04f,.012f,.01f,22,8,8);
            var light=Child(fx.transform,"Optional contact light").GetComponent<Light>();
            if(!light)light=Child(fx.transform,"Optional contact light").gameObject.AddComponent<Light>();
            light.type=LightType.Point; light.range=.22f; light.intensity=1.6f; light.shadows=LightShadows.None; light.enabled=false;
            fx.contactLight=light; fx.enableContactLight=false;
            fx.loopSource=Source(fx.transform,"Burner start and loop",.9f);
            fx.contactSource=Source(fx.transform,"Metal work loop",.75f);
            fx.oneShotSource=Source(fx.transform,"Thermal one shots",.55f);
            fx.cutStart=fx.weldStart=Clip("gas-burner-start.wav");
            fx.cutLoop=fx.weldLoop=Clip("gas-burner-loop.wav");
            fx.cutContactClip=fx.weldContactClip=Clip("welding-continuous-loop.wav");
            fx.cutCrackle=null; fx.weldCrackle=null;
        }
    }
}
