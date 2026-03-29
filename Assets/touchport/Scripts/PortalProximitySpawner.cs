using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server：两名玩家距离持续小于阈值达到 <see cref="holdSeconds"/> 后，在两人中点生成 <see cref="portalPrefab"/>，
/// 并令 <see cref="SharedState.IsShared"/> 为 true。物品 layer / Owner 不变；可见性由各自 URP mask + Portal 触发器处理。
/// </summary>
public class PortalProximitySpawner : NetworkBehaviour
{
    public static PortalProximitySpawner Instance { get; private set; }

    [SerializeField] private GameObject portalPrefab;
    [SerializeField] private float closeDistance = 2f;
    [SerializeField] private float holdSeconds = 3f;

    private float _closeTimer;
    private NetworkObject _spawnedPortal;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>由 <see cref="SharedState"/> 每帧调。</summary>
    public void Tick()
    {
        if (!IsServer) return;
        if (portalPrefab == null) return;
        if (SharedState.Instance == null) return;

        if (!SharedState.Instance.TryGetBothPlayers(out var t0, out var t1))
        {
            SharedState.Instance.InterPlayerDistance.Value = -1f;
            _closeTimer = 0f;
            return;
        }

        float dist = Vector3.Distance(t0.position, t1.position);
        SharedState.Instance.InterPlayerDistance.Value = dist;

        if (_spawnedPortal != null && _spawnedPortal.IsSpawned)
            return;
        if (dist <= closeDistance)
        {
            _closeTimer += Time.deltaTime;
            if (_closeTimer >= holdSeconds)
                SpawnPortalServer(t0.position, t1.position);
        }
        else
        {
            _closeTimer = 0f;
        }
    }

    void SpawnPortalServer(Vector3 p0, Vector3 p1)
    {
        Vector3 mid = (p0 + p1) * 0.5f;
        // Prefab：薄板在本地 XY，+Z 为法线/厚度方向。让 +Z 与「水平面上 P0→P1」同向，门板竖直、正对两人连线。
        Vector3 line = p1 - p0;
        line.y = 0f;
        if (line.sqrMagnitude < 1e-4f)
            line = Vector3.right;
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
        _closeTimer = 0f;

        SharedState.Instance.IsShared.Value = true;
    }

    /// <summary>仅 Server：收门并重置计时。<see cref="SharedState.StopShare"/> 会调。</summary>
    public void DespawnPortal()
    {
        if (!IsServer) return;

        if (_spawnedPortal != null)
        {
            if (_spawnedPortal.IsSpawned)
                _spawnedPortal.Despawn(true);
            _spawnedPortal = null;
        }

        _closeTimer = 0f;
    }
}
