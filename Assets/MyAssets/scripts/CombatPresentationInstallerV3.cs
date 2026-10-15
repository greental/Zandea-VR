using UnityEngine;
using VRKoombar.WeaponsStaging.Core;

// Single scene-only opt-in for Razara's new cockpit and hull presentation.
public sealed class CombatPresentationInstallerV3 : MonoBehaviour
{
    [SerializeField] ShipHUDManager playerHud;
    [SerializeField] GameObject smokePrefab;
    [SerializeField] GameObject sparksPrefab;
    [SerializeField, Range(0f, 1f)] float smokeHullThreshold = .65f;
    [SerializeField, Range(0f, 1f)] float sparksHullThreshold = .4f;

    void Awake()
    {
        if (playerHud == null) playerHud = FindFirstObjectByType<ShipHUDManager>();
        if (playerHud == null) return;
        CombatDamageable playerHull = playerHud.GetComponentInParent<CombatDamageable>();
        GameObject ship = playerHull != null ? playerHull.gameObject : playerHud.transform.root.gameObject;
        if (playerHull != null) playerHull.PreserveDestroyedObject();
        if (ship.GetComponent<CombatDamageable>() != null && ship.GetComponent<ShipDamagePresentationV3>() == null)
        {
            var damage = ship.AddComponent<ShipDamagePresentationV3>();
            damage.Configure(smokePrefab, sparksPrefab, smokeHullThreshold, sparksHullThreshold, true);
        }
        SpeedIndicatorHUD speed = ship.GetComponentInChildren<SpeedIndicatorHUD>(true);
        if (speed != null && speed.GetComponent<PlayerVitalsScreenV3>() == null)
            speed.gameObject.AddComponent<PlayerVitalsScreenV3>();
    }

    void Start()
    {
        foreach (NPCShipCombatV2 enemy in FindObjectsByType<NPCShipCombatV2>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            CombatDamageable hull = enemy.GetComponent<CombatDamageable>();
            if (hull == null) continue;
            hull.PreserveDestroyedObject();
            if (enemy.GetComponent<ShipDamagePresentationV3>() != null) continue;
            var damage = enemy.gameObject.AddComponent<ShipDamagePresentationV3>();
            damage.Configure(smokePrefab, sparksPrefab, smokeHullThreshold, sparksHullThreshold, true);
        }
    }
}
