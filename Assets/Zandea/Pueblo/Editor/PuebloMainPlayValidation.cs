using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using JunkerEquipment;
using JunkerBody;
using MyAssets.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRKoombar.WeaponsStaging.Contracts;
using Zandea.POI;
using Zandea.Props;
using Zandea.FriendlyNPC;
using Zandea.Missions;
using Zandea.Wildlife;

namespace Zandea.Pueblo.Editor
{
    [InitializeOnLoad]
    public static class PuebloMainPlayValidation
    {
        const string Main = "Assets/Main.unity";
        const string Report = "Temp/ZandeaPueblo.main-validation.txt";
        const string Step = "ZandeaPuebloMainStep";
        const string Next = "ZandeaPuebloMainNext";
        const string Deadline = "ZandeaPuebloMainDeadline";
        const string NpcId = "ZandeaPuebloMainNpcId";
        const string MerchantId = "ZandeaPuebloMainMerchantId";
        const string PoiId = "zandea.pueblo.settlement.1";

        static PuebloMainPlayValidation() { EditorApplication.update += Tick; }

        [MenuItem("Zandea/POI/Pueblo/Validate Main Streaming")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before Main validation.");
            if (SceneManager.GetActiveScene().path != Main)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("Could not save open scenes before Main validation.");
                EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
            }
            File.WriteAllText(Report, "RUNNING Pueblo Main treadmill validation\n");
            SessionState.SetInt(Step, 1);
            SessionState.SetFloat(Next, (float)(EditorApplication.timeSinceStartup + 4));
            SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
            EditorApplication.isPlaying = true;
        }

        // Allows the same Main Play Mode checks to finish in a headless editor.
        public static void BeginBatch() => Begin();

