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
        private const int MaxFrames = 500;
        private const float SimulationDeltaTime = 1f / 60f;  // Physics step size (fixed for stability)
        private const float PlaybackFps = 60f;               // Playback speed (independent of simulation)
        private readonly List<Entity> _entities = new();
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new(0.005f);
        private readonly List<Vector2[]> _tickPositions = new();
        private readonly Stopwatch _tickTimer = Stopwatch.StartNew();
        private readonly Stopwatch _playbackTimer = new();

        private int _tickIndex;
        private int _tickCounter;
        private bool _simulationComplete;
        private double _lastTickTime;

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
            WindowBounds.SetWindow(window);
        }

        protected override void Initialize()
        {
            CreateObjects("Steel", 1000, 0f, -1f);
            
            if (_particleSystems.Count > 0)
            {
                float radius = _particleSystems[0].Material.ParticleRadius;
                Renderer.InitializeInstancedRendering(radius, 8);
            }
        }

        protected override void Update(GameTime gameTime)
        {
            if (!_simulationComplete && _tickPositions.Count == 0)
            {
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

                // Pre-compute all frames using fixed timestep (not tied to framerate)
                for (int frame = 0; frame < MaxFrames; frame++)
                {
                    double tickStart = _tickTimer.Elapsed.TotalMilliseconds;
                    _tickCounter++;

                    // Always use fixed delta time for physics stability
                    GameTime.OverrideDeltaTime(SimulationDeltaTime);
                    _updateManager.Update(GameTime);

                    foreach (var ps in _particleSystems)
                        ps.StepFluid(SimulationDeltaTime, _grid);

                    _grid.UpdateAllAabb(_entities);
                    RunCollisionDetection(SimulationDeltaTime);
                    _tickPositions.Add(MathHelpers.CapturePositions(_entities));

                    double tickTime = _tickTimer.Elapsed.TotalMilliseconds - tickStart;
                    _lastTickTime = tickTime;

                    // Log statistics
                    var (avgSpeed, maxSpeed, _) = ParticleDynamics.GetVelocityStats(_entities);
                    var (avgPos, minY, maxY) = ParticleDynamics.GetPositionStats(_entities);
                    int groundedCount = ParticleDynamics.GetGroundedCount(_entities);
                    float kineticEnergy = ParticleDynamics.CalculateKineticEnergy(_entities);
                    float potentialEnergy = ParticleDynamics.CalculatePotentialEnergy(_entities);

                    Logger.LogCSV(
                        "Ticks",
                        _tickCounter,
                        tickTime,
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
                        $"Tick: {_tickCounter} | Time: {tickTime:F2}ms | AvgSpd: {avgSpeed:F4} | MaxSpd: {maxSpeed:F4} | AvgY: {avgPos.Y:F3}"
                    );
                }

                Logger.CloseAllCSV();
                Logger.Log($"Simulation complete: {_tickPositions.Count} frames recorded");
                _simulationComplete = true;
                _playbackTimer.Start();
            }

            GameTime.OverrideDeltaTime(gameTime.DeltaTime);
        }

        protected override void Draw()
        {
            Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

            int tickCount = _tickPositions.Count;
            if (tickCount == 0)
            {
                Renderer.EndFrame();
                return;
            }

            // Playback loop - uses PlaybackFps (independent of simulation step size)
            float elapsedSeconds = (float)_playbackTimer.Elapsed.TotalSeconds;
            float frameTime = 1f / PlaybackFps;
            float totalSimTime = tickCount * frameTime;
            float playbackTime = elapsedSeconds % totalSimTime;
            _tickIndex = (int)(playbackTime / frameTime);
            _tickIndex = Math.Clamp(_tickIndex, 0, tickCount - 1);

            var tickPositions = _tickPositions[_tickIndex];
            Renderer.DrawInstanced(_entities, tickPositions);
            Renderer.EndFrame();
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
