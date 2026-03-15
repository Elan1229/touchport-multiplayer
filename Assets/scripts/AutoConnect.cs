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
#if UNITY_EDITOR
        var tags = CurrentPlayer.ReadOnlyTags();

        if (tags.Contains("Host"))
        {
            NetworkManager.Singleton.StartHost();
            Debug.Log("猫老大上线，开房间了！");
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
        Debug.Log("猫小弟上线，进房间了！");
    }
}