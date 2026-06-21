// PortalClipPlaneEnd.cs
// 加进Renderer Features列表,放在 Stencil Portal World 下面 — 把镜头换回来
// PortalClipPlaneEnd.cs
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class PortalClipPlaneEnd : ScriptableRendererFeature
{
    class Pass : ScriptableRenderPass
    {
        class PassData { }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Camera cam = frameData.Get<UniversalCameraData>().camera;

            using var builder = renderGraph.AddUnsafePass<PassData>("PortalClipPlaneEnd", out var passData);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, UnsafeGraphContext ctx) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            });
        }
    }

    Pass pass;
    public override void Create() => pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        => renderer.EnqueuePass(pass);
}