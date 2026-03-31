using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 跑在服务端。收集所有客户端上报的手部数据，
/// 检测触发条件（握手接触、A键），满足时 fire OnHandsTouched。
/// 只管感知，不管游戏逻辑。
/// </summary>
public class HandsManager : NetworkBehaviour
{
    public static HandsManager Instance { get; private set; }

    // 手部接触/A键按下时触发（Server only）。通过 GameManager.FireInteract() 传出去。

    // ─── 数据结构 ────────────────────────────────────────────────

    // 每只手的状态，key = (clientId, isLeft)
    public struct HandData
    {
        public Vector3   position;   // 世界坐标
        public bool      isTracked;  // 是否正在被追踪
        public InputMode mode;       // 手追踪还是手柄
        public bool      isGripping; // 是否在握持/捏合
    }

    // 用于远端手部可视化的 7 个关键点，通过 NetworkVariable 同步给所有客户端
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

    // ─── 服务端内部状态 ──────────────────────────────────────────

    // 服务端存的所有手部数据，LocalHandsReporter 每帧上报更新这里
    private readonly Dictionary<(ulong, bool), HandData> _hands = new();

    // 每个客户端的头部世界坐标，key = clientId
    private readonly Dictionary<ulong, Vector3> _headPositions = new();

    private bool  _wasClose = false;          // 上一帧是否接触，用来检测上升沿
    private bool  _aPressedThisFrame = false; // 本帧是否收到A键上报
    private float _toggleCooldown = 0f;       // 触发冷却，防止反复 fire
    private const float ToggleCooldownDuration = 1.5f;

    // ─── Inspector 参数 ──────────────────────────────────────────

    // 接触判定的距离范围：手在 min~max 之间才算接触（太近可能是穿模）
    [SerializeField] private float proximityThreshold    = 0.13f;
    [SerializeField] private float proximityMinThreshold = 0.03f;
    public float ProximityThreshold    => proximityThreshold;
    public float ProximityMinThreshold => proximityMinThreshold;

    // ─── NetworkVariables（同步给所有客户端）────────────────────

    // 当前是否有跨玩家手部接触，ProximityDebugUI 读取显示用
    public NetworkVariable<bool> HandsAreClose = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 手部世界坐标，ProximityDebugUI 读取显示远端手的坐标
    public NetworkVariable<Vector3> Debug0Left      = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Vector3> Debug0Right     = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Vector3> Debug1Left      = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Vector3> Debug1Right     = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int>     Debug0LeftMode  = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int>     Debug0RightMode = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int>     Debug1LeftMode  = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int>     Debug1RightMode = new((int)InputMode.Off, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 手指关键点，HandJointVisualizer 读取来渲染远端手骨骼
    // KP0L = client0 左手，KP0R = client0 右手，KP1L = client1 左手，KP1R = client1 右手
    public NetworkVariable<HandKeyPoints> KP0L = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<HandKeyPoints> KP0R = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<HandKeyPoints> KP1L = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<HandKeyPoints> KP1R = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── 生命周期 ────────────────────────────────────────────────

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

    // ─── 数据上报（客户端 → 服务端）────────────────────────────

    // LocalHandsReporter 每 30Hz 调用一次，把本机手部数据发到服务端。
    // isLeft: 左手还是右手
    // position: 世界坐标
    // isTracked: 是否追踪到
    // mode: 手追踪 or 手柄
    // isGripping: 握持/捏合
    // keyPoints: 手指关键点（手追踪才有，否则是 default）
    // aButtonPressed: 右手 A 键是否在这一帧按下（只右手上报时传 true）
    [ServerRpc(RequireOwnership = false)]
    public void ReportHandServerRpc(
        bool isLeft, Vector3 position, bool isTracked, InputMode mode, bool isGripping,
        HandKeyPoints keyPoints, bool aButtonPressed,
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

        // 把手部数据写入 debug NetworkVariable，同步给所有客户端显示用
        if      (clientId == 0 &&  isLeft) { Debug0Left.Value  = position; Debug0LeftMode.Value  = (int)mode; KP0L.Value = keyPoints; }
        else if (clientId == 0 && !isLeft) { Debug0Right.Value = position; Debug0RightMode.Value = (int)mode; KP0R.Value = keyPoints; }
        else if (clientId == 1 &&  isLeft) { Debug1Left.Value  = position; Debug1LeftMode.Value  = (int)mode; KP1L.Value = keyPoints; }
        else if (clientId == 1 && !isLeft) { Debug1Right.Value = position; Debug1RightMode.Value = (int)mode; KP1R.Value = keyPoints; }

        // A键状态暂存，Update 里和 proximity 一起统一处理
        if (aButtonPressed && !isLeft)
        {
            _aPressedThisFrame = true;
            Debug.Log($"[touchport] 服务端收到A键 来自clientId={clientId}");
        }
    }

    // ─── 触发检测（每帧，Server only）──────────────────────────

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        if (_toggleCooldown > 0f) _toggleCooldown -= Time.deltaTime;

        CheckHandshakeInputs();
    }

