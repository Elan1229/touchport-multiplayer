using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Demo
{
    public class HandsManager : NetworkBehaviour
    {
        public static HandsManager Instance { get; private set; }

        // 每只手一个 entry，key = (clientId, isLeft)
        public struct HandData
        {
            public Vector3   position;
            public bool      isTracked;
            public InputMode mode;
            public bool      isGripping;
        }

        // 远端手部 7 个关键点，通过 NetworkVariable 同步
        public struct HandKeyPoints : INetworkSerializable
        {
            public Vector3 wrist, palm;
            public Vector3 thumbTip, indexTip, middleTip, ringTip, pinkyTip;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref wrist);     s.SerializeValue(ref palm);
                s.SerializeValue(ref thumbTip);  s.SerializeValue(ref indexTip);
                s.SerializeValue(ref middleTip); s.SerializeValue(ref ringTip);
                s.SerializeValue(ref pinkyTip);
            }
        }

        private readonly Dictionary<(ulong, bool), HandData> _hands = new();

        public NetworkVariable<bool> HandsAreClose = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // debug NetworkVariables（ProximityDebugUI 用）
        public NetworkVariable<Vector3>   Debug0Left      = new(default,          NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<Vector3>   Debug0Right     = new(default,          NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<Vector3>   Debug1Left      = new(default,          NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<Vector3>   Debug1Right     = new(default,          NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Debug0LeftMode  = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Debug0RightMode = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Debug1LeftMode  = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Debug1RightMode = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 关键点 NetworkVariables（HandJointVisualizer 用）
        public NetworkVariable<HandKeyPoints> KP0L = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<HandKeyPoints> KP0R = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<HandKeyPoints> KP1L = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<HandKeyPoints> KP1R = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [SerializeField] private float proximityThreshold    = 0.13f;
        [SerializeField] private float proximityMinThreshold = 0.03f;
        public float ProximityThreshold    => proximityThreshold;
        public float ProximityMinThreshold => proximityMinThreshold;

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

        // 每只手单独上报，含 mode 和关键点
        [ServerRpc(RequireOwnership = false)]
        public void ReportHandServerRpc(
            bool isLeft, Vector3 position, bool isTracked, InputMode mode, bool isGripping,
            HandKeyPoints keyPoints,
            ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            var key = (clientId, isLeft);
            bool firstReport = !_hands.ContainsKey(key);

            _hands[key] = new HandData
            {
                position   = position,
                isTracked  = isTracked,
                mode       = mode,
                isGripping = isGripping
            };

            if (firstReport)
                Debug.Log($"[touchport] First hand report clientId={clientId} isLeft={isLeft} mode={mode}");

            // 更新 debug NetworkVariables
            if      (clientId == 0 &&  isLeft) { Debug0Left.Value  = position; Debug0LeftMode.Value  = (int)mode; KP0L.Value = keyPoints; }
            else if (clientId == 0 && !isLeft) { Debug0Right.Value = position; Debug0RightMode.Value = (int)mode; KP0R.Value = keyPoints; }
            else if (clientId == 1 &&  isLeft) { Debug1Left.Value  = position; Debug1LeftMode.Value  = (int)mode; KP1L.Value = keyPoints; }
            else if (clientId == 1 && !isLeft) { Debug1Right.Value = position; Debug1RightMode.Value = (int)mode; KP1R.Value = keyPoints; }
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
            // 把所有手数据展开成列表，方便双重循环
            var list = new List<((ulong clientId, bool isLeft) key, HandData data)>();
            foreach (var kv in _hands)
                list.Add((kv.Key, kv.Value));

            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var (keyI, dataI) = list[i];
                    var (keyJ, dataJ) = list[j];

                    if (keyI.clientId == keyJ.clientId) continue;   // 同一玩家
                    if (!dataI.isTracked || !dataJ.isTracked) continue; // 任一手未追踪
                    if (dataI.mode != dataJ.mode) continue;             // 模式不同（ctrl vs hand）
                    if (dataI.mode == InputMode.Off) continue;          // 都是 Off 不算

                    float dist = Vector3.Distance(dataI.position, dataJ.position);
                    if (dist > proximityMinThreshold && dist < proximityThreshold)
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

        // 供其他脚本查询
        public bool TryGetHand(ulong clientId, bool isLeft, out HandData data)
            => _hands.TryGetValue((clientId, isLeft), out data);

        public IEnumerable<KeyValuePair<(ulong, bool), HandData>> AllHands() => _hands;
    }
}
