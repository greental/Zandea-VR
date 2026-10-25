#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using KenshiVR.MeleeCombat;
using NUnit.Framework;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.FriendlyNPC.Editor
{
    public sealed class MerchantTradeTests
    {
        [UnityTest]
        public IEnumerator CombinedTradePayoutAndFailureCases()
        {
            yield return new EnterPlayMode();
            var created = new List<UnityEngine.Object>();
            GameObject Root(string name)
            {
                var go = new GameObject(name);
                created.Add(go);
                return go;
            }
            NpcItemDefinition Definition(string id, int value)
            {
                var definition = ScriptableObject.CreateInstance<NpcItemDefinition>();
                definition.itemId = id;
                definition.valueCats = value;
                definition.category = NpcItemCategory.Miscellaneous;
                created.Add(definition);
                return definition;
            }
            TradeZone Zone(string name, Vector3 position)
            {
                var go = Root(name);
                go.SetActive(false);
                go.transform.position = position;
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one * .8f;
                var zone = go.AddComponent<TradeZone>();
                go.SetActive(true);
                return zone;
            }
            TradeItem Item(string name, NpcItemDefinition definition, string owner, Vector3 position)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                created.Add(go);
                go.name = name;
                go.SetActive(false);
                go.transform.position = position;
                go.AddComponent<Rigidbody>();
                go.AddComponent<XRGrabInteractable>();
                var item = go.AddComponent<TradeItem>();
                item.definition = definition;
                item.ownerId = owner;
                go.SetActive(true);
                return item;
            }

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            created.Add(dialogue);
            dialogue.entryNodeId = "start";
            dialogue.nodes.Add(new DialogueNode { id = "start", text = "Trade", onEnter = DialogueAction.BeginTrading });
            var vendor = ScriptableObject.CreateInstance<VendorConfiguration>();
            created.Add(vendor);
            vendor.vendorId = "test-vendor-" + Guid.NewGuid().ToString("N");
            vendor.startingCats = 400;
            vendor.purchaseMultiplier = 1f;
            vendor.saleMultiplier = .5f;

            var npcObject = Root("Test merchant");
            npcObject.SetActive(false);
            var npc = npcObject.AddComponent<FriendlyNpcInteraction>();
            npc.dialogue = dialogue;
            npcObject.SetActive(true);
            var viewer = Root("Test viewer").transform;
            viewer.position = new Vector3(0, 1.6f, -.8f);
            var counterObject = Root("Test counter");
            counterObject.SetActive(false);
            var counter = counterObject.AddComponent<VendorCounter>();
            counter.merchant = npc;
            counter.configuration = vendor;
            counter.buyArea = Zone("Buy", Vector3.left);
            counter.sellArea = Zone("Sell", Vector3.right);
            counter.paymentSpot = Zone("Payment", Vector3.forward);
            counter.buyArea.transform.SetParent(counter.transform, true);
            counter.sellArea.transform.SetParent(counter.transform, true);
            counter.paymentSpot.transform.SetParent(counter.transform, true);
            var payoutPoint = Root("Payout point");
            payoutPoint.transform.position = Vector3.forward * 1.5f;
            counter.payoutPosition = payoutPoint.transform;
            var payoutTemplate = Root("Inactive payout template");
            payoutTemplate.SetActive(false);
            payoutTemplate.AddComponent<SphereCollider>();
            payoutTemplate.AddComponent<Rigidbody>();
            payoutTemplate.AddComponent<XRGrabInteractable>();
            counter.payoutPouchPrefab = payoutTemplate.AddComponent<CurrencyPouch>();
            counterObject.SetActive(true);
            Assert.IsTrue(npc.TryBegin(viewer));
            Assert.IsTrue(npc.IsTrading);

            var purseObject = Root("Personal purse");
            purseObject.SetActive(false);
            purseObject.transform.position = Vector3.forward;
            purseObject.AddComponent<SphereCollider>();
            purseObject.AddComponent<Rigidbody>();
            purseObject.AddComponent<XRGrabInteractable>();
            var purse = purseObject.AddComponent<CurrencyPouch>();
            purse.personal = true;
            purse.SetInitialBalance(100);
            purseObject.SetActive(true);

            var sword = Item("Sword", Definition("sword", 80), vendor.vendorId, Vector3.left);
            sword.transform.localScale = new Vector3(.12f, .12f, 1.8f);
            var scrap = Item("Scrap", Definition("scrap", 90), "player", Vector3.right);
            counter.TryOffer(sword);
            counter.TryOffer(scrap);
            Assert.IsTrue(sword.IsFloatingForTrade, "Long stock must stay at its released pose");
            Assert.IsTrue(sword.GetComponent<Rigidbody>().isKinematic);
            Assert.IsTrue(scrap.IsFloatingForTrade);
            var swordPosition = sword.transform.position;
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(swordPosition, sword.transform.position);
            Assert.AreEqual(-35, counter.NetCats);
            Assert.IsTrue(counter.Ready, "The payment trigger must recognize the released purse");
            Assert.IsTrue(counter.TryCommit());
            Assert.AreEqual(65, purse.Cats);
            Assert.AreEqual(435, counter.MerchantCats);
            Assert.AreEqual("player", sword.ownerId);
            Assert.IsTrue(sword.IsFloatingForTrade, "Purchased stock remains accessible above the counter");
            sword.ReleaseTradeHold();
            Assert.IsFalse(sword.IsFloatingForTrade, "Grabbing restores the original body state");
            Assert.IsFalse(sword.GetComponent<Rigidbody>().isKinematic);
            Assert.AreEqual(vendor.vendorId, scrap.ownerId);
            Assert.IsFalse(counter.TryCommit(), "Repeated handshake must not recommit");

            var valuable = Item("Valuable loot", Definition("valuable", 100), "player", Vector3.right);
            counter.TryOffer(valuable);
            Assert.AreEqual(50, counter.NetCats);
            Assert.IsTrue(counter.Ready);
            Assert.IsTrue(counter.TryCommit());
            Assert.AreEqual(385, counter.MerchantCats);
            Assert.AreEqual(65, purse.Cats, "Payout must remain physical until collection");
            CurrencyPouch payout = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<CurrencyPouch>(FindObjectsSortMode.None))
                if (candidate && !candidate.personal && candidate.Cats == 50) payout = candidate;
            Assert.IsNotNull(payout);
            payout.transform.position = purse.transform.position;
            Assert.IsTrue(purse.TryMergePayout(payout));
            Assert.AreEqual(115, purse.Cats);
            Assert.IsFalse(purse.TryMergePayout(payout), "Payout cannot merge twice");

            var costly = Item("Costly stock", Definition("costly", 200), vendor.vendorId, Vector3.left);
            counter.TryOffer(costly);
            counter.TryPresentPurse(purse);
            Assert.IsFalse(counter.Ready);
            Assert.IsFalse(counter.TryCommit());
            Assert.AreEqual(115, purse.Cats);
            Assert.AreEqual(vendor.vendorId, costly.ownerId);

            var staff = Item("Long staff", Definition("staff", 20), vendor.vendorId,
                new Vector3(-1.55f, 0, 0));
            staff.transform.localScale = new Vector3(1.6f, .1f, .1f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(staff.IsFloatingForTrade,
                "A long item's collider must trigger the holding box even when its pivot is outside");

            npc.EndConversation();
            Assert.IsTrue(staff.IsFloatingForTrade, "Cancelled goods stay retrievable above the counter");
            Assert.AreEqual(115, purse.Cats, "Cancellation cannot debit the purse");
            Assert.AreEqual(385, counter.MerchantCats);
            foreach (var value in created) if (value) UnityEngine.Object.Destroy(value);
            yield return null;
            yield return new ExitPlayMode();
        }
    }
}
#endif
