#if UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Zandea.FriendlyNPC.Editor
{
    public sealed class FriendlyWaveSelectionTests
    {
        [UnityTest]
        public IEnumerator WaveChoosesNpcClosestToViewDirection()
        {
            yield return new EnterPlayMode();
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            dialogue.entryNodeId = "hello";
            dialogue.nodes.Add(new DialogueNode { id = "hello", text = "Hello" });
            var viewer = new GameObject("Test viewer");
            viewer.transform.position = new Vector3(0f, 1.6f, 0f);
            var straight = CreateNpc("Straight ahead", new Vector3(0f, 0f, 2f), dialogue);
            var right = CreateNpc("To the right", new Vector3(1f, 0f, 2f), dialogue);
            yield return null;

            Assert.IsTrue(FriendlyNpcInteraction.TryBeginBestWave(viewer.transform));
            Assert.IsTrue(straight.IsConversing);
            Assert.IsFalse(right.IsConversing);
            straight.EndConversation();
            viewer.transform.rotation = Quaternion.LookRotation(
                right.head.position - viewer.transform.position);
            Assert.IsTrue(FriendlyNpcInteraction.TryBeginBestWave(viewer.transform));
            Assert.IsTrue(right.IsConversing);
            Assert.IsFalse(straight.IsConversing);

            Object.Destroy(viewer);
            Object.Destroy(straight.gameObject);
            Object.Destroy(right.gameObject);
            Object.Destroy(dialogue);
            yield return null;
            yield return new ExitPlayMode();
        }

        [Test]
        public void WaveRequiresPalmFacingNpc()
        {
            var palm = new GameObject("Palm");
            palm.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            Assert.IsTrue(FriendlyNpcWaveInitiator.PalmFacesTarget(palm.transform, Vector3.forward));
            palm.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            Assert.IsFalse(FriendlyNpcWaveInitiator.PalmFacesTarget(palm.transform, Vector3.forward));
            Object.DestroyImmediate(palm);
        }

        static FriendlyNpcInteraction CreateNpc(string name, Vector3 position,
            DialogueDefinition dialogue)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            root.transform.position = position;
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = Vector3.up * 1.6f;
            var npc = root.AddComponent<FriendlyNpcInteraction>();
            npc.dialogue = dialogue;
            npc.head = head;
            root.SetActive(true);
            return npc;
        }
    }
}
#endif
