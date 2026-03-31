using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 业务逻辑层兼网络中转。所有触发 Share 的入口都在这里。
/// XR：HandsManager fire OnInteract；桌面：ScreenPlayerManager fire OnInteract。
/// UI 流程：FireUIRequestShare → 对方弹 Accept → FireUIAcceptShare → StartShare。
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    // ─── 事件 ────────────────────────────────────────────────────

    // 握手 / A键触发（XR + 桌面通用）
    public static event Action OnInteract;
    public static void FireInteract() => OnInteract?.Invoke();

    // UI 流程事件（本地触发，ShareUIManager 订阅）
    public static event Action OnUIWaiting;       // 发起方进入等待状态
    public static event Action OnUIRequestShare;  // 接收方收到请求，弹 Accept 窗口
    public static event Action OnUIAcceptShare;   // 双方 share 开始
    public static event Action OnStopSharing;     // share 结束
    public static event Action OnCancelRequest;   // 请求取消/超时，双方静默 Hide
    public static event Action OnNotAccept;       // 对方拒绝，双方显示 Not Accepted! 1s 后 Hide

    // ─── 生命周期 ────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Debug.Log($"[GM] 已生成 IsServer={IsServer} clientId={NetworkManager.LocalClientId}");
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()  => OnInteract += HandleInteract;
    private void OnDisable() => OnInteract -= HandleInteract;

    private void HandleInteract()
    {
        var session = SharedState.Instance;
        Debug.Log($"[GM] 收到握手/A键 SharedState存在={session != null} IsServer={session?.IsServer}");
        if (session == null || !session.IsServer) return;
        session.ToggleShare();
    }

    // ─── UI 流程 ─────────────────────────────────────────────────

    // 本机点了 Share? 按钮 → 告诉 Server
    public static void FireUIRequestShare()
    {
        Debug.Log($"[GM] 点了Share按钮 GM实例存在={Instance != null}");
        Instance?.RequestShareServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestShareServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        Debug.Log($"[GM] 服务端收到Share请求 发送方={sender} 在线={string.Join(",", NetworkManager.ConnectedClientsIds)}");

        NotifyWaitingClientRpc(new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } } });

        foreach (var id in NetworkManager.ConnectedClientsIds)
            if (id != sender)
            {
                // Debug.Log($"[GM] Notifying client {id} to show Accept");
                NotifyRequestShareClientRpc(new ClientRpcParams
                    { Send = new ClientRpcSendParams { TargetClientIds = new[] { id } } });
            }
    }

    [ClientRpc]
    private void NotifyWaitingClientRpc(ClientRpcParams _ = default)
        => OnUIWaiting?.Invoke();

    [ClientRpc]
    private void NotifyRequestShareClientRpc(ClientRpcParams _ = default)
        => OnUIRequestShare?.Invoke();

    // 接收方点了 Accept → 告诉 Server 开始 Share
    public static void FireUIAcceptShare()
    {
        Instance?.AcceptShareServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void AcceptShareServerRpc()
    {
        SharedState.Instance?.StartShare();
        // SharedState.OnSharedChanged 会广播给所有客户端
        NotifyAcceptShareClientRpc();
    }

    [ClientRpc]
    private void NotifyAcceptShareClientRpc()
        => OnUIAcceptShare?.Invoke();

    // 任意一方点了 Stop Sharing
    public static void FireStopSharing()
    {
        Instance?.StopSharingServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void StopSharingServerRpc()
    {
        SharedState.Instance?.StopShare();
        NotifyStopSharingClientRpc();
    }

    [ClientRpc]
    private void NotifyStopSharingClientRpc()
        => OnStopSharing?.Invoke();

    // 请求取消/超时，双方静默 Hide
    public static void FireCancelRequest()
    {
        Instance?.CancelRequestServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void CancelRequestServerRpc()
        => NotifyCancelRequestClientRpc();

    [ClientRpc]
    private void NotifyCancelRequestClientRpc()
        => OnCancelRequest?.Invoke();

    // 对方拒绝，双方显示 Not Accepted! 1s 后 Hide
    public static void FireNotAccept()
    {
        Instance?.NotAcceptServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotAcceptServerRpc()
        => NotifyNotAcceptClientRpc();

    [ClientRpc]
    private void NotifyNotAcceptClientRpc()
        => OnNotAccept?.Invoke();
}
