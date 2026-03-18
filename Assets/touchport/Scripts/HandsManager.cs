using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Demo
{
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

        // server-side only
        private readonly Dictionary<ulong, HandData> _hands = new();

        // 所有客户端可读，server 写
        public NetworkVariable<bool> HandsAreClose = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // debug 用：server 把双方手部坐标同步给所有客户端，供 ProximityDebugUI 读取
        public NetworkVariable<Vector3> Debug0Left  = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<Vector3> Debug0Right = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<Vector3> Debug1Left  = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<Vector3> Debug1Right = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [SerializeField] private float proximityThreshold = 0.10f;
        public float ProximityThreshold => proximityThreshold;

        private bool _wasClose = false;
        private float _toggleCooldown = 0f;
        private const float ToggleCooldownDuration = 1.5f;

#if UNITY_EDITOR
        private bool _debugForceClose = false;

        [ContextMenu("Toggle HandsAreClose")]
        private void ToggleHandsAreClose()
        {
            if (!IsServer) { Debug.LogWarning("Only works on server"); return; }
            _debugForceClose = !_debugForceClose;
            HandsAreClose.Value = _debugForceClose;
            Debug.Log($"[touchport] ContextMenu toggle HandsAreClose={_debugForceClose}");
        }
#endif

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Debug.Log($"[touchport] HandsManager spawned IsServer={IsServer} clientId={NetworkManager.LocalClientId}");
            HandsAreClose.OnValueChanged += (oldVal, newVal) =>
                Debug.Log($"[touchport] HandsAreClose {oldVal}->{newVal} clientId={NetworkManager.LocalClientId}");
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            _hands.Clear();
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReportHandsServerRpc(
            Vector3 leftPos, Vector3 rightPos,
            bool leftGrip, bool rightGrip,
            ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            bool firstReport = !_hands.ContainsKey(clientId);
            _hands[clientId] = new HandData
            {
                LeftPos   = leftPos,
                RightPos  = rightPos,
                LeftGrip  = leftGrip,
                RightGrip = rightGrip
            };
            if (firstReport)
                Debug.Log($"[touchport] First hands report from clientId={clientId}");

            // 同步给所有客户端供 ProximityDebugUI 读取
            if      (clientId == 0) { Debug0Left.Value = leftPos; Debug0Right.Value = rightPos; }
            else if (clientId == 1) { Debug1Left.Value = leftPos; Debug1Right.Value = rightPos; }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

#if UNITY_EDITOR
            if (_debugForceClose) return;
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.J].wasPressedThisFrame)
            {
                _debugForceClose = !_debugForceClose;
                HandsAreClose.Value = _debugForceClose;
                Debug.Log($"[touchport] J key toggle HandsAreClose={_debugForceClose}");
                return;
            }
#endif

            if (_toggleCooldown > 0f) _toggleCooldown -= Time.deltaTime;

            bool nowClose = CheckProximity();
            if (nowClose && !_wasClose)
            {
                NotifyCloseClientRpc();
                if (_toggleCooldown <= 0f)
                {
                    HandsAreClose.Value = !HandsAreClose.Value;
                    _toggleCooldown = ToggleCooldownDuration;
                    Debug.Log($"[touchport] Proximity triggered HandsAreClose={HandsAreClose.Value}");
                }
                else
                {
                    Debug.Log($"[touchport] Proximity on cooldown ({_toggleCooldown:F1}s left)");
                }
            }
            _wasClose = nowClose;
        }

        private bool CheckProximity()
        {
            if (!_hands.TryGetValue(0, out var host))  return false;
            if (!_hands.TryGetValue(1, out var guest)) return false;

            // (0,0,0) 表示该手未启用，跳过避免假阳性
            Vector3[] hostHands  = { host.LeftPos,  host.RightPos  };
            Vector3[] guestHands = { guest.LeftPos, guest.RightPos };

            foreach (var h in hostHands)
            {
                if (h == Vector3.zero) continue;
                foreach (var g in guestHands)
                {
                    if (g == Vector3.zero) continue;
                    if (Vector3.Distance(h, g) < proximityThreshold)
                        return true;
                }
            }

            return false;
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestToggleServerRpc()
        {
            HandsAreClose.Value = !HandsAreClose.Value;
            _toggleCooldown = ToggleCooldownDuration;
            Debug.Log($"[touchport] A button toggle HandsAreClose={HandsAreClose.Value}");
        }

        [ClientRpc]
        private void NotifyCloseClientRpc()
        {
            Debug.Log($"[touchport] Close event received clientId={NetworkManager.LocalClientId}");
        }

        public bool TryGetHands(ulong clientId, out HandData data)
            => _hands.TryGetValue(clientId, out data);

        public IEnumerable<KeyValuePair<ulong, HandData>> AllHands() => _hands;
    }
}
