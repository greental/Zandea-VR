using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.Props
{
    public sealed class PropTestSceneInitializer : MonoBehaviour
    {
        void Start()
        {
            foreach (var container in FindObjectsByType<PropContainer>(FindObjectsSortMode.None))
                container.Initialize("test/" + container.name);
            foreach (var socket in FindObjectsByType<PropRackSocket>(FindObjectsSortMode.None))
                socket.Initialize("test/" + socket.name);
        }
    }
}
