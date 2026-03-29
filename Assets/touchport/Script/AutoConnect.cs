using Anaglyph.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using System.Linq;
using Unity.Multiplayer.Playmode;
#endif

public class AutoConnect : MonoBehaviour
{
#if UNITY_EDITOR
    private bool _isHost;
    private bool _isClient;
#endif

    private void Awake()
    {
#if UNITY_EDITOR
        var tags = CurrentPlayer.ReadOnlyTags();
        _isHost   = tags.Contains("Host");
        _isClient = tags.Contains("Client");
        Debug.Log($"[AutoConnect] Awake — isHost={_isHost} isClient={_isClient}");
#endif
    }

    private void Start()
    {
#if UNITY_EDITOR
        if (_isHost)
        {
            NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
            Debug.Log("[AutoConnect] Host started");
        }
        else if (_isClient)
        {
            Invoke(nameof(ConnectAsClient), 6f);
            Debug.Log("[AutoConnect] Client — connecting in 6s");
        }
#endif
    }

#if UNITY_EDITOR
    private void ConnectAsClient()
    {
        var transport = Unity.Netcode.NetworkManager.Singleton
            .GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 7777);
        Unity.Netcode.NetworkManager.Singleton.StartClient();
        Debug.Log("[AutoConnect] StartClient called");
    }
#endif
}
