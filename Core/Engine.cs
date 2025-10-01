using Veldrid;
using Veldrid.Sdl2;
using System;
using Engine13.Graphics;
using System.Numerics;
using Engine13.Primitives;

namespace Engine13.Core
{
    public class Engine
    {
        private Sdl2Window Window;
        private GraphicsDevice GD;
        private GameTime GameTime;
        private PipeLineManager _PipeLineManager;
        private Renderer _Renderer;
        private Input.InputManager _InputManager;
        private byte R = 0, G = 0, B = 0;
        

        public Engine(Sdl2Window _Window, GraphicsDevice _GD)
        {
            Window = _Window;
            GD = _GD;
            GameTime = new GameTime();
            _PipeLineManager = new PipeLineManager(GD);
            _PipeLineManager.LoadDefaultShaders();   // or manually AddShader()
            _PipeLineManager.CreatePipeline();
            var cl = GD.ResourceFactory.CreateCommandList();
            _Renderer = new Renderer(GD, cl, _PipeLineManager);
            _InputManager = new Input.InputManager();
        }

        public void Run()
        {
            Mesh Sphere = SphereFactory.CreateSphere(GD, 0.05f);
            {
                var p = Sphere.Position;
                p.Y = 0.5f;
                Sphere.Position = p;
            }
            while (Window.Exists)
            {
                _InputManager.Update(Window);

                Window.PumpEvents();
                if (!Window.Exists) break;

                if (Window.Width != GD.MainSwapchain.Framebuffer.Width || Window.Height != GD.MainSwapchain.Framebuffer.Height)
                {
                    GD.ResizeMainWindow((uint)Window.Width, (uint)Window.Height);
                    // SC = GD.MainSwapchain;
                }

                GameTime.Update();

                R += 1;
                if (R >= 255) G += 1;
                if (G >= 255) { B += 1; G = 0; }
                RgbaFloat clearColor = new RgbaFloat(R / 255f, G / 255f, B / 255f, 1f);
                _Renderer.BeginFrame(clearColor);
                _Renderer.DrawMesh(Sphere);
                Sphere.Color = new Vector4(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
                _Renderer.EndFrame();
            }
            
            GD.Dispose();

        }
    }
} 