using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class PortalDirectionTrigger : MonoBehaviour
{
    public ChangeLayer ChangeLayer;

    [Header("联机 / 本地测试")]
    [Tooltip(
        "【正式用 / 打包】勾选：只处理本机玩家（沿父级找 NetworkObject + IsOwner）。\n" +
        "【测 Trigger 用】取消：无 NetworkManager 的空场景里，任意 Rigidbody+Collider 撞进来也会改 Stencil；测完务必勾回去。")]
    [SerializeField] private bool requireLocalNetworkOwner = true;

    [Header("Events")]
    public UnityEvent OnEnterFront;
    public UnityEvent OnEnterBack;

    void Awake()
    {
        if (ChangeLayer == null)
            ChangeLayer = FindFirstObjectByType<ChangeLayer>();
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Collide!");
        if (requireLocalNetworkOwner)
        {
            // 正式联机：Collider 可在子物体，NetworkObject 在父级时用 InParent
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj == null || !netObj.IsOwner)
                return;
        }
        else
        {
            // 仅本地测物理/Trigger：不校验 NGO；谁撞进来都改 Layer（勿用于正式联机）
            Debug.Log($"[PortalTrigger 调试] 进入: {other.name}（requireLocalNetworkOwner=false）", this);
        }

        Vector3 dir = (other.transform.position - transform.position).normalized;
        float dot = Vector3.Dot(dir, transform.forward);

        if (dot > 0)
        {
            ChangeLayer.ChangeRendererLayerMask("StencilThisWorld", "layer0");
            ChangeLayer.ChangeRendererLayerMask("StencilPortalWorld", "layer1");
        }
        else if (dot < 0)
        {
            ChangeLayer.ChangeRendererLayerMask("StencilThisWorld", "layer1");
            ChangeLayer.ChangeRendererLayerMask("StencilPortalWorld", "layer0");
        }
    }
}
