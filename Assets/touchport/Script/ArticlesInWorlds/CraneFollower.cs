using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 挂在纸鹤上。跟随指定玩家身体左前方45度0.5米，高度1米。
/// isShared 时停在原地；share 结束后恢复跟随。
/// 通过修改 Drifting.startPos 驱动位置，Drifting 继续在基准点上漂。
/// 客户端禁用 Drifting，由 NetworkTransform 同步服务端位置。
/// </summary>
public class CraneFollower : NetworkBehaviour
{
    [SerializeField] private ulong followClientId = 0;
    [SerializeField] private float followDistance = 0.5f;
    [SerializeField] private float followAngle    = 45f;   // 向左偏的角度
    [SerializeField] private float followHeight   = 1f;    // 世界坐标 y
    [SerializeField] private float lerpSpeed      = 2f;

    private Drifting _drifting;
    private bool _isFollowing = true;

    public override void OnNetworkSpawn()
    {
        _drifting = GetComponent<Drifting>();

        if (!IsServer)
        {
            if (_drifting != null) _drifting.enabled = false;
            return;
        }

        SharedState.OnSharedChanged += OnSharedChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            SharedState.OnSharedChanged -= OnSharedChanged;
    }

    private void OnSharedChanged(bool isShared)
    {
        _isFollowing = !isShared;
    }

    private void Update()
    {
        if (!IsServer || !_isFollowing || _drifting == null) return;

        var player = GetPlayerTransform(followClientId);
        if (player == null) return;

        // 玩家前方向左旋转 followAngle 度
        Vector3 dir = Quaternion.Euler(0, -followAngle, 0) * player.forward;
        Vector3 target = player.position + dir * followDistance;
        target.y = followHeight;

        _drifting.startPos = Vector3.Lerp(_drifting.startPos, target, Time.deltaTime * lerpSpeed);
    }

    private Transform GetPlayerTransform(ulong clientId)
    {
        foreach (var no in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            if (no.IsSpawned && no.IsPlayerObject && no.OwnerClientId == clientId)
                return no.transform;
        return null;
    }
}
