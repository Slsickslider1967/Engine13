using Veldrid;

namespace Engine13.Graphics
{
    public class PipeLineManager
    {
        private GraphicsDevice GD;
        private GraphicsPipelineDescription PipelineDescription = new GraphicsPipelineDescription();
        private Shader[] _Shaders;
        private Pipeline _Pipeline;
        private CommandList CL;
        private ResourceFactory factory;

        public PipeLineManager(GraphicsDevice _GD)
        {
            GD = _GD;
            factory = GD.ResourceFactory;

            PipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;
            PipelineDescription.DepthStencilState = new DepthStencilStateDescription
            (
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual
            );

            PipelineDescription.RasterizerState = new RasterizerStateDescription
            (
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false
            );

            PipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleList;
            PipelineDescription.ResourceLayouts = Array.Empty<ResourceLayout>();

            PipelineDescription.ShaderSet = new ShaderSetDescription
            (
                vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                shaders: _Shaders
            );

            PipelineDescription.Outputs = GD.SwapchainFramebuffer.OutputDescription;
            _Pipeline = factory.CreateGraphicsPipeline(PipelineDescription);

            CL = factory.CreateCommandList();
        }

        public Pipeline GetPipeline()
        {
            return _Pipeline;
        } 

        public void AddShader(Shader shader)
        {
            int oldLength = _Shaders.Length;
            Array.Resize(ref _Shaders, oldLength + 1);
            _Shaders[oldLength] = shader;
        }
        
    }
}