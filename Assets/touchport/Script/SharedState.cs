using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 共享状态权威。不再用 NetworkVariable，改用 ClientRpc 广播，彻底绕开权限坑。
/// 服务端改值 → BroadcastSharedClientRpc → 所有客户端（含host）同步并触发 OnSharedChanged。
/// </summary>
public class SharedState : NetworkBehaviour
{
    public static SharedState Instance { get; private set; }

    public static event Action<bool> OnSharedChanged;

    private bool _isShared = false;
    public bool IsShared => _isShared;

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log($"[SS] 已生成 IsServer={IsServer} clientId={NetworkManager.LocalClientId}");
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    public void StartShare()
    {
        if (!IsServer) return;
        Debug.Log("[SS] StartShare");
        _isShared = true;
        BroadcastSharedClientRpc(true);
    }

    public void StopShare()
    {
        if (!IsServer) return;
        Debug.Log("[SS] StopShare");
        _isShared = false;
        BroadcastSharedClientRpc(false);
    }

    public void ToggleShare()
    {
        if (!IsServer) return;
        _isShared = !_isShared;
        Debug.Log($"[SS] ToggleShare 赋值后={_isShared}");
        BroadcastSharedClientRpc(_isShared);
    }

    [ClientRpc]
    private void BroadcastSharedClientRpc(bool value)
    {
        Debug.Log($"[SS] 共享状态变化 →{value} clientId={NetworkManager.LocalClientId}");
        _isShared = value;
        OnSharedChanged?.Invoke(value);
    }

    // 3秒后停止共享
    public void StopShareAfterSeconds()
    {
        if (IsServer) StartCoroutine(StopShareDelayed());
    }

    private IEnumerator StopShareDelayed()
    {
        yield return new WaitForSeconds(3f);
        StopShare();
    }
}
