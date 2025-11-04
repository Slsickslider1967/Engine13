using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Engine13.Debug;
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
    private DebugOverlay _DebugOverlay;
    private Input.InputManager _InputManager;
    private System.Collections.Generic.List<Mesh> _Meshes = new();
    private UpdateManager _UpdateManager;
    private SpatialGrid _Grid = new SpatialGrid(0.25f);

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
            _DebugOverlay = new DebugOverlay(_Meshes, _Renderer);
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
                _UpdateManager.Update(GameTime);

                // Refresh spatial grid memberships using current AABBs
                _Grid.UpdateAllAabb(_Meshes);

                RunCollisionDetection();

                _Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

                for (int i = 0; i < _Meshes.Count; i++)
                {
                    _Renderer.DrawMesh(_Meshes[i]);
                }

                _DebugOverlay.Draw(GameTime.DeltaTime);

                _Renderer.EndFrame();
            }

            GD.Dispose();
        }

        private void RunCollisionDetection()
        {
            // Clear grounded each frame; it'll be set again when resting contacts are detected
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

                if (iter < iterations - 1)
                    _Grid.UpdateAllAabb(_Meshes);
            }
        }


        public void Objects()
        {
            for (int i = 0; i < 25; i++)
            {
                var Particle = SphereFactory.CreateSphere(GD, 0.03f, 8, 8);
                Particle.Position = new Vector2(0.0f + i * 0.06f, 0.5f+ i * 0.06f);
                Particle.Mass = 0.01f;

                Particle.AddAttribute(
                    new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: Particle.Mass)
                );
                Particle.AddAttribute(new ObjectCollision() { Mass = Particle.Mass, Restitution = 0.0f });
                Particle.AddAttribute(new EdgeCollision(loop: false));
                //Particle.AddAttribute(new MolecularDynamics(){ SpringConstant = 1f, AnchorFollowFactor = 2f, DriveAmplitude = 0.1f });

                _UpdateManager.Register(Particle);
                _Meshes.Add(Particle);
                _Grid.AddMesh(Particle);
            }   
        }
    }
}
