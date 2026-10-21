using System;
using System.IO;
using System.Linq;
using System.Reflection;
using JunkerBody;
using KenshiVR.Bandaging;
using KenshiVR.Bandaging.Editor;
using KenshiVR.MeleeCombat;
using KenshiVR.CharacterCreator;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using Zandea.POI;

[InitializeOnLoad]
public static class BugPassValidation
{
    const string Folder = "Logs/BugPass_20261019";
    const string Running = "BugPassValidation.Running";
    static BugPassValidation() => EditorApplication.update += Tick;
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        File.AppendAllText(Folder + "/report.txt", "PASS " + message + "\n");
    }
    static void Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    public static void RunBatch()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/report.txt", "19 October bug pass\n");
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bandage = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/medical/BandageRoll.prefab");
            Require(bandage.GetComponent<BoxCollider>() && !bandage.GetComponent<CapsuleCollider>() &&
                bandage.GetComponent<BoxCollider>().size.x >= .14f, "bandage has a broad box contact shape");
            foreach (string path in new[] { "Assets/MyAssets/prefabs/weapons/Player practice sword - generated long blade.prefab",
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/PlayerPracticeLongBlade.prefab" })
            {
                var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<PhysicalMeleeWeapon>();
                Require(weapon.rightGrip && weapon.leftGrip && weapon.rightGripEuler == Vector3.zero &&
                    weapon.leftGripEuler == Vector3.zero, "perpendicular practice sword pose: " + path);
            }
            CheckCollisionGuard();
            var scene = EditorSceneManager.OpenScene("Assets/Main.unity", OpenSceneMode.Single);
            var poi = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BanditOutpost>(true)).First();
            var preview = poi.transform.Find("Layout Preview");
            Require(preview && preview.childCount > 0, "Main contains the handcrafted layout");
            var before = poi.stableSite;
            Invoke(poi, "AdoptAuthoredSite", preview.position);
            File.AppendAllText(Folder + "/report.txt", $"Main preview={preview.position}, saved site={before}, resolved site={poi.stableSite}, height={poi.siteHeight}\n");
            Require(before != poi.stableSite, "edited settlement placement resolves independently of stale generated site");
            // Scene changes above are in memory only; never save validation fixtures.
            PrepareAuthoredFixture(scene);
            SessionState.SetInt(Running, 1);
            SessionState.SetString(Running + ".Start", DateTime.UtcNow.ToString("O"));
            BandagePlayValidation.Run();
        }
        catch (Exception e) { Fail(e); }
    }

    static void PrepareAuthoredFixture(Scene main)
    {
        var source = main.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BanditGroup>(true))
            .First(group => group.authoredMembers != null && group.authoredMembers.Any(member => member));
        var copy = UnityEngine.Object.Instantiate(source.gameObject);
        copy.name = "Authored group regression fixture";
        var group = copy.GetComponent<BanditGroup>();
        group.buildLocalNavMesh = false;
        foreach (var member in group.authoredMembers.Where(member => member))
        {
            member.enabled = false;
            member.GetComponent<NavMeshAgent>().enabled = false;
        }
        var empty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.MoveGameObjectToScene(copy, empty);
        EditorSceneManager.CloseScene(main, true);
        SceneManager.SetActiveScene(empty);
        SessionState.SetString(Running + ".Authored", Snapshot(group));
        SessionState.SetBool(Running + ".AuthoredChecked", false);
    }

    static string Snapshot(BanditGroup group) => string.Join("\n", group.authoredMembers.Where(member => member).Select(member =>
    {
        var appearance = member.GetComponent<NPCCharacterAppearance>();
        var cosmetics = member.GetComponent<CharacterCosmeticsController>();
        var weapon = member.GetComponent<NpcGeneratedWeapon>();
        return member.name + ":" + (appearance ? appearance.seed : 0) + ":" +
            (cosmetics && cosmetics.headwear ? cosmetics.headwear.name : "none") + ":" +
            (cosmetics && cosmetics.torso ? cosmetics.torso.name : "none") + ":" +
            (weapon && weapon.weaponPrefab ? weapon.weaponPrefab.name : "none");
    }));

    static void CheckAuthoredFixture()
    {
        var group = GameObject.Find("Authored group regression fixture").GetComponent<BanditGroup>();
        Require(Snapshot(group) == SessionState.GetString(Running + ".Authored", ""),
            "Main authored members retain their own seed, attire and weapon selection after startup");
        var member = group.authoredMembers.First(candidate => candidate);
        var cosmetics = member.GetComponent<CharacterCosmeticsController>();
        var hat = AssetDatabase.FindAssets("t:AppearanceItemDefinition")
            .Select(id => AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(id)))
            .First(item => item.slot == AppearanceSlot.Headwear && item.prefab && !item.skinned && item != cosmetics.headwear);
        cosmetics.headwear = hat; cosmetics.Refresh();
        var inventory = member.GetComponent<NpcInventory>();
        inventory.AdoptWornClothing();
        member.GetComponent<ScavengerHealth>().ForceDeath();
        var loot = member.GetComponent<NpcClothingLoot>();
        Invoke(loot, "Build", NpcEquipmentSlot.Headwear, AppearanceSlot.Headwear);
        Invoke(loot, "SetAvailability");
        var pull = member.GetComponentsInChildren<XRGrabInteractable>(true)
            .FirstOrDefault(grab => grab.name == "Pull " + hat.name);
        Require(pull && pull.GetComponent<Collider>().enabled && pull.interactionLayers.value != 0,
            "late-authored headwear creates an enabled corpse pull handle and inventory item");
        var managerObject = new GameObject("Headgear interaction manager");
        var manager = managerObject.AddComponent<XRInteractionManager>();
        var handObject = new GameObject("Headgear test hand");
        var hand = handObject.AddComponent<XRDirectInteractor>();
        hand.interactionManager = manager; pull.interactionManager = manager;
        hand.transform.position = pull.transform.position;
        hand.StartManualInteraction((IXRSelectInteractable)pull);
        Require(pull.isSelected, "dead bandit headwear accepts selection");
        hand.transform.position += Vector3.right;
        Invoke(loot, "Update");
        Require(inventory.Equipped(NpcEquipmentSlot.Headwear) == null &&
            UnityEngine.Object.FindObjectsByType<NpcWorldItem>(FindObjectsSortMode.None)
                .Any(item => item.Item?.Definition?.equippedAppearance == hat), "pulling headwear drops matching physical loot");
        UnityEngine.Object.Destroy(handObject); UnityEngine.Object.Destroy(managerObject);
        UnityEngine.Object.Destroy(group.gameObject);
    }

    static void CheckCollisionGuard()
    {
        var player = new GameObject("Collision guard fixture");
        var item = new GameObject("Held blade fixture");
        try
        {
            var body = player.AddComponent<VRBodyController>();
            body.enabled = false; body.xrOrigin = player.transform;
            var capsule = player.AddComponent<CapsuleCollider>();
            var handObject = new GameObject("Hand"); handObject.transform.SetParent(player.transform);
            var hand = handObject.AddComponent<XRDirectInteractor>();
            var gravity = player.AddComponent<GravityProvider>(); gravity.enabled = false;
            gravity.sphereCastLayerMask = ~0;
            var shape = item.AddComponent<BoxCollider>();
            var grab = item.AddComponent<XRGrabInteractable>();
            var guard = item.AddComponent<HeldLootCollisionGuard>(); guard.Bind(grab);
            grab.selectEntered.Invoke(new SelectEnterEventArgs { interactorObject = hand, interactableObject = grab });
            Require(Physics.GetIgnoreCollision(shape, capsule), "held item ignores the player capsule");
            Require((gravity.sphereCastLayerMask & (1 << item.layer)) == 0, "held item cannot satisfy the XRI ground query");
            shape.enabled = false; shape.enabled = true;
            Physics.IgnoreCollision(shape, capsule, false);
            Invoke(guard, "Protect");
            Require(Physics.GetIgnoreCollision(shape, capsule), "collider reactivation cannot restore held-player collision");
            Invoke(guard, "Restore");
            Require(item.layer == 0 && !Physics.GetIgnoreCollision(shape, capsule), "released item restores its original layer and contact");
        }
        finally { UnityEngine.Object.DestroyImmediate(item); UnityEngine.Object.DestroyImmediate(player); }
    }

    static void Tick()
    {
        int stage = SessionState.GetInt(Running, 0);
        if (stage == 0 || EditorApplication.isCompiling) return;
        try
        {
            if (stage == 1 && EditorApplication.isPlaying && Time.time > .75f && !SessionState.GetBool(Running + ".AuthoredChecked", false))
            {
                CheckAuthoredFixture();
                SessionState.SetBool(Running + ".AuthoredChecked", true);
            }
            var start = DateTime.Parse(SessionState.GetString(Running + ".Start", ""), null, System.Globalization.DateTimeStyles.RoundtripKind);
            if ((DateTime.UtcNow - start).TotalMinutes > 5) throw new Exception("Play validation timed out");
            if (stage == 3)
            {
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetInt(Running, 4);
                Zandea.POI.Editor.OutpostPlayValidation.BeginMain();
                return;
            }
            string result = stage == 1 ? "Temp/BandagePlayValidation.result" : stage == 2 ?
                "Temp/CombatInteractionPlayValidation.result" : "Temp/ZandeaPOI.play-validation.txt";
            if (!File.Exists(result) || File.GetLastWriteTimeUtc(result) < start || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var text = File.ReadAllText(result);
            if (stage < 3 ? !text.StartsWith("PASS") : text.Contains("FAIL") || !text.Contains("COMPLETE Main")) throw new Exception(text);
            File.AppendAllText(Folder + "/report.txt", text + "\n");
            if (stage == 1)
            {
                SessionState.SetInt(Running, 2);
                SessionState.SetString(Running + ".Start", DateTime.UtcNow.ToString("O"));
                CombatInteractionPlayValidation.Run();
                return;
            }
            if (stage == 2)
            {
                SessionState.SetInt(Running, 3);
                SessionState.SetString(Running + ".Start", DateTime.UtcNow.ToString("O"));
                return;
            }
            SessionState.SetInt(Running, 0);
            File.AppendAllText(Folder + "/report.txt", "SUITE PASS\n");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e) { Fail(e); }
    }
    static void Fail(Exception error)
    {
        SessionState.SetInt(Running, 0);
        File.AppendAllText(Folder + "/report.txt", "FAIL " + error + "\n");
        Debug.LogException(error);
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
