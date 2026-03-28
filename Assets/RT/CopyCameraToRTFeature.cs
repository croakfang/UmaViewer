using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CopyCameraToRTFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderTexture target;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRendering;
    }

    class CopyPass : ScriptableRenderPass
    {
        public RenderTexture target;

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
{
    if (target == null) return;

    // 避免 SceneView / Preview / 其它非主相机导致 RTHandle 断言
    var camData = renderingData.cameraData;
    if (camData.isSceneViewCamera || camData.isPreviewCamera) return;
    if (camData.camera == null) return;

    var cmd = CommandBufferPool.Get("Copy Camera Color To RT");

    
    var src = camData.renderer.cameraColorTarget;
    cmd.Blit(src, target);

    context.ExecuteCommandBuffer(cmd);
    CommandBufferPool.Release(cmd);
}

    }

    public Settings settings = new Settings();
    CopyPass pass;

    public override void Create()
    {
        pass = new CopyPass();
        pass.renderPassEvent = settings.passEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        pass.target = settings.target;
        renderer.EnqueuePass(pass);
    }
}

