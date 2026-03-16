using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Demo
{
    /// <summary>
    /// NetworkBehaviour singleton. 每帧接收所有客户端的手部位置和握持状态，
    /// server 端检测 host/guest 手柄是否靠近（< proximityThreshold），
    /// 结果通过 NetworkVariable 同步到所有客户端。
    /// </summary>
    public class HandsManager : NetworkBehaviour
    {
        public static HandsManager Instance { get; private set; }

        public struct HandData
        {
            public Vector3 LeftPos;
            public Vector3 RightPos;
            public bool LeftGrip;
            public bool RightGrip;
        }

        // server-side only，不需要同步
        private readonly Dictionary<ulong, HandData> _hands = new();

        // 所有客户端可读，server 写
        public NetworkVariable<bool> HandsAreClose = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [SerializeField] private float proximityThreshold = 0.10f;

        [ContextMenu("Toggle HandsAreClose")]
        private void ToggleHandsAreClose()
        {
            if (!IsServer) { Debug.LogWarning("Only works on server"); return; }
            HandsAreClose.Value = !HandsAreClose.Value;
            Debug.Log($"[HandsManager] HandsAreClose = {HandsAreClose.Value}");
        }

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            _hands.Clear();
        }

        // 每帧由 LocalHandsReporter 调用，RequireOwnership=false 允许任何客户端发
        [ServerRpc(RequireOwnership = false)]
        public void ReportHandsServerRpc(
            Vector3 leftPos, Vector3 rightPos,
            bool leftGrip, bool rightGrip,
            ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            _hands[clientId] = new HandData
            {
                LeftPos = leftPos,
                RightPos = rightPos,
                LeftGrip = leftGrip,
                RightGrip = rightGrip
            };
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

#if UNITY_EDITOR
            // 键盘模拟：按 J 切换 HandsAreClose（simulator 测试用）
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.J].wasPressedThisFrame)
            {
                HandsAreClose.Value = !HandsAreClose.Value;
                Debug.Log($"[HandsManager] (Editor) HandsAreClose forced = {HandsAreClose.Value}");
                return;
            }
#endif

            bool close = CheckProximity();
            if (HandsAreClose.Value != close)
            {
                HandsAreClose.Value = close;
                Debug.Log($"[HandsManager] HandsAreClose = {close}");
            }
        }

        // 检查 clientId 0（host）和 clientId 1（guest）各自的手柄是否有任意一对 < threshold
        private bool CheckProximity()
        {
            if (!_hands.TryGetValue(0, out var host)) return false;
            if (!_hands.TryGetValue(1, out var guest)) return false;

            Vector3[] hostHands  = { host.LeftPos,  host.RightPos  };
            Vector3[] guestHands = { guest.LeftPos, guest.RightPos };

            foreach (var h in hostHands)
                foreach (var g in guestHands)
                    if (Vector3.Distance(h, g) < proximityThreshold)
                        return true;

            return false;
        }

        // GrabbableObject 用
        public bool TryGetHands(ulong clientId, out HandData data)
            => _hands.TryGetValue(clientId, out data);

        public IEnumerable<KeyValuePair<ulong, HandData>> AllHands() => _hands;
    }
}
