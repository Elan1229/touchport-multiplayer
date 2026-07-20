using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PortalSpawner : NetworkBehaviour
{
    public static PortalSpawner Instance { get; private set; }

    [SerializeField] private GameObject portalPrefab;

    [Tooltip("勾选后 portal 生成在对方玩家位置，否则生成在两人中点")]
    [SerializeField] private bool spawnAtOpponent = false;

    private NetworkObject _spawnedPortal;
    private bool _spawnPending = false;

    void Awake() => Instance = this;

    public override void OnDestroy()
    {
        GameManager.OnHandshake -= OnHandshakeTriggered;
        if (Instance == this)
            Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        // 出生时视角初始化为自己的老家世界（P0 与场景默认一致；P1 等价于旧逻辑里的"反转"）
        ChangeLayer.Instance?.ViewHomeWorld();
        GameManager.OnHandshake += OnHandshakeTriggered;
    }

    public override void OnNetworkDespawn()
    {
        GameManager.OnHandshake -= OnHandshakeTriggered;
    }

    private void OnHandshakeTriggered()
    {
        if (!IsServer) return;
        _spawnPending = true;
    }

    // 每帧检测 portal 的生死
    private void Update()
    {
        if (!IsServer) return;
        if (portalPrefab == null) return;

        bool isShared = SharedState.Instance != null && SharedState.Instance.IsShared;
        bool hasPortal = _spawnedPortal != null && _spawnedPortal.IsSpawned;

        // 握手/A键触发了（_spawnPending），且处于共享状态，且还没有 portal → 生成
        if (_spawnPending && isShared && !hasPortal)
        {
            _spawnPending = false;
            SpawnPortalServer();
        }
        else if (_spawnPending && !isShared)
        {
            _spawnPending = false; // 握手把 sharing 关掉了，不生成
        }

        // 不再共享 且 还有 portal → 收门（优雅关门进行中就别抢跑，让动画放完）
        if (hasPortal && !isShared && !_closing)
            DespawnPortal();
    }

    // 生成 portal：中点模式或对方位置模式
    private void SpawnPortalServer()
    {
        if (!TryGetBothPlayerPositions(out Vector3 p0, out Vector3 p1))
        {
            // [HandsDiag] 静默失败改为显式打点——portal 没出来时第一个看这里
            Debug.LogWarning("[HandsDiag][Portal] spawn SKIPPED — missing player positions (need both heads/players)");
            return;
        }

        Vector3 line = p1 - p0;
        line.y = 0f;
        if (line.sqrMagnitude < 1e-4f)
            line = Vector3.right; // 两人重叠时的 fallback 方向
        line.Normalize();

        Vector3 spawnPos;
        if (spawnAtOpponent)
        {
            spawnPos = p1 + Vector3.up * 0.2f;
            line = -line; // 朝向从对方指向自己
        }
        else
        {
            spawnPos = (p0 + p1) * 0.5f + Vector3.up * 0.2f;
        }

        Quaternion rot = Quaternion.LookRotation(line, Vector3.up);

        var go = Instantiate(portalPrefab, spawnPos, rot);
        var net = go.GetComponent<NetworkObject>();
        if (net == null)
        {
            Debug.LogError("[PortalSpawner] portalPrefab 需要带 NetworkObject。");
            Destroy(go);
            return;
        }

        net.Spawn();
        _spawnedPortal = net;
        Debug.Log($"[HandsDiag][Portal] portal SPAWNED at {spawnPos:F2} (p0={p0:F2} p1={p1:F2})");
    }

    // 优先用 HandsManager 头部数据（XR），没有则 fallback 到玩家 NetworkObject 位置（桌面）
    private bool TryGetBothPlayerPositions(out Vector3 p0, out Vector3 p1)
    {
        p0 = Vector3.zero;
        p1 = Vector3.zero;

        var hm = HandsManager.Instance;
        if (hm != null)
            return hm.TryGetBothHeadPositions(out p0, out p1);

        // 桌面 fallback：从玩家 NetworkObject 拿位置
        if (ScreenPlayerManager.TryGetBothPlayers(out Transform t0, out Transform t1))
        {
            p0 = t0.position;
            p1 = t1.position;
            return true;
        }
        return false;
    }

    private bool _closing = false;

    // 优雅关门（换梦时用）：所有端一起播反向缩回动画，缩完 despawn + 各端 stencil 回家。
    // server-only；没有 portal 或已在关门中则 no-op，重复调用安全。
    public void ClosePortal()
    {
        if (!IsServer || _closing) return;
        if (_spawnedPortal == null || !_spawnedPortal.IsSpawned) return;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        _closing = true;
        var anim = _spawnedPortal.GetComponent<PortalSpawnAnim>();
        float wait = 0f;
        if (anim != null)
        {
            anim.CloseClientRpc();          // 大家一起缩
            wait = anim.CloseDuration;
        }
        if (wait > 0f) yield return new WaitForSeconds(wait + 0.1f);   // 留 0.1s 让 client 端动画收尾
        // 终态和按 U 关 share 完全一致：share 结束（广播 OnSharedChanged）+ despawn +
        // ViewHomeClientRpc（串门玩家视角回家）。动画期间 share 保持 true，避免 Update 抢跑。
        SharedState.Instance?.StopShare();
        DespawnPortal();
        _closing = false;
    }

    // 收掉 portal，供外部调用（比如 share 结束时）
    public void DespawnPortal()
    {
        if (!IsServer) return;

        if (_spawnedPortal != null)
        {
            if (_spawnedPortal.IsSpawned)
                _spawnedPortal.Despawn(true);
            _spawnedPortal = null;
        }

        // portal 消失时把视角恢复到出生时的状态（各端回自己老家）
        ViewHomeClientRpc();
    }

    [ClientRpc]
    private void ViewHomeClientRpc()
    {
        ChangeLayer.Instance?.ViewHomeWorld();
    }
}
