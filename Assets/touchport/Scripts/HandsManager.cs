using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Demo
{
    /// <summary>
    /// 多人触碰/握手状态管理器（server authoritative）。
    ///
    /// 核心职责：
    /// - **接收**每个客户端上报的左右手状态（位置、追踪、输入模式、是否握持/捏合、关键点）
    /// - **在服务器端**做“手靠近”判定，并在触发时切换 <see cref="HandsAreClose"/>
    /// - **用 NetworkVariable 广播** HandsAreClose 给所有客户端：客户端脚本（例如 <c>GrabbableObject</c>）
    ///   会依据该值决定“是否能看到/抓取对方世界的物体”
    ///
    /// 注意：
    /// - 本类中的距离判定、状态切换只在 server 执行；客户端只读 <see cref="HandsAreClose"/>
    /// - HandData 本身不直接同步给客户端；只用于 server 侧逻辑和调试 NetworkVariable 写入
    /// </summary>
    public class HandsManager : NetworkBehaviour
    {
        public static HandsManager Instance { get; private set; }

        /// <summary>
        /// XR UI/调试：把 A 键“两人协同确认”的三阶段同步给所有客户端 UI。
        /// stage：0=first press，1=confirmed，2=re-press same client
        /// </summary>
        public event Action<ulong, int> AButtonHandshakeEvent;
        
        /// <summary>
        /// server 侧缓存的单只手状态。
        /// </summary>
        public struct HandData
        {
            public Vector3   position;
            public bool      isTracked;
            public InputMode mode;
            public bool      isGripping;
        }

        /// <summary>
        /// 手部关键点（用于可视化/调试），通过 NetworkVariable 同步到所有客户端。
        /// </summary>
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

        /// <summary>
        /// server 侧的所有手缓存。key = (clientId, isLeft)。
        /// </summary>
        private readonly Dictionary<(ulong, bool), HandData> _hands = new();

        /// <summary>
        /// “世界合并/握手状态”开关。
        ///
        /// - false：只允许看到/操作自己世界（gameOwnerId == localId 的物体）
        /// - true ：允许看到/操作对方世界（gameOwnerId != localId 的物体）
        ///
        /// 写权限仅 server；所有客户端可读。
        /// </summary>
        public NetworkVariable<bool> HandsAreClose = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // debug NetworkVariables（ProximityDebugUI 用）：为了让客户端 UI 不依赖 server 字典，server 直接写入少量可视化数据
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

        /// <summary>
        /// 服务器端“靠近触发”距离区间（米）。距离在 (min, threshold) 才算有效接近。
        /// - min：避免重叠/抖动导致持续触发
        /// - threshold：最大触发距离
        /// </summary>
        [SerializeField] private float proximityThreshold    = 0.13f;
        [SerializeField] private float proximityMinThreshold = 0.03f;
        public float ProximityThreshold    => proximityThreshold;
        public float ProximityMinThreshold => proximityMinThreshold;

        /// <summary>
        /// 用于“边沿触发”：只在从不接近→接近的那一帧触发一次 toggle。
        /// </summary>
        private bool _wasClose = false;

        /// <summary>
        /// 触发后的冷却时间（秒），避免距离抖动/重复上报导致连续 toggle。
        /// </summary>
        private float _toggleCooldown = 0f;
        private const float ToggleCooldownDuration = 1.5f;

        // 下面这组字段看起来是为“握手窗口/双人确认”做的
        public ulong FirstPressClientId = ulong.MaxValue;
        public float FirstPressTime = -999f;
        public const float HandshakeWindow = 5f;  

        /// <summary>
        /// Netcode spawn 回调。用于建立单例、以及订阅 HandsAreClose 的调试输出。
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Debug.Log($"[touchport] HandsManager spawned IsServer={IsServer} clientId={NetworkManager.LocalClientId}");
            HandsAreClose.OnValueChanged += (oldVal, newVal) =>
                Debug.Log($"[touchport] HandsAreClose {oldVal}->{newVal} clientId={NetworkManager.LocalClientId}");
        }

        /// <summary>
        /// Netcode despawn 回调。清理单例和 server 侧缓存。
        /// </summary>
        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            _hands.Clear();
        }

        /// <summary>
        /// 客户端 → 服务器：上报单只手状态（左右手分别上报）。
        ///
        /// 设计点：
        /// - RequireOwnership=false：因为手部 tracker 可能不在同一个 NetworkObject ownership 体系里，直接允许上报
        /// - server 把数据写进 <see cref="_hands"/>，并写入少量调试 NetworkVariable 供 UI/可视化读取
        /// </summary>
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

        /// <summary>
        /// 仅 server 执行：每帧检查是否发生“接近事件”，在事件发生时按 toggle 规则切换 <see cref="HandsAreClose"/>。
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (_toggleCooldown > 0f) _toggleCooldown -= Time.deltaTime;

            bool nowClose = CheckProximity();
            if (nowClose && !_wasClose)
            {
                NotifyCloseClientRpc();
                if (_toggleCooldown <= 0f)
                {
                    // toggle：第一次接近 -> 打开；再次接近 -> 关闭
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

        /// <summary>
        /// server 侧接近判定：任意两只来自不同玩家的手，在同一输入模式（hand/ctrl）且都处于 tracked 状态，
        /// 并且距离落在阈值区间内，则视为“接近”。
        ///
        /// 这里返回的是“当前是否接近”；真正触发 toggle 的逻辑在 Update 里做边沿检测（nowClose && !_wasClose）。
        /// </summary>
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

        /// <summary>
        /// 客户端 → 服务器：显式请求 toggle（目前由 A 键触发）。
        ///
       
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void UIRequestServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;
            float now = Time.time;

            if (FirstPressClientId == ulong.MaxValue || now - FirstPressTime > HandshakeWindow)
            {
                // 第一个按的，或者上次窗口已过期
                FirstPressClientId = senderId;
                FirstPressTime = now;
                NotifyAButtonHandshakeClientRpc(senderId, 0);
                Debug.Log($"[touchport] A button first press clientId={senderId}, waiting...");
            }
            else if (senderId != FirstPressClientId)
            {
                // 第二个人，且在窗口内，且不是同一个人
                HandsAreClose.Value = !HandsAreClose.Value;
                _toggleCooldown = ToggleCooldownDuration;
                FirstPressClientId = ulong.MaxValue; // 重置
                NotifyAButtonHandshakeClientRpc(senderId, 1);
                Debug.Log($"[touchport] A button handshake confirmed, HandsAreClose={HandsAreClose.Value}");
            }
            else
            {
                // 同一个人重复按，刷新计时
                FirstPressTime = now;
                NotifyAButtonHandshakeClientRpc(senderId, 2);
                Debug.Log($"[touchport] A button re-press same client, timer reset");
            }
        }

        [ClientRpc]
        private void NotifyAButtonHandshakeClientRpc(ulong senderClientId, int stage)
        {
            AButtonHandshakeEvent?.Invoke(senderClientId, stage);
        }

        /// <summary>
        /// 客户端提示用的事件广播（目前只用于 log）。
        /// </summary>
        [ClientRpc]
        private void NotifyCloseClientRpc()
        {
            Debug.Log($"[touchport] Close event received clientId={NetworkManager.LocalClientId}");
        }

        /// <summary>
        /// 供 server 侧抓取逻辑查询：读取某个玩家的某只手当前状态。给其他脚本查询用的公共接口-HandData 这个 struct，四个字段
        /// </summary>
        public bool TryGetHand(ulong clientId, bool isLeft, out HandData data)
            => _hands.TryGetValue((clientId, isLeft), out data);

        /// <summary>
        /// 供 server 侧遍历：枚举所有手（例如抓取物体时遍历候选手）。返回的是整个 _hands 字典的遍历，每个 entry 是 哪个玩家的哪只手，和HandData 这个 struct四个字段
        /// </summary>
        public IEnumerable<KeyValuePair<(ulong, bool), HandData>> AllHands() => _hands;
    }
}
