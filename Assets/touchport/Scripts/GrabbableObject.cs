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

        private void Update()
        {
            if (!IsSpawned) return;

            ApplyVisibility();

            if (!IsServer) return;

            var manager = HandsManager.Instance;
            if (manager == null) return;

            bool handsAreClose = manager.HandsAreClose.Value;

            // 如果当前有人抓着
            if (_grabbingClientId != ulong.MaxValue)
            {
                // 手分开后失去跨世界操作权，强制释放（否则 host 抢不回自己的方块）
                bool stillAuthorized = alwaysVisible
                    || _grabbingClientId == gameOwnerId
                    || handsAreClose;

                if (!stillAuthorized)
                {
                    _grabbingClientId = ulong.MaxValue;
                }
                else if (manager.TryGetHands(_grabbingClientId, out var held))
                {
                    bool stillGripping = _grabbingLeft ? held.LeftGrip : held.RightGrip;
                    if (stillGripping)
                    {
                        // 跟随手柄（保持抓取时的偏移）
                        Vector3 handPos = _grabbingLeft ? held.LeftPos : held.RightPos;
                        transform.position = handPos + _grabOffset;
                        return;
                    }
                    else
                    {
                        _grabbingClientId = ulong.MaxValue; // 松手
                    }
                }
                else
                {
                    _grabbingClientId = ulong.MaxValue; // 客户端断开
                }
            }

            // 没人抓着，检查是否有人要抓
            foreach (var kvp in manager.AllHands())
            {
                ulong clientId = kvp.Key;
                var hands = kvp.Value;

                // 只有主人可以抓，或者 alwaysVisible，或者双方手柄已靠近
                bool canGrab = alwaysVisible
                    || clientId == gameOwnerId
                    || handsAreClose;

                if (!canGrab) continue;

                if (hands.LeftGrip && Vector3.Distance(hands.LeftPos, transform.position) < grabRadius)
                {
                    _grabbingClientId = clientId;
                    _grabbingLeft     = true;
                    _grabOffset       = transform.position - hands.LeftPos;
                    return;
                }

                if (hands.RightGrip && Vector3.Distance(hands.RightPos, transform.position) < grabRadius)
                {
                    _grabbingClientId = clientId;
                    _grabbingLeft     = false;
                    _grabOffset       = transform.position - hands.RightPos;
                    return;
                }
            }
        }

        private void ApplyVisibility()
        {
            bool visible = alwaysVisible
                || NetworkManager.LocalClientId == gameOwnerId
                || (HandsManager.Instance != null && HandsManager.Instance.HandsAreClose.Value);

            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = visible;
        }
    }
}
