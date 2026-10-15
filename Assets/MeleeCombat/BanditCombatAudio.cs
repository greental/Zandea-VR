using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Audio is kept on the NPC root so existing bandit prefabs receive it at runtime.
    public sealed class BanditCombatAudio : MonoBehaviour
    {
        static readonly AudioClip[][] Voices = new AudioClip[3][];
        static AudioClip[] sharedGrunts, parries;
        AudioSource voiceSource, metalSource;
        ScavengerNPC.ScavengerHealth health;
        int voiceIndex;
        float lastParryTime = -10f;

        public void Initialize(StarvingBandit bandit)
        {
            for (int i = 0; i < Voices.Length; i++)
                if (Voices[i] == null) Voices[i] = Resources.LoadAll<AudioClip>($"CombatAudio/Male{i + 1}");
            if (sharedGrunts == null) sharedGrunts = Resources.LoadAll<AudioClip>("CombatAudio/SharedGrunts");
            if (parries == null) parries = Resources.LoadAll<AudioClip>("CombatAudio/Parry");

            voiceIndex = Mathf.Abs(GetInstanceID() % Voices.Length);
            voiceSource = MakeSource("Voice", .85f);
            metalSource = MakeSource("Parry", 1f);
            health = bandit.health;
            if (health)
            {
                health.onDamaged.AddListener(PlayHit);
                health.onDeath.AddListener(PlayHit);
            }
        }

        AudioSource MakeSource(string sourceName, float volume)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1.5f;
            source.maxDistance = 18f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.volume = volume;
            return source;
        }

        void OnDestroy()
        {
            if (health)
            {
                health.onDamaged.RemoveListener(PlayHit);
                health.onDeath.RemoveListener(PlayHit);
            }
        }

        public void PlayAttack() => PlayVoice();
        void PlayHit() => PlayVoice();

        void PlayVoice()
        {
            if (!voiceSource) return;
            var pack = Voices[voiceIndex];
            var clips = sharedGrunts != null && sharedGrunts.Length > 0 && Random.value < .25f
                ? sharedGrunts : pack;
            if (clips == null || clips.Length == 0) clips = sharedGrunts;
            if (clips == null || clips.Length == 0) return;
            voiceSource.pitch = Random.Range(.94f, 1.06f);
            voiceSource.PlayOneShot(clips[Random.Range(0, clips.Length)]);
        }

        public void PlayParry()
        {
            if (!metalSource || parries == null || parries.Length == 0 ||
                Time.time - lastParryTime < .18f) return;
            lastParryTime = Time.time;
            metalSource.pitch = Random.Range(.96f, 1.04f);
            metalSource.PlayOneShot(parries[Random.Range(0, parries.Length)]);
        }
    }
}
