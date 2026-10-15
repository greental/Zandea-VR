using UnityEngine;
using VRKoombar.WeaponsStaging.Core;

// Audio companion for CombatDamageable's existing destruction effect.
[RequireComponent(typeof(CombatDamageable))]
public sealed class NPCShipDeathAudioV3 : MonoBehaviour
{
    [SerializeField] AudioClip explosionSound;
    [SerializeField, Range(0f, 2f)] float volume = 1f;
    [SerializeField, Range(0f, 1f)] float spatialBlend = .7f;
    [SerializeField, Min(.1f)] float minDistance = 25f;
    [SerializeField, Min(1f)] float maxDistance = 1200f;

    CombatDamageable damageable;
    bool played;

    void Awake() => damageable = GetComponent<CombatDamageable>();
    void Update() => PlayIfDestroyed();

    void OnDisable() => PlayIfDestroyed();
    void OnDestroy() => PlayIfDestroyed();

    void PlayIfDestroyed()
    {
        if (played || !Application.isPlaying || explosionSound == null ||
            ReferenceEquals(damageable, null) || !damageable.IsDestroyed)
            return;

        played = true;
        GameObject emitter = new GameObject("EnemyShipDeathAudioV3");
        emitter.transform.position = transform.position;
        AudioSource source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance + 1f, maxDistance);
        source.dopplerLevel = 0f;
        source.volume = volume;
        source.clip = explosionSound;
        source.Play();
        Destroy(emitter, explosionSound.length + .2f);
    }
}
