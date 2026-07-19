using System.Collections.Generic;
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

    [Tooltip("开门宽限期（秒）：trigger 启用后这么久之内碰到的可传递物体视为\"portal 开到了它头上\"，" +
             "不翻转 ownerPlayerId/layer，进忽略名单；先离开 trigger 再回来才正常传递。" +
             "与 GiftDeliveryTrigger.armDelay 同一套语义，要盖过 PortalSpawnAnim 的放大时长（默认 2s）。")]
    [SerializeField] private float armDelay = 2.5f;

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

        // Do NOT set the view here — respect each player's spawn-time state (PortalSpawner calls
        // ViewHomeWorld on spawn). Crossing flips it per-player via ChangeLayer.ViewOppositeWorld/ViewHomeWorld.
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
    // ownerPlayerId（该方法内部会顺带切 layer，见 IObjectXR.cs / IObjectScreen.cs）。
    // DreamGift 的礼物物体（ObjectGift）不走这里，是 GiftPortalDelivery.cs 单独处理的同类逻辑。
    // 开门时就在门体积里被"吞"进来的物体——OnTriggerExit 才把它们移出名单。
    private readonly HashSet<Component> swallowedAtSpawn = new HashSet<Component>();
    private float enabledAt;

    private void OnEnable()
    {
        enabledAt = Time.time;
        swallowedAtSpawn.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        Component transferable = other.GetComponentInParent<IObjectXR>();
        if (transferable == null) transferable = other.GetComponentInParent<IObjectScreen>();
        if (transferable == null) return;

        // 开门宽限期内碰到的物体：是 portal 开在了它所在的位置（含放大动画期间长进去的），
        // 不是有人拿着它穿门——不翻转 owner/layer，等它先出去一次。
        if (Time.time - enabledAt < armDelay)
        {
            if (swallowedAtSpawn.Add(transferable))
                Debug.Log($"[Portal] {transferable.name} was inside the portal when it opened — " +
                          "transfer skipped until it leaves the trigger once.", transferable);
            return;
        }
        if (swallowedAtSpawn.Contains(transferable)) return;   // 开门吞进来的，还没出去过

        if (transferable is IObjectXR xr) { xr.TransferToOther(); return; }
        if (transferable is IObjectScreen screen) screen.TransferToOther();
    }

    private void OnTriggerExit(Collider other)
    {
        Component transferable = other.GetComponentInParent<IObjectXR>();
        if (transferable == null) transferable = other.GetComponentInParent<IObjectScreen>();
        if (transferable != null && swallowedAtSpawn.Remove(transferable))
            Debug.Log($"[Portal] {transferable.name} left the portal — transfer armed.", transferable);
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
        if (inWorldB) changeLayer?.ViewOppositeWorld();   // 穿到对方世界
        else          changeLayer?.ViewHomeWorld();       // 退回自己世界
        (inWorldB ? OnCrossedToWorldB : OnCrossedToWorldA)?.Invoke();
        Debug.Log($"[Portal] view -> {(inWorldB ? "opposite" : "home")} world");
    }
}