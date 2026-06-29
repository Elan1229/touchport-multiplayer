using UnityEngine;

public class PortalInteraction : MonoBehaviour
{
    // ── 状态机 ──────────────────────────────────────────────────────────────

    enum PortalMode
    {
        Idle,
        WaitingSinglePush,
        SinglePushRotate,
        WaitingDualMove,
        DualPushMove,
        Scaling
    }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Input")]
    [SerializeField] private HandInputSource input;

    [Header("Scale Limits")]
    [SerializeField] private float minScaleX = 0.3f;
    [SerializeField] private float minScaleY = 0.4f;
    [SerializeField] private float minScaleZ = 0.09f;

    [Header("Settings")]
    [SerializeField] private float faceEnterDuration  = 2f;
    [SerializeField] private float rotateSensitivity  = 150f;
    [SerializeField] private float moveSensitivity    = 1f;
    [SerializeField] private float pushThreshold      = 0f;     // 推的最小增量
    [SerializeField] private float retractThreshold   = 0.05f;  // 缩回多少触发取消

    // ── 子Collider ───────────────────────────────────────────────────────────

    private Collider edgeTop, edgeBottom, edgeLeft, edgeRight;
    private Collider faceFront, faceBack;

    // ── 运行时状态 ───────────────────────────────────────────────────────────

    private PortalMode mode = PortalMode.Idle;

    // face检测结果（每帧刷新）
    private int  leftFaceSide,  rightFaceSide;  // +1=Front, -1=Back, 0=none
    private bool leftInFace,    rightInFace;

    // edge检测结果（每帧刷新）
    private Collider leftTouchedEdge, rightTouchedEdge;

    // waiting计时
    private float waitTimer;

    // 推动基准
    private Vector3 leftEntryPos, rightEntryPos;
    private int     activeFaceSide;

    // dual move用
    private Vector3 lastHandCenter;

    // scale grab snapshot
    private Vector3  leftGrabLocalPos,  rightGrabLocalPos;
    private Vector3  leftGrabScale,     rightGrabScale;
    private bool     leftGrabbing,      rightGrabbing;
    private Collider leftGrabbedEdge,   rightGrabbedEdge;  // grab时锁定的edge

    // 每帧input
    private PortalHandState left, right;

    // ── Start ────────────────────────────────────────────────────────────────

    void Start()
    {
        edgeTop    = FindChild("EdgeTop");
        edgeBottom = FindChild("EdgeBottom");
        edgeLeft   = FindChild("EdgeLeft");
        edgeRight  = FindChild("EdgeRight");
        faceFront  = FindChild("FaceFront");
        faceBack   = FindChild("FaceBack");
    }

    Collider FindChild(string n)
    {
        var t = transform.Find(n);
        if (t == null) { Debug.LogWarning($"[Portal] 找不到 '{n}'"); return null; }
        var c = t.GetComponent<Collider>();
        if (c == null) Debug.LogWarning($"[Portal] '{n}' 没有Collider");
        return c;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (input == null) return;
        left  = input.LeftHand;
        right = input.RightHand;

        UpdateSensors();
        RunStateMachine();
    }

    // ── 传感器层（每帧刷新原始检测数据）────────────────────────────────────

 void UpdateSensors()
{
    int prevLeftFace  = leftFaceSide;
    int prevRightFace = rightFaceSide;
    Collider prevLeftEdge  = leftTouchedEdge;
    Collider prevRightEdge = rightTouchedEdge;

    leftFaceSide  = GetFaceSide(left);
    rightFaceSide = GetFaceSide(right);
    leftInFace    = leftFaceSide  != 0;
    rightInFace   = rightFaceSide != 0;

    leftTouchedEdge  = (left.isTracked  && left.collider  != null) ? GetTouchedEdge(left.collider)  : null;
    rightTouchedEdge = (right.isTracked && right.collider != null) ? GetTouchedEdge(right.collider) : null;

    if (leftFaceSide != prevLeftFace)
    {
        string name = leftFaceSide ==  1 ? "FaceFront" :
                      leftFaceSide == -1 ? "FaceBack"  : "none";
        Debug.Log($"[Portal] LeftHand face → {name} (side={leftFaceSide})");
    }
    if (rightFaceSide != prevRightFace)
    {
        string name = rightFaceSide ==  1 ? "FaceFront" :
                      rightFaceSide == -1 ? "FaceBack"  : "none";
        Debug.Log($"[Portal] RightHand face → {name} (side={rightFaceSide})");
    }
    if (leftTouchedEdge != prevLeftEdge)
        Debug.Log($"[Portal] LeftHand edge → {(leftTouchedEdge != null ? leftTouchedEdge.name : "none")}");
    if (rightTouchedEdge != prevRightEdge)
        Debug.Log($"[Portal] RightHand edge → {(rightTouchedEdge != null ? rightTouchedEdge.name : "none")}");
}

