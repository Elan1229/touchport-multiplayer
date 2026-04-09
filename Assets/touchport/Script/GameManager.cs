using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 业务逻辑层兼网络中转。所有触发 Share 的入口都在这里。
/// XR触发路径：HandsManager检测到握手/A键 → FireHandshake() → OnHandshake事件 → Handshaked() → SharedState.ToggleShare()
/// UI触发路径：本机点Share按钮 → FireUIRequestShare() → 对方弹Accept → FireUIAcceptShare() → SharedState.StartShare()
/// 两条路最终都写 SharedState.IsShared，由 SharedState 广播给下游（PortalSpawner等）
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    // ─── 触发事件（XR + 桌面通用）────────────────────────────────
    // HandsManager（XR握手/A键）和 ScreenPlayerManager（桌面）都调 FireHandshake()
    // Handshaked 订阅此事件，收到后调 SharedState.ToggleShare()
    public static event Action OnHandshake;
    public static void FireHandshake() => OnHandshake?.Invoke();

    // ─── UI 流程事件（ShareUIManager 订阅这些来驱动 UI 显示）────
    public static event Action OnUIWaiting;       // 发起方进入等待状态（显示"等待对方..."）
    public static event Action OnUIRequestShare;  // 接收方收到请求（弹出 Accept 窗口）
    public static event Action OnUIAcceptShare;   // 双方 share 开始（隐藏所有 UI）
    public static event Action OnStopSharing;     // share 结束
    public static event Action OnCancelRequest;   // 请求取消/超时，双方静默 Hide
    public static event Action OnNotAccept;       // 对方拒绝，显示 Not Accepted! 1s 后 Hide

    // ─── 生命周期 ────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Debug.Log($"[GM] spawned IsServer={IsServer} clientId={NetworkManager.LocalClientId}");
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // OnEnable/OnDisable 订阅 OnHandshake，确保 GameManager 激活时才处理事件
    private void OnEnable()  => OnHandshake += Handshaked;
    private void OnDisable() => OnHandshake -= Handshaked;

    // XR握手/A键 触发路径的终点：收到事件后切换共享状态
    // 只有服务端（IsServer）才能写 SharedState.IsShared
    private void Handshaked()
    {
        var session = SharedState.Instance;
        if (session == null || !session.IsServer) return;
        session.ToggleShare(); // False→True 开始共享，True→False 停止共享
    }

    // ─── UI Share流程（本机点按钮 → RPC → 对方UI → Accept → StartShare）

    // 第1步：本机点了 Share? 按钮，发 RPC 给服务端
    public static void FireUIRequestShare()
    {
        Debug.Log($"[GM] 点了Share按钮 GM实例存在={Instance != null}");
        Instance?.RequestShareServerRpc();
    }

    // 第2步：服务端收到请求，分别通知发起方（进入等待）和对方（弹Accept窗口）
    [ServerRpc(RequireOwnership = false)]
    private void RequestShareServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        Debug.Log($"[GM] 服务端收到Share请求 发送方={sender} 在线={string.Join(",", NetworkManager.ConnectedClientsIds)}");

        // 告诉发起方：进入等待状态
        NotifyWaitingClientRpc(new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } } });

        // 告诉其他人：弹出 Accept 窗口
        foreach (var id in NetworkManager.ConnectedClientsIds)
            if (id != sender)
                NotifyRequestShareClientRpc(new ClientRpcParams
                    { Send = new ClientRpcSendParams { TargetClientIds = new[] { id } } });
    }

    [ClientRpc]
    private void NotifyWaitingClientRpc(ClientRpcParams _ = default)
        => OnUIWaiting?.Invoke(); // 发起方UI：显示"等待中..."

    [ClientRpc]
    private void NotifyRequestShareClientRpc(ClientRpcParams _ = default)
        => OnUIRequestShare?.Invoke(); // 接收方UI：显示 Accept 按钮

    // 第3步：接收方点了 Accept，通知服务端正式开始 Share
    public static void FireUIAcceptShare()
    {
        Debug.Log($"[GM] 点了Accept GM存在={Instance != null}");
        Instance?.AcceptShareServerRpc();
    }

    // 第4步：服务端调 StartShare()，IsShared变True，PortalSpawner等下游自动响应
    // accepter（点Accept的人）的 stencil 在 2s 后切换，对应 "Connecting..." 面板时长
    [ServerRpc(RequireOwnership = false)]
    private void AcceptShareServerRpc(ServerRpcParams rpcParams = default)
    {
        Debug.Log("[GM] 服务端收到Accept，StartShare");
        SharedState.Instance?.StartShare(); // 只设True，不Toggle
        NotifyAcceptShareClientRpc();
        ulong accepter = rpcParams.Receive.SenderClientId;
        StartCoroutine(DelayedSwitchStencil(accepter));
    }

    private IEnumerator DelayedSwitchStencil(ulong targetClientId)
    {
        yield return new WaitForSeconds(2f);
        SwitchStencilClientRpc(new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } } });
    }

    [ClientRpc]
    private void SwitchStencilClientRpc(ClientRpcParams _ = default)
        => ChangeLayer.Instance?.SwitchStencilLocal();

    [ClientRpc]
    private void NotifyAcceptShareClientRpc()
        => OnUIAcceptShare?.Invoke(); // 双方UI：隐藏所有 share UI

    // ─── 停止共享 ────────────────────────────────────────────────

    // 任意一方点了 Stop Sharing 按钮
    public static void FireStopSharing()
    {
        Instance?.StopSharingServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void StopSharingServerRpc()
    {
        SharedState.Instance?.StopShare(); // IsShared→False，PortalSpawner收到后收门
        NotifyStopSharingClientRpc();
    }

    [ClientRpc]
    private void NotifyStopSharingClientRpc()
        => OnStopSharing?.Invoke();

    // ─── 取消/拒绝 ───────────────────────────────────────────────

    // 请求超时或发起方主动取消，双方静默恢复初始UI
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

    // 接收方点了拒绝，双方显示 Not Accepted! 提示1秒后收起
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
