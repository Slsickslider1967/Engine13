using System;
using System.Numerics;
using Engine13.Debug;
using Engine13.Graphics;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Veldrid;
using Veldrid.Sdl2;

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
            // Get all collision pairs from spatial grid
            var collisionPairs = _Grid.GetCollisionPairs();

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
                        ResolveCollision(collisionInfo);
                    }
                }
                if (!anyContacts)
                    break;
            }
        }

        private void ResolveCollision(CollisionInfo collision)
        {
            var meshA = collision.MeshA;
            var meshB = collision.MeshB;
            var objA = meshA.GetAttribute<ObjectCollision>();
            var objB = meshB.GetAttribute<ObjectCollision>();

            float invMassA =
                (objA == null || objA.IsStatic || objA.Mass <= 0f) ? 0f : 1f / objA.Mass;
            float invMassB =
                (objB == null || objB.IsStatic || objB.Mass <= 0f) ? 0f : 1f / objB.Mass;

            var mtv = collision.PenetrationDepth;
            float mtvLenSq = mtv.LengthSquared();
            if (mtvLenSq <= 1e-12f)
                return;
            float mtvLen = MathF.Sqrt(mtvLenSq);

            Vector2 Normal = mtv / mtvLen;

            float slop = 0.002f;
            float percent = 0.15f;
            float denom = invMassA + invMassB;
            if (denom > 1e-6f)
            {
                float correctionMag = MathF.Max(mtvLen - slop, 0f) * percent;
                Vector2 correction = (correctionMag / denom) * Normal;
                if (objA != null && invMassA > 0f)
                    meshA.Position -= correction * invMassA;
                if (objB != null && invMassB > 0f)
                    meshB.Position += correction * invMassB;
            }

            Vector2 VelocityA = objA?.Velocity ?? Vector2.Zero;
            Vector2 VelocityB = objB?.Velocity ?? Vector2.Zero;
            Vector2 relativeVelocity = VelocityB - VelocityA;
            float relVelN = Vector2.Dot(relativeVelocity, Normal);
            if (relVelN > 0f)
                return;

            if (denom <= 1e-8f)
                return; // both static or infinite mass guard

            float e = Math.Clamp(
                MathF.Min(objA?.Restitution ?? 0f, objB?.Restitution ?? 0f),
                0f,
                1f
            );
            float j = -(1f + e) * relVelN / denom;
            Vector2 impulse = j * Normal;

            if (objA != null && invMassA > 0f)
                objA.Velocity -= impulse * invMassA;
            if (objB != null && invMassB > 0f)
                objB.Velocity += impulse * invMassB;

            // Recompute relative velocity after normal impulse for friction accuracy
            VelocityA = objA?.Velocity ?? Vector2.Zero;
            VelocityB = objB?.Velocity ?? Vector2.Zero;
            relativeVelocity = VelocityB - VelocityA;

            Vector2 Tangent = relativeVelocity - (Vector2.Dot(relativeVelocity, Normal) * Normal);
            float TangentLenSq = Tangent.LengthSquared();
            if (TangentLenSq > 1e-12f)
            {
                Tangent /= MathF.Sqrt(TangentLenSq);
                float muA = objA?.Friction ?? 0.5f;
                float muB = objB?.Friction ?? 0.5f;
                float FrictionCoefficient = Math.Clamp(MathF.Min(muA, muB), 0f, 1f);

                float vRelT = Vector2.Dot(relativeVelocity, Tangent);
                float TangentialImpulse = -vRelT / denom;

                float maxFriction = FrictionCoefficient * MathF.Abs(j);
                if (TangentialImpulse > maxFriction)
                    TangentialImpulse = maxFriction;
                else if (TangentialImpulse < -maxFriction)
                    TangentialImpulse = -maxFriction;

                Vector2 frictionImpulse = TangentialImpulse * Tangent;

                if (objA != null && invMassA > 0f)
                    objA.Velocity -= frictionImpulse * invMassA;
                if (objB != null && invMassB > 0f)
                    objB.Velocity += frictionImpulse * invMassB;
            }

            const float verticalNormal = 0.6f;
            const float tinyDown = 0.005f;
            if (Normal.Y > verticalNormal)
            {
                if (objA != null)
                {
                    objA.IsGrounded = true;
                    // Zero only small downward drift; allow small upward motion for realistic minor rebounds
                    if (objA.Velocity.Y > 0f && objA.Velocity.Y < tinyDown)
                        objA.Velocity = new Vector2(objA.Velocity.X, 0f);
                }
            }
            else if (Normal.Y < -verticalNormal)
            {
                if (objB != null)
                {
                    objB.IsGrounded = true;
                    // Zero only small downward drift; allow small upward motion for realistic minor rebounds
                    if (objB.Velocity.Y > 0f && objB.Velocity.Y < tinyDown)
                        objB.Velocity = new Vector2(objB.Velocity.X, 0f);
                }
            }
        }

        public void Objects()
        {
            for (int i = 0; i < 1; i++)
            {
                var Cube1 = CubeFactory.CreateCube(GD, 0.05f);
                Cube1.Position = new Vector2(0.5f, -1f);
                Cube1.Mass = 10f;

                Cube1.AddAttribute(
                    new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: Cube1.Mass)
                );
                Cube1.AddAttribute(new ObjectCollision() { Mass = Cube1.Mass, Restitution = 0.4f });
                Cube1.AddAttribute(new EdgeCollision(loop: false));

                _UpdateManager.Register(Cube1);
                _Meshes.Add(Cube1);
                _Grid.AddMesh(Cube1);
            }

            var Cube2 = CubeFactory.CreateCube(GD, 0.05f);
            Cube2.Position = new Vector2(-0.5f, -0.9f);
            Cube2.Mass = 500f;

            //Cube2.AddAttribute(new Gravity(acceleration: 9.81f, initialVelocity: 0f, mass: Cube2.Mass));
            Cube2.AddAttribute(new ObjectCollision() { Mass = Cube2.Mass, Restitution = 0.8f });
            Cube2.AddAttribute(new AtomicWiggle(amplitude: 0.0005f, frequency: 50f));
            Cube2.AddAttribute(new EdgeCollision(loop: false));

            _UpdateManager.Register(Cube2);
            _Meshes.Add(Cube2);
            _Grid.AddMesh(Cube2);
        }
    }
}
