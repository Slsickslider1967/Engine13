using System;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Vortice.Mathematics;

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

        Sdl2Window Window = VeldridStartup.CreateWindow(Window_Create_Info);

        GraphicsBackend BakcEnd = GraphicsBackend.Vulkan;

        GraphicsDeviceOptions GD_Options = new GraphicsDeviceOptions
        {
            Debug = true,
            SyncToVerticalBlank = true,
            SwapchainDepthFormat = PixelFormat.R16_UNorm
        };

        GraphicsDevice GD = VeldridStartup.CreateGraphicsDevice(Window, GD_Options, BakcEnd);
        Swapchain SC = GD.MainSwapchain;
        CommandList Cl = GD.ResourceFactory.CreateCommandList();

        // Example RGB values (0-255)
        byte R = 0, G = 0, B = 0; // DodgerBlue
        RgbaFloat clearColor = new RgbaFloat(R / 255f, G / 255f, B / 255f, 1f);

        while (Window.Exists)
        {
            Window.PumpEvents();
            if (!Window.Exists) break;

             if (Window.Width != SC.Framebuffer.Width || Window.Height != SC.Framebuffer.Height)
             {
                 GD.ResizeMainWindow((uint)Window.Width, (uint)Window.Height);
                 SC = GD.MainSwapchain;
             }

            R += 1;
            if (R >= 255) G += 1;
            if (G >= 255) B += 1;
            Console.WriteLine($"R: {R}, G: {G}, B: {B}");
            clearColor = new RgbaFloat(R / 255f, G / 255f, B / 255f, 1f);

            // Here you would typically call your engine's run method
            // For example: engine.Run();

            Cl.Begin();
            Cl.SetFramebuffer(GD.SwapchainFramebuffer);
            Cl.ClearColorTarget(0, clearColor);
            Cl.End();

            GD.SubmitCommands(Cl);
            GD.SwapBuffers();
        }

        GD.Dispose();
    }
}