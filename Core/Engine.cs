using Veldrid;
using Veldrid.Sdl2;
using System;
using Engine13.Graphics;
using System.Numerics;
using Engine13.Primitives;
using Engine13.Utilities.Attributes;

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
        private System.Collections.Generic.List<Mesh> _Meshes = new();
        private UpdateManager _UpdateManager;


        public Engine(Sdl2Window _Window, GraphicsDevice _GD)
        {
            Window = _Window;
            GD = _GD;
            GameTime = new GameTime();  //Initializes GameTime
            _PipeLineManager = new PipeLineManager(GD); //Creates a new Pipeline Manager Object
            _PipeLineManager.LoadDefaultShaders(); //Loads the shaders for the Pipeline
            _PipeLineManager.CreatePipeline();  //Creates the Pipeline for a Mesh and shaders to interract with the buffers and graphics card
            var cl = GD.ResourceFactory.CreateCommandList();    // CommandList for the Renderer
            _Renderer = new Renderer(GD, cl, _PipeLineManager); //Creates the Renderer Object
            _InputManager = new Input.InputManager();   //Object managing
            _UpdateManager = new UpdateManager();
        }

        public void Run()
        {
            Objects();
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
                _UpdateManager.Update(GameTime);

                R += 1;
                if (R >= 255) G += 1;
                if (G >= 255) { B += 1; G = 0; }
                RgbaFloat clearColor = new RgbaFloat(R / 255f, G / 255f, B / 255f, 1f);

                _Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

                // Draw all meshes
                for (int i = 0; i < _Meshes.Count; i++)
                {
                    _Renderer.DrawMesh(_Meshes[i]);
                }

                _Renderer.EndFrame();
            }

            GD.Dispose();

        }

        public void Objects()
        {
            // One sphere that drops due to gravity and collides with all edges (no bounce)
            for (int i = 0; i < 1; i++)
            {
                float radius = 0.05f;
                var s = SphereFactory.CreateSphere(GD, radius);
                s.Position = new Vector2(0f, -1f);
                s.AddAttribute(new GravityAttribute(acceleration: 9.81f, initialVelocity: 5f, mass: 2.0f));

                _UpdateManager.Register(s);
                _Meshes.Add(s);
            }
        }

    }
} 