        static void Tick()
        {
            int step = SessionState.GetInt(Step, 0);
            if (step == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling ||
                EditorApplication.timeSinceStartup < SessionState.GetFloat(Next, 0)) return;
            SessionState.SetFloat(Next, (float)(EditorApplication.timeSinceStartup + 2));
            try
            {
                var poi = UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).FirstOrDefault(p => p.poiId == PoiId);
                if (!poi) throw new InvalidOperationException("Main is missing the Pueblo POI.");
                var player = UnityEngine.Object.FindFirstObjectByType<VRBodyController>();
                if (!player || !player.xrOrigin)
                    throw new InvalidOperationException("Main has no active walking player's XR origin.");
                if (step == 1)
                {
                    var transition = UnityEngine.Object.FindObjectsByType<PlanetTerrainTransitionController>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(t => t.gameObject.scene == poi.gameObject.scene);
                    if (transition) transition.ForceEnterSurfaceMode();
                    var testStart = poi.GetComponent<PuebloMissionTestStart>();
                    Check(testStart && testStart.ApplyIfReady(),
                        "Pueblo surface-frame test start is configured");
                    float initialDistance = Vector2.Distance(
                        new Vector2(player.xrOrigin.position.x, player.xrOrigin.position.z),
                        new Vector2(poi.Center.x, poi.Center.z));
                    File.AppendAllText(Report, "Initial player=" + player.xrOrigin.position +
                        " Pueblo=" + poi.Center + " layout=" + poi.transform.Find("Layout Preview")?.position +
                        " distance=" + initialDistance + " m\n");
                    Check(initialDistance < 150f, "active player starts near Pueblo before validation moves it");
                    player.xrOrigin.position = poi.Center + poi.ApproachDirection * 45 + Vector3.up * 1.7f;
                    poi.treadmill.ConfigureTarget(player.xrOrigin);
                    File.AppendAllText(Report, "Approach=" + player.xrOrigin.position +
                        " site=" + poi.stableSite + " tile=" + poi.treadmill.CenterTile + "\n");
                    SessionState.SetInt(Step, 2);
                    SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
                    return;
                }
                if (step == 2)
                {
                    if (!poi.IsLoaded)
                    {
                        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0)) return;
                        throw new InvalidOperationException("Friendly Pueblo did not load after approach: " +
                            "loaded=" + poi.IsLoaded + " members=" + poi.Members.Count +
                            " tile=" + poi.treadmill.CenterTile);
                    }
                    Check(poi.Members.Count == 0, "friendly Pueblo has no hostile residents");
                    var savedEncounter = poi.transform.Find("Layout Preview/Editable Bandit Groups");
                    Check(!savedEncounter || savedEncounter.GetComponentsInChildren<StarvingBandit>(true)
                        .All(n => !n.gameObject.activeInHierarchy),
                        "authored hostile groups stay disabled");
                    var terrain = Terrain.activeTerrains.FirstOrDefault(t => t && t.enabled && t.terrainData &&
                        t.GetComponent<TerrainCollider>()?.enabled == true &&
                        poi.Center.x >= t.transform.position.x &&
                        poi.Center.x <= t.transform.position.x + t.terrainData.size.x &&
                        poi.Center.z >= t.transform.position.z &&
                        poi.Center.z <= t.transform.position.z + t.terrainData.size.z);
                    Check(terrain && Mathf.Abs(terrain.SampleHeight(poi.Center) +
                        terrain.transform.position.y - poi.Center.y) < .5f,
                        "stamped Main terrain and collider support the Pueblo center");
                    Check(Mathf.Abs(poi.transform.Find("Layout Preview").position.y -
                        poi.Center.y + .15f) < .02f,
                        "authored Pueblo structures sit 15 cm below the terrain stamp anchor");
                    Check(WildlifeSettlementExclusion.IsExcluded(poi.Center),
                        "settlement wall footprint excludes wildlife and random bandit spawns");
                    var props = poi.GetComponent<OutpostPropPopulation>();
                    // Authored Main layouts keep their hand-placed PhysicalProp instances
                    // and intentionally skip procedural OutpostPropPopulation.Create().
                    var authoredLayout = poi.transform.Find("Layout Preview");
                    var merchant = authoredLayout
                        ? authoredLayout.GetComponentsInChildren<FriendlyNpcInteraction>(true)
                            .FirstOrDefault(n => n.npcId == "merchant-daran" && n.gameObject.activeSelf) : null;
                    Check(merchant && merchant.gameObject.activeInHierarchy &&
                        merchant.GetComponent<StarvingBandit>()?.agent?.isOnNavMesh == true,
                        "Daran is active at his Pueblo post on settlement navigation");
                    if (merchant.contract?.barrels != null)
                        foreach (var barrel in merchant.contract.barrels)
                            File.AppendAllText(Report, "Barrel " + (barrel ? barrel.objectId : "null") +
                                " self=" + (barrel && barrel.gameObject.activeSelf) +
                                " hierarchy=" + (barrel && barrel.gameObject.activeInHierarchy) +
                                " definition=" + (barrel && barrel.definition == merchant.contract.definition) +
                                " breakable=" + (barrel && barrel.GetComponent<BreakableProp>()) +
                                " weight=" + (barrel && barrel.GetComponent<Zandea.Missions.CarryWeight>()) + "\n");
                    var sceneBarrels = UnityEngine.Object.FindObjectsByType<Zandea.Missions.ContractBarrel>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None);
                    File.AppendAllText(Report, "All runtime contract barrels=" + sceneBarrels.Length + " " +
                        string.Join("; ", sceneBarrels.Select(b => b.name + "/" + b.objectId +
                            " active=" + b.gameObject.activeInHierarchy +
                            " definition=" + (b.definition ? b.definition.name : "null") +
                            " matches=" + (merchant.contract && b.definition == merchant.contract.definition) +
                            " root=" + b.transform.root.name +
                            " sameRoot=" + (b.transform.root == merchant.transform.root) +
                            " parent=" + (b.transform.parent ? b.transform.parent.name : "none"))) + "\n");
                    Check(merchant.contract && merchant.contract.barrels?.Length == 3 &&
                        merchant.contract.barrels.All(b => b && b.gameObject.activeInHierarchy &&
                            b.definition == merchant.contract.definition &&
                            b.GetComponent<BreakableProp>() && b.GetComponent<Zandea.Missions.CarryWeight>()),
                        "three physical contract barrels stream in with Pueblo");
                    merchant.contract.Accept();
                    Check(MissionSessions.Get(merchant.contract.definition.missionId).state == MissionState.Accepted &&
                        merchant.contract.ResolveNodeId("daran_job_accept") == "daran_job_accept",
                        "accepting work keeps Daran's get-moving acknowledgement");
                    var merchantCounter = authoredLayout
                        ? authoredLayout.GetComponentsInChildren<VendorCounter>(true)
                            .FirstOrDefault(c => c.merchant == merchant) : null;
                    File.AppendAllText(Report, "Merchant links: counter=" + (merchantCounter ? merchantCounter.name : "null") +
                        " counterActive=" + (merchantCounter && merchantCounter.gameObject.activeInHierarchy) +
                        " handshake=" + (merchantCounter && merchantCounter.handshake ? merchantCounter.handshake.name : "null") +
                        " handshakeCounter=" + (merchantCounter && merchantCounter.handshake && merchantCounter.handshake.counter == merchantCounter) +
                        " choices=" + (merchant && merchant.choices ? merchant.choices.name : "null") +
                        " choicesActive=" + (merchant && merchant.choices && merchant.choices.gameObject.activeInHierarchy) +
                        " playerChoices=" + (player.transform.root.GetComponentInChildren<PalmDialogueChoices>(true) ?
                            player.transform.root.GetComponentInChildren<PalmDialogueChoices>(true).name : "null") +
                        " sameChoices=" + (merchant && merchant.choices == player.transform.root.GetComponentInChildren<PalmDialogueChoices>(true)) + "\n");
                    Check(merchantCounter && merchantCounter.gameObject.activeInHierarchy &&
                        merchantCounter.handshake && merchantCounter.handshake.counter == merchantCounter &&
                        merchant.choices && merchant.choices.gameObject.activeInHierarchy &&
                        merchant.choices == player.transform.root.GetComponentInChildren<PalmDialogueChoices>(true),
                        "Daran, counter, handshake, and player dialogue are linked");
                    var utilities = player.transform.root.GetComponentInChildren<BackpackUtilityAttachments>(true);
                    var purse = CurrencyPouch.PersonalInstance;
                    File.AppendAllText(Report, "Purse: utility=" + (utilities ? utilities.name : "null") +
                        " utilityActive=" + (utilities && utilities.gameObject.activeInHierarchy) +
                        " pursePrefab=" + (utilities && utilities.personalPursePrefab ? utilities.personalPursePrefab.name : "null") +
                        " purse=" + (purse ? purse.name : "null") +
                        " personal=" + (purse && purse.personal) +
                        " home=" + (purse && purse.homeAnchor ? purse.homeAnchor.name : "null") +
                        " anchor=" + (utilities && utilities.coinPurseAnchor ? utilities.coinPurseAnchor.name : "null") +
                        " amount=" + (purse ? purse.Cats : -1) + "\n");
                    Check(utilities && purse && purse.personal && purse.homeAnchor &&
                        purse.homeAnchor == utilities.coinPurseAnchor && purse.Cats > 0,
                        "active V2 player owns the mounted personal purse");
                    var backpackShell = utilities.GetComponent<BoxCollider>();
                    var backpackItem = utilities.GetComponent<JunkerEquipmentItem>();
                    Check(backpackShell && backpackShell.size.x >= .5f &&
                        backpackShell.size.z >= .39f && backpackItem && backpackItem.Grab &&
                        backpackItem.Grab.isActiveAndEnabled &&
                        player.transform.root.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>(true)
                            .All(hand => hand.physicsTriggerInteraction == QueryTriggerInteraction.Collide),
                        "V2 backpack has a broad reachable grab shell for both hands");
                    Check(purse.visualPrefab && purse.visualMaterial &&
                        purse.GetComponentInChildren<TMPro.TMP_Text>(true)?.text ==
                            CurrencyDefinition.FormatZeks(purse.Cats),
                        "personal purse uses the authored money bag and shows its Zeks balance");
                    Check(merchantCounter.paymentSpot && merchantCounter.buyArea &&
                        merchantCounter.sellArea && merchantCounter.payoutPouchPrefab &&
                        merchantCounter.payoutPosition && merchantCounter.configuration,
                        "Main counter has payment and payout sockets");
                    SessionState.SetInt(MerchantId, merchant.GetInstanceID());
                    int authoredPropCount = authoredLayout
                        ? authoredLayout.GetComponentsInChildren<PhysicalProp>(true).Length : 0;
                    int proceduralPropCount = props ? props.Instances.Count : 0;
                    File.AppendAllText(Report, "Props authored=" + authoredPropCount +
                        " procedural=" + proceduralPropCount + "\n");
                    Check(authoredPropCount >= 16 || proceduralPropCount >= 16,
                        "Main Pueblo has populated pots, baskets, and storage props");
                    var authoredPot = authoredLayout
                        ? authoredLayout.GetComponentsInChildren<BreakableProp>(true).FirstOrDefault(p =>
                            p.fracturedPrefab && p.name.IndexOf("pot", StringComparison.OrdinalIgnoreCase) >= 0)
                        : null;
                    Check(authoredPot, "Main Pueblo has a breakable authored pot");
                    var potHit = new DamagePacket { BaseDamage = 20f, WeaponId = "validation-melee",
                        HitPointWorld = authoredPot.transform.position, HitNormalWorld = Vector3.up };
                    authoredPot.ApplyDamage(in potHit);
                    Check(authoredPot.IsBroken, "authored pot breaks from existing damage contract");
                    var path = new NavMeshPath();
                    Check(NavMesh.SamplePosition(poi.GatePosition + poi.ApproachDirection * 5,
                        out var start, 3, NavMesh.AllAreas) &&
                        NavMesh.SamplePosition(poi.Center, out var end, 3, NavMesh.AllAreas) &&
                        NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete,
                        "Main gate connects to the courtyard");
                    // Exercise Main's actual stock, counter, and the active
                    // player's purse. Play Mode restores the authored scene.
                    player.xrOrigin.position = merchant.transform.position +
                        merchant.transform.forward * 1.5f + Vector3.up * 1.7f;
                    Check(merchant.TryBeginTradeFromCounter() && merchantCounter.Trading,
                        "nearby Main player can open Daran's trade session");
                    var stock = authoredLayout.GetComponentsInChildren<TradeItem>(true)
                        .Where(item => item && item.isActiveAndEnabled &&
                            item.ownerId == merchantCounter.configuration.vendorId &&
                            merchantCounter.configuration.Quote(item, true) > 0)
                        .OrderBy(item => merchantCounter.configuration.Quote(item, true))
                        .FirstOrDefault();
                    Check(stock, "Main has priced physical stock");
                    int cost = merchantCounter.configuration.Quote(stock, true);
                    int startingPurse = purse.Cats;
                    Check(startingPurse >= cost, "test purse covers the selected Main stock");
                    stock.transform.position = merchantCounter.buyArea.ClampHoldPoint(
                        merchantCounter.buyArea.transform.position);
                    Physics.SyncTransforms();
                    var stockGrab = stock.GetComponent<XRGrabInteractable>();
                    Check(stockGrab, "Main stock has an XR grab interactable");
                    stockGrab.selectExited.Invoke(new SelectExitEventArgs());
                    Check(stock.HasPhysicalRelease && merchantCounter.TryCapture(stock, merchantCounter.buyArea),
                        "released Main stock is captured in the buy box");
                    merchantCounter.TryOffer(stock);
                    Check(merchantCounter.NetCats == -cost,
                        "Main counter quotes the placed stock");
                    var purseItem = purse.GetComponent<JunkerEquipmentItem>();
                    if (purseItem && purseItem.currentSlot)
                        purseItem.currentSlot.ReleaseItem(purseItem);
                    purse.transform.position = merchantCounter.paymentSpot.transform.TransformPoint(
                        new Vector3(.6f, 0f, 0f));
                    Physics.SyncTransforms();
                    Check(!merchantCounter.paymentSpot.Contains(purse.transform.position) &&
                        merchantCounter.paymentSpot.Contains(purse),
                        "payment socket accepts a pouch touching its edge");
                    merchantCounter.TryPresentPurse(purse);
                    Check(merchantCounter.Ready && merchantCounter.handshake.IsArmed,
                        "Main payment spot accepts the personal purse and arms the handshake");
                    Check(merchantCounter.TryCommit() && purse.Cats == startingPurse - cost &&
                        stock.ownerId == "player" && merchantCounter.CommittedTransactions == 1,
                        "one Main purchase transfers the stock and exactly its net price");
                    Check(!merchantCounter.TryCommit(), "repeated Main handshake cannot charge twice");
                    var scrapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Zandea/FriendlyNPC/Generated/PlayerScrap.prefab");
                    Check(scrapPrefab, "Main sale test has a physical player scrap prefab");
                    var scrapObject = UnityEngine.Object.Instantiate(scrapPrefab,
                        merchantCounter.sellArea.ClampHoldPoint(merchantCounter.sellArea.transform.position),
                        Quaternion.identity);
                    var scrap = scrapObject.GetComponent<TradeItem>();
                    Check(scrap && scrap.ownerId == "player", "sale starts with player-owned scrap");
                    int earnings = merchantCounter.configuration.Quote(scrap, false);
                    Check(earnings > 0, "Daran prices player scrap");
                    Physics.SyncTransforms();
                    scrap.GetComponent<XRGrabInteractable>().selectExited.Invoke(new SelectExitEventArgs());
                    Check(scrap.HasPhysicalRelease && merchantCounter.TryCapture(scrap, merchantCounter.sellArea),
                        "released player scrap is captured in the sell box");
                    merchantCounter.TryOffer(scrap);
                    int purseBeforeSale = purse.Cats;
                    Check(merchantCounter.NetCats == earnings && merchantCounter.Ready,
                        "Main sale awaits a payout handshake without charging the purse");
                    Check(merchantCounter.TryCommit() && scrap.ownerId == merchantCounter.configuration.vendorId &&
                        purse.Cats == purseBeforeSale && merchantCounter.CommittedTransactions == 2,
                        "Main sale transfers ownership and leaves the personal balance unchanged");
                    var payout = UnityEngine.Object.FindObjectsByType<CurrencyPouch>(
                            FindObjectsSortMode.None)
                        .FirstOrDefault(p => p && !p.personal && p.Cats == earnings &&
                            p.transform.IsChildOf(merchantCounter.transform));
                    Check(payout, "merchant creates one physical payout pouch");
                    payout.transform.position = purse.transform.position;
                    Check(purse.TryMergePayout(payout) && purse.Cats == purseBeforeSale + earnings,
                        "collecting Main payout credits the personal purse exactly once");
                    var contract = merchant.contract;
                    var mission = MissionSessions.Get(contract.definition.missionId);
                    mission.barrels.Clear();
                    foreach (var barrel in contract.barrels)
                        mission.barrels[barrel.objectId] = TransportState.DeliveredIntact;
                    mission.state = MissionState.ReadyToTurnIn;
                    int beforeJob = purse.Cats;
                    int merchantBeforeJob = merchantCounter.MerchantCats;
                    Check(contract.ResolveNodeId("work_result") == "daran_job_perfect" &&
                        mission.state == MissionState.Completed && purse.Cats == beforeJob,
                        "choosing settlement creates a pending physical job payout");
                    string payoutId = "contract-" + contract.definition.missionId;
                    var jobPouch = UnityEngine.Object.FindObjectsByType<CurrencyPouch>(
                        FindObjectsSortMode.None).FirstOrDefault(p => p && p.stableId == payoutId);
                    Check(jobPouch && jobPouch.Cats == 30 && jobPouch.visualPrefab &&
                        jobPouch.visualMaterial &&
                        jobPouch.GetComponentInChildren<TMPro.TMP_Text>(true)?.text ==
                            CurrencyDefinition.FormatZeks(30) &&
                        merchantCounter.MerchantCats == merchantBeforeJob - 30 &&
                        contract.ResolveNodeId("work_settlement") == "daran_job_payout",
                        "job settlement releases exactly 30 Zeks in one physical pouch");
                    var payoutTray = merchantCounter.transform.Find("Payout tray");
                    Check(payoutTray && payoutTray.GetComponent<Collider>() &&
                        merchantCounter.TryCommit() &&
                        merchantCounter.transform.Find("Payout cue")
                            ?.GetComponent<TMPro.TMP_Text>()?.text.Contains("COLLECT HERE") == true,
                        "mission handshake presents the existing pouch on a marked tray");
                    contract.ResolveNodeId("work_result");
                    Check(merchantCounter.MerchantCats == merchantBeforeJob - 30 &&
                        !merchantCounter.TryContractPayout(contract.definition.missionId, 30),
                        "repeating settlement cannot create a second job payout");
                    jobPouch.transform.position = purse.transform.position;
                    Check(purse.TryMergePayout(jobPouch) && purse.Cats == beforeJob + 30 &&
                        purse.GetComponentInChildren<TMPro.TMP_Text>(true)?.text ==
                            CurrencyDefinition.FormatZeks(beforeJob + 30),
                        "job pouch merges into the existing personal purse once");
                    mission.barrels.Clear();
                    for (int i = 0; i < contract.barrels.Length; i++)
                        mission.barrels[contract.barrels[i].objectId] = i == 0 ?
                            TransportState.DeliveredIntact : TransportState.Destroyed;
                    mission.state = MissionState.ReadyToTurnIn;
                    mission.debt = 0;
                    Check(contract.ResolveNodeId("work_result") == "daran_job_one_intact" &&
                        mission.debt == 10 && mission.state == MissionState.ReadyToTurnIn,
                        "one delivered and two destroyed creates a 10-Zek debt");
                    var paymentCue = merchantCounter.transform.Find("Payment cue")
                        ?.GetComponent<TMPro.TMP_Text>();
                    Check(paymentCue && paymentCue.text.Contains("PAY HERE") &&
                        paymentCue.text.Contains("10 Zeks"),
                        "the counter marks the physical purse payment spot and debt amount");
                    int beforeDebt = purse.Cats;
                    purse.transform.position = merchantCounter.paymentSpot.transform.position;
                    Physics.SyncTransforms();
                    Check(contract.ResolveNodeId("work_debt_result") == "daran_job_debt_paid" &&
                        purse.Cats == beforeDebt - 10 &&
                        purse.GetComponentInChildren<TMPro.TMP_Text>(true)?.text ==
                            CurrencyDefinition.FormatZeks(beforeDebt - 10) && mission.debt == 0 &&
                        mission.state == MissionState.Completed,
                        "explicit payment from the physical purse settles the debt once");
                    // Main keeps an 11x11 (121 tile) visible window. Travel beyond
                    // that window so the POI site actually leaves active terrain.
                    player.xrOrigin.position = poi.Center + Vector3.right * 7000 + Vector3.up * 1.7f;
                    File.AppendAllText(Report, "Depart target tile=" + poi.treadmill.WorldToTile(player.xrOrigin.position) +
                        " center=" + poi.treadmill.CenterTile + " treadmillEnabled=" + poi.treadmill.enabled +
                        " active=" + poi.treadmill.gameObject.activeInHierarchy +
                        " tileSize=" + poi.treadmill.TileSizeMeters + "\n");
                    SessionState.SetInt(Step, 3);
                    SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
                    return;
                }
                if (step == 3)
                {
                    if (poi.IsLoaded)
                    {
                        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0)) return;
                        throw new InvalidOperationException("Pueblo did not unload after leaving its terrain tile: " +
                            "observer=" + player.xrOrigin.position + " target=" + poi.treadmill.Target.position +
                            " centerTile=" + poi.treadmill.CenterTile + " targetTile=" +
                            poi.treadmill.WorldToTile(poi.treadmill.Target.position) +
                            " treadmillEnabled=" + poi.treadmill.enabled +
                            " active=" + poi.treadmill.gameObject.activeInHierarchy + " poi=" + poi.Center);
                    }
                    File.AppendAllText(Report, "PASS Pueblo structures unload with distant terrain\n");
                    player.xrOrigin.position = poi.Center + poi.ApproachDirection * 45 + Vector3.up * 1.7f;
                    SessionState.SetInt(Step, 4);
                    SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
                    return;
                }
                if (step == 4)
                {
                    if (!poi.IsLoaded)
                    {
                        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0)) return;
                        throw new InvalidOperationException("Pueblo did not reload after returning to its site.");
                    }
                    Check(poi.Members.Count == 0,
                        "Pueblo remains friendly after terrain recycling");
                    var returnedMerchant = poi.transform.Find("Layout Preview")
                        ?.GetComponentsInChildren<FriendlyNpcInteraction>(true)
                        .FirstOrDefault(n => n.npcId == "merchant-daran" && n.gameObject.activeSelf);
                    Check(returnedMerchant && returnedMerchant.GetInstanceID() == SessionState.GetInt(MerchantId, 0) &&
                        returnedMerchant.gameObject.activeInHierarchy &&
                        returnedMerchant.GetComponent<StarvingBandit>()?.agent?.isOnNavMesh == true,
                        "same active Daran returns when Pueblo reloads");
                    File.AppendAllText(Report, "COMPLETE; headset traversal and visual feel remain manual checks.\n");
                    SessionState.SetInt(Step, 0);
                    EditorApplication.isPlaying = false;
                    if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(0);
                }
            }
            catch (Exception error)
            {
                File.AppendAllText(Report, "FAIL " + error + "\n");
                SessionState.SetInt(Step, 0);
                EditorApplication.isPlaying = false;
                if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(1);
            }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            File.AppendAllText(Report, "PASS " + label + "\n");
        }
    }
}
