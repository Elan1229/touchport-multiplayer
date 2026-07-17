// using Unity.Netcode;
// using UnityEngine;
// using UnityEngine.Events;

// public class PortalDirectionTrigger : MonoBehaviour
// {
//     private ChangeLayer changeLayer;


//     [Header("Network or Local")]
//     [Tooltip(
//         "【正式用 / 打包】勾选：只处理本机玩家（沿父级找 NetworkObject + IsOwner）。\n" +
//         "【本地测试】取消：任意 Rigidbody+Collider 撞进来也会触发。")]
//     [SerializeField] private bool requireNetwork = true;


//     [Header("Events")]
//     public UnityEvent OnCrossedToWorldB;   // 原 OnEnterFront
//     public UnityEvent OnCrossedToWorldA;   // 原 OnEnterBack


//     // 当前世界状态：false = 在A世界（portal显示B），true = 在B世界（portal显示A）
//     private bool inOtherWorld = false;


//     // 记录本次触发的进入方向和来源 Collider
//     private float entryDot = 0f;
//     private Collider trackedHead = null;

//     void Start()
//     {
//         if (changeLayer == null)
//             changeLayer = ChangeLayer.Instance;

       
//     }

//     private void OnTriggerEnter(Collider other)
//     {
//         // 可传递物品（XR / Screen），不影响世界状态
//         var objXR = other.GetComponentInParent<IObjectXR>();
//         if (objXR != null) { objXR.TransferToOther(); return; }

//         var objScreen = other.GetComponentInParent<IObjectScreen>();
//         if (objScreen != null) { objScreen.TransferToOther(); return; }

//         if (!other.CompareTag("Head")) return;

//         if (requireNetwork)
//         {
//             var netObj = other.GetComponentInParent<NetworkObject>();
//             if (netObj == null || !netObj.IsOwner) return;
//         }

//         // 记录进入时的方向（head在portal哪一侧）
//         entryDot = GetDot(other.transform.position);
//         trackedHead = other;

//         // 脑袋碰到portal → 立刻切世界
//         inOtherWorld = !inOtherWorld;
//         ApplyLayers();
//         (inOtherWorld ? OnCrossedToWorldB : OnCrossedToWorldA)?.Invoke();

//         Debug.Log($"[Portal] Enter dot={entryDot:F3} → 现在在 {(inOtherWorld ? "B" : "A")} 世界");
//     }

//     private void OnTriggerExit(Collider other)
//     {
//         if (other != trackedHead) return;
//         trackedHead = null;

//         float exitDot = GetDot(other.transform.position);

//         // 出去方向和进来方向同侧 → 退回来了，撤销切换
//         bool retreated = Mathf.Sign(exitDot) == Mathf.Sign(entryDot);

//         if (retreated)
//         {
//             inOtherWorld = !inOtherWorld;
//             ApplyLayers();
//             (inOtherWorld ? OnCrossedToWorldB : OnCrossedToWorldA)?.Invoke();
//             Debug.Log($"[Portal] 退回来了 → 恢复到 {(inOtherWorld ? "B" : "A")} 世界");
//         }
//         else
//         {
//             Debug.Log($"[Portal] 穿过去了 → 保持 {(inOtherWorld ? "B" : "A")} 世界");
//         }
//     }

//     // portal中心到head的方向，与portal正面做点积
//     private float GetDot(Vector3 otherPos)
//         => Vector3.Dot((otherPos - transform.position).normalized, transform.forward);

//     private void ApplyLayers()
//     {
//         if (changeLayer == null) changeLayer = ChangeLayer.Instance;
//         if (changeLayer == null) { Debug.LogWarning("[Portal] ChangeLayer.Instance is null"); return; }

//         if (inOtherWorld)
//         {
//             // 身在B世界，portal窗口显示A
//             changeLayer.ChangeRendererLayerMask("StencilThisWorld", "layer1");
//             changeLayer.ChangeRendererLayerMask("StencilPortalWorld", "layer0");
//         }
//         else
//         {
//             // 身在A世界，portal窗口显示B
//             changeLayer.ChangeRendererLayerMask("StencilThisWorld", "layer0");
//             changeLayer.ChangeRendererLayerMask("StencilPortalWorld", "layer1");
//         }
//     }
// }



