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
        Sdl2Window window = new Sdl2Window(
            "Engine-13", // Window title
            100,         // X
            100,         // Y
            800,         // Width
            600,         // Height
            SDL_WindowFlags.Resizable,
            false
        );

        GraphicsDevice GD = VeldridStartup.CreateGraphicsDevice(
            window,
            new GraphicsDeviceOptions(true, null, true),
            GraphicsBackend.Direct3D11
        );
        CommandList Cl = GD.ResourceFactory.CreateCommandList();

        // Example RGB values (0-255)
        int R = 30, G = 144, B = 255; // DodgerBlue
        RgbaFloat clearColor = new RgbaFloat(R / 255f, G / 255f, B / 255f, 1f);

        while (window.Exists)
        {
            window.PumpEvents();
            if (!window.Exists) break;

            // Here you would typically call your engine's run method
            // For example: engine.Runn();

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