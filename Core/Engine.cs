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
        private const float _PhysicsTick = 120f;

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

                float frameDelta = GameTime.DeltaTime;
                int physicsSteps = Math.Max(1, (int)MathF.Ceiling(frameDelta * _PhysicsTick));
                float stepDelta = physicsSteps > 0 ? frameDelta / physicsSteps : 0f;

                for (int step = 0; step < physicsSteps; step++)
                {
                    GameTime.OverrideDeltaTime(stepDelta);
                    _UpdateManager.Update(GameTime);
                    _Grid.UpdateAllAabb(_Meshes);
                    RunCollisionDetection(stepDelta);
                }

                GameTime.OverrideDeltaTime(frameDelta);

                _Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

                for (int i = 0; i < _Meshes.Count; i++)
                {
                    _Renderer.DrawMesh(_Meshes[i]);
                }

                _Renderer.EndFrame();
            }

            GD.Dispose();
        }

        private void RunCollisionDetection(float stepDelta)
        {
            foreach (var m in _Meshes)
            {
                var oc = m.GetAttribute<ObjectCollision>();
                if (oc != null)
                    oc.IsGrounded = false;
            }

            const int iterations = 12;
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
                        PhysicsSolver.ResolveCollision(collisionInfo, stepDelta);
                    }
                }

                if (!anyContacts)
                    break;

                if (iter < iterations - 1)
                    _Grid.UpdateAllAabb(_Meshes);
            }
        }

        public void Objects()
        {
            const int particleCount = 200;
            const int columns = 25;
            const float particleRadius = 0.01f;
            const float diameter = particleRadius * 2f;
            const float horizontalSpacing = diameter * 1.35f;
            const float verticalSpacing = diameter * 1.35f;
            var origin = new Vector2(-0.3f, 0.4f);

            _Meshes.EnsureCapacity(_Meshes.Count + particleCount + 1);

            for (int i = 0; i < particleCount; i++)
            {
                var particle = CircleFactory.CreateCircle(GD, particleRadius, 8, 8);
                int column = i % columns;
                int row = i / columns;
                particle.Position = new Vector2(
                    origin.X + column * horizontalSpacing,
                    origin.Y + row * verticalSpacing
                );
                particle.Mass = 1f;

                particle.AddAttribute(
                    new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: particle.Mass)
                );
                particle.AddAttribute(
                    new ObjectCollision() { Mass = particle.Mass, Restitution = 0.4f }
                );
                particle.AddAttribute(new EdgeCollision(loop: false));

                _UpdateManager.Register(particle);
                _Meshes.Add(particle);
                _Grid.AddMesh(particle);
            }

            var EdgeLeft = QuadFactory.CreateQuad(GD, 0.15f, 100f);
            EdgeLeft.Position = new Vector2(-0.25f, 0f);

            EdgeLeft.AddAttribute(new ObjectCollision() { IsStatic = true, Restitution = 0.5f });

            _UpdateManager.Register(EdgeLeft);
            _Meshes.Add(EdgeLeft);
            _Grid.AddMesh(EdgeLeft);
        }
    }
}
