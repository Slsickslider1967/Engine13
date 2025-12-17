using System;
using System.Numerics;
using System.Text;
using Engine13.Input;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;

namespace Engine13.UI
{
    public class ImGuiController : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly ResourceFactory _factory;
        private readonly Sdl2Window _window;
        private readonly InputManager _input;
        private CommandList _cl;

        private DeviceBuffer? _vertexBuffer;
        private DeviceBuffer? _indexBuffer;
        private DeviceBuffer? _projBuffer;
        private uint _vertexBufferSize = 65536;
        private uint _indexBufferSize = 65536;
        private readonly uint _vertexStructSize = (uint)
            System.Runtime.InteropServices.Marshal.SizeOf(typeof(ImDrawVert));

        private Shader[]? _shaders;
        private Pipeline? _pipeline;
        private ResourceLayout? _layout;
        private ResourceSet? _fontSet;
        private Texture? _fontTexture;
        private TextureView? _fontTextureView;
        private Sampler? _fontSampler;

        // Diagnostics
        private bool _printDrawData = true; // enabled during debug by default
        public bool PrintDrawData
        {
            get => _printDrawData;
            set => _printDrawData = value;
        }

        public ImGuiController(
            Sdl2Window window,
            GraphicsDevice gd,
            CommandList cl,
            InputManager input
        )
        {
            _window = window;
            _gd = gd;
            _factory = gd.ResourceFactory;
            _cl = cl;
            _input = input;

            ImGui.CreateContext();
            ImGui.StyleColorsDark();

            var io = ImGui.GetIO();
            SetKeyMappings(io);

            CreateDeviceResources();
        }

        public void NewFrame(float deltaSeconds)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(
                _gd.MainSwapchain.Framebuffer.Width,
                _gd.MainSwapchain.Framebuffer.Height
            );
            io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : (1f / 60f);

            io.MousePos = _input.MousePosition;
            io.MouseDown[0] = _input.IsMouseButtonDown(MouseButton.Left);
            io.MouseDown[1] = _input.IsMouseButtonDown(MouseButton.Right);
            io.MouseDown[2] = _input.IsMouseButtonDown(MouseButton.Middle);
            io.MouseWheel = _input.MouseWheelDelta;

