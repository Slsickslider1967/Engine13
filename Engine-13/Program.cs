using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Engine13.Core;
using System;

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

        Sdl2Window Window = VeldridStartup.CreateWindow(Window_Create_Info);
        GraphicsBackend Backend = GraphicsBackend.Vulkan;
        GraphicsDeviceOptions GD_Options = new GraphicsDeviceOptions
        {
            Debug = true,
            SyncToVerticalBlank = true,
            SwapchainDepthFormat = PixelFormat.R16_UNorm
        };
        //Swapchain SC = GD_Options.MainSwapchain;

        Engine _engine = new Engine(Window, GD_Options, Backend);
        GameTime _GameTime = new GameTime();
        _engine.Run();

    }
}