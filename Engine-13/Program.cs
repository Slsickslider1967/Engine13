using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using System;
using Engine13.Graphics;
using Engine13.Core;

class Program
{
    static void Main()
    {
        WindowCreateInfo Window_Create_Info = new WindowCreateInfo
        {
            WindowTitle = "Engine-13", // Window title
            X = 100,         // X
            Y = 100,         // Y
            WindowWidth = 800,         // Width
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
        
        Engine engine = new Engine(window, gdOptions, backend);
        engine.Run();
    }
}