    // 检测所有触发条件，满足任一条件就 fire OnHandsTouched
    private void CheckHandshakeInputs()
    {
        bool proximityTriggered = CheckProximity();
        HandsAreClose.Value = proximityTriggered; // 仅用于 debug 显示

        bool shouldFire = false;

        if (proximityTriggered && !_wasClose)
        {
            NotifyCloseClientRpc();
            shouldFire = true;
            Debug.Log("[touchport] Proximity touch detected");
        }

        if (_aPressedThisFrame)
        {
            shouldFire = true;
            Debug.Log("[touchport] A button detected");
        }

        if (shouldFire && _toggleCooldown <= 0f)
        {
            _toggleCooldown = ToggleCooldownDuration;
            GameManager.FireInteract();
            Debug.Log("[touchport] FireInteract called");
        }
        else if (shouldFire)
        {
            Debug.Log($"[touchport] On cooldown ({_toggleCooldown:F1}s left)");
        }

        _wasClose = proximityTriggered;
        _aPressedThisFrame = false;
    }

    // 遍历所有手部数据，检查是否有来自不同玩家的两只手在接触范围内
    private bool CheckProximity()
    {
        var list = new List<((ulong clientId, bool isLeft) key, HandData data)>();
        foreach (var kv in _hands)
            list.Add((kv.Key, kv.Value));

        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                var (keyI, dataI) = list[i];
                var (keyJ, dataJ) = list[j];

                if (keyI.clientId == keyJ.clientId) continue;      // 同一玩家的两只手不算
                if (!dataI.isTracked || !dataJ.isTracked) continue; // 任一手没追踪到不算
                if (dataI.mode != dataJ.mode) continue;             // 一个手追踪一个手柄不算
                if (dataI.mode == InputMode.Off) continue;          // 都是 Off 不算

                float dist = Vector3.Distance(dataI.position, dataJ.position);
                if (dist > proximityMinThreshold && dist < proximityThreshold)
                    return true;
            }
        }
        return false;
    }

    // 通知所有客户端"接触发生了"，目前只用于 debug log
    [ClientRpc]
    private void NotifyCloseClientRpc()
    {
        Debug.Log($"[touchport] Close event received clientId={NetworkManager.LocalClientId}");
    }

    // ─── 对外查询接口 ────────────────────────────────────────────

    // GrabbableObject 用：查询某个客户端的某只手的状态
    public bool TryGetHand(ulong clientId, bool isLeft, out HandData data)
        => _hands.TryGetValue((clientId, isLeft), out data);

    // GrabbableObject 用：遍历所有手，检查谁在够物体
    public IEnumerable<KeyValuePair<(ulong, bool), HandData>> AllHands() => _hands;

    // LocalHandsReporter 上报头部位置
    [ServerRpc(RequireOwnership = false)]
    public void ReportHeadServerRpc(Vector3 headPosition, ServerRpcParams rpcParams = default)
    {
        _headPositions[rpcParams.Receive.SenderClientId] = headPosition;
    }

    // PortalProximitySpawner 用：拿两个玩家的头部位置，两个都有才返回 true
    public bool TryGetBothHeadPositions(out Vector3 head0, out Vector3 head1)
    {
        head0 = Vector3.zero;
        head1 = Vector3.zero;
        if (!_headPositions.TryGetValue(0, out head0)) return false;
        if (!_headPositions.TryGetValue(1, out head1)) return false;
        return true;
    }

}
