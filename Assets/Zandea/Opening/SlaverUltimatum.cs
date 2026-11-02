using System;
using JunkerBody;
using KenshiVR.MeleeCombat;
using TMPro;
using UnityEngine;

namespace Zandea.Opening
{
    public enum UltimatumResult { None, Surrendered, Combat, Pursuit }

    /// <summary>Opening-specific demand. Capture and combat systems own the resulting behavior.</summary>
    public sealed class SlaverUltimatum : MonoBehaviour
    {
        public StarvingBandit leader;
        public StarvingBandit[] guards;
        public VRBodyController player;
        public PlayerSurrenderGesture surrender;
        public AudioClip demandClip, warningClip;
        public TMP_Text subtitle;
        [TextArea] public string demandText = "Hands up. Come quietly, and you might live.";
        [TextArea] public string warningText = "Last chance. Hands up.";
        [Min(1f)] public float responseSeconds = 8f;
        [Min(0f)] public float warningExtensionSeconds = 3f;
        [Min(.5f)] public float flightDistance = 8f;
        [Min(.1f)] public float outwardSpeed = .4f;
        [Min(.1f)] public float flightHoldSeconds = .8f;
        [Min(.1f)] public float weaponStrikeSpeed = 2.5f;
        [Min(.1f)] public float weaponThreatDistance = 2.5f;
        public bool escalateAfterTimeout = true;

        public bool IsDemanding { get; private set; }
        public UltimatumResult Result { get; private set; }
        public event Action DemandStarted, WarningIssued, DemandInterrupted;
        public event Action CaptureIntent, CombatIntent, PursuitIntent;

        AudioSource voice;
        bool[] priorDefensive;
        float responseAt, priorDistance, flightSince = -1f;
        bool warned;

        void Awake()
        {
            if (!player && surrender) player = surrender.body;
            if (!surrender && player) surrender = player.GetComponentInParent<PlayerSurrenderGesture>();
        }

        void OnEnable()
        {
            if (surrender)
            {
                surrender.SurrenderConfirmed += OnSurrender;
                surrender.SurrenderCancelled += OnSurrenderCancelled;
            }
        }

        void OnDisable()
        {
            if (surrender)
            {
                surrender.SurrenderConfirmed -= OnSurrender;
                surrender.SurrenderCancelled -= OnSurrenderCancelled;
            }
            if (IsDemanding) DemandInterrupted?.Invoke();
            RestoreDefensive();
            IsDemanding = false;
            if (voice) voice.Stop();
            HideSubtitle();
        }

        public bool Begin()
        {
            if (IsDemanding || Result != UltimatumResult.None || !leader || !player || !player.hmd) return false;
            IsDemanding = true;
            warned = false;
            flightSince = -1f;
            priorDistance = DistanceToPlayer();
            priorDefensive = new bool[(guards?.Length ?? 0) + 1];
            priorDefensive[0] = leader.defensiveOnly;
            leader.defensiveOnly = true;
            for (int i = 0; i < (guards?.Length ?? 0); i++)
                if (guards[i]) { priorDefensive[i + 1] = guards[i].defensiveOnly; guards[i].defensiveOnly = true; }
            Speak(demandClip, demandText);
            responseAt = Time.time + Mathf.Max(responseSeconds, demandClip ? demandClip.length : 0f);
            DemandStarted?.Invoke();
            if (surrender && surrender.IsSurrendering) OnSurrender();
            return true;
        }

        public void ResetDemand()
        {
            if (IsDemanding) DemandInterrupted?.Invoke();
            RestoreDefensive();
            IsDemanding = false;
            Result = UltimatumResult.None;
            warned = false;
            flightSince = -1f;
            if (voice) voice.Stop();
            HideSubtitle();
        }

