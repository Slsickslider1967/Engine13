using Veldrid;
using Veldrid.Sdl2;
using System;
using Engine13.Graphics;
using System.Numerics;
using Engine13.Primitives;
using Engine13.Utilities.Attributes;
using Engine13.Utilities;

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
        private SpatialGrid _Grid = new SpatialGrid(0.05f);


        public Engine(Sdl2Window _Window, GraphicsDevice _GD)
        {
            Window = _Window;
            GD = _GD;
            GameTime = new GameTime();  //Initializes GameTime
            _PipeLineManager = new PipeLineManager(GD);
            _PipeLineManager.LoadDefaultShaders();
            _PipeLineManager.CreatePipeline();
            var cl = GD.ResourceFactory.CreateCommandList();
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
                
                // Update each mesh position in the spatial grid
                foreach (var mesh in _Meshes)
                {
                    _Grid.UpdateMeshPosition(mesh);
                }
                
                RunCollisionDetection();


                R += 1;
                if (R >= 255) G += 1;
                if (G >= 255) { B += 1; G = 0; }
                RgbaFloat clearColor = new RgbaFloat(R / 255f, G / 255f, B / 255f, 1f);

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
            // Get all collision pairs from spatial grid (broad phase)
            var collisionPairs = _Grid.GetCollisionPairs();

            // Process each potential collision (narrow phase)
            foreach (var pair in collisionPairs)
            {
                if (CollisionInfo.AreColliding(pair.MeshA, pair.MeshB, out CollisionInfo collisionInfo))
                {
                    // Handle the collision
                    ResolveCollision(collisionInfo);
                }
            }
        }

        private void ResolveCollision(CollisionInfo collision)
        {
            var meshA = collision.MeshA;
            var meshB = collision.MeshB;

            var objA = meshA.GetAttribute<ObjectCollision>();
            var objB = meshB.GetAttribute<ObjectCollision>();

            var separation = collision.SeparationDirection * (collision.PenetrationDepth.Length() * 0.5f);
            
            if (objA != null && !objA.IsStatic)
            {
                meshA.Position -= separation;
            }
            if (objB != null && !objB.IsStatic)
            {
                meshB.Position += separation;
            }

            if (objA != null && objB != null && !objA.IsStatic && !objB.IsStatic)
            {
                var tempVel = objA.Velocity;
                objA.Velocity = objB.Velocity * objA.Restitution;
                objB.Velocity = tempVel * objB.Restitution;
            }
        }

        public void Objects()
        {
            for (int i = 0; i < 3; i++) // Create a set amount of meshes
            {
                float size = 0.1f;
                var s = CubeFactory.CreateCube(GD, size);
                s.Position = new Vector2(0f, -1f + i * 0.2f);
                s.Mass = 0.5f;
                s.AddAttribute(new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: s.Mass));
                s.AddAttribute(new EdgeCollision(loop: false)); //true for looping, false for clamping
                s.AddAttribute(new ObjectCollision { Mass = s.Mass, Restitution = 0.8f });

                _UpdateManager.Register(s);
                _Meshes.Add(s);

                _Grid.AddMesh(s);
            }

        }

    }
} 