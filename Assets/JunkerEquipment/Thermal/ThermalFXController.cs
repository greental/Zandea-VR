using UnityEngine;
using UnityEngine.Events;

namespace JunkerEquipment.Thermal
{
    public sealed class ThermalFXController : MonoBehaviour
    {
        public ParticleSystem cutNozzle,weldNozzle,cutContact,weldContact,metalGlow,cutComplete,weldComplete;
        public ParticleSystem cutMicroSpatter,weldMicroSpatter,cutContactFlash,weldContactFlash;
        public LineRenderer beam,beamCore,beamHalo;
        public Light contactLight;
        public bool enableContactLight;
        [Min(0), Tooltip("Pull sparks back toward the nozzle, along the tool's local -Z axis, so they stay visible above the metal.")]
        public float sparkBackOffset=.1f;
        public AudioSource loopSource,oneShotSource,contactSource;
        public AudioClip modeSwitch,cutStart,cutLoop,cutStop,weldStart,weldLoop,weldStop,cutContactClip,weldContactClip,cutCompleteClip,weldCompleteClip,invalidClip;
        public AudioClip cutCrackle,weldCrackle;
        public UnityEvent switched=new(),started=new(),stopped=new(),contactStarted=new();
        public Color cutColor=new(1,.22f,.02f),weldColor=new(.12f,.6f,1);
        bool running,contact,starting; float nextCrackle;
        ThermalMode mode;
        public Material PathHeatMaterial => beam ? beam.sharedMaterial : null;
        void Shot(AudioClip clip) { if(clip && oneShotSource) oneShotSource.PlayOneShot(clip); }
        static void Particle(ParticleSystem ps,bool play) { if(!ps) return; if(play) { if(!ps.isPlaying) ps.Play(); } else if(ps.isPlaying || ps.particleCount>0) ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
        public void Switch() { Stop(); Shot(modeSwitch); switched.Invoke(); }
        void StartBurner()
        {
            if(!loopSource) return;
            loopSource.Stop(); loopSource.loop=false; loopSource.clip=mode==ThermalMode.Cut?cutStart:weldStart;
            starting=loopSource.clip;
            if(starting) loopSource.Play(); else BeginLoop();
        }
        void BeginLoop()
        {
            if(!loopSource) return;
            loopSource.Stop(); loopSource.clip=mode==ThermalMode.Cut?cutLoop:weldLoop; loopSource.loop=true; starting=false;
            if(loopSource.clip) loopSource.Play();
        }
        void Update()
        {
            if(running && starting && loopSource && !loopSource.isPlaying) BeginLoop();
        }
        static void SetBeam(LineRenderer line,bool active,Vector3 a,Vector3 b,Color color)
        {
            if(!line) return;
            line.enabled=active;
            if(!active) return;
            line.useWorldSpace=true; line.positionCount=2; line.SetPosition(0,a); line.SetPosition(1,b); line.startColor=line.endColor=color;
        }
        public void Render(ThermalMode value,bool active,bool touching,Vector3 origin,Vector3 endpoint,Vector3 normal)
        {
            if(running && (!active || value!=mode)) Stop();
            mode=value;
            if(active && !running) { running=true; StartBurner(); started.Invoke(); }
            Particle(cutNozzle,active && mode==ThermalMode.Cut); Particle(weldNozzle,active && mode==ThermalMode.Weld);
            Vector3 sparkPoint=endpoint+(origin-endpoint).normalized*sparkBackOffset;
            Move(cutContact,sparkPoint,normal); Move(weldContact,sparkPoint,normal); Move(metalGlow,endpoint,normal);
            Move(cutMicroSpatter,sparkPoint,normal); Move(weldMicroSpatter,sparkPoint,normal);
            Move(cutContactFlash,endpoint,normal); Move(weldContactFlash,endpoint,normal);
            Particle(cutContact,active && touching && mode==ThermalMode.Cut); Particle(weldContact,active && touching && mode==ThermalMode.Weld);
            Particle(cutMicroSpatter,active && touching && mode==ThermalMode.Cut); Particle(weldMicroSpatter,active && touching && mode==ThermalMode.Weld);
            Particle(cutContactFlash,active && touching && mode==ThermalMode.Cut); Particle(weldContactFlash,active && touching && mode==ThermalMode.Weld);
            Particle(metalGlow,active && touching);
            if(touching!=contact)
            {
                contact=touching;
                if(contactSource) { contactSource.Stop(); contactSource.clip=mode==ThermalMode.Cut?cutContactClip:weldContactClip; contactSource.loop=true; if(contact && contactSource.clip) contactSource.Play(); }
                if(contact) contactStarted.Invoke();
            }
            if(active && touching)
            {
                if(Time.time>=nextCrackle) { Shot(mode==ThermalMode.Cut?cutCrackle:weldCrackle); nextCrackle=Time.time+Random.Range(.25f,.5f); }
            }
            if(contactLight)
            {
                contactLight.enabled=enableContactLight && active && touching;
                if(contactLight.enabled) { contactLight.transform.position=endpoint+normal*.012f; contactLight.color=mode==ThermalMode.Cut?new Color(1,.55f,.22f):new Color(.55f,.85f,1); }
            }
            SetBeam(beam,active,origin,endpoint,mode==ThermalMode.Cut?new Color(3,1.15f,.15f):new Color(.4f,2.2f,3.2f));
            SetBeam(beamCore,active,origin,endpoint,mode==ThermalMode.Cut?new Color(3,2.6f,1.5f):new Color(2.2f,3,3.5f));
            SetBeam(beamHalo,active,origin,endpoint,mode==ThermalMode.Cut?new Color(1.7f,.12f,.015f,.25f):new Color(.07f,.35f,2.1f,.25f));
        }
        static void Move(ParticleSystem ps,Vector3 point,Vector3 normal) { if(ps) { ps.transform.position=point; ps.transform.rotation=Quaternion.LookRotation(normal); } }
        public void Completed(ThermalMode value,Vector3 point) { var effect=value==ThermalMode.Cut?cutComplete:weldComplete; if(effect) { effect.transform.position=point; effect.Play(); } Shot(value==ThermalMode.Cut?cutCompleteClip:weldCompleteClip); }
        public void Stop()
        {
            if(running) { Shot(mode==ThermalMode.Cut?cutStop:weldStop); stopped.Invoke(); }
            running=false; contact=false; starting=false;
            if(loopSource) loopSource.Stop(); if(contactSource) contactSource.Stop(); if(contactLight) contactLight.enabled=false;
            Particle(cutNozzle,false); Particle(weldNozzle,false); Particle(cutContact,false); Particle(weldContact,false); Particle(metalGlow,false);
            Particle(cutMicroSpatter,false); Particle(weldMicroSpatter,false); Particle(cutContactFlash,false); Particle(weldContactFlash,false);
            if(beam) beam.enabled=false; if(beamCore) beamCore.enabled=false; if(beamHalo) beamHalo.enabled=false;
        }
        void OnDisable() { Stop(); Particle(cutComplete,false); Particle(weldComplete,false); if(oneShotSource) oneShotSource.Stop(); }
    }
}
