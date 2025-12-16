using System;
using Engine13.Core;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

class Program
{
    static void Main()
    {
        WindowCreateInfo windowCreateInfo = new WindowCreateInfo
        {
            WindowTitle = "Engine-13",
            X = 100,
            Y = 100,
            WindowWidth = 800,
            WindowHeight = 600
        };

        Sdl2Window window = VeldridStartup.CreateWindow(windowCreateInfo);
        Console.WriteLine(
            $"[Startup] Created window: Title='{window.Title}', Size={window.Width}x{window.Height}, IsVisible={window.Exists}"
        );
        GraphicsBackend backend = GraphicsBackend.Vulkan;
        GraphicsDeviceOptions gdOptions = new GraphicsDeviceOptions
        {
            Debug = true,
            SyncToVerticalBlank = true,
            SwapchainDepthFormat = null,
        };

        GraphicsDevice gd = VeldridStartup.CreateGraphicsDevice(window, gdOptions, backend);
        var fb = gd.MainSwapchain.Framebuffer;
        Console.WriteLine(
            $"[Startup] GraphicsDevice created. Backend={gd.BackendType}, Framebuffer={fb.Width}x{fb.Height}"
        );
        Game game = new Game(window, gd);
        game.Run();
    }
}
