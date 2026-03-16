using Anaglyph.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using System.Linq;
using Unity.Multiplayer.Playmode;
#endif

namespace Anaglyph.Demo
{
    public class AutoConnect : MonoBehaviour
    {
        private void Awake()
        {
#if UNITY_EDITOR
            bool isClient = false;

            // ParrelSync: clone editor = client
#if PARREL_SYNC
            if (ParrelSync.ClonesManager.IsClone())
                isClient = true;
#endif

            // MPPM tags fallback
            var tags = CurrentPlayer.ReadOnlyTags();
            if (tags.Contains("Client")) isClient = true;
            if (tags.Contains("Host"))   isClient = false;

            Debug.Log($"[AutoConnect] isClient={isClient}");

            if (isClient)
                DemoNetworkUI.SuppressAutoHost = true;
#endif
        }

        private void Start()
        {
#if UNITY_EDITOR
            var tags = CurrentPlayer.ReadOnlyTags();
            bool isClient = DemoNetworkUI.SuppressAutoHost;

            if (isClient)
            {
                // Host 需要几秒启动，等久一点再连
                Invoke(nameof(ConnectAsClient), 6f);
                Debug.Log("[AutoConnect] Client — connecting in 6s");
            }
            else if (tags.Contains("Host"))
            {
                NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
                Debug.Log("[AutoConnect] Host started");
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
}