using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class PortalDirectionTrigger : MonoBehaviour
{
    [Header("References")]
    [Tooltip("CenterEyeAnchor 上的 Camera")]
    [SerializeField] private Camera headCamera;
    [Tooltip("portal 面片的 Renderer，进入区域时隐藏")]
    [SerializeField] private Renderer portalRenderer;

    [Header("Network")]
    [SerializeField] private bool requireNetwork = true;

    [Header("Threshold")]
    [Tooltip("切换距离，建议和 nearClipPlane 一致，Quest 约 0.1m")]
    [SerializeField] private float switchDistance = 0.1f;

    [Tooltip("可见开口参照物（拖 PortalRender 进来）。开口的实际世界大小 = 它的世界尺寸；空则退回 portalRenderer / 自身。")]
    [SerializeField] private Transform openingReference;

    [Tooltip("开口范围：相对上面参照物(PortalRender)网格的本地半尺寸。0.5=正好等于可见开口" +
             "（InverseTransformPoint 已自动含它的缩放/非等比 + 父级缩放）。调大=更宽松。")]
    [SerializeField] private Vector2 openingHalfSize = new Vector2(0.5f, 0.5f);

    [Header("Events")]
    public UnityEvent OnCrossedToWorldB;
    public UnityEvent OnCrossedToWorldA;

    private ChangeLayer changeLayer;
    private bool inWorldB = false;

    // 状态机
    private bool insideZone = false;
    private bool enteredFromPositiveSide;
    private float lastDist;
    private bool initialized = false;

    void Start()
    {
        changeLayer = ChangeLayer.Instance;
        ResolveHeadCamera();

        // Do NOT set stencil here — respect each player's spawn-time config (PortalSpawner sets
        // Player1 reversed). Crossing flips it per-player via ChangeLayer.SwitchStencilLocal/Reset.
    }

    // 每帧的"人头"穿门判断：0.1 平面双线状态机 + 开口范围。只影响本机玩家视角的 stencil 世界切换，
    // 跟下面 OnTriggerEnter 处理的物体传递是两套完全独立的机制。
    void Update()
    {
        if (!initialized) ResolveHeadCamera(); // 玩家可能比 portal 晚生成/晚确定 owner，持续重试直到拿到本机摄像机
        if (!initialized || headCamera == null || changeLayer == null) return;

        float dist = GetSignedDist();
        float t = switchDistance;

        if (!insideZone)
        {
            // 监听两条线，哪条进来都切，并记住是哪条；但只在 portal 开口范围内才算
            if (lastDist >= t && dist < t && HeadInsideOpening())
            {
                insideZone = true;
                enteredFromPositiveSide = true;
                FlipWorld();
            }
            else if (lastDist <= -t && dist > -t && HeadInsideOpening())
            {
                insideZone = true;
                enteredFromPositiveSide = false;
                FlipWorld();
            }
        }
        else
        {
            // insideZone 该重置就重置，不依赖开口范围（避免卡在 true 出不来，portalRenderer 一直被藏着）；
            // 但"退回→切世界"这个动作，只有跨出去那一刻仍在开口范围内才算数——
            // 否则曾经正经进过门一次之后，只要贴着这条 0.1 带子走到远处再跨出去，
            // 也会被当成"退回"误触发一次世界切换。
            if (enteredFromPositiveSide)
            {
                if (lastDist < t && dist >= t)        // 同一条线出去 → 退回
                {
                    insideZone = false;
                    if (HeadInsideOpening()) FlipWorld(); // → 切回
                }
                else if (lastDist > -t && dist <= -t) // 另一条线出去 → 穿过 → 忽略
                {
                    insideZone = false;
                }
            }
            else
            {
                if (lastDist > -t && dist <= -t)      // 同一条线出去 → 退回
                {
                    insideZone = false;
                    if (HeadInsideOpening()) FlipWorld(); // → 切回
                }
                else if (lastDist < t && dist >= t)   // 另一条线出去 → 穿过 → 忽略
                {
                    insideZone = false;
                }
            }
        }

        // 在区域内隐藏面片（near clip 范围内反正看不见，隐藏更干净）
        if (portalRenderer != null)
            portalRenderer.enabled = !insideZone;

        lastDist = dist;
    }

    // 物体（IObjectXR/IObjectScreen 挂的花、球等可抓取道具）穿门传递：走普通 Unity Trigger 碰撞回调，
    // 不走上面 Update() 那套 0.1 平面/开口范围判断——物体没有近裁剪面视觉穿帮的问题，不需要那么精细的
    // 双线状态机，进了 PortalTrigger 的碰撞体就算数，直接调对应接口的 TransferToOther() 翻转
    // gameOwnerId（该方法内部会顺带切 layer，见 IObjectXR.cs / IObjectScreen.cs）。
    // DreamGift 的礼物物体（ObjectGift）不走这里，是 GiftPortalDelivery.cs 单独处理的同类逻辑。
    private void OnTriggerEnter(Collider other)
    {
        var objXR = other.GetComponentInParent<IObjectXR>();
        if (objXR != null) { objXR.TransferToOther(); return; }

        var objScreen = other.GetComponentInParent<IObjectScreen>();
        if (objScreen != null) { objScreen.TransferToOther(); return; }
    }

    // 优先用 Inspector 手动指定的 headCamera（XR 场景就是这么配的，不受影响）。
    // 桌面场景没配的话不依赖 Camera.main——两个玩家的相机都带 MainCamera tag，
    // Camera.main 的 tag 缓存偶尔会指错（或者玩家还没生成完就拿到 null 然后再也不重试），
    // 一旦指错，0.1 平面 + 开口范围判断的就是别的物体的位置，会出现"远处的东西穿过也触发"。
    // 改成直接按 NetworkObject.IsOwner 找本机玩家身上的摄像机，且没找到就每帧继续找，
    // 不会像原来那样只在 Start() 试一次就定死。
    private void ResolveHeadCamera()
    {
        if (headCamera == null)
        {
            foreach (var no in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                if (!no.IsSpawned || !no.IsPlayerObject || !no.IsOwner) continue;
                headCamera = no.GetComponentInChildren<Camera>(true);
                if (headCamera != null) break;
            }
        }
        if (headCamera == null) headCamera = Camera.main; // 兜底

        if (headCamera != null)
        {
            lastDist = GetSignedDist();
            initialized = true;
        }
    }

    private float GetSignedDist()
        => Vector3.Dot(headCamera.transform.position - transform.position, transform.forward);

    private bool openingWarned = false;

    // 头投影到可见开口(PortalRender)本地平面后，是否落在开口矩形内。
    // 用 PortalRender 作参照：InverseTransformPoint 已包含它的全部缩放（含 y 非等比 + 父级缩放），
    // 所以 openingHalfSize=0.5 就正好等于可见开口，不用手动乘 scale。
    // 注意：不再兜底到 transform（自身）——万一以后自身物体的尺寸跟可视开口对不上，
    // 会导致远处穿过 0.1 平面也被误判为"在开口范围内"。没配好参照物就宁可不触发，也不要在远处误触发。
    private bool HeadInsideOpening()
    {
        Transform opening = openingReference != null ? openingReference : portalRenderer?.transform;
        if (opening == null)
        {
            if (!openingWarned)
            {
                Debug.LogWarning("[Portal] openingReference 未设置（portalRenderer 也没有），跳过开口范围检测，" +
                                  "穿越不会触发。请在 Inspector 里把可见 portal 面片拖进 openingReference。");
                openingWarned = true;
            }
            return false;
        }

        Vector3 local = opening.InverseTransformPoint(headCamera.transform.position);
        return Mathf.Abs(local.x) <= openingHalfSize.x && Mathf.Abs(local.y) <= openingHalfSize.y;
    }

    private void FlipWorld()
    {
        inWorldB = !inWorldB;
        if (inWorldB) changeLayer?.SwitchStencilLocal();   // 穿到对方世界（按 LocalClientId 翻转）
        else          changeLayer?.ResetStencilLocal();     // 退回自己世界
        (inWorldB ? OnCrossedToWorldB : OnCrossedToWorldA)?.Invoke();
        Debug.Log($"[Portal] 切换到 {(inWorldB ? "B" : "A")} 世界");
    }

    private void ApplyLayers()
    {
        if (changeLayer == null) return;
        if (inWorldB)
        {
            changeLayer.ChangeRendererLayerMask("StencilThisWorld", "layer1");
            changeLayer.ChangeRendererLayerMask("StencilPortalWorld", "layer0");
        }
        else
        {
            changeLayer.ChangeRendererLayerMask("StencilThisWorld", "layer0");
            changeLayer.ChangeRendererLayerMask("StencilPortalWorld", "layer1");
        }
    }
}