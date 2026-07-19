using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 替换 NetworkGrab。完全 server-authoritative：
///   - server 检测手柄靠近 + 握持 → 跟随手柄移动
///   - server 的 transform 变化由 NetworkTransform 同步到所有客户端
///   - 所有客户端根据 ownerPlayerId 和 HandsManager.HandsAreClose 控制显示/隐藏
///
/// ownerPlayerId（游戏归属，≠ NGO 的网络 OwnerClientId）：
///   0 = host 的物件（方块），客机默认看不见
///   1 = guest 的物件（球），主机默认看不见
///   ulong.MaxValue 或 alwaysVisible=true → 所有人都能看见
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class IObjectXR : NetworkBehaviour
{
    [Tooltip("0 = host 的物件，1 = guest 的物件")]
    [UnityEngine.Serialization.FormerlySerializedAs("gameOwnerId")]
    public ulong ownerPlayerId = 0;

    private NetworkVariable<ulong> _networkOwnerId = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Tooltip("勾选后忽略 ownerPlayerId，所有人都能看见并抓取（如 SharedCube）")]
    public bool alwaysVisible = false;

    [SerializeField] private float grabRadius = 0.15f;

    // server-side 状态，不需要同步
    private ulong _grabbingClientId = ulong.MaxValue;
    private bool  _grabbingLeft;
    private Vector3 _grabOffset;

    public bool IsBeingHeld => _grabbingClientId != ulong.MaxValue;

    // 缓存碰撞体（可能有多个），spawn 后取一次
    private Collider[] _colliders;

    public override void OnNetworkSpawn()
    {
        _colliders = GetComponentsInChildren<Collider>();
        if (IsServer) _networkOwnerId.Value = ownerPlayerId;
        _networkOwnerId.OnValueChanged += (_, newVal) =>
        {
            ownerPlayerId = newVal;
            PlayerWorld.ApplyOwnerLayer(gameObject, newVal);
        };

        // OnValueChanged 只在值真的变化时触发——spawn 这一刻同步过来的初始值不算"变化"，
        // 不会走上面那个回调。这里用当前已同步好的值强制刷一次，保证刚 spawn 出来那一刻
        // layer 就是对的，不用等到真的换属主（穿门）才第一次生效。
        ownerPlayerId = _networkOwnerId.Value;
        PlayerWorld.ApplyOwnerLayer(gameObject, ownerPlayerId);
    }

    // 由 PortalDirectionTrigger.OnTriggerEnter 在物体的 Collider 进了 PortalTrigger 时调用
    // （普通 Trigger 碰撞，不是距离判断）：翻转 ownerPlayerId，上面 _networkOwnerId.OnValueChanged
    // 会顺带把 layer 切过去。
    public void TransferToOther()
    {
        if (!IsServer) return;
        ulong newOwner = PlayerWorld.OtherPlayer(ownerPlayerId);
        Debug.Log($"[touchport] IObject Collided! {gameObject.name} owner {ownerPlayerId}->{newOwner}");
        _networkOwnerId.Value = newOwner;
    }

    // 返回手柄到物体表面（或中心）的最近距离
    private float DistanceToObject(Vector3 handPos)
    {
        if (_colliders != null && _colliders.Length > 0)
        {
            float minDist = float.MaxValue;
            foreach (var col in _colliders)
            {
                Vector3 closest = col.ClosestPoint(handPos);
                float d = Vector3.Distance(handPos, closest);
                if (d < minDist) minDist = d;
            }
            return minDist;
        }
        return Vector3.Distance(handPos, transform.position);
    }

    private void Update()
    {
        ApplyVisibility(); // 不依赖网络权限，客机也需要跑

        if (!IsSpawned) return;
        if (!IsServer) return;

        var manager = HandsManager.Instance;
        if (manager == null) return;

        bool handsAreClose = SharedState.Instance != null && SharedState.Instance.IsShared;

        // 如果当前有人抓着
        if (_grabbingClientId != ulong.MaxValue)
        {
            bool stillAuthorized = alwaysVisible
                || _grabbingClientId == ownerPlayerId
                || handsAreClose;

            if (!stillAuthorized)
            {
                _grabbingClientId = ulong.MaxValue;
            }
            else if (manager.TryGetHand(_grabbingClientId, _grabbingLeft, out var held))
            {
                if (held.isGripping)
                {
                    transform.position = held.position + _grabOffset;
                    return;
                }
                else
                {
                    _grabbingClientId = ulong.MaxValue;
                }
            }
            else
            {
                _grabbingClientId = ulong.MaxValue;
            }
        }

        // 没人抓着，检查是否有人要抓
        foreach (var kvp in manager.AllHands())
        {
            ulong clientId = kvp.Key.Item1;
            var hand = kvp.Value;

            bool canGrab = alwaysVisible
                || clientId == ownerPlayerId
                || handsAreClose;

            if (!canGrab) continue;
            if (!hand.isTracked || !hand.isGripping) continue;
            if (DistanceToObject(hand.position) >= grabRadius) continue;

            _grabbingClientId = clientId;
            _grabbingLeft     = kvp.Key.Item2;
            _grabOffset       = transform.position - hand.position;
            return;
        }
    }

    private bool _lastVisible = true; // 用于只在变化时 log

    private void ApplyVisibility()
    {
        bool isShared = SharedState.Instance != null && SharedState.Instance.IsShared;
        bool isOwner  = NetworkManager.LocalClientId == ownerPlayerId;
        bool visible  = alwaysVisible || isOwner || isShared;

        if (visible != _lastVisible)
        {
            //Debug.Log($"[GrabbableObject] {gameObject.name} visible={visible} " +
            //          $"localClientId={NetworkManager.LocalClientId} ownerPlayerId={ownerPlayerId} " +
             //        $"alwaysVisible={alwaysVisible} isShared={isShared}");
            _lastVisible = visible;
        }

        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = visible;
    }
}