            ImGui.NewFrame();
        }

        // Render into the provided CommandList. If cl is null, fall back to the controller's stored command list.
        public void Render(CommandList? cl = null)
        {
            ImGui.Render();
            var drawData = ImGui.GetDrawData();
            var target = cl ?? _cl;
            if (target == null)
            {
                // Nothing to record into
                return;
            }
            RenderDrawData(drawData, target);
        }

        // Builds an on-frame diagnostics UI showing the checklist and allows toggles
        public void BuildDiagnosticsUI()
        {
            var io = ImGui.GetIO();
            ImGui.Begin("ImGui Debug", ImGuiWindowFlags.AlwaysAutoResize);
            ImGui.Text("Runtime checklist:");
            ImGui.Separator();
            ImGui.Text($"DisplaySize: {io.DisplaySize.X}x{io.DisplaySize.Y}");
            ImGui.Text($"DeltaTime: {io.DeltaTime:F4}");
            ImGui.Text($"Shaders loaded: {(_shaders != null ? "OK" : "MISSING")}");
            ImGui.Text($"Pipeline: {(_pipeline != null ? "OK" : "MISSING")}");
            ImGui.Text($"Font texture: {(_fontTexture != null ? "OK" : "MISSING")}");
            ImGui.Text($"Font resource set: {(_fontSet != null ? "OK" : "MISSING")}");
            var dd = ImGui.GetDrawData();
            try
            {
                ImGui.Text(
                    $"Draw lists: {dd.CmdListsCount}, Vtx: {dd.TotalVtxCount}, Idx: {dd.TotalIdxCount}"
                );
            }
            catch
            {
                ImGui.Text("Draw lists: (none yet)");
            }
            ImGui.Separator();
            ImGui.Checkbox("Print draw-data to console", ref _printDrawData);
            if (ImGui.Button("Show ImGui Demo Window"))
            {
                ImGui.ShowDemoWindow();
            }
            ImGui.End();
        }

        private void CreateDeviceResources()
        {
            _vertexBuffer = _factory.CreateBuffer(
                new BufferDescription(
                    _vertexBufferSize,
                    BufferUsage.VertexBuffer | BufferUsage.Dynamic
                )
            );
            _indexBuffer = _factory.CreateBuffer(
                new BufferDescription(
                    _indexBufferSize,
                    BufferUsage.IndexBuffer | BufferUsage.Dynamic
                )
            );
            _projBuffer = _factory.CreateBuffer(
                new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic)
            );

            LoadDefaultShaders();

            _layout = _factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription(
                        "ProjectionMatrix",
                        ResourceKind.UniformBuffer,
                        ShaderStages.Vertex
                    ),
                    new ResourceLayoutElementDescription(
                        "FontTexture",
                        ResourceKind.TextureReadOnly,
                        ShaderStages.Fragment
                    ),
                    new ResourceLayoutElementDescription(
                        "FontSampler",
                        ResourceKind.Sampler,
                        ShaderStages.Fragment
                    )
                )
            );

            var vlayout = new VertexLayoutDescription(
                new VertexElementDescription(
                    "Position",
                    VertexElementSemantic.Position,
                    VertexElementFormat.Float2
                ),
                new VertexElementDescription(
                    "TexCoord",
                    VertexElementSemantic.TextureCoordinate,
                    VertexElementFormat.Float2
                ),
                new VertexElementDescription(
                    "Color",
                    VertexElementSemantic.Color,
                    VertexElementFormat.Byte4_Norm
                )
            );

            var pd = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    false,
                    false,
                    ComparisonKind.Always
                ),
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None,
                    PolygonFillMode.Solid,
                    FrontFace.Clockwise,
                    true,
                    false
                ),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ShaderSet = new ShaderSetDescription(new[] { vlayout }, _shaders),
                ResourceLayouts = new[] { _layout },
                Outputs = _gd.MainSwapchain.Framebuffer.OutputDescription,
            };

            _pipeline = _factory.CreateGraphicsPipeline(pd);

            // font
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(
                out IntPtr pixels,
                out int width,
                out int height,
                out int bpp
            );
            var desc = new TextureDescription(
                (uint)width,
                (uint)height,
                1,
                1,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled,
                TextureType.Texture2D
            );
            _fontTexture = _factory.CreateTexture(desc);
            _gd.UpdateTexture(
                _fontTexture,
                pixels,
                (uint)(width * height * bpp),
                0,
                0,
                0,
                (uint)width,
                (uint)height,
                1,
                0,
                0
            );
            _fontTextureView = _factory.CreateTextureView(_fontTexture);
            var fontTexId = (IntPtr)_fontTextureView.GetHashCode();
            io.Fonts.SetTexID(fontTexId);
            io.Fonts.ClearTexData();

            _fontSampler = _factory.CreateSampler(
                new SamplerDescription(
                    SamplerAddressMode.Clamp,
                    SamplerAddressMode.Clamp,
                    SamplerAddressMode.Clamp,
                    SamplerFilter.MinLinear_MagLinear_MipLinear,
                    ComparisonKind.Never,
                    16,
                    0,
                    16,
                    0,
                    SamplerBorderColor.OpaqueBlack
                )
            );

            _fontSet = _factory.CreateResourceSet(
                new ResourceSetDescription(_layout, _projBuffer, _fontTextureView, _fontSampler)
            );
        }

        public void LoadDefaultShaders()
        {
            string shaderDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Shaders");
            string vertPath = System.IO.Path.Combine(shaderDir, "ImGui.vert");
            string fragPath = System.IO.Path.Combine(shaderDir, "ImGui.frag");

            string vertexCode = System.IO.File.ReadAllText(vertPath);
            string fragmentCode = System.IO.File.ReadAllText(fragPath);

            _shaders = _factory.CreateFromSpirv(
                new ShaderDescription(
                    ShaderStages.Vertex,
                    System.Text.Encoding.UTF8.GetBytes(vertexCode),
                    "main"
                ),
                new ShaderDescription(
                    ShaderStages.Fragment,
                    System.Text.Encoding.UTF8.GetBytes(fragmentCode),
                    "main"
                )
            );
        }

        private void RenderDrawData(ImDrawDataPtr drawData, CommandList cl)
        {
            if (drawData.CmdListsCount == 0)
                return;

            uint totalVtx = (uint)drawData.TotalVtxCount;
            uint totalIdx = (uint)drawData.TotalIdxCount;

            uint vtxSize = totalVtx * _vertexStructSize;
            uint idxSize = totalIdx * (uint)sizeof(ushort);

            if (vtxSize > _vertexBufferSize)
            {
                _vertexBuffer.Dispose();
                _vertexBufferSize = Math.Max(vtxSize, _vertexBufferSize * 2);
                _vertexBuffer = _factory.CreateBuffer(
                    new BufferDescription(
                        _vertexBufferSize,
                        BufferUsage.VertexBuffer | BufferUsage.Dynamic
                    )
                );
            }

            if (idxSize > _indexBufferSize)
            {
                _indexBuffer.Dispose();
                _indexBufferSize = Math.Max(idxSize, _indexBufferSize * 2);
                _indexBuffer = _factory.CreateBuffer(
                    new BufferDescription(
                        _indexBufferSize,
                        BufferUsage.IndexBuffer | BufferUsage.Dynamic
                    )
                );
            }

            uint vtxOffset = 0;
            uint idxOffset = 0;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdListsRange[n];
                var vb = (uint)(cmdList.VtxBuffer.Size * _vertexStructSize);
                var ib = (uint)(cmdList.IdxBuffer.Size * sizeof(ushort));
                if (vb > 0)
                {
                    _gd.UpdateBuffer(_vertexBuffer, vtxOffset, (IntPtr)cmdList.VtxBuffer.Data, vb);
                    vtxOffset += vb;
                }
                if (ib > 0)
                {
                    _gd.UpdateBuffer(_indexBuffer, idxOffset, (IntPtr)cmdList.IdxBuffer.Data, ib);
                    idxOffset += ib;
                }
            }

            var width = Math.Max(1, _gd.MainSwapchain.Framebuffer.Width);
            var height = Math.Max(1, _gd.MainSwapchain.Framebuffer.Height);
            var proj = Matrix4x4.CreateOrthographicOffCenter(0, width, 0, height, -1f, 1f);
            _gd.UpdateBuffer(_projBuffer!, 0, ref proj);

            cl.SetPipeline(_pipeline!);
            cl.SetVertexBuffer(0, _vertexBuffer!);
            cl.SetIndexBuffer(_indexBuffer!, IndexFormat.UInt16);
            cl.SetGraphicsResourceSet(0, _fontSet!);

            uint vtxBase = 0;
            uint idxBase = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdListsRange[n];
                for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
                {
                    var pcmd = cmdList.CmdBuffer[cmdi];
                    if (pcmd.ElemCount == 0)
                        continue;

                    var clip = pcmd.ClipRect;
                    uint x = (uint)Math.Max((int)Math.Floor(clip.X), 0);
                    uint y = (uint)Math.Max((int)Math.Floor(clip.Y), 0);
                    uint w = (uint)Math.Max((int)Math.Ceiling(clip.Z - clip.X), 0);
                    uint h = (uint)Math.Max((int)Math.Ceiling(clip.W - clip.Y), 0);
                    cl.SetScissorRect(0, x, y, w, h);

                    if (_printDrawData)
                    {
                        // pcmd.TextureId is an IntPtr; lib maps it to our texture view hash earlier
                        var texId = pcmd.TextureId;
                    }

                    cl.DrawIndexed(
                        (uint)pcmd.ElemCount,
                        1,
                        (uint)(pcmd.IdxOffset + idxBase),
                        (int)(pcmd.VtxOffset + vtxBase),
                        0
                    );
                }
                vtxBase += (uint)cmdList.VtxBuffer.Size;
                idxBase += (uint)cmdList.IdxBuffer.Size;
            }
        }

        private void SetKeyMappings(ImGuiIOPtr io)
        {
            io.KeyMap[(int)ImGuiKey.Tab] = (int)Key.Tab;
            io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Key.Left;
            io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Key.Right;
            io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Key.Up;
            io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Key.Down;
            io.KeyMap[(int)ImGuiKey.PageUp] = (int)Key.PageUp;
            io.KeyMap[(int)ImGuiKey.PageDown] = (int)Key.PageDown;
            io.KeyMap[(int)ImGuiKey.Home] = (int)Key.Home;
            io.KeyMap[(int)ImGuiKey.End] = (int)Key.End;
            io.KeyMap[(int)ImGuiKey.Enter] = (int)Key.Enter;
            io.KeyMap[(int)ImGuiKey.Escape] = (int)Key.Escape;
            io.KeyMap[(int)ImGuiKey.Space] = (int)Key.Space;
            io.KeyMap[(int)ImGuiKey.A] = (int)Key.A;
            io.KeyMap[(int)ImGuiKey.C] = (int)Key.C;
            io.KeyMap[(int)ImGuiKey.V] = (int)Key.V;
            io.KeyMap[(int)ImGuiKey.X] = (int)Key.X;
            io.KeyMap[(int)ImGuiKey.Y] = (int)Key.Y;
            io.KeyMap[(int)ImGuiKey.Z] = (int)Key.Z;
        }

        public void Dispose()
        {
            _fontSampler?.Dispose();
            _fontTextureView?.Dispose();
            _fontTexture?.Dispose();
            _projBuffer?.Dispose();
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            if (_shaders != null)
            {
                foreach (var s in _shaders)
                    s.Dispose();
            }
            _pipeline?.Dispose();
            _layout?.Dispose();
        }
    }
}
