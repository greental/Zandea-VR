using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zandea.FriendlyNPC;
using JunkerEquipment;
using KenshiVR.MeleeCombat;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
namespace Zandea.Missions.Editor
{
    public static class HonestWorkAuthoring
    {
        const string AssetPath="Assets/Zandea/Missions/HonestDayWork.asset";
        [MenuItem("Zandea/Missions/Place Daran in Main Pueblo")]
        public static void Configure()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Exit Play Mode first.");
            if(new DriveInfo("D").AvailableFreeSpace<20L*1024*1024*1024)throw new System.InvalidOperationException("D: has less than 20 GiB free.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!="Assets/Main.unity")throw new System.InvalidOperationException("Open Assets/Main.unity first.");
            var poi=Object.FindObjectsByType<Zandea.POI.BanditOutpost>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Transform layout=null;
            foreach(var item in poi)if(item.poiId=="zandea.pueblo.settlement.1")layout=item.transform.Find("Layout Preview");
            if(!layout)throw new System.InvalidOperationException("Main Pueblo authored layout missing.");
            var definition=AssetDatabase.LoadAssetAtPath<TransportMissionDefinition>(AssetPath);
            if(!definition){definition=ScriptableObject.CreateInstance<TransportMissionDefinition>();AssetDatabase.CreateAsset(definition,AssetPath);}
            var currency=AssetDatabase.LoadAssetAtPath<CurrencyDefinition>("Assets/Zandea/FriendlyNPC/Generated/Zeks.asset");
            if(!currency){currency=ScriptableObject.CreateInstance<CurrencyDefinition>();AssetDatabase.CreateAsset(currency,"Assets/Zandea/FriendlyNPC/Generated/Zeks.asset");}
            var npc=FindInLayout<FriendlyNpcInteraction>(layout);
            if(!npc){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zandea/FriendlyNPC/Generated/DaranMerchant.prefab");if(!prefab)throw new System.InvalidOperationException("Daran prefab missing.");var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);go.transform.SetParent(layout,false);go.name="Daran merchant";go.transform.localPosition=new Vector3(-18f,0f,-6f);go.transform.localRotation=Quaternion.Euler(0,90f,0);npc=go.GetComponent<FriendlyNpcInteraction>();}
            var counter=FindInLayout<VendorCounter>(layout);
            if(!counter){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zandea/FriendlyNPC/Generated/VendorCounter.prefab");if(!prefab)throw new System.InvalidOperationException("Counter prefab missing.");var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);go.transform.SetParent(layout,false);go.name="Daran market counter";go.transform.localPosition=new Vector3(-16.9f,0f,-6f);go.transform.localRotation=Quaternion.Euler(0,90f,0);counter=go.GetComponent<VendorCounter>();}
            counter.merchant=npc;counter.currency=currency;
            var handshake=npc.GetComponentInChildren<HandshakeTarget>(true);
            if(handshake){handshake.counter=counter;counter.handshake=handshake;var pose=npc.GetComponent<MerchantHandshakePose>();if(pose)pose.handshake=handshake;}
            var priorStock=layout.Find("Daran merchant stock");
            if(priorStock && priorStock.GetComponentsInChildren<TradeItem>(true).Length < counter.configuration.startingStock.Count) { Object.DestroyImmediate(priorStock.gameObject); priorStock=null; }
            if(counter.configuration && !priorStock)
            {
                var stockRoot=new GameObject("Daran merchant stock").transform;stockRoot.SetParent(layout,false);
                stockRoot.localPosition=new Vector3(-16.9f,0f,-6f);
                var shelf=GameObject.CreatePrimitive(PrimitiveType.Cube);shelf.name="Stock shelf";shelf.transform.SetParent(stockRoot,false);shelf.transform.localPosition=new Vector3(0,.87f,.6f);shelf.transform.localScale=new Vector3(1.65f,.1f,.32f);
                int index=0;
                foreach(var item in counter.configuration.startingStock)
                {
                    if(!item)continue;
                    var stockPrefab=item.worldPrefab ? item.worldPrefab : item.itemId=="bo-staff" ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/Bo Staff.prefab") : item.itemId=="bandage" ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/medical/BandageRoll.prefab") : null;
                    if(!stockPrefab)continue;
                    var goods=(GameObject)PrefabUtility.InstantiatePrefab(stockPrefab,scene);goods.transform.SetParent(stockRoot,false);
                    goods.name=item.displayName+" merchant stock";goods.transform.localPosition=new Vector3((index++-.5f)*.52f,1.05f,.6f);
                    if(!goods.GetComponent<Rigidbody>())goods.AddComponent<Rigidbody>();
                    if(!goods.GetComponent<XRGrabInteractable>())goods.AddComponent<XRGrabInteractable>();
                    var trade=goods.GetComponent<TradeItem>();if(!trade)trade=goods.AddComponent<TradeItem>();
                    trade.definition=item;trade.ownerId=npc.npcId;trade.AssignPersistentIdentity(npc.npcId+"-stock-"+item.itemId+"-001");
                }
            }
            var contract=npc.GetComponent<DaranContract>();if(!contract)contract=npc.gameObject.AddComponent<DaranContract>();contract.definition=definition;contract.counter=counter;npc.contract=contract;
            var barrelPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/buildings/props/barrel.prefab");if(!barrelPrefab)throw new System.InvalidOperationException("Existing barrel prefab missing.");
            var list=new List<ContractBarrel>();
            for(int i=0;i<3;i++)
            {
                string id="daran-honest-work-barrel-"+(i+1);
                ContractBarrel barrel=null;
                foreach(var item in layout.GetComponentsInChildren<ContractBarrel>(true))if(item.objectId==id)barrel=item;
                if(!barrel){var go=(GameObject)PrefabUtility.InstantiatePrefab(barrelPrefab,scene);go.transform.SetParent(layout,false);go.name="Contract barrel "+(i+1);go.transform.localPosition=new Vector3(-31f+i*1.5f,.2f,-31f);barrel=go.AddComponent<ContractBarrel>();}
                barrel.definition=definition;barrel.objectId=id;
                var weight=barrel.GetComponent<CarryWeight>();if(!weight)weight=barrel.gameObject.AddComponent<CarryWeight>();weight.effectiveWeight=20f;
                list.Add(barrel);
            }
            contract.barrels=list.ToArray();
            var zone=layout.Find("Daran contract delivery zone");
            if(!zone){var go=new GameObject("Daran contract delivery zone");go.transform.SetParent(layout,false);go.transform.localPosition=new Vector3(-13.5f,0f,-6f);var box=go.AddComponent<BoxCollider>();box.size=new Vector3(2.5f,2f,2.5f);box.center=Vector3.up;box.isTrigger=true;zone=go.transform;}
            var delivery=zone.GetComponent<ContractDeliveryZone>();if(!delivery)delivery=zone.gameObject.AddComponent<ContractDeliveryZone>();delivery.definition=definition;
            var sprint=Object.FindFirstObjectByType<JunkerBody.JunkerSprint>();if(sprint && !sprint.GetComponent<StrengthAndEncumbrance>()){var strength=sprint.gameObject.AddComponent<StrengthAndEncumbrance>();strength.worldTrackingRoot=sprint.xrOrigin;}
            var playerSprint=Object.FindFirstObjectByType<JunkerBody.JunkerSprint>();
            if(playerSprint)
            {
                var backpack=playerSprint.GetComponentInChildren<BackpackInventory>(true);
                if(backpack)
                {
                    var utility=backpack.GetComponent<BackpackUtilityAttachments>();if(!utility)utility=backpack.gameObject.AddComponent<BackpackUtilityAttachments>();
                    utility.personalPursePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zandea/FriendlyNPC/Generated/PersonalCoinPurse.prefab")?.GetComponent<CurrencyPouch>();
                }
                var choices=playerSprint.GetComponentInChildren<PalmDialogueChoices>(true);
                if(!choices)choices=Zandea.FriendlyNPC.Editor.DaranMerchantBuilder.BuildPalmChoices(playerSprint.gameObject);
                npc.choices=choices;
            }
            EnsureDialogue(npc.dialogue);
            EditorUtility.SetDirty(npc);EditorUtility.SetDirty(counter);EditorUtility.SetDirty(contract);EditorUtility.SetDirty(definition);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("Placed Daran and hauling contract in Main Pueblo.");
        }
        public static void ValidateMain()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!="Assets/Main.unity")throw new System.InvalidOperationException("Main is not active.");
            var pois=Object.FindObjectsByType<Zandea.POI.BanditOutpost>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Transform layout=null;foreach(var poi in pois)if(poi.poiId=="zandea.pueblo.settlement.1")layout=poi.transform.Find("Layout Preview");
            if(!layout)throw new System.InvalidOperationException("Pueblo layout missing.");
            FriendlyNpcInteraction npc=null;
            foreach(var candidate in layout.GetComponentsInChildren<FriendlyNpcInteraction>(true))
                if(candidate.npcId=="merchant-daran" && candidate.gameObject.activeSelf) { npc=candidate; break; }
            var counter=FindInLayout<VendorCounter>(layout);
            var contract=npc ? npc.GetComponent<DaranContract>() : null;var zone=FindInLayout<ContractDeliveryZone>(layout);
            if(!npc || !counter || !contract || !zone || !npc.dialogue || !npc.choices || !counter.handshake || !counter.currency || !contract.definition || contract.barrels==null || contract.barrels.Length!=3)
                throw new System.InvalidOperationException("Daran contract wiring incomplete.");
            if(counter.merchant!=npc || counter.handshake.counter!=counter || npc.contract!=contract)
                throw new System.InvalidOperationException("Active Daran is not wired to his contract and trading counter.");
            var ids=new HashSet<string>();
            foreach(var barrel in contract.barrels)
                if(!barrel || barrel.definition!=contract.definition || !ids.Add(barrel.objectId) || !barrel.GetComponent<Zandea.Props.BreakableProp>() || !barrel.GetComponent<CarryWeight>())
                    throw new System.InvalidOperationException("Contract barrel missing identity, breakage, or weight.");
            if(npc.dialogue.Find("work_offer")==null || npc.dialogue.Find("work_ready")==null)throw new System.InvalidOperationException("Contract dialogue missing.");
            Debug.Log("PASS Main Pueblo Daran merchant and three barrel contract references.");
        }
        [MenuItem("Zandea/Missions/Repair Main contract barrel assignments")]
        public static void RepairMainContractBarrels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play Mode before repairing contract barrels.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Main.unity")
                throw new System.InvalidOperationException("Open Assets/Main.unity first.");
            var contract = Object.FindObjectsByType<DaranContract>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            DaranContract target = null;
            foreach (var candidate in contract)
                if (candidate.definition && candidate.definition.missionId == "daran-honest-work-v1")
                    { target = candidate; break; }
            if (!target || target.barrels == null || target.barrels.Length != 3)
                throw new System.InvalidOperationException("Main has no three-barrel Daran contract.");
            for (int i = 0; i < target.barrels.Length; i++)
            {
                var barrel = target.barrels[i];
                if (!barrel) throw new System.InvalidOperationException("Assigned barrel " + (i + 1) + " is missing.");
                barrel.definition = target.definition;
                barrel.objectId = "daran-honest-work-barrel-" + (i + 1);
                if (!barrel.GetComponent<Zandea.Props.BreakableProp>())
                    throw new System.InvalidOperationException("Assigned barrel " + (i + 1) + " lacks the existing breakable component.");
                var weight = barrel.GetComponent<CarryWeight>();
                if (!weight) weight = barrel.gameObject.AddComponent<CarryWeight>();
                weight.effectiveWeight = 20f;
                EditorUtility.SetDirty(barrel);
                EditorUtility.SetDirty(weight);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateMain();
        }
        static T FindInLayout<T>(Transform layout) where T:Component => layout.GetComponentInChildren<T>(true);
        public static void EnsureDialogue(DialogueDefinition d)
        {
            if(!d)return;
            Add(d,"work_offer","Looking for work? Got three barrels I need moved from the warehouse to my stall. Ten Zeks each. Break one, and you owe me ten. Simple enough?",("I'll do it.","work_accept",DialogueAction.AcceptContract),("Where's the warehouse?","work_location",DialogueAction.None),("What happens if I break one?","work_breakage",DialogueAction.None),("Not interested.","farewell_001",DialogueAction.None));
            Add(d,"work_location","Storage shed, just across the settlement. Three barrels outside. Bring them here.",("I'll do it.","work_accept",DialogueAction.AcceptContract),("Back.","work_offer",DialogueAction.None));
            Add(d,"work_breakage","You don't get paid for a broken barrel, and I charge ten Zeks for the damage. Those barrels aren't free.",("I'll do it.","work_accept",DialogueAction.AcceptContract),("Back.","work_offer",DialogueAction.None));
            Add(d,"work_accept","Good. Get moving, then. And try not to smash anything.",("Goodbye.","farewell_001",DialogueAction.None));
            Add(d,"work_progress","Those barrels won't move themselves, you know.",("I'll get to it.","farewell_001",DialogueAction.None),("I quit.","work_abandoned",DialogueAction.AbandonContract));
            Add(d,"work_ready","All done, then? Let's see what you've earned.",("Settle up.","work_result",DialogueAction.SettleContract));
            Add(d,"work_result","A deal's a deal. Take what you've earned, or put your purse on the counter if you owe me.",("Goodbye.","farewell_001",DialogueAction.None));
            Add(d,"work_finished","Not bad for a day's work. Come back if you need a few more Zeks.",("Show me your wares.","trade_offer_001",DialogueAction.BeginTrading));
            Add(d,"work_abandoned","Suit yourself. But don't make a habit of wasting my time.",("Goodbye.","farewell_001",DialogueAction.None));
            Zandea.FriendlyNPC.Editor.DaranMissionVoiceImporter.EnsureDialogue(d);
            EditorUtility.SetDirty(d);
        }
        static void Add(DialogueDefinition d,string id,string text,params (string label,string next,DialogueAction action)[] replies)
        {var n=d.Find(id);if(n==null){n=new DialogueNode{id=id,speakerId="merchant-daran"};d.nodes.Add(n);}n.text=text;n.responses.Clear();foreach(var r in replies)n.responses.Add(new DialogueResponse{text=r.label,nextNodeId=r.next,action=r.action});}
    }
}
