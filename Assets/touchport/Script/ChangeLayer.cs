using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 本机视角状态机：我现在"看着哪个世界"（ViewingWorldId），并把 4 个 stencil
/// renderer feature（门外不透明/透明 + 门内不透明/透明）的 LayerMask 切到对应世界层。
/// 只影响本机渲染，不上网。playerId↔worldId↔layer 的换算都走 PlayerWorld。
/// </summary>
public class ChangeLayer : MonoBehaviour
{
    public static ChangeLayer Instance { get; private set; }

    void Awake() => Instance = this;

    [SerializeField] private UniversalRendererData rendererData; // 拖入你的 Renderer Data

    // Renderer Feature 名（与 Stencil URP Asset_Renderer.asset 里的 m_Name 一一对应）。
    // "Side" 指门的哪一侧（门外=ThisSide / 门内=PortalSide），不是哪个世界——世界由 LayerMask 决定。
    public const string FeatureThisSide             = "StencilThisSide";
    public const string FeatureThisSideTransparent  = "StencilThisSideTransparent";
    public const string FeaturePortalSide            = "StencilPortalSide";
    public const string FeaturePortalSideTransparent = "StencilPortalSideTransparent";

    /// <summary>本机当前看着哪个 world。-1 = 尚未设置过（场景默认状态，等价于 world0 视角）。</summary>
    public int ViewingWorldId { get; private set; } = -1;

    /// <summary>门外显示 worldId 的世界，门内显示对面世界，并记录视角状态。</summary>
    public void ViewWorld(int worldId)
    {
        ViewingWorldId = worldId;
        string thisLayer   = PlayerWorld.LayerNameOf(worldId);
        string portalLayer = PlayerWorld.LayerNameOf(PlayerWorld.OtherWorld(worldId));
        ChangeRendererLayerMask(FeatureThisSide, thisLayer);
        ChangeRendererLayerMask(FeatureThisSideTransparent, thisLayer);
        ChangeRendererLayerMask(FeaturePortalSide, portalLayer);
        ChangeRendererLayerMask(FeaturePortalSideTransparent, portalLayer);
    }

    /// <summary>回到自己老家世界的视角（出生状态 / portal 关闭 / share 结束）。</summary>
    public void ViewHomeWorld()
    {
        if (TryGetHomeWorld(out int home)) ViewWorld(home);
    }

    /// <summary>切到对面世界的视角（穿门过去 / UI share 接收方直接切换）。</summary>
    public void ViewOppositeWorld()
    {
        if (TryGetHomeWorld(out int home)) ViewWorld(PlayerWorld.OtherWorld(home));
    }

    private bool TryGetHomeWorld(out int worldId)
    {
        worldId = 0;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient) return false;
        worldId = PlayerWorld.WorldOf(nm.LocalClientId);
        return true;
    }

    // 把指定名字的 Render Objects feature 的 LayerMask 切到指定层。
    // 同名 feature 全部更新（不透明/透明变体可以同名，也可以分开命名各调一次）。
    public void ChangeRendererLayerMask(string featureName, string layerName)
    {
        if (rendererData == null)
        {
            Debug.LogError("[ChangeLayer] rendererData is not assigned!");
            return;
        }

        bool found = false;
        foreach (var feature in rendererData.rendererFeatures)
        {
            if (!feature.isActive || feature is not RenderObjects renderObjectsFeature) continue;
            if (feature.name != featureName) continue;

            var settings = renderObjectsFeature.settings;
            settings.filterSettings.LayerMask = LayerMask.GetMask(layerName);
            renderObjectsFeature.settings = settings;
            rendererData.SetDirty(); // 让 URP 重新序列化
            found = true;
        }

        if (found)
            Debug.Log($"[ChangeLayer] Render Objects [{featureName}] LayerMask -> {layerName}");
        else
            Debug.LogWarning($"[ChangeLayer] Render Objects feature [{featureName}] not found");
    }

    //Change layer for Multiple Objects
    [SerializeField] private List<GameObject> objectsToChange = new List<GameObject>();
    public void ChangeMultipleObjectsLayer(List<GameObject> objectsToChange, LayerMask newLayerMask)
    {
        //把 LayerMask 转换成对应的图层索引（int）
        int targetLayer = Mathf.RoundToInt(Mathf.Log(newLayerMask.value, 2));

        foreach (GameObject obj in objectsToChange)
        {
            if (obj != null)
            {
                ChangeObjectLayerRecursive(obj, targetLayer);
            }
        }
    }

    // //Change layer for single object
    public void ChangeObjectLayer(GameObject obj, LayerMask newLayerMask)
    {
        //把 LayerMask 转换成对应的图层索引（int）
        int targetLayer = Mathf.RoundToInt(Mathf.Log(newLayerMask.value, 2));
        ChangeObjectLayerRecursive(obj, targetLayer);
    }



    private void ChangeObjectLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            ChangeObjectLayerRecursive(child.gameObject, layer);
        }
    }
}