        void Update()
        {
            if (!IsDemanding || !leader || !player || !player.hmd) return;
            if (PlayerAttacked()) { Resolve(UltimatumResult.Combat); return; }
            float distance = DistanceToPlayer();
            float radialSpeed = (distance - priorDistance) / Mathf.Max(Time.deltaTime, .001f);
            priorDistance = distance;
            if (distance > flightDistance && radialSpeed > outwardSpeed)
            {
                if (flightSince < 0f) flightSince = Time.time;
                if (Time.time - flightSince >= flightHoldSeconds)
                { Resolve(UltimatumResult.Pursuit); return; }
            }
            else flightSince = -1f;
            if (Time.time < responseAt || !escalateAfterTimeout) return;
            if (!warned && warningExtensionSeconds > 0f)
            {
                warned = true;
                Speak(warningClip, warningText);
                responseAt = Time.time + Mathf.Max(warningExtensionSeconds, warningClip ? warningClip.length : 0f);
                WarningIssued?.Invoke();
            }
            else Resolve(UltimatumResult.Combat);
        }

        bool PlayerAttacked()
        {
            if (!player) return false;
            foreach (var hand in player.GetComponentsInChildren<PlayerMartialHand>(true))
                if (hand.State == PlayerMartialHand.HandState.ActivePunch) return true;
            foreach (var weapon in PhysicalMeleeWeapon.Active)
            {
                if (!weapon || weapon.State != PhysicalMeleeWeapon.HoldState.HeldByPlayer ||
                    weapon.ownerRoot != player.transform.root) continue;
                var body = weapon.GetComponent<Rigidbody>();
                if (!body || body.linearVelocity.magnitude < weaponStrikeSpeed ||
                    Vector3.Distance(weapon.transform.position, leader.transform.position) > weaponThreatDistance) continue;
                if (Vector3.Dot(body.linearVelocity, leader.transform.position - weapon.transform.position) > 0f) return true;
            }
            return false;
        }

        float DistanceToPlayer() => Vector3.Distance(player.hmd.position, leader.transform.position);
        void OnSurrender() { if (IsDemanding) Resolve(UltimatumResult.Surrendered); }
        void OnSurrenderCancelled(SurrenderCancelReason reason)
        { if (IsDemanding && reason == SurrenderCancelReason.Attack) Resolve(UltimatumResult.Combat); }
        public void RegisterAggression() { if (IsDemanding) Resolve(UltimatumResult.Combat); }

        void Resolve(UltimatumResult result)
        {
            if (!IsDemanding || Result != UltimatumResult.None) return;
            IsDemanding = false;
            Result = result;
            RestoreDefensive();
            HideSubtitle();
            if (voice) voice.Stop();
            switch (result)
            {
                case UltimatumResult.Surrendered: CaptureIntent?.Invoke(); break;
                case UltimatumResult.Pursuit: PursuitIntent?.Invoke(); break;
                case UltimatumResult.Combat: CombatIntent?.Invoke(); break;
            }
        }

        void RestoreDefensive()
        {
            if (priorDefensive == null) return;
            if (leader) leader.defensiveOnly = priorDefensive[0];
            for (int i = 0; i < (guards?.Length ?? 0) && i + 1 < priorDefensive.Length; i++)
                if (guards[i]) guards[i].defensiveOnly = priorDefensive[i + 1];
            priorDefensive = null;
        }

        void Speak(AudioClip clip, string text)
        {
            if (!voice && leader)
            {
                var source = new GameObject("Opening demand voice");
                source.transform.SetParent(leader.transform, false);
                source.transform.localPosition = Vector3.up * 1.5f;
                voice = source.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.minDistance = 1.5f;
                voice.maxDistance = 18f;
                voice.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            if (clip && voice) voice.PlayOneShot(clip);
            else Debug.Log("Slaver demand (recorded voice unassigned): " + text, this);
            if (subtitle) { subtitle.text = text; subtitle.gameObject.SetActive(true); }
        }

        void HideSubtitle() { if (subtitle) subtitle.gameObject.SetActive(false); }
    }
}
