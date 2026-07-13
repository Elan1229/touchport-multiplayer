using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class PlayerSpawner : NetworkBehaviour
{
    public GameObject playerPrefab;

    [SerializeField] private Vector3[] spawnPoints =
    {
        new Vector3(-3f, 0.5f, 0f),  // 玩家 0（WorldA 侧）
        new Vector3( 3f, 0.5f, 0f),  // 玩家 1（WorldB 侧）
    };

    private readonly HashSet<ulong> _spawnedClientIds = new HashSet<ulong>();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        // 先注册回调（处理之后连入的客户端）
        NetworkManager.OnClientConnectedCallback += Spawn;

        // 再补齐当前已经连接的客户端（包括 Host 自己）
        foreach (var clientId in NetworkManager.ConnectedClientsIds)
            Spawn(clientId);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            NetworkManager.OnClientConnectedCallback -= Spawn;
    }

    void Spawn(ulong clientId)
    {
        if (_spawnedClientIds.Contains(clientId)) return; // 防止 Host/重复回调导致生成两次
        if (clientId >= (ulong)spawnPoints.Length) return;
        var go = Instantiate(playerPrefab, spawnPoints[clientId], Quaternion.identity);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        _spawnedClientIds.Add(clientId);
    }
}
