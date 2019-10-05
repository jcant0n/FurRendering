using System;
using System.Runtime.CompilerServices;
using WaveEngine.Common.Graphics;
using WaveEngine.Common.Graphics.VertexFormats;
using WaveEngine.Mathematics;
using Buffer = WaveEngine.Common.Graphics.Buffer;

namespace Fur
{
    public class FurRenderingTest : BaseTest
    {
        private Viewport[] viewports;
        private Rectangle[] scissors;
        private CommandQueue graphicsCommandQueue;
        private GraphicsPipelineState graphicsPipelineState;
        private ResourceSet resourceSet;
        private Buffer[] vertexBuffers;
        private Buffer constantBuffer;
        private uint width;
        private uint height;

        public FurRenderingTest()
           : base("FurRendering")
        {
        }

        private VertexPositionColorTexture[] vertexData = new VertexPositionColorTexture[]
        {
            new VertexPositionColorTexture(new Vector3(-1.0f, -1.0f,  1.0f), new Color(0.0f, 1.0f, 0.0f, 1.0f), new Vector2(1, 0)), // BACK
            new VertexPositionColorTexture(new Vector3(-1.0f,  1.0f,  1.0f), new Color(0.0f, 1.0f, 0.0f, 1.0f), new Vector2(0, 0)),
            new VertexPositionColorTexture(new Vector3(1.0f,   1.0f,  1.0f), new Color(0.0f, 1.0f, 0.0f, 1.0f), new Vector2(0, 1)),
            new VertexPositionColorTexture(new Vector3(-1.0f, -1.0f,  1.0f), new Color(0.0f, 1.0f, 0.0f, 1.0f), new Vector2(1, 0)),
            new VertexPositionColorTexture(new Vector3(1.0f,   1.0f,  1.0f), new Color(0.0f, 1.0f, 0.0f, 1.0f), new Vector2(0, 1)),
            new VertexPositionColorTexture(new Vector3(1.0f,  -1.0f,  1.0f), new Color(0.0f, 1.0f, 0.0f, 1.0f), new Vector2(1, 1)),
        };

        protected override async void InternalLoad()
        {
            // Compile Vertex and Pixel shaders
            var vertexShaderDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "HLSL", "VertexShader", ShaderStages.Vertex, "VS");
            var pixelShaderDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "HLSL", "FragmentShader", ShaderStages.Pixel, "PS");

            var vertexShader = this.graphicsContext.Factory.CreateShader(ref vertexShaderDescription);
            var pixelShader = this.graphicsContext.Factory.CreateShader(ref pixelShaderDescription);


            var vertexBufferDescription = new BufferDescription((uint)Unsafe.SizeOf<VertexPositionColorTexture>() * (uint)vertexData.Length, BufferFlags.VertexBuffer, ResourceUsage.Default);
            var vertexBuffer = this.graphicsContext.Factory.CreateBuffer(vertexData, ref vertexBufferDescription);

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 4), new Vector3(0, 0, 0), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, (float)this.frameBuffer.Width / (float)this.frameBuffer.Height, 0.1f, 100f);
            var viewProj = Matrix4x4.Multiply(view, proj);

            // Constant Buffer
            var constantBufferDescription = new BufferDescription(64, BufferFlags.ConstantBuffer, ResourceUsage.Default);
            this.constantBuffer = this.graphicsContext.Factory.CreateBuffer(ref viewProj, ref constantBufferDescription);


            // Create Texture from file
            Texture texture2D = null;
            using (var stream = this.assetsDirectory.Open("Leopard.ktx"))
            {
                if (stream != null)
                {
                    VisualTests.LowLevel.Images.Image image = VisualTests.LowLevel.Images.Image.Load(stream);
                    var textureDescription = image.TextureDescription;
                    texture2D = graphicsContext.Factory.CreateTexture(image.DataBoxes, ref textureDescription);
                }
            }

            SamplerStateDescription samplerDescription = SamplerStates.LinearClamp;
            var sampler = this.graphicsContext.Factory.CreateSamplerState(ref samplerDescription);


            // Prepare Pipeline
            var vertexLayouts = new InputLayouts()
                  .Add(VertexPositionColorTexture.VertexFormat);

            ResourceLayoutDescription layoutDescription = new ResourceLayoutDescription(
                    new LayoutElementDescription(0, ResourceType.ConstantBuffer, ShaderStages.Vertex),
                    new LayoutElementDescription(0, ResourceType.Texture, ShaderStages.Pixel),
                    new LayoutElementDescription(0, ResourceType.Sampler, ShaderStages.Pixel));

            ResourceLayout resourcesLayout = this.graphicsContext.Factory.CreateResourceLayout(ref layoutDescription);

            ResourceSetDescription resourceSetDescription = new ResourceSetDescription(resourcesLayout, this.constantBuffer, texture2D, sampler);
            this.resourceSet = this.graphicsContext.Factory.CreateResourceSet(ref resourceSetDescription);

            var pipelineDescription = new GraphicsPipelineDescription()
            {
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                InputLayouts = vertexLayouts,
                ResourceLayouts = new[] { resourcesLayout },
                Shaders = new ShaderStateDescription()
                {
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                },
                RenderStates = new RenderStateDescription()
                {
                    RasterizerState = RasterizerStates.CullBack,
                    BlendState = BlendStates.Opaque,
                    DepthStencilState = DepthStencilStates.None,
                },
                Outputs = this.frameBuffer.OutputDescription,
            };

            this.graphicsPipelineState = this.graphicsContext.Factory.CreateGraphicsPipeline(ref pipelineDescription);
            this.graphicsCommandQueue = this.graphicsContext.Factory.CreateCommandQueue();

            var swapChainDescription = this.swapChain?.SwapChainDescription;
            var width = swapChainDescription.HasValue ? swapChainDescription.Value.Width : this.surface.Width;
            var height = swapChainDescription.HasValue ? swapChainDescription.Value.Height : this.surface.Height;

            this.viewports = new Viewport[1];
            this.viewports[0] = new Viewport(0, 0, width, height);
            this.scissors = new Rectangle[1];
            this.scissors[0] = new Rectangle(0, 0, (int)width, (int)height);

            this.vertexBuffers = new Buffer[1];
            this.vertexBuffers[0] = vertexBuffer;
        }

        protected override void InternalDrawCallback(TimeSpan gameTime)
        {
            // Draw
            var commandBuffer = this.graphicsCommandQueue.CommandBuffer();

            commandBuffer.Begin();

            RenderPassDescription renderPassDescription = new RenderPassDescription(this.frameBuffer, new ClearValue(ClearFlags.Target, Color.CornflowerBlue));
            commandBuffer.BeginRenderPass(ref renderPassDescription);

            commandBuffer.SetViewports(this.viewports);
            commandBuffer.SetScissorRectangles(this.scissors);

            commandBuffer.SetGraphicsPipelineState(this.graphicsPipelineState);
            commandBuffer.SetResourceSet(this.resourceSet);
            commandBuffer.SetVertexBuffers(this.vertexBuffers);

            commandBuffer.Draw((uint)vertexData.Length);

            commandBuffer.EndRenderPass();

            commandBuffer.End();
            commandBuffer.Commit();

            this.graphicsCommandQueue.Submit();
            this.graphicsCommandQueue.WaitIdle();
        }
    }
}
