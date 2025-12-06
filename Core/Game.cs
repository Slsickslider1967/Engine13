using System.Diagnostics;
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
        private const int Frames = 250;
        private const int StepsPerFrame = 1;

        private readonly List<Entity> _entities = new();
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new(0.05f);
        private readonly List<Vector2[]> _tickPositions = new();
        private readonly Stopwatch _tickTimer = Stopwatch.StartNew();

        private int _tickIndex;
        private int _bufferStart;
        private int _tickCounter;
        private double _lastTickTime;

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
            WindowBounds.SetWindow(window);
        }

        protected override void Initialize()
        {
            CreateObjects("Water", 2000, -0.5f, -0.5f);
        }

        protected override void Update(GameTime gameTime)
        {
            const float targetFps = 120f;
            float stepDelta = MathHelpers.ComputeStepDelta(targetFps);

            if (_tickPositions.Count == _bufferStart)
            {
                // Initialize CSV logs with more useful headers
                Logger.InitCSV(
                    "Ticks",
                    "TickCount",
                    "TickTime(ms)",
                    "AvgSpeed",
                    "MaxSpeed",
                    "AvgPosY",
                    "MinPosY",
                    "MaxPosY",
                    "GroundedCount",
                    "TotalKE",
                    "TotalPE"
                );

                for (int step = _tickPositions.Count; step < Frames; step++)
                {
                    double currentTime = _tickTimer.Elapsed.TotalMilliseconds;
                    double timeBetweenTicks = currentTime - _lastTickTime;
                    _lastTickTime = currentTime;
                    _tickCounter++;

                    for (int i = 0; i < StepsPerFrame; i++)
                    {
                        GameTime.OverrideDeltaTime(stepDelta);
                        _updateManager.Update(GameTime);
                        
                        foreach (var ps in _particleSystems)
                            ps.StepFluid(stepDelta, _grid);
                        
                        _grid.UpdateAllAabb(_entities);
                        RunCollisionDetection(stepDelta);
                        _tickPositions.Add(MathHelpers.CapturePositions(_entities));
                    }

                    // Calculate useful statistics
                    var (avgSpeed, maxSpeed, minSpeed) = ParticleDynamics.GetVelocityStats(_entities);
                    var (avgPos, minY, maxY) = ParticleDynamics.GetPositionStats(_entities);
                    int groundedCount = ParticleDynamics.GetGroundedCount(_entities);
                    float kineticEnergy = ParticleDynamics.CalculateKineticEnergy(_entities);
                    float potentialEnergy = ParticleDynamics.CalculatePotentialEnergy(_entities);

                    Logger.LogCSV(
                        "Ticks",
                        _tickCounter,
                        timeBetweenTicks,
                        avgSpeed,
                        maxSpeed,
                        avgPos.Y,
                        minY,
                        maxY,
                        groundedCount,
                        kineticEnergy,
                        potentialEnergy
                    );
                    Logger.Log(
                        $"Tick: {_tickCounter} | Time: {timeBetweenTicks:F2}ms | AvgSpd: {avgSpeed:F4} | MaxSpd: {maxSpeed:F4} | AvgY: {avgPos.Y:F3} | Grounded: {groundedCount}/{_entities.Count}"
                    );
                }

                Logger.CloseAllCSV();

                if (_tickPositions.Count > 0)
                    MathHelpers.ApplyPositionsToEntities(_entities, _tickPositions[0]);

                _bufferStart = _tickPositions.Count - Frames;
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

            _tickIndex = MathHelpers.WrapIndex(_tickIndex, tickCount);
            Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

            var tickPositions = _tickPositions[_tickIndex];
            int count = Math.Min(_entities.Count, tickPositions.Length);
            for (int i = 0; i < count; i++)
            {
                _entities[i].Position = tickPositions[i];
                Renderer.DrawMesh(_entities[i]);
            }
            Renderer.EndFrame();
            _tickIndex += 1;
            _tickIndex = MathHelpers.WrapIndex(_tickIndex, tickCount);
        }

        private void RunCollisionDetection(float stepDelta)
        {
            foreach (var entity in _entities)
            {
                var collision = entity.GetComponent<ObjectCollision>();
                if (collision != null)
                    collision.IsGrounded = false;
            }

            const int iterations = 4;
            for (int iter = 0; iter < iterations; iter++)
            {
                var collisionPairs = _grid.GetCollisionPairs();
                if (collisionPairs.Count == 0)
                    break;

                bool anyContacts = false;
                foreach (var pair in collisionPairs)
                {
                    var objA = pair.EntityA.GetComponent<ObjectCollision>();
                    var objB = pair.EntityB.GetComponent<ObjectCollision>();
                    if (objA != null && objB != null && objA.IsFluid && objB.IsFluid)
                        continue;

                    if (
                        CollisionInfo.AreColliding(
                            pair.EntityA,
                            pair.EntityB,
                            out var collisionInfo
                        )
                    )
                    {
                        anyContacts = true;
                        PhysicsSolver.ResolveCollision(collisionInfo, stepDelta);
                    }
                }

                if (!anyContacts)
                    break;

                if (iter < iterations - 1 && iter % 2 == 1)
                    _grid.UpdateAllAabb(_entities);
            }
        }

        private void CreateObjects(
            string materialName = "Sand",
            int particleCount = 1000,
            float topLeftX = -0.3f,
            float topLeftY = -0.4f,
            float bottomRightX = 0.3f,
            float bottomRightY = 0.4f)
        {
            var topLeftPos = new Vector2(topLeftX, topLeftY);

            var particleSystem = ParticleSystemFactory.Create(
                $"{materialName}Object",
                materialName
            );

            float areaSize = (bottomRightX - topLeftX) * (bottomRightY - topLeftY);
            float radius = particleSystem.Material.ParticleRadius;
            int totalParticlesPerArea = (int)(areaSize / (MathF.PI * radius * radius));

            _entities.EnsureCapacity(_entities.Count + particleCount + 1);

            particleSystem.CreateParticles(
                GraphicsDevice,
                particleCount,
                topLeftPos,
                25,
                _entities,
                _updateManager,
                _grid
            );

            if (particleSystem.Material.IsFluid)
            {
                particleSystem.InitializeFluid();
                Logger.Log($"[Fluid] Initialized {particleSystem.Particles.Count} particles");
            }

            _particleSystems.Add(particleSystem);
        }
    }
}
