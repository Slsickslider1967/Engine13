using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Engine13.Graphics;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Engine13.Utilities.JsonReader;
using Veldrid;
using Veldrid.Sdl2;

namespace Engine13.Core
{
    public class Game : EngineBase
    {
        private readonly List<Entity> _entities = new();
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new SpatialGrid(0.005f);
        private readonly List<Vector2[]> _tickPositions = new();
        private int _tickIndex;
        private int _bufferStart;
        private const int BufferedFrames = 250;
        private const int StepsPerFrame = 1;
        private int _tickCounter = 0;
        private readonly System.Diagnostics.Stopwatch _tickTimer =
            new System.Diagnostics.Stopwatch();
        private double _lastTickTime = 0;
        public const int ParticleCount = 1000;

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
            _tickTimer.Start();
            WindowBounds.SetWindow(window);
        }

        protected override void Initialize()
        {
            CreateObjects("Sand", ParticleCount, 0f, 0f, 0f, 0f);
            CreateObjects("Steel", ParticleCount, 0.5f, -0.1f, 0.2f, 0.1f);
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

                for (int step = _tickPositions.Count; step < BufferedFrames; step++)
                {
                    double currentTime = _tickTimer.Elapsed.TotalMilliseconds;
                    double timeBetweenTicks = currentTime - _lastTickTime;
                    _lastTickTime = currentTime;
                    _tickCounter++;

                    for (int i = 0; i < StepsPerFrame; i++)
                    {
                        GameTime.OverrideDeltaTime(stepDelta);
                        _updateManager.Update(GameTime);
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

        private void CreateObjects(string MaterialName = "Sand", int ParticleCount = 1000, float TopLeftX = -0.3f, float TopLeftY = -0.4f, float BottomRightX = 0.3f, float BottomRightY = 0.4f)
        {
            Vector2 TopLeftPos = new Vector2(TopLeftX, TopLeftY);
            Vector2 BottomRightPos = new Vector2(BottomRightX, BottomRightY);
            Vector2 AreaSize = BottomRightPos - TopLeftPos;

            _entities.EnsureCapacity(_entities.Count + ParticleCount + 1);
            var particleSystem = ParticleSystemFactory.Create(
                $"{MaterialName}Object",
                MaterialName
            );

            // Create all particles for this system
            particleSystem.CreateParticles(
                GraphicsDevice,
                ParticleCount,
                TopLeftPos,
                25,
                _entities,
                _updateManager,
                _grid
            );

            _particleSystems.Add(particleSystem);
        }

        /// <summary>
        /// Generates a short hash key for cache identification.
        /// </summary>
        public static string GenKey(
            ParticlePresetReader preset,
            int particleCount,
            int frameCount,
            float tickStep
        )
        {
            return GenerateHash(preset, particleCount, frameCount, tickStep)[..16];
        }

        /// <summary>
        /// Generates a full MD5 hash based on simulation parameters.
        /// </summary>
        public static string GenerateHash(
            ParticlePresetReader preset,
            int particleCount,
            int frameCount,
            float tickStep
        )
        {
            var sb = new StringBuilder();

            sb.Append($"preset:{preset.Name ?? "unknown"}_");
            sb.Append($"mass:{preset.Mass:F6}_");
            sb.Append($"radius:{preset.ParticleRadius:F6}_");
            sb.Append($"restitution:{preset.Restitution:F6}_");
            sb.Append($"gravity:{preset.GravityStrength:F6}_");

            if (preset.Composition is { Count: > 0 })
            {
                sb.Append("comp:");
                foreach (var comp in preset.Composition)
                    sb.Append($"{comp.ParticleType ?? "standard"}x{comp.Ratio}_");
            }

            sb.Append($"particles:{particleCount}_frames:{frameCount}_tickstep:{tickStep:F6}");

            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hashBytes).ToLower();
        }
    }
}
