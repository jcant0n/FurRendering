using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WaveEngine.Common.Graphics;
using WaveEngine.Common.Graphics.VertexFormats;
using WaveEngine.Mathematics;
using Buffer = WaveEngine.Common.Graphics.Buffer;

namespace Fur
{
    public class FurRenderingTest : BaseTest
    {
        public static FastRandom rand = new FastRandom(Environment.TickCount);

        private Viewport[] viewports;
        private Rectangle[] scissors;
        private CommandQueue graphicsCommandQueue;
        private GraphicsPipelineState graphicsPipelineState;
        private ResourceSet resourceSet;
        private Buffer[] vertexBuffers;
        private Buffer constantBuffer;
        private Matrix4x4 view;
        private Matrix4x4 proj;
        private float time;
        private Parameters parameters;

        private VertexPositionNormalTexture[] vertexData = new VertexPositionNormalTexture[]
        {
            new VertexPositionNormalTexture(new Vector3(-1.0f, -1.0f,  1.0f), new Vector3(0.0f, 0.0f,  1.0f), new Vector2(1, 0)),
            new VertexPositionNormalTexture(new Vector3(-1.0f,  1.0f,  1.0f), new Vector3(0.0f, 0.0f,  1.0f), new Vector2(0, 0)),
            new VertexPositionNormalTexture(new Vector3(1.0f,   1.0f,  1.0f), new Vector3(0.0f, 0.0f,  1.0f), new Vector2(0, 1)),
            new VertexPositionNormalTexture(new Vector3(-1.0f, -1.0f,  1.0f), new Vector3(0.0f, 0.0f,  1.0f), new Vector2(1, 0)),
            new VertexPositionNormalTexture(new Vector3(1.0f,   1.0f,  1.0f), new Vector3(0.0f, 0.0f,  1.0f), new Vector2(0, 1)),
            new VertexPositionNormalTexture(new Vector3(1.0f,  -1.0f,  1.0f), new Vector3(0.0f, 0.0f,  1.0f), new Vector2(1, 1)),
        };

        [StructLayout(LayoutKind.Explicit, Size = 80)]
        struct Parameters
        {
            [FieldOffset(0)]
            public Matrix4x4 viewProj;

            [FieldOffset(64)]
            public float MaxHairLength;

            [FieldOffset(68)]
            public float numLayers;

            [FieldOffset(72)]
            public float startShadowValue;
        }

        public FurRenderingTest()
           : base("FurRendering")
        {
        }

        protected override async void InternalLoad()
        {
            // Compile Vertex and Pixel shaders
            var vertexShaderDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "HLSL", "VertexShader", ShaderStages.Vertex, "VS");
            var pixelShaderDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "HLSL", "FragmentShader", ShaderStages.Pixel, "PS");

            var vertexShader = this.graphicsContext.Factory.CreateShader(ref vertexShaderDescription);
            var pixelShader = this.graphicsContext.Factory.CreateShader(ref pixelShaderDescription);


            var vertexBufferDescription = new BufferDescription((uint)Unsafe.SizeOf<VertexPositionNormalTexture>() * (uint)vertexData.Length, BufferFlags.VertexBuffer, ResourceUsage.Default);
            var vertexBuffer = this.graphicsContext.Factory.CreateBuffer(vertexData, ref vertexBufferDescription);

