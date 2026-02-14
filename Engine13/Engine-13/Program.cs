using System;
using Engine13.Core;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Vulkan.Xlib;

class Program
{
    static void Main()
    {
        WindowCreateInfo windowCreateInfo = new WindowCreateInfo
        {
            WindowTitle = "Engine-13",
            X = 100,
            Y = 100,
            WindowWidth = 1600,
            WindowHeight = 900,
        };

        Sdl2Window window = VeldridStartup.CreateWindow(windowCreateInfo);
        window.WindowState = WindowState.BorderlessFullScreen;

        // Pump SDL events so the fullscreen state change is actually processed
        // BEFORE the GraphicsDevice/Swapchain is created.  Without this the
        // swapchain is created at 1600x900 while the Vulkan surface is already
        // at the monitor's native resolution, which causes a "swapchain image
        // not acquired" validation error on the very first present.
        window.PumpEvents();

        Console.WriteLine(
            $"[Startup] Created window: Title='{window.Title}', Size={window.Width}x{window.Height}, IsVisible={window.Exists}"
        );
        GraphicsBackend backend = GraphicsBackend.Vulkan;
        GraphicsDeviceOptions gdOptions = new GraphicsDeviceOptions
        {
            Debug = false,
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

    public static void Restart()
    {
        Console.WriteLine("[Program] Restarting application...");
        System.Diagnostics.Process.Start(Environment.GetCommandLineArgs()[0]);
        Environment.Exit(0);
    }
}
