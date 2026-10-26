#if UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

namespace Zandea.FriendlyNPC.Editor
{
    public sealed class MerchantPoseTests
    {
        [UnityTest]
        public IEnumerator FoldedArmsAndHandshakeReachUseTheExistingRig()
        {
            yield return new EnterPlayMode();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Zandea/FriendlyNPC/Generated/DaranMerchant.prefab");
            Assert.IsNotNull(prefab);
            var npcObject = Object.Instantiate(prefab);
            npcObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var npc = npcObject.GetComponent<FriendlyNpcInteraction>();
            var pose = npcObject.GetComponent<MerchantHandshakePose>();
            var handshake = npcObject.GetComponentInChildren<HandshakeTarget>(true);
            Assert.IsNotNull(npc);
            Assert.IsNotNull(pose);
            Assert.IsNotNull(handshake);
            Transform Bone(string name)
            {
                foreach (var candidate in npcObject.GetComponentsInChildren<Transform>(true))
                    if (candidate.name == name) return candidate;
                return null;
            }
            var leftUpper = Bone("CC_Base_L_Upperarm_050");
            var leftLower = Bone("CC_Base_L_Forearm_051");
            var leftHand = Bone("CC_Base_L_Hand_055");
            var rightUpper = Bone("CC_Base_R_Upperarm_074");
            var rightLower = Bone("CC_Base_R_Forearm_077");
            var rightHand = Bone("CC_Base_R_Hand_081");
            Assert.IsNotNull(leftUpper);
            Assert.IsNotNull(rightUpper);
            yield return new WaitForSeconds(.65f);
            float Bend(Transform upper, Transform lower, Transform hand) =>
                Vector3.Angle(upper.position - lower.position, hand.position - lower.position);
            Assert.Less(Bend(leftUpper, leftLower, leftHand), 145f,
                "The left elbow should be visibly bent across Daran's chest.");
            Assert.Less(Bend(rightUpper, rightLower, rightHand), 145f,
                "The right elbow should be visibly bent across Daran's chest.");
            var cameraObject = new GameObject("Nearby viewer");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = npcObject.transform.position + npcObject.transform.forward * 1.4f +
                Vector3.up * 1.6f;
            cameraObject.AddComponent<Camera>();
            Assert.IsTrue(npc.TryBeginTradeFromCounter());
            handshake.Arm(true);
            Assert.AreSame(handshake, pose.handshake);
            Assert.IsTrue(npc.IsTrading);
            Assert.IsTrue(handshake.IsArmed);
            Vector3 startRoot = npcObject.transform.position;
            Vector3 startHand = rightHand.position;
            yield return new WaitForSeconds(.65f);
            Assert.IsTrue(npc.IsTrading, "Trade session ended before the offered handshake was reached.");
            Assert.IsTrue(handshake.IsArmed, "Handshake disarmed before the player could reach it.");
            Assert.GreaterOrEqual(pose.handshakeStep, .26f);
            Assert.GreaterOrEqual(pose.handTargetLocal.z, .95f);
            Assert.Greater(Vector3.Dot(npcObject.transform.position - startRoot, npcObject.transform.forward), .02f,
                "The merchant should start stepping toward the player for the handshake.");
            Assert.Greater(Vector3.Dot(rightHand.position - startHand, npcObject.transform.forward), .12f,
                "The offered hand should move toward the player.");
            Object.Destroy(cameraObject);
            Object.Destroy(npcObject);
            yield return null;
            yield return new ExitPlayMode();
        }
    }
}
#endif
