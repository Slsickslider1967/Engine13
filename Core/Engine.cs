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
        private SpatialGrid _Grid = new SpatialGrid(0.25f);


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
                
                // Refresh spatial grid memberships using current AABBs
                _Grid.UpdateAllAabb(_Meshes);
                
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
            var sepDist = collision.SeparationDirection;
            var objA = meshA.GetAttribute<ObjectCollision>();
            var objB = meshB.GetAttribute<ObjectCollision>();

            //ObjectCollision and values 
            Vector2 MeshAV = objA?.Velocity ?? Vector2.Zero;
            Vector2 MeshBV = objA?.Velocity ?? Vector2.Zero;

            float invMassA = (objA == null || objA.IsStatic || objA.Mass <= 0f) ? 0f : 1f / objA.Mass;
            float invMassB = (objB == null || objB.IsStatic || objB.Mass <= 0f) ? 0f : 1f / objB.Mass;

            Vector2 RelativeVelocity = MeshBV - MeshAV;
            float VReIn = Vector2.Dot(RelativeVelocity, sepDist); //Velocity againt normal
            float Rest = Math.Clamp(MathF.Min(objA?.Restitution ?? 0f, objB?.Restitution ?? 0f), 0f, 1f);

            // Use the minimum translation vector (penetration vector) directly, split between bodies.
            var mtv = collision.PenetrationDepth;
            if (mtv.LengthSquared() <= 1e-12f)
            {
                return; // no reliable separation needed
            }
            var separation = mtv * 0.5f;
            
            if (objA != null && !objA.IsStatic)
            {
                meshA.Position -= separation;
            }
            if (objB != null && !objB.IsStatic)
            {
                meshB.Position += separation;
            }

            //Add impulse 

            if (sepDist.LengthSquared() < 1e-8f) return;
            sepDist = Vector2.Normalize(sepDist);

            if (VReIn > 0f) return; 
            

        }

        public void Objects()
        {
            var Cube1 = CubeFactory.CreateCube(GD, 0.05f);
            Cube1.Position = new Vector2(0.5f, -0.9f);
            Cube1.Mass = 1f;

            Cube1.AddAttribute(new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: Cube1.Mass));
            Cube1.AddAttribute(new EdgeCollision(loop: false)); //true for looping, false for clamping

            _UpdateManager.Register(Cube1);
            _Meshes.Add(Cube1);
            _Grid.AddMesh(Cube1);



            var Cube2 = CubeFactory.CreateCube(GD, 0.05f);
            Cube2.Position = new Vector2(-0.5f, -0.9f);
            Cube2.Mass = 5f;

            Cube2.AddAttribute(new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: Cube2.Mass));
            Cube2.AddAttribute(new EdgeCollision(loop: false)); //true for looping, false for clamping
            Cube2.AddAttribute(new ObjectCollision() { Mass = Cube2.Mass, Restitution = 0.8f, Velocity = new Vector2(1f, 0f) });

            _UpdateManager.Register(Cube2);
            _Meshes.Add(Cube2);
            _Grid.AddMesh(Cube2);
        }

    }
} 