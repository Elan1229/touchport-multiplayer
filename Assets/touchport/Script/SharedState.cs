using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 共享状态权威。维护 IsShared NetworkVariable，广播 OnSharedChanged 给下游所有系统。
/// 下游系统只依赖这个类，不需要知道触发原因。
/// </summary>
public class SharedState : NetworkBehaviour
{
    public static SharedState Instance { get; private set; }

    public static event Action<bool> OnSharedChanged;

    public NetworkVariable<bool> IsShared = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log($"[SS] 已生成 IsServer={IsServer} clientId={NetworkManager.LocalClientId}");
        IsShared.OnValueChanged += (old, next) =>
        {
            Debug.Log($"[SS] 共享状态变化 {old}→{next}");
            OnSharedChanged?.Invoke(next);
        };
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    public void StartShare()
    {
        if (IsServer) IsShared.Value = true;
    }

    public void StopShare()
    {
        if (IsServer) IsShared.Value = false;
    }

    public void ToggleShare()
    {
        Debug.Log($"[SS] 切换共享 IsServer={IsServer} 当前={IsShared.Value}");
        if (IsServer) IsShared.Value = !IsShared.Value;
        Debug.Log($"[SS] 赋值后={IsShared.Value}");
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
