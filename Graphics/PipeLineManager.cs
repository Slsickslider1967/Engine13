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

        public void CreatePipeline()
        {

            if (_Shaders == null || _Shaders.Length == 0)
                throw new Exception("No shaders added");

            var vertexLayout = new VertexLayoutDescription
            (
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
            );

            PipelineDescription.ShaderSet = new ShaderSetDescription
            (
                new[] { vertexLayout },
                _Shaders
            );

            PipelineDescription.Outputs = GD.SwapchainFramebuffer.OutputDescription;
            //_Pipeline = factory.CreateGraphicsPipeline(PipelineDescription);


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

            PipelineDescription.Outputs = GD.SwapchainFramebuffer.OutputDescription;
            _Pipeline = factory.CreateGraphicsPipeline(PipelineDescription);

            CL = factory.CreateCommandList();
        }

        public PipeLineManager(GraphicsDevice _GD)
        {
            GD = _GD;
            factory = GD.ResourceFactory;
        }

        public void LoadDefaultShaders()
        {
            string vertexCode =
            @"
            #version 450
            layout(location = 0) in vec2 Position;
            void main()
            {
                gl_Position = vec4(Position, 0, 1);
            }";

            ShaderDescription vertexShaderDesc = new ShaderDescription
            (
                ShaderStages.Vertex,
                System.Text.Encoding.UTF8.GetBytes(vertexCode),
                "main"
            );

            Shader vertexShader = factory.CreateShader(vertexShaderDesc);

            string fragmentCode =
            @"
            #version 450

            layout(location = 0) in vec4 fsin_Color;
            layout(location = 0) out vec4 fsout_Color;

            void main()
            {
                fsout_Color = fsin_Color;
            }";

            ShaderDescription fragmentShaderDesc = new ShaderDescription
            (
                ShaderStages.Fragment,
                System.Text.Encoding.UTF8.GetBytes(fragmentCode),
                "main"
            );

            Shader fragmentShader = factory.CreateShader(fragmentShaderDesc);

        

            _Shaders = new Shader[] { vertexShader, fragmentShader };
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