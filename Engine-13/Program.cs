using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using System;
using Engine13.Graphics;

class Program
{
    static void Main()
    {
        WindowCreateInfo Window_Create_Info = new WindowCreateInfo
        {
            WindowTitle = "Engine-13", // Window title
            X = 100,         // X
            Y = 100,         // Y
            WindowWidth  = 800,         // Width
            WindowHeight = 600         // Height
        };

        Sdl2Window window = VeldridStartup.CreateWindow(Window_Create_Info);
        GraphicsBackend backend = GraphicsBackend.Vulkan;
        GraphicsDeviceOptions gdOptions = new GraphicsDeviceOptions
        {
            Debug = true,
            SyncToVerticalBlank = true,
            SwapchainDepthFormat = null
        };

        GraphicsDevice gd = VeldridStartup.CreateGraphicsDevice(window, gdOptions, backend);
        CommandList cl = gd.ResourceFactory.CreateCommandList();

        var pipelineMgr = new PipeLineManager(gd);
        pipelineMgr.InitializeDefaultPipeline();
        var renderer = new Renderer(gd, cl, pipelineMgr);

        var quad = Mesh.CreateQuad(gd, 0.5f, 0.5f);

        RgbaFloat clearColor = new RgbaFloat(0.1f, 0.1f, 0.15f, 1f);

        while (window.Exists)
        {
            window.PumpEvents();
            if (!window.Exists) break;

            if (window.Width != gd.MainSwapchain.Framebuffer.Width || window.Height != gd.MainSwapchain.Framebuffer.Height)
            {
                gd.ResizeMainWindow((uint)window.Width, (uint)window.Height);
            }

            renderer.BeginFrame(clearColor);
            renderer.DrawMesh(quad);
            renderer.EndFrame();
        }

        gd.Dispose();

    }
}