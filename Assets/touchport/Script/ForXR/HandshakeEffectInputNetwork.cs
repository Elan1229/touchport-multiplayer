using UnityEngine;

/// <summary>
/// HandshakeEffectInput 的联机版：不读本机 OVRSkeleton，改从 HandsManager 已经复制到全端的
/// 手部 NetworkVariable 里取「两个不同玩家」的手，喂给同样的两个目标 Transform。
/// HandshakeEffect 本身不用改——它只认那两个 Transform，距离阈值也在它自己身上配。
///
/// 本地版喂的是「本机左手 + 本机右手」（单机自测用），这版喂的是「player0 的手 + player1 的手」，
/// 这才是真正要的跨玩家握手。两个脚本别同时挂在同一对 target 上，会互相打架。
///
/// 数据优先级：手追踪的指尖关键点（KP*）→ 退回手部根位置（Debug*，手柄模式也有值）。
/// 任一侧无效（没追踪到 / 还没上报）时把两个 target 拉到极远处，HandshakeEffect 的距离判定
/// 自然不成立，特效自动熄——不需要在 HandshakeEffect 里加任何开关。
/// </summary>
public class HandshakeEffectInputNetwork : MonoBehaviour
{
    [Tooltip("喂给 HandshakeEffect 的 Hand A —— 每帧设为 player0（clientId 0）的手的位置")]
    [SerializeField] private Transform handATarget;
    [Tooltip("喂给 HandshakeEffect 的 Hand B —— 每帧设为 player1（clientId 1）的手的位置")]
    [SerializeField] private Transform handBTarget;

    [Tooltip("不勾 = 两人的左手（默认）。勾上 = 两人的右手。\n" +
             "默认走左手是因为右手经常拿着手柄，手指关键点拿不到。\n" +
             "左右手都要就再挂一份本组件 + 另一对 target + 另一个 HandshakeEffect。")]
    [SerializeField] private bool useRightHand = false;

    [Tooltip("任一侧手无效时把两个 target 分开这么远，让 HandshakeEffect 的距离判定必然不成立。")]
    [SerializeField] private float parkDistance = 10000f;

    // [HsDiag] 每侧这一帧的取数结果，只用于下面那条节流日志
    private readonly string[] _why = { "?", "?" };
    private float _nextDiagTime;

    private void LateUpdate()
    {
        var hm = HandsManager.Instance;
        if (hm == null) { Park(); Diag("HandsManager.Instance == null"); return; }

        // 两个人的手都得有效才有「握手」可言，缺一不可
        bool ok0 = TryGetHand(hm, 0, out Vector3 p0);
        bool ok1 = TryGetHand(hm, 1, out Vector3 p1);

        if (!ok0 || !ok1)
        {
            Park();
            Diag($"parked — p0:{(ok0 ? "ok" : "FAIL")}({_why[0]}) p1:{(ok1 ? "ok" : "FAIL")}({_why[1]})");
            return;
        }

        if (handATarget != null) handATarget.position = p0;
        if (handBTarget != null) handBTarget.position = p1;

        Diag($"live — p0={p0:F2}({_why[0]}) p1={p1:F2}({_why[1]}) dist={Vector3.Distance(p0, p1):F2}");
    }

    // 3 秒一条，够看清「到底是哪一侧没数据、走的哪条取数路径、两手距离多少」。
    // 特效不出来时第一个看这个：dist 比 HandshakeEffect 的 handshakeDistance 大就是没靠够近，
    // FAIL 就是那一侧压根没数据。
    private void Diag(string msg)
    {
        if (Time.time < _nextDiagTime) return;
        _nextDiagTime = Time.time + 3f;
        Debug.Log($"[HsDiag] {(useRightHand ? "RIGHT" : "LEFT")} {msg}");
    }

    // player: 0 = clientId 0，1 = clientId 1
    private bool TryGetHand(HandsManager hm, int player, out Vector3 pos)
    {
        pos = Vector3.zero;
        bool left = !useRightHand;

        // mode == Off 表示这只手当前没追踪到，NetworkVariable 里留的是过期位置，不能用
        int mode = player == 0
            ? (left ? hm.Debug0LeftMode.Value : hm.Debug0RightMode.Value)
            : (left ? hm.Debug1LeftMode.Value : hm.Debug1RightMode.Value);
        if (mode == (int)InputMode.Off) { _why[player] = "mode=Off"; return false; }

        // 手追踪模式：用食指指尖。本地版取的是 Hand_Index2（中间关节），但 HandKeyPoints
        // 只同步了 7 个点、没有中间关节，指尖是其中最接近的一个。
        // wrist 落在原点 = 这份 KP 根本没填（手柄模式下 LocalHandsReporter 传的是 default）。
        var kp = player == 0
            ? (left ? hm.KP0L.Value : hm.KP0R.Value)
            : (left ? hm.KP1L.Value : hm.KP1R.Value);

        if (mode == (int)InputMode.Hand && !IsUnset(kp.wrist))
        {
            pos = kp.indexTip;
            _why[player] = "indexTip";
        }
        else
        {
            // 手柄模式（或 KP 缺失）：退回手部根位置，一样是全端复制的
            pos = player == 0
                ? (left ? hm.Debug0Left.Value : hm.Debug0Right.Value)
                : (left ? hm.Debug1Left.Value : hm.Debug1Right.Value);
            _why[player] = mode == (int)InputMode.Hand ? "root(noKP)" : "root(controller)";
        }

        // 统一兜底：位置落在原点 = 这个 NetworkVariable 还没被写过（默认值就是 zero），
        // 而不是「手真的在原点」。不拦的话会在世界原点凭空长出一个握手特效，人还能凑过去
        // 跟它互动，非常出戏。任一侧取不到，上面 LateUpdate 就会 Park 掉整对 target。
        return !IsUnset(pos);
    }

    // 距原点 1mm 以内一律当成「没有数据」。被追踪的真手坐标带浮点噪声，不可能精确落进
    // 这个球里；就算玩家真站在世界原点上、某帧碰巧落进去了，也只是特效闪一帧，无害。
    private static bool IsUnset(Vector3 p) => p.sqrMagnitude < 1e-6f;

    private void Park()
    {
        if (handATarget != null) handATarget.position = Vector3.right * parkDistance;
        if (handBTarget != null) handBTarget.position = Vector3.left  * parkDistance;
    }
}
