using Unity.Netcode;
using UnityEngine;

public class PortalProximitySpawner : NetworkBehaviour
{
    public static PortalProximitySpawner Instance { get; private set; }

    [SerializeField] private GameObject portalPrefab;

    private NetworkObject _spawnedPortal;

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
    }

    // 每帧检测 portal 的生死
    private void Update()
    {
        if (!IsServer) return;
        if (portalPrefab == null) return;

        bool isShared = SharedState.Instance != null && SharedState.Instance.IsShared.Value;
        bool hasPortal = _spawnedPortal != null && _spawnedPortal.IsSpawned;

        // 处于共享状态 且 还没有 portal → 生成（位置由手部数据决定）
        if (!hasPortal && isShared)
            SpawnPortalServer();

        // 不再共享 且 还有 portal → 收门
        if (hasPortal && !isShared)
            DespawnPortal();
    }

    // 在两手中点生成 portal，朝向两人连线方向，并触发 StartShare
    private void SpawnPortalServer()
    {
        if (!TryGetBothPlayerPositions(out Vector3 p0, out Vector3 p1)) return;

        Vector3 mid = (p0 + p1) * 0.5f;
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
            Debug.LogError("[PortalProximitySpawner] portalPrefab 需要带 NetworkObject。");
            Destroy(go);
            return;
        }

        net.Spawn();
        _spawnedPortal = net;
    }

    // 从 HandsManager 拿两个玩家各自的头部位置
    private bool TryGetBothPlayerPositions(out Vector3 p0, out Vector3 p1)
    {
        p0 = Vector3.zero;
        p1 = Vector3.zero;

        var hm = HandsManager.Instance;
        if (hm == null) return false;

        return hm.TryGetBothHeadPositions(out p0, out p1);
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

        // portal 消失时把 stencil 恢复到出生时的状态
        ResetStencilClientRpc();
    }

    [ClientRpc]
    private void ResetStencilClientRpc()
    {
        ChangeLayer.Instance?.ResetStencilLocal();
    }
}
