using Unity.Netcode;
using UnityEngine;

public class PortalSpawner : NetworkBehaviour
{
    public static PortalSpawner Instance { get; private set; }

    [SerializeField] private GameObject portalPrefab;

    private NetworkObject _spawnedPortal;
    private bool _spawnPending = false;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        // Player1（guest）出生时 stencil 初始化为反的，和 Player0 的世界互换
        if (NetworkManager.LocalClientId == 1 && ChangeLayer.Instance != null)
        {
            ChangeLayer.Instance.ChangeRendererLayerMask("StencilThisWorld", "layer1");
            ChangeLayer.Instance.ChangeRendererLayerMask("StencilPortalWorld", "layer0");
        }
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

        // 不再共享 且 还有 portal → 收门
        if (hasPortal && !isShared)
            DespawnPortal();
    }

    // 在两手中点生成 portal，朝向两人连线方向，并触发 StartShare
    private void SpawnPortalServer()
    {
        if (!TryGetBothPlayerPositions(out Vector3 p0, out Vector3 p1)) return;

        Vector3 mid = (p0 + p1) * 0.5f + Vector3.up * 0.2f;
        Vector3 line = p1 - p0;
        line.y = 0f;
        if (line.sqrMagnitude < 1e-4f)
            line = Vector3.right; // 两人重叠时的 fallback 方向
        line.Normalize();

        Quaternion rot = Quaternion.LookRotation(line, Vector3.up);

        var go = Instantiate(portalPrefab, mid, rot);
        var net = go.GetComponent<NetworkObject>();
        if (net == null)
        {
            Debug.LogError("[PortalSpawner] portalPrefab 需要带 NetworkObject。");
            Destroy(go);
            return;
        }

        net.Spawn();
        _spawnedPortal = net;
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

        // stencil 的恢复不在这里做：ChangeLayer 订阅了 SharedState.OnSharedChanged，
        // 共享一结束就统一恢复，三条结束路径都覆盖得到。
    }
}