            this.view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 3f), new Vector3(0, 0, 0), Vector3.UnitY);
            this.proj = Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, (float)this.frameBuffer.Width / (float)this.frameBuffer.Height, 0.1f, 100f);

            // Parameters
            float density = 0.4f;
            float minHairLength = 0.5f;
            this.parameters = new Parameters();
            this.parameters.numLayers = 50f;
            this.parameters.startShadowValue = 0.2f;
            this.parameters.MaxHairLength = 0.2f;
            this.parameters.viewProj = Matrix4x4.Multiply(this.view, this.proj);

            // Constant Buffer
            var constantBufferDescription = new BufferDescription((uint)Unsafe.SizeOf<Parameters>(), BufferFlags.ConstantBuffer, ResourceUsage.Default);
            this.constantBuffer = this.graphicsContext.Factory.CreateBuffer(ref this.parameters, ref constantBufferDescription);

            // Create FurTexture
            uint size = 1024;
            var description = new TextureDescription()
            {
                Type = TextureType.Texture2D,
                Width = size,
                Height = size,
                Depth = 1,
                ArraySize = 1,
                Faces = 1,
                Usage = ResourceUsage.Default,
                CpuAccess = ResourceCpuAccess.None,
                Flags = TextureFlags.ShaderResource,
                Format = PixelFormat.R8_UNorm,
                MipLevels = 1,
                SampleCount = TextureSampleCount.None,
            };
            var textureFur = this.graphicsContext.Factory.CreateTexture(ref description);

            uint totalPixels = size * size;
            byte[] data = new byte[totalPixels];

            int strands = (int)(density * totalPixels);
            int minValue = (int)(minHairLength * 255f);
            for (int i = 0; i < strands; i++)
            {
                int x = rand.Next((int)size);
                int y = rand.Next((int)size);
                data[(x * size) + y] = (byte)rand.Next(minValue, 255);
            }

            this.graphicsContext.UpdateTextureData(textureFur, data);

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

            SamplerStateDescription sampler1Description = SamplerStates.LinearClamp;
            var sampler1 = this.graphicsContext.Factory.CreateSamplerState(ref sampler1Description);

            SamplerStateDescription sampler2Description = SamplerStates.PointClamp;
            var sampler2 = this.graphicsContext.Factory.CreateSamplerState(ref sampler2Description);

            // Prepare Pipeline
            var vertexLayouts = new InputLayouts()
                  .Add(VertexPositionNormalTexture.VertexFormat);

            ResourceLayoutDescription layoutDescription = new ResourceLayoutDescription(
                    new LayoutElementDescription(0, ResourceType.ConstantBuffer, ShaderStages.Vertex),
                    new LayoutElementDescription(0, ResourceType.Texture, ShaderStages.Pixel),
                    new LayoutElementDescription(1, ResourceType.Texture, ShaderStages.Pixel),
                    new LayoutElementDescription(0, ResourceType.Sampler, ShaderStages.Pixel),
                    new LayoutElementDescription(1, ResourceType.Sampler, ShaderStages.Pixel));

            ResourceLayout resourcesLayout = this.graphicsContext.Factory.CreateResourceLayout(ref layoutDescription);

            ResourceSetDescription resourceSetDescription = new ResourceSetDescription(resourcesLayout, this.constantBuffer, texture2D, textureFur, sampler1, sampler2);
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
                    RasterizerState = RasterizerStates.None,
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
            // Update
            this.time += (float)gameTime.TotalSeconds * 0.5f;
            var viewProj = Matrix4x4.Multiply(this.view, this.proj);
            this.parameters.viewProj = Matrix4x4.CreateRotationY((float)Math.Sin(this.time) * 0.4f) * viewProj;

            // Draw
            var commandBuffer = this.graphicsCommandQueue.CommandBuffer();

            commandBuffer.Begin();

            commandBuffer.UpdateBufferData(this.constantBuffer, ref this.parameters);

            RenderPassDescription renderPassDescription = new RenderPassDescription(this.frameBuffer, new ClearValue(ClearFlags.Target, Color.Black));
            commandBuffer.BeginRenderPass(ref renderPassDescription);

            commandBuffer.SetViewports(this.viewports);
            commandBuffer.SetScissorRectangles(this.scissors);

            commandBuffer.SetGraphicsPipelineState(this.graphicsPipelineState);
            commandBuffer.SetResourceSet(this.resourceSet);
            commandBuffer.SetVertexBuffers(this.vertexBuffers);

            commandBuffer.DrawInstanced((uint)vertexData.Length, (uint)this.parameters.numLayers);

            commandBuffer.EndRenderPass();

            commandBuffer.End();
            commandBuffer.Commit();

            this.graphicsCommandQueue.Submit();
            this.graphicsCommandQueue.WaitIdle();
        }
    }
}
