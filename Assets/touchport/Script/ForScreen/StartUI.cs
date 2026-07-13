using Unity.Netcode;
using UnityEngine;

public class StartUI : MonoBehaviour
{
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 280, 160));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host (服务器+客户端)"))
                NetworkManager.Singleton.StartHost();

            if (GUILayout.Button("Client (仅客户端)"))
                NetworkManager.Singleton.StartClient();

            if (GUILayout.Button("Server (仅服务器)"))
                NetworkManager.Singleton.StartServer();
        }
        else
        {
            GUILayout.Label($"模式: {(NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client")}");
            GUILayout.Label($"已连接玩家数: {NetworkManager.Singleton.ConnectedClientsIds.Count}");

            var ss = SharedState.Instance;
            if (ss != null)
            {
                GUILayout.Label($"IsShared: {ss.IsShared}");
            }

            if (GUILayout.Button("断开"))
                NetworkManager.Singleton.Shutdown();
        }

        GUILayout.EndArea();
    }
}