using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// Add this to the URP Renderer asset (PC_Renderer in Assets/Settings).
/// HorrorPostFX drives BlockSize via the static Instance — no Inspector wiring needed.
public class PixelateFeature : ScriptableRendererFeature
{
    public static PixelateFeature Instance { get; private set; }

    [Range(1, 32)] public int BlockSize = 1;

    private PixelatePass _pass;
    private Material     _material;

    public override void Create()
    {
        Instance = this;

        var shader = Shader.Find("Hidden/Pixelate");
        if (shader == null)
        {
            Debug.LogWarning("PixelateFeature: shader 'Hidden/Pixelate' not found.");
            return;
        }

        _material = CoreUtils.CreateEngineMaterial(shader);
        _pass     = new PixelatePass(_material)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null || BlockSize <= 1) return;
        _pass.Setup(BlockSize);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        Instance = null;
        CoreUtils.Destroy(_material);
    }

    // ── Inner pass ────────────────────────────────────────────────────────────

    sealed class PixelatePass : ScriptableRenderPass
    {
        private readonly Material _mat;
        private int               _blockSize;

        private static readonly int BlockSizeID = Shader.PropertyToID("_BlockSize");

        public PixelatePass(Material mat) => _mat = mat;
        public void Setup(int blockSize)  => _blockSize = blockSize;

        // Per-pass data carried into the render function
        private class PassData
        {
            public TextureHandle source;
            public Material      mat;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_mat == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData   = frameData.Get<UniversalCameraData>();

            // Skip if the camera is rendering directly to the backbuffer
            if (resourceData.isActiveTargetBackBuffer) return;

            var source = resourceData.activeColorTexture;

            var desc             = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples     = 1;

            _mat.SetVector(BlockSizeID, new Vector4(
                (float)desc.width  / _blockSize,
                (float)desc.height / _blockSize,
                0f, 0f));

            // Intermediate texture for ping-pong blit
            var temp = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_PixelateTempRT", false, FilterMode.Point);

            // Pass A — pixelate: source → temp
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Pixelate", out var pd))
            {
                pd.source = source;
                pd.mat    = _mat;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(temp, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.mat, 0));
            }

            // Pass B — copy back: temp → source
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Pixelate_CopyBack", out var pd))
            {
                pd.source = temp;
                pd.mat    = null;
                builder.UseTexture(temp, AccessFlags.Read);
                builder.SetRenderAttachment(source, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false));
            }
        }
    }
}
