using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Veldrid;
using Veldrid.Sdl2;

namespace Engine13.Core
{
    public class Game : EngineBase
    {
        private readonly List<Mesh> _meshes = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new SpatialGrid(0.025f);
        private readonly List<Vector2[]> _tickPositions = new();
        private int _tickIndex;
        private int _bufferStart;
        private const int NumberOfTicks = 250;
        private const int BufferedFrames = 250;
        private const int StepsPerFrame = 1;

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
        }

        protected override void Initialize()
        {
            CreateObjects();
        }

        protected override void Update(GameTime gameTime)
        {
            const float targetFps = 60f;
            float simulationDuration = NumberOfTicks / targetFps;
            float stepDelta = simulationDuration / NumberOfTicks;

            if (_tickPositions.Count == _bufferStart)
            {
                for (int step = _tickPositions.Count; step < BufferedFrames; step++)
                {
                    GameTime.OverrideDeltaTime(stepDelta);
                    _updateManager.Update(GameTime);
                    _grid.UpdateAllAabb(_meshes);
                    RunCollisionDetection(stepDelta);

                    var snapshot = new Vector2[_meshes.Count];
                    for (int i = 0; i < _meshes.Count; i++)
                    {
                        snapshot[i] = _meshes[i].Position;
                    }

                    _tickPositions.Add(snapshot);
                    Console.WriteLine("Tick: " + step);
                    Console.WriteLine("TickTime: " + stepDelta);
                }

                if (_tickPositions.Count > 0)
                {
                    var initialPositions = _tickPositions[0];
                    int meshCount = Math.Min(_meshes.Count, initialPositions.Length);
                    for (int i = 0; i < meshCount; i++)
                    {
                        _meshes[i].Position = initialPositions[i];
                    }
                }

                _bufferStart = _tickPositions.Count - BufferedFrames;
            }

            GameTime.OverrideDeltaTime(gameTime.DeltaTime);
        }

        protected override void Draw()
        {
            int tickCount = _tickPositions.Count;
            if (tickCount == 0)
            {
                Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));
                Renderer.EndFrame();
                return;
            }

            _tickIndex %= tickCount;

            Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));
            var tickPositions = _tickPositions[_tickIndex];
            int count = Math.Min(_meshes.Count, tickPositions.Length);
            for (int i = 0; i < count; i++)
            {
                _meshes[i].Position = tickPositions[i];
                Renderer.DrawMesh(_meshes[i]);
            }
            Console.WriteLine(_tickIndex);
            Renderer.EndFrame();
            _tickIndex += StepsPerFrame;
            _tickIndex %= tickCount;
        }

        private void RunCollisionDetection(float stepDelta)
        {
            foreach (var mesh in _meshes)
            {
                var collision = mesh.GetAttribute<ObjectCollision>();
                if (collision != null)
                {
                    collision.IsGrounded = false;
                }
            }

            const int iterations = 12;
            for (int iter = 0; iter < iterations; iter++)
            {
                var collisionPairs = _grid.GetCollisionPairs();
                if (collisionPairs.Count == 0)
                {
                    break;
                }

                bool anyContacts = false;
                foreach (var pair in collisionPairs)
                {
                    if (CollisionInfo.AreColliding(pair.MeshA, pair.MeshB, out CollisionInfo collisionInfo))
                    {
                        anyContacts = true;
                        PhysicsSolver.ResolveCollision(collisionInfo, stepDelta);
                    }
                }

                if (!anyContacts)
                {
                    break;
                }

                if (iter < iterations - 1)
                {
                    _grid.UpdateAllAabb(_meshes);
                }
            }
        }

        private void CreateObjects()
        {
            const int particleCount = 1500;
            const int columns = 25;
            const float particleRadius = 0.01f;
            const float diameter = particleRadius * 2f;
            const float horizontalSpacing = diameter * 1.35f;
            const float verticalSpacing = diameter * 1.35f;
            var origin = new Vector2(-0.3f, -0.4f);

            _meshes.EnsureCapacity(_meshes.Count + particleCount + 1);

            for (int i = 0; i < particleCount; i++)
            {
                var particle = CircleFactory.CreateCircle(GraphicsDevice, particleRadius, 8, 8);
                int column = i % columns;
                int row = i / columns;
                particle.Position = new Vector2(
                    origin.X + column * horizontalSpacing,
                    origin.Y + row * verticalSpacing
                );
                particle.Mass = 100f;

                particle.AddAttribute(new Gravity(9.81f, 0f, particle.Mass));
                particle.AddAttribute(new ObjectCollision { Mass = particle.Mass, Restitution = 0.0f });
                particle.AddAttribute(new EdgeCollision(false));

                _updateManager.Register(particle);
                _meshes.Add(particle);
                _grid.AddMesh(particle);
            }

            var whiteQue = CircleFactory.CreateCircle(GraphicsDevice, 0.01f, 8, 8);
            whiteQue.Position = new Vector2(0.25f, 0f);
            whiteQue.Mass = 0.01f;
            whiteQue.AddAttribute(new ObjectCollision { IsStatic = true, Restitution = 0.5f });
            whiteQue.AddAttribute(new EdgeCollision(false));
            whiteQue.AddAttribute(new Gravity(9.81f, 0f, whiteQue.Mass));
            _updateManager.Register(whiteQue);
            _meshes.Add(whiteQue);
            _grid.AddMesh(whiteQue);
        }
    }
}
