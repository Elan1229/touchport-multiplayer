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
        private bool _wasClose = false; // 上一帧是否靠近，用于检测上升沿
        private float _toggleCooldown = 0f;
        private const float ToggleCooldownDuration = 1.5f;
        private float _debugLogTimer = 0f;

#if UNITY_EDITOR
        private bool _debugForceClose = false;

        [ContextMenu("Toggle HandsAreClose")]
        private void ToggleHandsAreClose()
        {
            if (!IsServer) { Debug.LogWarning("Only works on server"); return; }
            _debugForceClose = !_debugForceClose;
            HandsAreClose.Value = _debugForceClose;
            Debug.Log($"[HandsManager] HandsAreClose forced = {_debugForceClose}");
        }
#endif

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Debug.Log($"[HandsManager] OnNetworkSpawn IsServer={IsServer} IsClient={IsClient} clientId={NetworkManager.LocalClientId}");
            HandsAreClose.OnValueChanged += (oldVal, newVal) =>
                Debug.Log($"[HandsManager] HandsAreClose changed {oldVal}→{newVal} on clientId={NetworkManager.LocalClientId}");
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
            if (_debugForceClose) return; // 保持手动设置的值，跳过自动检测
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.J].wasPressedThisFrame)
            {
                _debugForceClose = !_debugForceClose;
                HandsAreClose.Value = _debugForceClose;
                Debug.Log($"[HandsManager] HandsAreClose forced = {_debugForceClose}");
                return;
            }
#endif

            if (_toggleCooldown > 0f) _toggleCooldown -= Time.deltaTime;

            _debugLogTimer -= Time.deltaTime;
            if (_debugLogTimer <= 0f)
            {
                _debugLogTimer = 2f;
                string keys = string.Join(",", _hands.Keys);
                Debug.Log($"[HandsManager] _hands keys=[{keys}] HandsAreClose={HandsAreClose.Value}");
                if (_hands.TryGetValue(0, out var h0) && _hands.TryGetValue(1, out var h1))
                {
                    float d = Mathf.Min(
                        Vector3.Distance(h0.LeftPos,  h1.LeftPos),
                        Vector3.Distance(h0.LeftPos,  h1.RightPos),
                        Vector3.Distance(h0.RightPos, h1.LeftPos),
                        Vector3.Distance(h0.RightPos, h1.RightPos));
                    Debug.Log($"[HandsManager] minDist={d:F3}m threshold={proximityThreshold}m");
                }
                else
                {
                    Debug.Log($"[HandsManager] missing hands — h0={_hands.ContainsKey(0)} h1={_hands.ContainsKey(1)}");
                }
            }

            // 上升沿触发：从"分开"变"靠近"的瞬间 toggle（冷却期内忽略抖动）
            bool nowClose = CheckProximity();
            if (nowClose && !_wasClose)
            {
                Debug.Log($"[HandsManager] CLOSE detected!");
                NotifyCloseClientRpc();
                if (_toggleCooldown <= 0f)
                {
                    HandsAreClose.Value = !HandsAreClose.Value;
                    _toggleCooldown = ToggleCooldownDuration;
                    Debug.Log($"[HandsManager] HandsAreClose toggled → {HandsAreClose.Value}");
                }
            }
            _wasClose = nowClose;
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
                    {
                        Debug.Log($"[CheckProximity] CLOSE! dist={Vector3.Distance(h, g):F3}m");
                        return true;
                    }

            return false;
        }

        // A 键触发：任意客户端按下 → server toggle
        [ServerRpc(RequireOwnership = false)]
        public void RequestToggleServerRpc()
        {
            HandsAreClose.Value = !HandsAreClose.Value;
            _toggleCooldown = ToggleCooldownDuration;
            Debug.Log($"[HandsManager] A键触发 → HandsAreClose = {HandsAreClose.Value}");
        }

        [ClientRpc]
        private void NotifyCloseClientRpc()
        {
            Debug.Log($"[HandsManager] CLOSE detected! (received on clientId={NetworkManager.LocalClientId})");
        }

        // GrabbableObject 用
        public bool TryGetHands(ulong clientId, out HandData data)
            => _hands.TryGetValue(clientId, out data);

        public IEnumerable<KeyValuePair<ulong, HandData>> AllHands() => _hands;
    }
}
