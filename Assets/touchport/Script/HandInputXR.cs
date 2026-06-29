using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Meta Quest 手追踪实现，继承 HandInputSource。
/// 交互点：食指指尖（IndexTip）。
/// Scale intent：食指捏合（pinch）。
/// 无手追踪时自动 fallback 到手柄：grip 键代替捏合，手柄位置代替指尖。
/// 挂在场景里任意 GO 上，Inspector 拖入两个 OVRSkeleton，无需额外手球。
/// </summary>
public class HandInputXR : HandInputSource
{
    [Header("OVR Skeletons")]
    [SerializeField] private OVRSkeleton leftSkeleton;
    [SerializeField] private OVRSkeleton rightSkeleton;

    [Header("Tip Colliders（自动创建，也可手动拖入）")]
    [SerializeField] private SphereCollider leftTipCollider;
    [SerializeField] private SphereCollider rightTipCollider;
    [SerializeField] private float tipRadius = 0.015f;   // 1.5cm，指尖大小

    // ── 运行时缓存 ─────────────────────────────────────────────────────────
    private XROrigin    _xrOrigin;
    private InputDevice _leftDevice;
    private InputDevice _rightDevice;

    // 上一帧捏合状态，用于检测 isScaleJustPressed（pinch 开始瞬间）
    private bool _prevLeftPinch;
    private bool _prevRightPinch;

    // ── HandInputSource 接口 ───────────────────────────────────────────────
    public override PortalHandState LeftHand =>
        BuildState(OVRPlugin.Hand.HandLeft,  leftSkeleton,  leftTipCollider,
                   ref _prevLeftPinch,  XRNode.LeftHand,  ref _leftDevice);

    public override PortalHandState RightHand =>
        BuildState(OVRPlugin.Hand.HandRight, rightSkeleton, rightTipCollider,
                   ref _prevRightPinch, XRNode.RightHand, ref _rightDevice);

    // ── 初始化 ─────────────────────────────────────────────────────────────
    private void Start()
    {
        _xrOrigin = FindObjectOfType<XROrigin>();

        if (leftTipCollider  == null) leftTipCollider  = CreateTipCollider("LeftTipCollider");
        if (rightTipCollider == null) rightTipCollider = CreateTipCollider("RightTipCollider");
    }

    private SphereCollider CreateTipCollider(string goName)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform);
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
        var col = go.AddComponent<SphereCollider>();
        col.radius    = tipRadius;
        col.isTrigger = false;
        return col;
    }

    // ── 构建单手状态 ────────────────────────────────────────────────────────
    private PortalHandState BuildState(
        OVRPlugin.Hand ovrHand, OVRSkeleton skeleton, SphereCollider tipCol,
        ref bool prevPinch, XRNode node, ref InputDevice device)
    {
        bool handTracked = TryGetIndexTipPos(ovrHand, skeleton, out Vector3 tipPos);

        if (!handTracked)
            return BuildControllerState(node, ref device, tipCol, ref prevPinch);

        // 指尖碰撞体跟随世界坐标
        if (tipCol != null)
            tipCol.transform.position = tipPos;

        bool pinchNow     = GetPinch(ovrHand);
        bool justPressed  = pinchNow && !prevPinch;
        prevPinch         = pinchNow;

        return new PortalHandState
        {
            isTracked          = true,
            isScaleIntent      = pinchNow,
            isScaleJustPressed = justPressed,
            worldPosition      = tipPos,
            collider           = tipCol
        };
    }

    // ── 手追踪：拿食指指尖世界坐标 ─────────────────────────────────────────
    private bool TryGetIndexTipPos(OVRPlugin.Hand hand, OVRSkeleton skeleton, out Vector3 tipPos)
    {
        tipPos = Vector3.zero;

        var state = new OVRPlugin.HandState();
        if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state)) return false;
        if ((state.Status & OVRPlugin.HandStatus.HandTracked) == 0)          return false;

        // Skeleton 就绪时直接读指尖，精度最高
        if (skeleton != null && skeleton.IsDataValid &&
            skeleton.Bones != null && skeleton.Bones.Count > (int)OVRSkeleton.BoneId.Hand_IndexTip)
        {
            tipPos = skeleton.Bones[(int)OVRSkeleton.BoneId.Hand_IndexTip].Transform.position;
            return true;
        }

        // Skeleton 未就绪时用手根部 fallback（OVR 坐标系 Z 轴取反）
        var p = state.RootPose.Position;
        var trackingPos = new Vector3(p.x, p.y, -p.z);
        tipPos = _xrOrigin != null
            ? _xrOrigin.transform.TransformPoint(trackingPos)
            : trackingPos;
        return true;
    }

    // ── 手柄 fallback ───────────────────────────────────────────────────────
    private PortalHandState BuildControllerState(
        XRNode node, ref InputDevice device, SphereCollider tipCol, ref bool prevGrip)
    {
        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid) return default;

        if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPos))
            return default;

        Vector3 worldPos = _xrOrigin != null
            ? _xrOrigin.transform.TransformPoint(localPos)
            : localPos;

        if (tipCol != null)
            tipCol.transform.position = worldPos;

        bool gripNow     = device.TryGetFeatureValue(CommonUsages.gripButton, out bool g) && g;
        bool justPressed = gripNow && !prevGrip;
        prevGrip         = gripNow;

        return new PortalHandState
        {
            isTracked          = true,
            isScaleIntent      = gripNow,
            isScaleJustPressed = justPressed,
            worldPosition      = worldPos,
            collider           = tipCol
        };
    }

    // ── 工具：读 OVR 捏合状态 ──────────────────────────────────────────────
    private static bool GetPinch(OVRPlugin.Hand hand)
    {
        var state = new OVRPlugin.HandState();
        if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state)) return false;
        if ((state.Status & OVRPlugin.HandStatus.HandTracked) == 0)          return false;
        return (state.Pinches & OVRPlugin.HandFingerPinch.Index) != 0;
    }
}
