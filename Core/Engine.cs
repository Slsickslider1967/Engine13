using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using System;
using Engine13.Core;
using Engine13.Graphics;
using System.IO.Pipelines;

namespace Engine13.Core
{
    public class Engine
    {
        private Sdl2Window Window;
        private GraphicsDevice GD;
        private CommandList CL;
        private Scene CurrentScene;
        private GameTime GameTime;
        private Mesh mesh;
        private PipeLineManager _PipeLineManager;
        private Renderer _Renderer;

        private byte R = 0, G = 0, B = 0;

        public Engine(Sdl2Window _Window, GraphicsDeviceOptions _GDOptions, GraphicsBackend _Backend)
        {
            Window = _Window;
            GD = VeldridStartup.CreateGraphicsDevice(_Window, _GDOptions, _Backend);
            CL = GD.ResourceFactory.CreateCommandList();
            GameTime = new GameTime();
            _PipeLineManager = new PipeLineManager(GD);
            _PipeLineManager.LoadDefaultShaders();   // or manually AddShader()
            _PipeLineManager.CreatePipeline();
            Pipeline pipeline = _PipeLineManager.GetPipeline();
            _Renderer = new Renderer(GD, CL, _PipeLineManager);
        }

        public void Run()
        {

            while (Window.Exists)
            {
                Window.PumpEvents();
                if (!Window.Exists) break;

                if (Window.Width != GD.MainSwapchain.Framebuffer.Width || Window.Height != GD.MainSwapchain.Framebuffer.Height)
                {
                    GD.ResizeMainWindow((uint)Window.Width, (uint)Window.Height);
                    // SC = GD.MainSwapchain;
                }

                GameTime.Update();

                // R += 1;
                // if (R >= 255) G += 1;
                // if (G >= 255) {B += 1; G = 0;}
                // Console.WriteLine($"R: {R}, G: {G}, B: {B}");
                RgbaFloat clearColor = new RgbaFloat(R / 255f, G / 255f, B / 255f, 1f);

                _Renderer.BeginFrame(clearColor);
                _Renderer.DrawMesh(mesh);
                //_Renderer.EndFrame();


                Mesh BlackBox = Mesh.CreateQuad(GD, 2f, 1f);
                _Renderer.DrawMesh(BlackBox);
                _Renderer.EndFrame();


                // Here you would typically call your engine's run method
                // For example: engine.Run();

                // CL.Begin();
                // CL.SetFramebuffer(GD.SwapchainFramebuffer);
                // CL.ClearColorTarget(0, clearColor);
                // CL.End();

                // GD.SubmitCommands(CL);
                // GD.SwapBuffers(GD.MainSwapchain);
            }
            

        GD.Dispose();

        }
    }
} 