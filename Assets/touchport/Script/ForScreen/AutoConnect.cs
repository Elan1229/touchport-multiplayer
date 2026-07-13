using Unity.Netcode;
using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using Unity.Multiplayer.Playmode;
#endif

public class AutoConnect : MonoBehaviour
{
    void Start()
    {
        Debug.Log("[AutoConnect] Start called");
#if UNITY_EDITOR
        var tags = CurrentPlayer.ReadOnlyTags();
        Debug.Log($"[AutoConnect] tags: {string.Join(", ", tags)}");

        if (tags.Contains("Host"))
        {
            NetworkManager.Singleton.StartHost();
            Debug.Log("[AutoConnect] StartHost");
        }
        else if (tags.Contains("Client"))
        {
            Invoke(nameof(ConnectAsClient), 2f);
        }
#endif
    }

    void ConnectAsClient()
    {
        NetworkManager.Singleton.StartClient();
        Debug.Log("[AutoConnect] StartClient");
    }
}