using Veldrid;
using Veldrid.SPIRV;

namespace Engine13.Graphics
{
    public class PipeLineManager
    {
        private GraphicsDevice GD;
        private GraphicsPipelineDescription PipelineDescription = new GraphicsPipelineDescription();
    private Shader[] _Shaders = Array.Empty<Shader>();
    private Pipeline _Pipeline = null!;
    private CommandList CL = null!;
        private ResourceFactory factory;
    public ResourceLayout? PositionLayout { get; private set; }
    public ResourceLayout? ProjectionLayout { get; private set; }
        public ResourceLayout? ColorLayout { get; private set; }

        public void CreatePipeline()
        {

            if (_Shaders == null || _Shaders.Length == 0)
                throw new Exception("No shaders added");

            var vertexLayout = new VertexLayoutDescription
            (
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2)
            );

            PipelineDescription.ShaderSet = new ShaderSetDescription
            (
                new[] { vertexLayout },
                _Shaders
            );

            PipelineDescription.BlendState = BlendStateDescription.SingleAlphaBlend;
            PipelineDescription.DepthStencilState = new DepthStencilStateDescription
            (
                depthTestEnabled: false,
                depthWriteEnabled: false,
                comparisonKind: ComparisonKind.Always
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
            PositionLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("PositionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            ));

            ProjectionLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            ));

            ColorLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
            ));

            PipelineDescription.ResourceLayouts = new[] { PositionLayout, ProjectionLayout, ColorLayout };
            PipelineDescription.Outputs = GD.SwapchainFramebuffer.OutputDescription;
            _Pipeline = factory.CreateGraphicsPipeline(PipelineDescription);

            CL = factory.CreateCommandList();
        }

        public void InitializeDefaultPipeline()
        {
            LoadDefaultShaders();
            CreatePipeline();
        }

        public PipeLineManager(GraphicsDevice _GD)
        {
            GD = _GD;
            factory = GD.ResourceFactory;
        }

        public void LoadDefaultShaders()
        {
            string shaderDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Shaders");
            string vertPath = System.IO.Path.Combine(shaderDir, "basic.vert");
            string fragPath = System.IO.Path.Combine(shaderDir, "basic.frag");

            string vertexCode = System.IO.File.ReadAllText(vertPath);
            string fragmentCode = System.IO.File.ReadAllText(fragPath);

            _Shaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertexCode), "main"),
                new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragmentCode), "main")
            );
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