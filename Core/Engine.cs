using System;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Veldrid;
using Veldrid.Sdl2;
using Vulkan;

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
        private System.Collections.Generic.List<Mesh> _Meshes = new();
        private UpdateManager _UpdateManager;
        private SpatialGrid _Grid = new SpatialGrid(0.025f);
        private const float _PhysicsTick = 20f;

        public Engine(Sdl2Window _Window, GraphicsDevice _GD)
        {
            Window = _Window;
            GD = _GD;
            GameTime = new GameTime();
            _PipeLineManager = new PipeLineManager(GD);
            _PipeLineManager.LoadDefaultShaders();
            _PipeLineManager.CreatePipeline();
            var cl = GD.ResourceFactory.CreateCommandList();
            _Renderer = new Renderer(GD, cl, _PipeLineManager);
            _InputManager = new Input.InputManager();
            _InputManager.Attach(Window);
            _UpdateManager = new UpdateManager();
        }

        public void Run()
        {
            Objects();
            while (Window.Exists)
            {
                _InputManager.Update(Window);

                Window.PumpEvents();
                if (!Window.Exists)
                    break;

                if (
                    Window.Width != GD.MainSwapchain.Framebuffer.Width
                    || Window.Height != GD.MainSwapchain.Framebuffer.Height
                )
                {
                    GD.ResizeMainWindow((uint)Window.Width, (uint)Window.Height);
                }

                GameTime.Update();

                int Ticks = (int)(GameTime.DeltaTime / _PhysicsTick);

                for (int i = 0; i <= Ticks; i++)
                {
                    _UpdateManager.Update(GameTime);
                    _Grid.UpdateAllAabb(_Meshes);
                    RunCollisionDetection();
                }

                _Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

                for (int i = 0; i < _Meshes.Count; i++)
                {
                    _Renderer.DrawMesh(_Meshes[i]);
                }

                _Renderer.EndFrame();
            }

            GD.Dispose();
        }

        private void RunCollisionDetection()
        {
            foreach (var m in _Meshes)
            {
                var oc = m.GetAttribute<ObjectCollision>();
                if (oc != null)
                    oc.IsGrounded = false;
            }

            const int iterations = 8;
            for (int iter = 0; iter < iterations; iter++)
            {
                var collisionPairs = _Grid.GetCollisionPairs();
                if (collisionPairs.Count == 0)
                    break;

                bool anyContacts = false;
                foreach (var pair in collisionPairs)
                {
                    if (
                        CollisionInfo.AreColliding(
                            pair.MeshA,
                            pair.MeshB,
                            out CollisionInfo collisionInfo
                        )
                    )
                    {
                        anyContacts = true;
                        PhysicsSolver.ResolveCollision(collisionInfo, GameTime.DeltaTime);
                    }
                }

                if (!anyContacts)
                    break;
            }
        }

        public void Objects()
        {
            for (int i = 0; i < 200; i++)
            {
                var Particle = CircleFactory.CreateCircle(GD, 0.01f, 8, 8);
                Particle.Position = new Vector2(0.25f, 0.5f + i * 0.06f);
                Particle.Mass = 1f;

                Particle.AddAttribute(
                    new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: Particle.Mass)
                );
                Particle.AddAttribute(
                    new ObjectCollision() { Mass = Particle.Mass, Restitution = 0.4f }
                );
                Particle.AddAttribute(new EdgeCollision(loop: false));

                _UpdateManager.Register(Particle);
                _Meshes.Add(Particle);
                _Grid.AddMesh(Particle);
            }

            var EdgeLeft = QuadFactory.CreateQuad(GD, 0.15f, 100f);
            EdgeLeft.Position = new Vector2(-0.25f, 0f);

            EdgeLeft.AddAttribute(new ObjectCollision() { IsStatic = true, Restitution = 0.0f });

            _UpdateManager.Register(EdgeLeft);
            _Meshes.Add(EdgeLeft);
            _Grid.AddMesh(EdgeLeft);
        }
    }
}