    int GetFaceSide(PortalHandState hand)
    {
        if (!hand.isTracked || hand.collider == null) return 0;
        if (faceFront != null && IsOverlapping(hand.collider, faceFront)) return  1;
        if (faceBack  != null && IsOverlapping(hand.collider, faceBack))  return -1;
        return 0;
    }

    Collider GetTouchedEdge(Collider handCol)
    {
        foreach (var edge in new[] { edgeTop, edgeBottom, edgeLeft, edgeRight })
            if (edge != null && IsOverlapping(handCol, edge)) return edge;
        return null;
    }

    bool IsOverlapping(Collider a, Collider b)
    {
        return Physics.ComputePenetration(
            a, a.transform.position, a.transform.rotation,
            b, b.transform.position, b.transform.rotation,
            out _, out _);
    }

    // ── 状态机 ───────────────────────────────────────────────────────────────

    void RunStateMachine()
    {
        PortalMode prev = mode;

        switch (mode)
        {
            case PortalMode.Idle:             UpdateIdle();            break;
            case PortalMode.WaitingSinglePush: UpdateWaitingSingle();  break;
            case PortalMode.SinglePushRotate:  UpdateSingleRotate();   break;
            case PortalMode.WaitingDualMove:   UpdateWaitingDual();    break;
            case PortalMode.DualPushMove:      UpdateDualMove();       break;
            case PortalMode.Scaling:           UpdateScaling();        break;
        }

        if (mode != prev) Debug.Log($"[Portal] {prev} → {mode}");
    }

    // ── Idle ─────────────────────────────────────────────────────────────────

    void UpdateIdle()
    {
        // Scale优先
        if ((left.isScaleIntent  && leftTouchedEdge  != null) ||
            (right.isScaleIntent && rightTouchedEdge != null))
        {
            EnterScaling();
            return;
        }

        // 双手同面 → WaitingDualMove
        if (CanStartDualMove())
        {
            EnterWaiting(PortalMode.WaitingDualMove);
            return;
        }

        // 单手在face → WaitingSinglePush
        if (leftInFace || rightInFace)
        {
            EnterWaiting(PortalMode.WaitingSinglePush);
            return;
        }
    }

    // ── Waiting（Single / Dual共用计时逻辑）─────────────────────────────────

    void EnterWaiting(PortalMode waitMode)
    {
        mode      = waitMode;
        waitTimer = 0f;
    }

    void UpdateWaitingSingle()
    {
        // 如果变成双手同面，升级到WaitingDualMove
        if (CanStartDualMove()) { EnterWaiting(PortalMode.WaitingDualMove); return; }

        // 手离开face → Idle
        if (!leftInFace && !rightInFace) { EnterIdle(); return; }

        waitTimer += Time.deltaTime;
        if (waitTimer >= faceEnterDuration)
        {
            // 记录入场位置，进入旋转
            bool useLeft = leftInFace;
            activeFaceSide   = useLeft ? leftFaceSide : rightFaceSide;
            leftEntryPos     = left.worldPosition;
            rightEntryPos    = right.worldPosition;
            mode             = PortalMode.SinglePushRotate;
        }
    }

    void UpdateWaitingDual()
    {
        // 不再满足双手同面 → 退回WaitingSingle或Idle
        if (!CanStartDualMove())
        {
            if (leftInFace || rightInFace) EnterWaiting(PortalMode.WaitingSinglePush);
            else EnterIdle();
            return;
        }

        waitTimer += Time.deltaTime;
        if (waitTimer >= faceEnterDuration)
        {
            activeFaceSide = leftFaceSide; // 此时left==right
            lastHandCenter = (left.worldPosition + right.worldPosition) * 0.5f;
            leftEntryPos   = left.worldPosition;
            rightEntryPos  = right.worldPosition;
            mode           = PortalMode.DualPushMove;
        }
    }

