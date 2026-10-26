using System.Collections.Generic;
using System.IO;
using KenshiVR.CharacterCreator;
using KenshiVR.MeleeCombat;
using JunkerEquipment;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using JunkerBody;

namespace Zandea.FriendlyNPC.Editor
{
    public static class DaranMerchantBuilder
    {
        const string Folder = "Assets/Zandea/FriendlyNPC/Generated";
        const string SceneFolder = "Assets/Scenes/FriendlyNPC";
        const string ScenePath = SceneFolder + "/DaranMerchantTest.unity";
        const string NpcSource = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
        const string PlayerSource = "Assets/MyAssets/prefabs/Player/VR player - Man Creator.prefab";

        [MenuItem("Zandea/Friendly NPC/Build Daran test scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("Exit Play Mode before building Daran"); return; }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (new DriveInfo("D").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
            { Debug.LogError("Less than 20 GiB free on D:. Merchant build stopped."); return; }
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(SceneFolder);
            AssetDatabase.Refresh();
            var voice = Asset<VoiceProfile>("VoiceProfile_MerchantMale01");
            voice.providerId = "elevenlabs";
            voice.providerVoiceId = ""; // Prerecorded pack; original ElevenLabs voice ID was not supplied.
            voice.voiceDescription = "Recorded weathered frontier merchant; restrained delivery.";
            voice.licenseNotes = "ElevenLabs prerecorded pack supplied by project owner. Confirm voice rights and commercial terms before release.";
            EditorUtility.SetDirty(voice);
            var dialogue = Asset<DialogueDefinition>("DaranDialogue");
            dialogue.npcId = "merchant-daran";
            dialogue.defaultVoice = voice;
            dialogue.entryNodeId = "greeting_001";
            var voiceBank = AssetDatabase.LoadAssetAtPath<NpcVoiceBank>(Folder + "/DaranVoiceBank.asset");
            if (dialogue.nodes.Count == 0) AuthorDialogue(dialogue);
            EnsureTradeContextResponses(dialogue);
            foreach (var id in new[] { "greeting_001", "trade_offer_001" })
            {
                var node = dialogue.Find(id);
                if (node != null && string.IsNullOrEmpty(node.gestureId))
                { node.gestureId = "nod"; EditorUtility.SetDirty(dialogue); }
            }
            var vendor = Asset<VendorConfiguration>("DaranVendor");
            vendor.vendorId = "merchant-daran";
            vendor.startingCats = 400;
            vendor.purchaseMultiplier = 1f;
            vendor.saleMultiplier = .5f;
            var sword = Asset<NpcItemDefinition>("DaranPracticeSword");
            sword.itemId = "practice-sword";
            sword.displayName = "Sword";
            sword.category = NpcItemCategory.Weapon;
            sword.valueCats = 80;
            sword.worldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MyAssets/prefabs/weapons/Player practice sword - generated long blade.prefab");
            sword.defaultSlot = NpcEquipmentSlot.MainWeapon;
            EditorUtility.SetDirty(sword);
            vendor.startingStock.Clear();
            vendor.startingStock.Add(sword);
            foreach (var path in new[] { "Bo Staff", "Bandage" })
            {
                var item = Item(path);
                if (item) vendor.startingStock.Add(item);
            }
            EditorUtility.SetDirty(vendor);
            var leather = Material("MerchantLeather", new Color(.25f, .14f, .08f));
            var wood = Material("MerchantCounterWood", new Color(.35f, .24f, .13f));
            var red = Material("MerchantBuyZone", new Color(.38f, .10f, .08f));
            var green = Material("MerchantSellZone", new Color(.08f, .27f, .13f));
            var metal = Material("MerchantScrap", new Color(.33f, .35f, .34f));
            var purse = BuildPouch("PersonalCoinPurse", true, 150, leather);
            var payout = BuildPouch("PayoutPouch", false, 0, leather);
            var scrap = BuildScrap(metal);
            var price = BuildLabel("PriceLabel", 2.5f);
            var merchantPrefab = BuildMerchant(dialogue, voiceBank);
            var counterPrefab = BuildCounter(vendor, payout, price, wood, red, green);
            var stockPaths = new List<string>(vendor.startingStock.Count);
            foreach (var item in vendor.startingStock) stockPaths.Add(AssetDatabase.GetAssetPath(item));
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Safe test floor";
            floor.transform.position = new Vector3(0, -.1f, 1.2f);
            floor.transform.localScale = new Vector3(8f, .2f, 8f);
            floor.GetComponent<Renderer>().sharedMaterial = wood;
            var light = new GameObject("Test sunlight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSource);
            if (!playerPrefab || !merchantPrefab || !counterPrefab)
            { Debug.LogError("Merchant scene source prefab missing"); return; }
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var backpack = player.GetComponentInChildren<BackpackInventory>(true);
            if (backpack)
            {
                var utility = backpack.gameObject.AddComponent<BackpackUtilityAttachments>();
                utility.personalPursePrefab = purse;
                var fixture = player.AddComponent<StarterBackpackGoods>();
                fixture.backpack = backpack;
                fixture.startingItemPrefab = scrap.GetComponent<VRInventoryItem>();
            }
            var dialogueHand = BuildPalmChoices(player);
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(merchantPrefab, scene);
            npc.transform.SetPositionAndRotation(new Vector3(0, 0, 2.55f), Quaternion.Euler(0, 180, 0));
            var interaction = npc.GetComponent<FriendlyNpcInteraction>();
            interaction.choices = dialogueHand;
            var counter = (GameObject)PrefabUtility.InstantiatePrefab(counterPrefab, scene);
            counter.transform.SetPositionAndRotation(new Vector3(0, 0, 1.55f), Quaternion.identity);
            var vendorCounter = counter.GetComponent<VendorCounter>();
            vendorCounter.merchant = interaction;
            var handshake = npc.GetComponentInChildren<HandshakeTarget>(true);
            handshake.counter = vendorCounter;
            vendorCounter.handshake = handshake;
            npc.GetComponent<MerchantHandshakePose>().handshake = handshake;
            PlaceStock(scene, counter.transform, stockPaths, interaction.npcId);
            var scenePath = ScenePath;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Built Daran prefab, counter, purse, dialogue and safe test scene at " + scenePath);
        }

        public static void BuildAndValidate()
        {
            Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            T Find<T>() where T : Component
            {
                foreach (var root in roots)
                {
                    var found = root.GetComponentInChildren<T>(true);
                    if (found) return found;
                }
                return null;
            }
            var npc = Find<FriendlyNpcInteraction>();
            var counter = Find<VendorCounter>();
            var utilities = Find<BackpackUtilityAttachments>();
            var palm = Find<PalmDialogueChoices>();
            var starter = Find<StarterBackpackGoods>();
            var pose = Find<MerchantHandshakePose>();
            var motion = npc ? npc.GetComponent<BanditRigMotion>() : null;
            var missing = new List<string>();
            if (!npc) missing.Add("friendly NPC");
            if (!npc || !npc.brain || npc.brain.hostileToPlayer) missing.Add("friendly brain");
            if (npc && npc.brain && npc.brain.guardAuthoringMode)
                missing.Add("merchant rig stuck in bandit guard authoring mode");
            if (!npc || !npc.face) missing.Add("existing face controller");
            if (!motion || !motion.visualRoot) missing.Add("existing CC_Base rig motion");
            if (!npc || !npc.mouthAudio || !npc.conversationTarget) missing.Add("NPC speech/contact");
            if (!npc || !npc.dialogue || npc.dialogue.nodes.Count < 10) missing.Add("Daran dialogue");
            if (!npc || !npc.voiceBank || npc.voiceBank.Find("greeting_001") == null)
                missing.Add("recorded voice bank");
            else
            {
                int takes = 0;
                foreach (var voiceEvent in npc.voiceBank.events)
                    foreach (var take in voiceEvent.takes)
                    {
                        takes++;
                        if (!take.clip || take.mouthEnvelope == null || take.mouthEnvelope.Length == 0)
                            missing.Add("voice clip/envelope: " + voiceEvent.eventId);
                        else
                        {
                            float peak = 0f;
                            foreach (float sample in take.mouthEnvelope)
                                peak = Mathf.Max(peak, sample);
                            if (peak < .03f) missing.Add("silent mouth envelope: " + voiceEvent.eventId);
                        }
                    }
                if (takes != 18) missing.Add("18 recorded voice takes");
                if (npc.voiceBank.ambientEventIds.Count != 3) missing.Add("three market calls");
            }
            if (!counter || counter.merchant != npc || !counter.configuration) missing.Add("vendor binding");
            if (!counter || !counter.buyArea || !counter.sellArea || !counter.paymentSpot)
                missing.Add("trade zones");
            if (!counter || !counter.handshake || !counter.payoutPouchPrefab || !counter.priceLabelPrefab)
                missing.Add("handshake/payout/price");
            if (!utilities) missing.Add("backpack utility component");
            else if (!utilities.personalPursePrefab) missing.Add("backpack purse prefab");
            if (!palm || !starter || !pose) missing.Add("palm/starter/pose");
            if (missing.Count > 0)
                throw new System.InvalidOperationException("Daran test scene validation failed: " +
                    string.Join(", ", missing));
            if (!palm.leftHand || !palm.rightHand)
                throw new System.InvalidOperationException("Palm dialogue has no two tracked controller interactors");
            int stockCount = 0;
            foreach (var root in roots)
                foreach (var item in root.GetComponentsInChildren<TradeItem>(true))
                    if (item.ownerId == counter.configuration.vendorId) stockCount++;
            if (stockCount < 3)
                throw new System.InvalidOperationException("Fewer than three physical merchant stock items were built");
            Debug.Log("Daran merchant Edit Mode wiring validation passed. Actual dialogue, trading and ergonomics require Play Mode and headset testing.");
        }

        [MenuItem("Zandea/Friendly NPC/Validate saved Daran voice scene")]
        public static void ValidateSavedVoiceScene()
        {
            const string voiceScene = SceneFolder + "/DaranMerchantVoiceTest.unity";
            var scene = EditorSceneManager.OpenScene(voiceScene, OpenSceneMode.Single);
            FriendlyNpcInteraction npc = null;
            VendorCounter counter = null;
            Unity.XR.CoreUtils.XROrigin origin = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!npc) npc = root.GetComponentInChildren<FriendlyNpcInteraction>(true);
                if (!counter) counter = root.GetComponentInChildren<VendorCounter>(true);
                if (!origin) origin = root.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true);
            }
            var faults = new List<string>();
            if (!npc || !npc.brain || npc.brain.guardAuthoringMode || npc.brain.hostileToPlayer)
                faults.Add("friendly merchant rig/brain");
            if (!npc || !npc.choices || !npc.conversationTarget || !npc.voiceBank)
                faults.Add("conversation/voice/palm choices");
            if (!counter || counter.merchant != npc || !counter.buyArea || !counter.sellArea ||
                !counter.paymentSpot || !counter.handshake || counter.handshake.counter != counter)
                faults.Add("counter/handshake links");
            if (!origin || origin.transform.position.y > .5f)
                faults.Add("player XR origin starts above the trading floor");
            if (counter && counter.buyArea && counter.sellArea &&
                Vector3.Distance(counter.buyArea.transform.position,
                    counter.sellArea.transform.position) < .5f)
                faults.Add("buy and sell areas overlap");
            if (faults.Count > 0)
                throw new System.InvalidOperationException("Daran voice scene wiring failed: " + string.Join(", ", faults));
            Debug.Log("Saved Daran voice scene wiring validation passed: player, merchant, palm choices, " +
                "recorded voice, counter zones and handshake are connected.");
        }

