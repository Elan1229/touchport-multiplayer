// PortalClipPlaneBegin.cs
// 加进Renderer Features列表,放在 Portal Depth Clear 下面、Stencil Portal World 上面
// PortalClipPlaneBegin.cs
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class PortalClipPlaneBegin : ScriptableRendererFeature
{
    class Pass : ScriptableRenderPass
    {
        class PassData { }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!PortalClipPlaneProvider.Active) return;

            Camera cam = frameData.Get<UniversalCameraData>().camera;

            using var builder = renderGraph.AddUnsafePass<PassData>("PortalClipPlaneBegin", out var passData);
            builder.AllowPassCulling(false);            // 不写任何资源,默认会被当成"没用"裁掉,这行强制保留
            builder.AllowGlobalStateModification(true);  // 要改全局的view/projection矩阵,得声明一下

            builder.SetRenderFunc((PassData data, UnsafeGraphContext ctx) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

                Vector3 posVS = cam.worldToCameraMatrix.MultiplyPoint(PortalClipPlaneProvider.PlanePosition);
                Vector3 normalVS = cam.worldToCameraMatrix.MultiplyVector(PortalClipPlaneProvider.PlaneNormal).normalized;
                Vector4 clipPlane = new Vector4(normalVS.x, normalVS.y, normalVS.z, -Vector3.Dot(normalVS, posVS));

                Matrix4x4 obliqueProj = cam.CalculateObliqueMatrix(clipPlane);
                cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, obliqueProj);
            });
        }
    }

    Pass pass;
    public override void Create() => pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        => renderer.EnqueuePass(pass);
}