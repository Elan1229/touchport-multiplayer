using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class ChangeLayer : MonoBehaviour
{
    public static ChangeLayer Instance { get; private set; }

    void Awake() => Instance = this;

    // 共享结束 → 恢复各自世界。
    // 握手再次握手、UI 的 Stop Sharing、拼词任务自动结束——三条路最终都会把
    // IsShared 广播成 false，所以统一在这里响应一次，下游不必各自重置。
    void OnEnable()  => SharedState.OnSharedChanged += OnSharedChanged;
    void OnDisable() => SharedState.OnSharedChanged -= OnSharedChanged;

    private void OnSharedChanged(bool isShared)
    {
        if (!isShared) ResetStencilLocal();
    }

    [SerializeField] private UniversalRendererData rendererData; // 拖入你的 Renderer Data
    //[SerializeField] private LayerMask newLayerMask; // 在 Inspector 中选择新 Layer
    //[SerializeField] private string featureName = "StencilThisWorld";
    


    //Change the layer mask of the Renderer- Render Objects
    public void ChangeRendererLayerMask(string featureName, string layerName)
    {


        ////将 Layer 名称转换为 LayerMask；也可以不做，直接下面GetMask（string）
        //LayerMask newLayerMask = LayerMask.GetMask(layerName);


        if (rendererData == null)
        {
            Debug.LogError("Renderer Data 未赋值！");
            return;
        }

        // go over all Renderer Features
        foreach (var feature in rendererData.rendererFeatures)
        {
            if (feature.isActive && feature is RenderObjects renderObjectsFeature)
            {
                // 方式1：通过 Feature 名称匹配（推荐）
                if (feature.name == featureName)
                {
                    //Debug.Log($"Found {featureName} !");

                    var settings = renderObjectsFeature.settings;
                    settings.filterSettings.LayerMask = LayerMask.GetMask(layerName);
                    renderObjectsFeature.settings = settings;

                    //you can put in multiple layers
                    //settings.filterSettings.LayerMask = LayerMask.GetMask("UI", "Player", "Enemy");


                    // 重要：标记为脏，让 URP 重新序列化
                    rendererData.SetDirty();
                    Debug.Log($"Render Objects [{featureName}] 的 LayerMask 已改为: {layerName}");
                    return;
                }

             
            }
        }

        Debug.LogWarning($"Can not find [{featureName}] Render Objects Feature");

    }

    /// <summary>把 Stencil 两个 feature 设为与出生相反（UI sharing 接收方用：直接切换到对方世界）。</summary>
    public void SwitchStencilLocal()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient) return;

        if (nm.LocalClientId == 0)
        {
            ChangeRendererLayerMask("StencilThisWorld", "layer1");
            ChangeRendererLayerMask("StencilPortalWorld", "layer0");
        }
        else if (nm.LocalClientId == 1)
        {
            ChangeRendererLayerMask("StencilThisWorld", "layer0");
            ChangeRendererLayerMask("StencilPortalWorld", "layer1");
        }
    }

    /// <summary>按本机 ClientId 把 Stencil 两个 feature 设回与出生一致。</summary>
    public void ResetStencilLocal()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient)
            return;

        if (nm.LocalClientId == 0)
        {
            ChangeRendererLayerMask("StencilThisWorld", "layer0");
            ChangeRendererLayerMask("StencilPortalWorld", "layer1");
        }
        else if (nm.LocalClientId == 1)
        {
            ChangeRendererLayerMask("StencilThisWorld", "layer1");
            ChangeRendererLayerMask("StencilPortalWorld", "layer0");
        }
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