        static T Asset<T>(string name) where T : ScriptableObject
        {
            string path = Folder + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
        static NpcItemDefinition Item(string name) => AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(
            "Assets/MeleeCombat/Inventory/" + name + ".asset");
        static Material Material(string name, Color color)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        static void AuthorDialogue(DialogueDefinition definition)
        {
            DialogueNode Node(string id, string text, ExpressionType expression = ExpressionType.Neutral)
                => new() { id = id, speakerId = definition.npcId, text = text, expression = expression };
            DialogueResponse Reply(string text, string next, DialogueAction action = DialogueAction.None)
                => new() { text = text, nextNodeId = next, action = action };
            var greeting = Node("greeting_001", "Looking to trade, traveler? Or are you looking for work?", ExpressionType.Happy);
            greeting.gestureId = "nod";
            greeting.responses.Add(Reply("Show me your wares.", "trade_offer_001", DialogueAction.BeginTrading));
            greeting.responses.Add(Reply("Any work available?", "work_001"));
            greeting.responses.Add(Reply("Tell me about this place.", "place_001"));
            greeting.responses.Add(Reply("Goodbye.", "farewell_001"));
            var trade = Node("trade_offer_001", "Go on, have a look. Anything on the counter is for sale. Well, almost anything.");
            trade.gestureId = "present";
            trade.responses.Add(Reply("I'll look around.", "", DialogueAction.BrowseFreely));
            trade.responses.Add(Reply("That's all for now.", "farewell_001"));
            var work = Node("work_001", "A caravan vanished near the old toll station. I might pay for a useful answer.", ExpressionType.Sad);
            work.onEnter = DialogueAction.QuestHook;
            work.responses.Add(Reply("Back to trading.", "trade_offer_001", DialogueAction.BeginTrading));
            work.responses.Add(Reply("Goodbye.", "farewell_001"));
            var place = Node("place_001", "People call this a settlement. The wind calls it a place to leave.");
            place.responses.Add(Reply("Show me your wares.", "trade_offer_001", DialogueAction.BeginTrading));
            place.responses.Add(Reply("Goodbye.", "farewell_001"));
            var complete = Node("trade_complete", "Pleasure doing business with you. A fair trade, more or less.", ExpressionType.Happy);
            complete.responses.Add(Reply("Keep trading.", "trade_offer_001", DialogueAction.BeginTrading));
            complete.responses.Add(Reply("Goodbye.", "farewell_001"));
            var farewell = Node("farewell_001", "Safe travels! And try not to die before you come back.");
            farewell.endOnComplete = true;
            definition.nodes = new List<DialogueNode> {
                greeting, trade, work, place, complete, farewell,
                Node("insufficient_funds_001", "You're short on cats, friend. Afraid good intentions don't pay the bills. Put something back.", ExpressionType.Angry),
                Node("merchant_lacks_funds_001", "I'd buy it, if I had the cats to spare.", ExpressionType.Sad),
                Node("item_rejected_001", "That won't fetch anything here.", ExpressionType.Angry),
                Node("transaction_ready_001", "Fair enough. Come on, let's shake on it.", ExpressionType.Happy),
                Node("trade_cancelled_001", "Changed your mind? Happens more than you'd think.")
            };
            EditorUtility.SetDirty(definition);
        }

        static void EnsureTradeContextResponses(DialogueDefinition definition)
        {
            foreach (var id in new[] { "insufficient_funds_001", "merchant_lacks_funds_001",
                         "item_rejected_001", "transaction_ready_001", "trade_cancelled_001" })
            {
                var node = definition.Find(id);
                if (node == null || node.responses.Count != 0) continue;
                node.responses.Add(new DialogueResponse { text = "Adjust the offer.",
                    nextNodeId = "trade_offer_001", action = DialogueAction.BeginTrading });
                node.responses.Add(new DialogueResponse { text = "Goodbye.", nextNodeId = "farewell_001" });
                EditorUtility.SetDirty(definition);
            }
        }

        static CurrencyPouch BuildPouch(string name, bool personal, int cats, Material leather)
        {
            string path = Folder + "/" + name + ".prefab";
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (old) return old.GetComponent<CurrencyPouch>();
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = name;
            root.transform.localScale = new Vector3(.14f, .17f, .09f);
            root.GetComponent<Renderer>().sharedMaterial = leather;
            Object.DestroyImmediate(root.GetComponent<SphereCollider>());
            root.AddComponent<BoxCollider>().size = new Vector3(.95f, .85f, 1.1f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = .2f; body.linearDamping = .25f; body.angularDamping = 1.5f;
            root.AddComponent<XRGrabInteractable>();
            var equipment = root.AddComponent<JunkerEquipmentItem>();
            equipment.itemId = personal ? "personal-coin-purse" : "payout-pouch";
            root.AddComponent<VRInventoryItem>().CanStoreInBackpack = false;
            var currency = root.AddComponent<CurrencyPouch>();
            currency.personal = personal;
            currency.stableId = personal ? "player-personal-purse" : "";
            currency.SetInitialBalance(cats);
            var loop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            loop.name = "Leather loop";
            loop.transform.SetParent(root.transform, false);
            loop.transform.localPosition = new Vector3(0, .55f, 0);
            loop.transform.localScale = new Vector3(.25f, .15f, .12f);
            loop.GetComponent<Renderer>().sharedMaterial = leather;
            Object.DestroyImmediate(loop.GetComponent<Collider>());
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<CurrencyPouch>();
        }

        static GameObject BuildScrap(Material metal)
        {
            string path = Folder + "/PlayerScrap.prefab";
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (old) return old;
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Scrap metal";
            root.transform.localScale = new Vector3(.16f, .06f, .12f);
            root.GetComponent<Renderer>().sharedMaterial = metal;
            root.AddComponent<Rigidbody>().mass = .6f;
            root.AddComponent<XRGrabInteractable>();
            root.AddComponent<VRInventoryItem>();
            var trade = root.AddComponent<TradeItem>();
            trade.definition = Item("Scrap Metal"); trade.ownerId = "player";
            trade.AssignPersistentIdentity("player-starter-scrap-001");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static TMP_Text BuildLabel(string name, float fontSize)
        {
            string path = Folder + "/" + name + ".prefab";
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (old) return old.GetComponent<TMP_Text>();
            var root = new GameObject(name);
            var text = root.AddComponent<TextMeshPro>();
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            root.transform.localScale = Vector3.one * .05f;
            root.AddComponent<BillboardLabel>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<TMP_Text>();
        }

        static GameObject BuildMerchant(DialogueDefinition dialogue, NpcVoiceBank voiceBank)
        {
            string path = Folder + "/DaranMerchant.prefab";
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (old)
            {
                var existing = PrefabUtility.LoadPrefabContents(path);
                var headBone = FindBone(existing.transform, "CC_Base_Head_038");
                var handBone = FindBone(existing.transform, "CC_Base_R_Hand_081");
                var existingInteraction = existing.GetComponent<FriendlyNpcInteraction>();
                var existingHandshake = existing.GetComponentInChildren<HandshakeTarget>(true);
                var existingBrain = existing.GetComponent<StarvingBandit>();
                if (!headBone || !handBone || !existingInteraction || !existingHandshake)
                    throw new System.InvalidOperationException("Cannot update Daran: CC_Base bones or interaction missing");
                existingInteraction.head = headBone;
                if (existingBrain)
                {
                    existingBrain.guardAuthoringMode = false;
                    existingBrain.previewEnabled = false;
                }
                existingInteraction.dialogue = dialogue;
                existingInteraction.voiceBank = voiceBank;
                existingInteraction.maxConversationDistance = 9f;
                existingInteraction.invitationDistance = 6f;
                existingInteraction.browseDistance = 3f;
                existingInteraction.wanderRadius = 0f;
                if (existingInteraction.mouthAudio)
                    existingInteraction.mouthAudio.transform.SetParent(headBone, false);
                if (existingInteraction.subtitle)
                    existingInteraction.subtitle.transform.SetParent(headBone, false);
                existingHandshake.transform.SetParent(handBone, false);
                var existingContact = existingHandshake.GetComponent<SphereCollider>();
                if (existingContact) existingContact.radius = .22f;
                var existingPose = existing.GetComponent<MerchantHandshakePose>();
                if (existingPose) existingPose.handTargetLocal = new Vector3(.22f, 1.25f, .55f);
                PrefabUtility.SaveAsPrefabAsset(existing, path);
                PrefabUtility.UnloadPrefabContents(existing);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(NpcSource);
            if (!source) return null;
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(source);
            npc.name = "Daran Merchant";
            var brain = npc.GetComponent<StarvingBandit>();
            brain.hostileToPlayer = false;
            brain.guardAuthoringMode = false;
            brain.previewEnabled = false;
            brain.staff = null;
            foreach (var weapon in npc.GetComponentsInChildren<PhysicalMeleeWeapon>(true))
                if (weapon && weapon.gameObject != npc) Object.DestroyImmediate(weapon.gameObject);
            var appearance = npc.GetComponent<NPCCharacterAppearance>();
            if (appearance) appearance.archetype = AssetDatabase.LoadAssetAtPath<AppearanceArchetype>(
                "Assets/CharacterCreator/Archetypes/Merchant.asset");
            var rig = npc.GetComponentInChildren<Animator>();
            var head = rig && rig.isHuman ? rig.GetBoneTransform(HumanBodyBones.Head) :
                FindBone(npc.transform, "CC_Base_Head_038");
            var rightHand = rig && rig.isHuman ? rig.GetBoneTransform(HumanBodyBones.RightHand) :
                FindBone(npc.transform, "CC_Base_R_Hand_081");
            if (!head || !rightHand) throw new System.InvalidOperationException("Daran's existing head or hand bone is missing");
            var conversation = new GameObject("Conversation contact");
            conversation.transform.SetParent(npc.transform, false);
            conversation.transform.localPosition = new Vector3(0, 1.35f, .35f);
            var contact = conversation.AddComponent<SphereCollider>();
            contact.isTrigger = true; contact.radius = .2f;
            var talkTarget = conversation.AddComponent<XRSimpleInteractable>();
            var handContact = new GameObject("Handshake contact");
            handContact.transform.SetParent(rightHand, false);
            handContact.transform.localPosition = new Vector3(0, 0, .05f);
            var handCollider = handContact.AddComponent<SphereCollider>();
            handCollider.isTrigger = true; handCollider.radius = .22f;
            var handTarget = handContact.AddComponent<XRSimpleInteractable>();
            handTarget.enabled = false;
            handContact.AddComponent<HandshakeTarget>();
            var audioObject = new GameObject("MouthAudio");
            audioObject.transform.SetParent(head, false);
            var audio = audioObject.AddComponent<AudioSource>();
            audio.spatialBlend = 1f; audio.minDistance = 1f; audio.maxDistance = 10f; audio.playOnAwake = false;
            var subtitleObject = new GameObject("NPC subtitle");
            subtitleObject.transform.SetParent(head, false);
            subtitleObject.transform.localPosition = new Vector3(0, .25f, .1f);
            subtitleObject.transform.localScale = Vector3.one * .05f;
            var subtitle = subtitleObject.AddComponent<TextMeshPro>();
            subtitle.fontSize = 2.6f;
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.textWrappingMode = TMPro.TextWrappingModes.Normal;
            subtitleObject.AddComponent<BillboardLabel>();
            var interaction = npc.AddComponent<FriendlyNpcInteraction>();
            interaction.dialogue = dialogue;
            interaction.voiceBank = voiceBank;
            interaction.brain = brain;
            interaction.health = npc.GetComponent<ScavengerNPC.ScavengerHealth>();
            interaction.face = npc.GetComponentInChildren<NPCFacialExpressionController>();
            interaction.head = head;
            interaction.conversationTarget = talkTarget;
            interaction.mouthAudio = audio;
            interaction.subtitle = subtitle;
            interaction.maxConversationDistance = 9f;
            interaction.invitationDistance = 6f;
            interaction.browseDistance = 3f;
            interaction.wanderRadius = 0f;
            npc.AddComponent<MerchantHandshakePose>().merchant = interaction;
            var prefab = PrefabUtility.SaveAsPrefabAsset(npc, path);
            Object.DestroyImmediate(npc);
            return prefab;
        }

        static GameObject BuildCounter(VendorConfiguration config, CurrencyPouch payout, TMP_Text price,
            Material wood, Material red, Material green)
        {
            string path = Folder + "/VendorCounter.prefab";
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (old) return old;
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Vendor Counter";
            table.transform.localScale = new Vector3(1.75f, .12f, .72f);
            table.transform.position = new Vector3(0, .88f, 0);
            table.GetComponent<Renderer>().sharedMaterial = wood;
            var root = new GameObject("VendorCounter");
            table.transform.SetParent(root.transform, true);
            TradeZone Zone(string name, Vector3 position, Vector3 size, Material surface, bool holdingVolume = false)
            {
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = name;
                pad.transform.SetParent(root.transform, false);
                pad.transform.localPosition = position;
                pad.transform.localScale = new Vector3(size.x, .015f, size.z);
                pad.GetComponent<Renderer>().sharedMaterial = surface;
                var collider = pad.GetComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = holdingVolume
                    ? new Vector3(1.15f, .6f / .015f, 1.13f)
                    : new Vector3(1f, size.y / .015f, 1f);
                if (holdingVolume) collider.center = new Vector3(0, .3f / .015f, 0);
                var zone = pad.AddComponent<TradeZone>();
                zone.holdingVolume = holdingVolume;
                return zone;
            }
            var buy = Zone("Buy area", new Vector3(-.43f, .97f, 0), new Vector3(.66f, .24f, .6f), red, true);
            var sell = Zone("Sell area", new Vector3(.43f, .97f, 0), new Vector3(.66f, .24f, .6f), green, true);
            var payment = Zone("Payment spot", new Vector3(.47f, .99f, -.24f),
                new Vector3(.22f, .26f, .18f), wood);
            var total = new GameObject("Net total");
            total.transform.SetParent(root.transform, false);
            total.transform.localPosition = new Vector3(0, 1.22f, -.40f);
            total.transform.localScale = Vector3.one * .05f;
            var text = total.AddComponent<TextMeshPro>();
            text.fontSize = 2.8f; text.alignment = TextAlignmentOptions.Center;
            total.AddComponent<BillboardLabel>();
            var payoutPoint = new GameObject("Payout spot").transform;
            payoutPoint.SetParent(root.transform, false);
            payoutPoint.localPosition = new Vector3(0, 1.1f, 0);
            var counter = root.AddComponent<VendorCounter>();
            counter.configuration = config; counter.buyArea = buy; counter.sellArea = sell;
            counter.paymentSpot = payment; counter.priceLabelPrefab = price;
            counter.netLabel = text; counter.payoutPouchPrefab = payout;
            counter.payoutPosition = payoutPoint;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            return null;
        }

        static PalmDialogueChoices BuildPalmChoices(GameObject player)
        {
            var root = new GameObject("Palm dialogue");
            root.transform.SetParent(player.transform, false);
            var choices = root.AddComponent<PalmDialogueChoices>();
            var hands = player.GetComponentsInChildren<PlayerMartialHand>(true);
            var body = player.GetComponentInChildren<VRBodyController>(true);
            foreach (var hand in hands)
            {
                var interactor = hand.interactor ? hand.interactor : hand.GetComponentInChildren<XRDirectInteractor>(true);
                if (!interactor && body && body.xrOrigin)
                {
                    var controller = hand.left ? body.leftController : body.rightController;
                    foreach (var candidate in body.xrOrigin.GetComponentsInChildren<XRDirectInteractor>(true))
                        if (controller && (candidate.transform == controller || candidate.transform.IsChildOf(controller)))
                        { interactor = candidate; break; }
                }
                if (hand.left) { choices.leftHand = interactor; choices.leftPalm = hand.transform; }
                else { choices.rightHand = interactor; choices.rightPalm = hand.transform; }
            }
            var panel = new GameObject("Choices");
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(0, .09f, .16f);
            choices.panel = panel;
            choices.optionLabels = new TMP_Text[4];
            choices.optionTargets = new XRSimpleInteractable[4];
            for (int i = 0; i < 4; i++)
            {
                var row = new GameObject("Choice " + (i + 1));
                row.transform.SetParent(panel.transform, false);
                row.transform.localPosition = new Vector3(0, (3 - i) * .045f, 0);
                var label = row.AddComponent<TextMeshPro>();
                label.fontSize = 2.5f; label.alignment = TextAlignmentOptions.Left;
                row.transform.localScale = Vector3.one * .05f;
                var hit = row.AddComponent<BoxCollider>();
                hit.isTrigger = true;
                hit.size = new Vector3(5f, .7f, .3f);
                choices.optionLabels[i] = label;
                choices.optionTargets[i] = row.AddComponent<XRSimpleInteractable>();
            }
            return choices;
        }

        static void PlaceStock(Scene scene, Transform counter, IReadOnlyList<string> stockPaths, string vendorId)
        {
            int placed = 0;
            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(shelf, scene);
            shelf.name = "Merchant stock shelf";
            shelf.transform.position = counter.position + new Vector3(0, .87f, .60f);
            shelf.transform.localScale = new Vector3(1.65f, .1f, .32f);
            for (int index = 0; index < stockPaths.Count; index++)
            {
                var definition = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(stockPaths[index]);
                var prefab = definition && definition.worldPrefab ? definition.worldPrefab :
                    definition && definition.itemId == "bo-staff" ? AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MyAssets/prefabs/weapons/Bo Staff.prefab") :
                    definition && definition.itemId == "bandage" ? AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MyAssets/prefabs/medical/BandageRoll.prefab") : null;
                if (!prefab)
                { Debug.LogWarning("No physical stock prefab for " + (definition ? definition.itemId : "missing item")); continue; }
                var stock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                stock.name = definition.displayName + " merchant stock";
                float across = (index - (stockPaths.Count - 1) * .5f) * .52f;
                stock.transform.position = shelf.transform.position + new Vector3(across, .18f, 0);
                if (!stock.GetComponent<Rigidbody>()) stock.AddComponent<Rigidbody>();
                if (!stock.GetComponent<XRGrabInteractable>()) stock.AddComponent<XRGrabInteractable>();
                var trade = stock.GetComponent<TradeItem>();
                if (!trade) trade = stock.AddComponent<TradeItem>();
                trade.definition = definition;
                trade.ownerId = vendorId;
                trade.AssignPersistentIdentity(vendorId + "-stock-" + definition.itemId + "-001");
                placed++;
            }
            Debug.Log("Placed " + placed + " physical merchant stock objects");
        }
    }
}
