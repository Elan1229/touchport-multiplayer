using Unity.Netcode;
using UnityEngine;

public class StickyObject : NetworkBehaviour
{
    public ulong gameOwnerId;

    private Transform _followTarget;
    private Vector3 _offset;

    // 服务器写，所有客户端读：两人靠近时 true
    private NetworkVariable<bool> _sharedVisible = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private PlayerMove _player0;
    private PlayerMove _player1;

    public override void OnNetworkSpawn()
    {
        _sharedVisible.OnValueChanged += (_, val) => ApplyVisibility(val);
        ApplyVisibility(_sharedVisible.Value);
    }

    void ApplyVisibility(bool sharedVisible)
    {
        bool iAmNonOwner = NetworkManager.LocalClientId != gameOwnerId;
        bool show = !iAmNonOwner || sharedVisible;
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = show;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (_followTarget != null) return;

        var player = other.GetComponentInParent<PlayerMove>();
        if (player == null) return;

        // 非主人：只有 sharedVisible（两人靠近，看得见）时才能粘
        if (player.OwnerClientId != gameOwnerId && !_sharedVisible.Value) return;

        _followTarget = player.transform;
        _offset = transform.position - _followTarget.position;
    }

    void Update()
    {
        if (!IsServer) return;

        if (_followTarget != null)
            transform.position = _followTarget.position + _offset;

        if (_player0 == null || _player1 == null)
            FindPlayers();
        if (_player0 == null || _player1 == null) return;

        bool shouldShow = Vector3.Distance(_player0.transform.position, _player1.transform.position) < 1f;
        if (_sharedVisible.Value != shouldShow)
        {
            _sharedVisible.Value = shouldShow;
            // 刚变成不可见：如果是非主人粘着的，断开
            if (!shouldShow && _followTarget != null)
            {
                var pm = _followTarget.GetComponent<PlayerMove>();
                if (pm != null && pm.OwnerClientId != gameOwnerId)
                    _followTarget = null;
            }
        }
    }

    void FindPlayers()
    {
        foreach (var p in FindObjectsByType<PlayerMove>(FindObjectsSortMode.None))
        {
            if (p.OwnerClientId == 0) _player0 = p;
            else if (p.OwnerClientId == 1) _player1 = p;
        }
    }
}
