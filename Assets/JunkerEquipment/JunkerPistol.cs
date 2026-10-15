using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
namespace JunkerEquipment
{
    [RequireComponent(typeof(JunkerEquipmentItem))]
    public sealed class JunkerPistol : MonoBehaviour
    {
        public Transform muzzle, recoilVisual;
        [Tooltip("Movable muzzle compensator/upper barrel section.")]
        public Transform muzzleCompe;
        [Min(0f)] public float muzzleCompeTravel=.014f;
        public GameObject flash;
        [Header("Audio")]
        public AudioSource shotAudio;
        public AudioClip[] shotClips;
        [Header("Optional physical ammunition")]
        public JunkerPistolBatterySocket batterySocket;
        [Range(0f,.15f)] public float pitchVariation=.035f;
        [Min(.1f)] public float fireRate=5;
        public float range=100, damage=25;
        public LayerMask hitMask=~0;
        [Header("Laser shot visual (optional)")]
        public bool showLaserShot;
        public Material laserShotMaterial;
        [Min(1f)] public float laserShotSpeed=45f;
        [Min(.02f)] public float laserShotLength=.28f;
        [Min(.001f)] public float laserShotWidth=.009f;
        [Min(0f)] public float impactImpulse=3f;
        [Min(.005f)] public float scorchSize=.045f;
        public int ShotsFired { get; private set; }
        public bool LastShotHit { get; private set; }
        float nextShot, flashUntil, kick;
        Vector3 restPosition, muzzleCompeRestPosition;
        JunkerEquipmentItem item;
        void Start() { item=GetComponent<JunkerEquipmentItem>(); item.Grab.activated.AddListener(Fire); if(recoilVisual) restPosition=recoilVisual.localPosition; if(muzzleCompe) muzzleCompeRestPosition=muzzleCompe.localPosition; if(flash) flash.SetActive(false); }
        void OnDestroy() { if(item && item.Grab) item.Grab.activated.RemoveListener(Fire); }
        void Fire(ActivateEventArgs _) { TryFire(); }
        public bool TryFire()
        {
            if(!item || !item.Grab.isSelected || !muzzle || Time.time<nextShot) return false;
            return FireShot(null);
        }
        // NPC-owned pistols use the same cell, rate limit, hit, recoil and audio path.
        // XR selection is intentionally required only by the player's TryFire path.
        public bool TryFireNpc(Transform shooterRoot, Vector3 aimPoint)
        {
            if (!shooterRoot || !muzzle || Time.time < nextShot || (aimPoint-muzzle.position).sqrMagnitude < .001f) return false;
            return FireShot(shooterRoot, (aimPoint-muzzle.position).normalized);
        }
        bool FireShot(Transform shooterRoot, Vector3 npcDirection = default)
        {
            if(batterySocket && !batterySocket.TryConsumeShot()) return false;
            nextShot=Time.time+1/Mathf.Max(.1f,fireRate); ShotsFired++;
            LastShotHit=false;
            Vector3 direction=shooterRoot ? npcDirection : muzzle.forward;
            Transform ignoredRig=shooterRoot ? shooterRoot : item && item.rig && item.rig.body ? item.rig.body.xrOrigin : null;
            JunkerLaserShot.Spawn(muzzle.position,direction,range,showLaserShot ? laserShotMaterial : null,
                laserShotSpeed,laserShotLength,laserShotWidth,hitMask,ignoredRig,transform,
                shooterRoot ? shooterRoot.gameObject : gameObject,this,damage,impactImpulse,scorchSize);
            flashUntil=Time.time+.055f; if(flash) flash.SetActive(true);
            if(shotAudio && shotClips!=null && shotClips.Length>0)
            {
                var clip=shotClips[Random.Range(0,shotClips.Length)];
                if(clip) { shotAudio.pitch=1f+Random.Range(-pitchVariation,pitchVariation); shotAudio.PlayOneShot(clip); }
            }
            kick=muzzleCompeTravel; if(item) item.Pulse(.45f,.05f); return true;
        }
        internal void NotifyProjectileImpact() { LastShotHit=true; }
        void Update()
        {
            if(flash && Time.time>=flashUntil) flash.SetActive(false);
            kick=Mathf.MoveTowards(kick,0,Time.deltaTime*.12f);
            if(muzzleCompe && muzzle)
                muzzleCompe.localPosition=muzzleCompeRestPosition-muzzleCompe.parent.InverseTransformDirection(muzzle.forward)*kick;
            else if(recoilVisual && muzzle)
                recoilVisual.localPosition=restPosition-transform.InverseTransformDirection(muzzle.forward)*kick;
        }
    }
}