    // ── SinglePushRotate ─────────────────────────────────────────────────────

    void UpdateSingleRotate()
    {
        // 哪只手在face
        bool useLeft = leftInFace && (!rightInFace || leftFaceSide == activeFaceSide);

        if (!leftInFace && !rightInFace) { EnterIdle(); return; }

        // 双手同面升级
        if (CanStartDualMove())
        {
            lastHandCenter = (left.worldPosition + right.worldPosition) * 0.5f;
            leftEntryPos   = left.worldPosition;
            rightEntryPos  = right.worldPosition;
            mode           = PortalMode.DualPushMove;
            return;
        }

        PortalHandState activeHand    = useLeft ? left  : right;
        Vector3         activeEntry   = useLeft ? leftEntryPos : rightEntryPos;

        Vector3 pushDir    = GetPushDirection(activeFaceSide);
        Vector3 delta      = activeHand.worldPosition - (useLeft ? leftEntryPos : rightEntryPos);
        float   totalPush  = Vector3.Dot(activeHand.worldPosition - activeEntry, pushDir);

        // 缩回超过threshold → 取消
        if (totalPush < -retractThreshold) { EnterIdle(); return; }

        // 每帧增量
        Vector3 frameDelta   = activeHand.worldPosition - (useLeft ? leftEntryPos : rightEntryPos);
        float   framePush    = Vector3.Dot(useLeft
                                   ? (left.worldPosition  - leftPrevPos)
                                   : (right.worldPosition - rightPrevPos),
                               pushDir);

        if (framePush > pushThreshold)
        {
            float handLocalX = transform.InverseTransformPoint(activeHand.worldPosition).x;
            float angle = framePush * rotateSensitivity
                          * Mathf.Sign(handLocalX)
                          * activeFaceSide;
            transform.Rotate(Vector3.up, angle, Space.World);
        }

        // per-frame delta 已由 leftPrevPos/rightPrevPos（LateUpdate）维护，entry pos 不动
    }

    // ── DualPushMove ─────────────────────────────────────────────────────────

    void UpdateDualMove()
    {
        if (!CanStartDualMove()) { EnterIdle(); return; }

        Vector3 centerNow   = (left.worldPosition + right.worldPosition) * 0.5f;
        Vector3 centerDelta = centerNow - lastHandCenter;
        lastHandCenter      = centerNow;

        // 缩回检测：任一手比入场点退回超过 threshold → 退出
        Vector3 pushDir   = GetPushDirection(activeFaceSide);
        float   leftTotal  = Vector3.Dot(left.worldPosition  - leftEntryPos,  pushDir);
        float   rightTotal = Vector3.Dot(right.worldPosition - rightEntryPos, pushDir);
        if (leftTotal < -retractThreshold || rightTotal < -retractThreshold)
        {
            EnterIdle(); return;
        }

        // portal 沿自身 local Z 方向平移，量由双手中心 delta 在该方向的投影决定
        float along = Vector3.Dot(centerDelta, transform.forward);
        transform.position += transform.forward * along * moveSensitivity;
    }

    // ── Scaling ──────────────────────────────────────────────────────────────

    void EnterScaling()
    {
        mode = PortalMode.Scaling;
        leftGrabbing      = false;
        rightGrabbing     = false;
        leftGrabbedEdge   = null;
        rightGrabbedEdge  = null;
    }

