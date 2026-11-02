using System;
using System.IO;
using System.Linq;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zandea.FriendlyNPC;
using Zandea.Missions;
using Zandea.POI;
using Zandea.Pueblo;

namespace Zandea.Pueblo.Editor
{
    [InitializeOnLoad]
    public static class PuebloMissionRepair
    {
        const string Request = "Temp/PuebloMissionRepair/run.txt";
        const string Report = "Temp/PuebloMissionRepair/result.txt";

        static PuebloMissionRepair()
        {
            EditorApplication.delayCall += RunRequested;
            EditorApplication.update += RunRequested;
            EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += RunRequested;
        }

        [MenuItem("Zandea/POI/Pueblo/Repair friendly mission test setup")]
        public static void Repair()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before editing Main.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Main.unity")
                throw new InvalidOperationException("Open Assets/Main.unity first.");
            var poi = UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(p => p.gameObject.scene == scene &&
                p.poiId == "zandea.pueblo.settlement.1");
            var layout = poi.transform.Find("Layout Preview");
            if (!layout) throw new InvalidOperationException("Pueblo's authored layout is missing.");
            var players = UnityEngine.Object.FindObjectsByType<VRBodyController>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None).Where(p => p.gameObject.scene == scene &&
                p.gameObject.activeInHierarchy && p.xrOrigin).ToArray();
            if (players.Length != 1)
                throw new InvalidOperationException("Expected one active Main player, found " + players.Length);
            var player = players[0];
            var playerRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(player.gameObject);
            if (!playerRoot) playerRoot = player.transform.root.gameObject;
            var notebook = playerRoot.GetComponentInChildren<PalmDialogueChoices>(true);
            if (!notebook) throw new InvalidOperationException("Active player has no palm notebook.");
            var merchants = layout.GetComponentsInChildren<FriendlyNpcInteraction>(true)
                .Where(n => n.npcId == "merchant-daran").ToArray();
            var oldMerchant = merchants.Single(n => n.name == "Daran merchant");
            var merchant = merchants.Single(n => n.name == "DaranMerchant");
            var oldContract = oldMerchant.GetComponent<DaranContract>();
            if (!oldContract || !oldContract.definition || oldContract.barrels?.Length != 3 ||
                !oldContract.counter) throw new InvalidOperationException("Old Daran's contract is incomplete.");
            var counter = oldContract.counter;
            var newHandshake = merchant.GetComponentInChildren<HandshakeTarget>(true);
            var newPose = merchant.GetComponent<MerchantHandshakePose>();
            if (!newHandshake || !newPose)
                throw new InvalidOperationException("New Daran has no handshake target or pose.");

            poi.authoredLayoutVerticalOffset = -.15f;
            var testStart = poi.GetComponent<PuebloMissionTestStart>();
            if (!testStart) testStart = poi.gameObject.AddComponent<PuebloMissionTestStart>();
            testStart.enableForTesting = true;
            testStart.localStart = new Vector3(0f, 1.7f, -55f);
            // Place the walking rig outside the southern wall, close enough for
            // the treadmill to load Pueblo without colliding with the stall.
            Vector3 desiredOrigin = layout.position + poi.Rotation *
                new Vector3(0f, 1.7f, -55f);
            playerRoot.transform.position += desiredOrigin - player.xrOrigin.position;
            var contract = merchant.GetComponent<DaranContract>();
            if (!contract) contract = merchant.gameObject.AddComponent<DaranContract>();
            contract.definition = oldContract.definition;
            contract.barrels = oldContract.barrels;
            contract.counter = counter;
            merchant.contract = contract;
            merchant.choices = notebook;
            counter.merchant = merchant;
            counter.handshake = newHandshake;
            newHandshake.counter = counter;
            newPose.handshake = newHandshake;
            oldMerchant.gameObject.SetActive(false);
            if (PrefabUtility.IsPartOfPrefabInstance(playerRoot))
                PrefabUtility.RecordPrefabInstancePropertyModifications(playerRoot.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(merchant);
            PrefabUtility.RecordPrefabInstancePropertyModifications(counter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(newHandshake);
            PrefabUtility.RecordPrefabInstancePropertyModifications(newPose);
            EditorUtility.SetDirty(poi);
            EditorUtility.SetDirty(testStart);
            EditorUtility.SetDirty(playerRoot.transform);
            EditorUtility.SetDirty(merchant);
            EditorUtility.SetDirty(contract);
            EditorUtility.SetDirty(oldMerchant);
            EditorUtility.SetDirty(counter);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Zandea.Missions.Editor.HonestWorkAuthoring.ValidateMain();
            Debug.Log("[Pueblo] Friendly mission test setup repaired. Active player starts near town.");
        }

        static void RunRequested()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                if (File.ReadAllText(Request).StartsWith("validate", StringComparison.OrdinalIgnoreCase))
                {
                    if (SceneManager.GetActiveScene().isDirty)
                        throw new InvalidOperationException("Save Main before reloading its script references for validation.");
                    File.Delete(Request);
                    EditorSceneManager.OpenScene("Assets/Main.unity");
                    PuebloMainPlayValidation.Begin();
                    File.WriteAllText(Report, "STARTED Main Play Mode streaming validation; see Temp/ZandeaPueblo.main-validation.txt.\n");
                    return;
                }
                Repair();
                File.WriteAllText(Report, "PASS Pueblo setup, player start, and Daran mission references.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(Report, "FAIL " + error + "\n");
                Debug.LogException(error);
            }
            finally { File.Delete(Request); }
        }
    }
}
