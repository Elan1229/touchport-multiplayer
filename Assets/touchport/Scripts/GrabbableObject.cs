using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Anaglyph.Demo
{
    /// <summary>
    /// 替换 NetworkGrab。完全 server-authoritative：
    ///   - server 检测手柄靠近 + 握持 → 跟随手柄移动
    ///   - server 的 transform 变化由 NetworkTransform 同步到所有客户端
    ///   - 所有客户端根据 gameOwnerId 和 HandsManager.HandsAreClose 控制显示/隐藏
    ///
    /// gameOwnerId:
    ///   0 = host 的物件（方块），客机默认看不见
    ///   1 = guest 的物件（球），主机默认看不见
    ///   ulong.MaxValue 或 alwaysVisible=true → 所有人都能看见
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public class GrabbableObject : NetworkBehaviour
    {
        [Tooltip("0 = host 的物件，1 = guest 的物件")]
        public ulong gameOwnerId = 0;

        [Tooltip("勾选后忽略 gameOwnerId，所有人都能看见并抓取（如 SharedCube）")]
        public bool alwaysVisible = false;

        [SerializeField] private float grabRadius = 0.15f;

        // server-side 状态，不需要同步
        private ulong _grabbingClientId = ulong.MaxValue;
        private bool  _grabbingLeft;
        private Vector3 _grabOffset;

        // 缓存碰撞体（可能有多个），spawn 后取一次
        private Collider[] _colliders;

        public override void OnNetworkSpawn()
        {
            _colliders = GetComponentsInChildren<Collider>();
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

            bool handsAreClose = SharedSession.Instance != null && SharedSession.Instance.IsShared.Value;

            // 如果当前有人抓着
            if (_grabbingClientId != ulong.MaxValue)
            {
                bool stillAuthorized = alwaysVisible
                    || _grabbingClientId == gameOwnerId
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
                    || clientId == gameOwnerId
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
            bool isShared = SharedSession.Instance != null && SharedSession.Instance.IsShared.Value;
            bool isOwner  = NetworkManager.LocalClientId == gameOwnerId;
            bool visible  = alwaysVisible || isOwner || isShared;

            if (visible != _lastVisible)
            {
                Debug.Log($"[GrabbableObject] {gameObject.name} visible={visible} " +
                          $"localClientId={NetworkManager.LocalClientId} gameOwnerId={gameOwnerId} " +
                          $"alwaysVisible={alwaysVisible} isShared={isShared}");
                _lastVisible = visible;
            }

            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = visible;
        }
    }
}