    void UpdateScaling()
    {
        // 退出条件：key 松开且没有新的 edge 接触
        // 一旦 grab 住，只看 key 是否还按着；不要求手一直贴着 edge
        bool leftActive  = left.isScaleIntent  && (leftGrabbing  || leftTouchedEdge  != null);
        bool rightActive = right.isScaleIntent && (rightGrabbing || rightTouchedEdge != null);

        if (!leftActive && !rightActive) { EnterIdle(); return; }

        if (leftActive)
        {
            if (!leftGrabbing)
            {
                leftGrabLocalPos = transform.InverseTransformPoint(left.worldPosition);
                leftGrabScale    = transform.localScale;
                leftGrabbedEdge  = leftTouchedEdge;   // 锁定触发的 edge
                leftGrabbing     = true;
            }
            if (leftGrabbedEdge != null)
                ApplyScale(left.worldPosition, leftGrabbedEdge, leftGrabLocalPos, leftGrabScale);
        }
        else leftGrabbing = false;

        if (rightActive)
        {
            if (!rightGrabbing)
            {
                rightGrabLocalPos = transform.InverseTransformPoint(right.worldPosition);
                rightGrabScale    = transform.localScale;
                rightGrabbedEdge  = rightTouchedEdge;
                rightGrabbing     = true;
            }
            if (rightGrabbedEdge != null)
                ApplyScale(right.worldPosition, rightGrabbedEdge, rightGrabLocalPos, rightGrabScale);
        }
        else rightGrabbing = false;
    }

    void ApplyScale(Vector3 worldPos, Collider edge, Vector3 grabLocalPos, Vector3 grabScale)
    {
        if (edge == edgeLeft || edge == edgeRight)
        {
            float sign    = (edge == edgeRight) ? 1f : -1f;
            float distant = (transform.InverseTransformPoint(worldPos).x - grabLocalPos.x) * sign;
            if (distant < 0f)
                distant = Mathf.Max(distant, -Mathf.Min(
                    (grabScale.x - minScaleX) / 2f,
                     grabScale.y - minScaleY,
                     grabScale.z - minScaleZ));
            transform.localScale = new Vector3(
                Mathf.Max(grabScale.x + 2f * distant, minScaleX),
                Mathf.Max(grabScale.y +      distant, minScaleY),
                Mathf.Max(grabScale.z +      distant, minScaleZ));
        }
        else if (edge == edgeTop || edge == edgeBottom)
        {
            float sign    = (edge == edgeTop) ? 1f : -1f;
            float distant = (worldPos.y - transform.TransformPoint(grabLocalPos).y) * sign;
            if (distant < 0f)
                distant = Mathf.Max(distant, -Mathf.Min(
                     grabScale.x - minScaleX,
                    (grabScale.y - minScaleY) / 2f,
                     grabScale.z - minScaleZ));
            transform.localScale = new Vector3(
                Mathf.Max(grabScale.x +      distant, minScaleX),
                Mathf.Max(grabScale.y + 2f * distant, minScaleY),
                Mathf.Max(grabScale.z +      distant, minScaleZ));
        }
    }

    // ── 工具方法 ─────────────────────────────────────────────────────────────

    void EnterIdle()
    {
        mode          = PortalMode.Idle;
        waitTimer     = 0f;
        leftGrabbing  = false;
        rightGrabbing = false;
    }

    bool CanStartDualMove()
    {
        return leftInFace && rightInFace && leftFaceSide == rightFaceSide;
    }

    Vector3 GetPushDirection(int faceSide)
    {
        // Front(+1)往里推是 -forward，Back(-1)往里推是 +forward
        return -faceSide * transform.forward;
    }

    // prev pos用于每帧delta
    private Vector3 leftPrevPos, rightPrevPos;

    void LateUpdate()
    {
        if (left.isTracked)  leftPrevPos  = left.worldPosition;
        if (right.isTracked) rightPrevPos = right.worldPosition;
    }

    // ── Debug ────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 500, 240),
            $"Mode: {mode}\n" +
            $"L face: {leftFaceSide} | R face: {rightFaceSide}\n" +
            $"L edge: {(leftTouchedEdge  != null ? leftTouchedEdge.name  : "none")}\n" +
            $"R edge: {(rightTouchedEdge != null ? rightTouchedEdge.name : "none")}\n" +
            $"WaitTimer: {waitTimer:F2}\n" +
            $"Scale: {transform.localScale}\n" +
            $"Rot Y: {transform.eulerAngles.y:F1}");
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        foreach (var e in new[] { edgeTop, edgeBottom, edgeLeft, edgeRight })
        {
            if (e == null) continue;
            var box = e as BoxCollider;
            if (box == null) continue;
            Gizmos.matrix = e.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
        Gizmos.color = Color.cyan;
        foreach (var f in new[] { faceFront, faceBack })
        {
            if (f == null) continue;
            var box = f as BoxCollider;
            if (box == null) continue;
            Gizmos.matrix = f.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}