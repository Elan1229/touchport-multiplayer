using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : NetworkBehaviour
{
    public GameObject playerPrefab;

    static readonly Vector3[] SpawnPoints =
    {
        new Vector3(-3f, 0.5f, 0f),  // 玩家 0（方块）
        new Vector3( 3f, 0.5f, 0f),  // 玩家 1（球球）
    };

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        // 先注册回调（处理之后连入的客户端），再生成 Host 自己
        NetworkManager.OnClientConnectedCallback += Spawn;
        Spawn(NetworkManager.LocalClientId);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            NetworkManager.OnClientConnectedCallback -= Spawn;
    }

    void Spawn(ulong clientId)
    {
        if (clientId >= (ulong)SpawnPoints.Length) return;
        var go = Instantiate(playerPrefab, SpawnPoints[clientId], Quaternion.identity);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}
