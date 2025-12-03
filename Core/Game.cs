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
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new SpatialGrid(0.005f);
        private readonly List<Vector2[]> _tickPositions = new();
        private int _tickIndex;
        private int _bufferStart;
        private const int BufferedFrames = 250;
        private const int StepsPerFrame = 8;
        private int _tickCounter = 0;
        private readonly System.Diagnostics.Stopwatch _tickTimer = new System.Diagnostics.Stopwatch();
        private double _lastTickTime = 0;
        public const int ParticleCount = 500;

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
            _tickTimer.Start();
            WindowBounds.SetWindow(window);
        }

        protected override void Initialize()
        {
            CreateObjects();
        }

        protected override void Update(GameTime gameTime)
        {
            const float targetFps = 120f; // Higher FPS = smaller time steps
            float stepDelta = MathHelpers.ComputeStepDelta(targetFps);

            if (_tickPositions.Count == _bufferStart)
            {
                // Initialize CSV logs
                Logger.InitCSV(
                    "Ticks",
                    "TickCount",
                    "TickTime(ms)",
                    "Average KineticEnergy",
                    "Average PotentialEnergy",
                    "Average TotalEnergy",
                    "TotalEnergy"
                );

                var detailTimer = new System.Diagnostics.Stopwatch();
                double updateTime = 0,
                    gridTime = 0,
                    collisionTime = 0,
                    energyTime = 0;

                for (int step = _tickPositions.Count; step < BufferedFrames; step++)
                {
                    double currentTime = _tickTimer.Elapsed.TotalMilliseconds;
                    double timeBetweenTicks = currentTime - _lastTickTime;
                    _lastTickTime = currentTime;
                    _tickCounter++;

                    for (int i = 0; i < StepsPerFrame; i++)
                    {
                        GameTime.OverrideDeltaTime(stepDelta);

                        detailTimer.Restart();
                        _updateManager.Update(GameTime);
                        detailTimer.Stop();
                        updateTime += detailTimer.Elapsed.TotalMilliseconds;

                        detailTimer.Restart();
                        _grid.UpdateAllAabb(_entities);
                        detailTimer.Stop();
                        gridTime += detailTimer.Elapsed.TotalMilliseconds;

                        detailTimer.Restart();
                        RunCollisionDetection(stepDelta);
                        detailTimer.Stop();
                        collisionTime += detailTimer.Elapsed.TotalMilliseconds;

                        var snapshot = MathHelpers.CapturePositions(_entities);
                        _tickPositions.Add(snapshot);
                    }
                    // Calculate energies
                    detailTimer.Restart();
                    float kineticEnergy = MolecularDynamics.CalculateKineticEnergy(_entities);
                    float potentialEnergy = MolecularDynamics.CalculatePotentialEnergy(_entities);
                    detailTimer.Stop();
                    energyTime += detailTimer.Elapsed.TotalMilliseconds;

                    float totalEnergy = kineticEnergy + potentialEnergy;
                    // Log tick data to CSV
                    Logger.LogCSV(
                        "Ticks",
                        _tickCounter,
                        timeBetweenTicks,
                        kineticEnergy / ParticleCount,
                        potentialEnergy / ParticleCount,
                        totalEnergy / ParticleCount,
                        totalEnergy
                    );
                    Logger.Log(
                        $"Tick: {_tickCounter} | Time: {timeBetweenTicks:F2}ms | AVG KE: {kineticEnergy / ParticleCount:F2} | AVG PE: {potentialEnergy / ParticleCount:F2} | AVG Total: {totalEnergy / ParticleCount:F2} | Total: {totalEnergy:F2}"
                    );
                }

                Logger.CloseAllCSV();

                if (_tickPositions.Count > 0)
                {
                    var initialPositions = _tickPositions[0];
                    MathHelpers.ApplyPositionsToEntities(_entities, initialPositions);
                }

                _bufferStart = _tickPositions.Count - BufferedFrames;
            }

            GameTime.OverrideDeltaTime(gameTime.DeltaTime);
        }

        protected override void Draw()
        {
            int tickCount = _tickPositions.Count;
            //bool Run = false;
            if (tickCount == 0)
            {
                Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));
                Renderer.EndFrame();
                return;
            }

            _tickIndex = MathHelpers.WrapIndex(_tickIndex, tickCount);

            Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

            // while (!Run)
            // {
            //     _Input.Update();
            //     if (_Input.KeysDown(Key.Space))
            //     {
            //         Run = true;
            //     }
            // }
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
                {
                    collision.IsGrounded = false;
                }
            }

            const int iterations = 4; // Reduced from 20 - most collisions resolve in 2-3 iterations
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
                    // Skip collision for bonded particles - bonds handle their separation
                    if (MolecularDynamics.IsBonded(pair.EntityA, pair.EntityB))
                        continue;

                    if (
                        CollisionInfo.AreColliding(
                            pair.EntityA,
                            pair.EntityB,
                            out CollisionInfo collisionInfo
                        )
                    )
                    {
                        anyContacts = true;
                        PhysicsSolver.ResolveCollision(collisionInfo, stepDelta);
                    }
                }

                if (!anyContacts)
                {
                    break;
                }

                // Only update grid every other iteration to reduce overhead
                if (iter < iterations - 1 && iter % 2 == 1)
                {
                    _grid.UpdateAllAabb(_entities);
                }
            }
        }

        private void CreateObjects()
        {
            const int columns = 25;
            var origin = new Vector2(-0.3f, -0.4f);

            _entities.EnsureCapacity(_entities.Count + ParticleCount + 1);

            var preset = MDPresetReader.Load("Wood");

            // Log preset configuration
            Logger.Log($"Loaded MD Preset: {preset.Name}");
            Logger.Log(
                $"  Bonds: k={preset.BondSpringConstant:F0}, eq={preset.BondEquilibriumLength:F4}, cutoff={preset.BondCutoffDistance:F4}"
            );
            Logger.Log(
                $"  Forces: maxForce={preset.MaxForceMagnitude:F0}, restitution={preset.Restitution:F2}"
            );

            float particleRadius = preset.ParticleRadius;

            float diameter = particleRadius * 2f;
            float horizontalSpacing = diameter * 1.15f;
            float verticalSpacing = diameter * 1.15f;

            // Log particle info
            Logger.Log(
                $"Creating {ParticleCount} {preset.Name} particles (radius={particleRadius:F4}, mass={preset.Mass:F3})"
            );

            if (preset.Composition != null && preset.Composition.Count > 0)
            {
                int totalRatio = preset.Composition.Sum(c => Math.Max(1, c.Ratio));
                var compCounts = new List<int>(preset.Composition.Count);
                int remaining = ParticleCount;

                // Compute per-composition counts (last gets remainder to ensure sum==particleCount)
                for (int ci = 0; ci < preset.Composition.Count; ci++)
                {
                    var c = preset.Composition[ci];
                    int count =
                        (ci == preset.Composition.Count - 1)
                            ? remaining
                            : (int)Math.Round((double)ParticleCount * c.Ratio / totalRatio);
                    compCounts.Add(Math.Max(0, count));
                    remaining -= count;
                }

                int index = 0;
                for (int ci = 0; ci < preset.Composition.Count; ci++)
                {
                    var comp = preset.Composition[ci];
                    int compCount = compCounts[ci];

                    for (int k = 0; k < compCount; k++, index++)
                    {
                        int i = index;
                        var particle = CircleFactory.CreateCircle(
                            GraphicsDevice,
                            particleRadius,
                            8,
                            8
                        );
                        int column = i % columns;
                        int row = i / columns;
                        particle.Position = new Vector2(
                            origin.X + column * horizontalSpacing,
                            origin.Y + row * verticalSpacing
                        );

                        // Use actual particle radius for collision (not inflated)
                        particle.CollisionRadius = particleRadius;
                        particle.Mass = preset.Mass;

                        particle.AddComponent(
                            new Gravity(preset.GravityStrength, 0f, particle.Mass)
                        );
                        particle.AddComponent(
                            new ObjectCollision
                            {
                                Mass = particle.Mass,
                                Restitution = preset.Restitution,
                                Friction = preset.Friction,
                            }
                        );

                        particle.AddComponent(
                            preset.EnableEdgeCollision
                                ? new EdgeCollision(false)
                                : new EdgeCollision(true)
                        );

                        MolecularDynamics molecularDynamics;
                        string mdType = comp.MDType?.ToLowerInvariant() ?? "solid";
                        switch (mdType)
                        {
                            case "ion":
                                float charge = comp.Charge.HasValue
                                    ? comp.Charge.Value
                                    : (preset.Charge ?? 0f);
                                molecularDynamics = MolecularDynamics.CreateIon(_entities, charge);
                                break;
                            case "water":
                                molecularDynamics = MolecularDynamics.CreateWaterMolecule(
                                    _entities
                                );
                                break;
                            case "gas":
                                molecularDynamics = MolecularDynamics.CreateGasParticle(_entities);
                                break;
                            case "liquid":
                                molecularDynamics = MolecularDynamics.CreateLiquidParticle(
                                    _entities
                                );
                                break;
                            case "solid":
                            default:
                                molecularDynamics = MolecularDynamics.CreateSolidParticle(
                                    _entities
                                );
                                break;
                        }

                        preset.ApplyTo(molecularDynamics);
                        if (comp.Charge.HasValue)
                            molecularDynamics.Charge = comp.Charge.Value;

                        // Log first particle MD values for verification
                        if (i == 0)
                        {
                            Logger.Log(
                                $"  MD Applied: bonds={molecularDynamics.EnableBonds}, LJ={molecularDynamics.EnableLennardJones}, damping={molecularDynamics.VelocityDamping:F2}"
                            );
                        }

                        particle.AddComponent(molecularDynamics);

                        _updateManager.Register(particle);
                        _entities.Add(particle);
                        _grid.AddEntity(particle);
                    }
                }
            }
            else
            {
                for (int i = 0; i < ParticleCount; i++)
                {
                    var particle = CircleFactory.CreateCircle(GraphicsDevice, particleRadius, 8, 8);
                    int column = i % columns;
                    int row = i / columns;
                    particle.Position = new Vector2(
                        origin.X + column * horizontalSpacing,
                        origin.Y + row * verticalSpacing
                    );

                    // Use actual particle radius for collision (not inflated)
                    particle.CollisionRadius = particleRadius;
                    particle.Mass = preset.Mass;

                    particle.AddComponent(new Gravity(preset.GravityStrength, 0f, particle.Mass));
                    particle.AddComponent(
                        new ObjectCollision
                        {
                            Mass = particle.Mass,
                            Restitution = preset.Restitution,
                            Friction = 0.1f,
                        }
                    );

                    particle.AddComponent(
                        preset.EnableEdgeCollision
                            ? new EdgeCollision(false)
                            : new EdgeCollision(true)
                    );

                    var molecularDynamics = MolecularDynamics.CreateSolidParticle(_entities);
                    preset.ApplyTo(molecularDynamics);

                    // Log first particle MD values for verification
                    if (i == 0)
                    {
                        Logger.Log(
                            $"  MD Applied: bonds={molecularDynamics.EnableBonds}, LJ={molecularDynamics.EnableLennardJones}, damping={molecularDynamics.VelocityDamping:F2}"
                        );
                    }

                    particle.AddComponent(molecularDynamics);

                    _updateManager.Register(particle);
                    _entities.Add(particle);
                    _grid.AddEntity(particle);
                }
            }
        }

        public static string GenKey(
            MDPresetReader Preset,
            int ParticleCount,
            int frameCount,
            float tickstep
        )
        {
            string fullHash = GenerateHash(Preset, ParticleCount, frameCount, tickstep);
            return fullHash.Substring(0, 16);
        }

        public static string GenerateHash(
            MDPresetReader preset,
            int particleCount,
            int frameCount,
            float tickStep
        )
        {
            var sb = new StringBuilder();

            // Add preset identifier
            sb.Append($"preset:{preset.Name ?? "unknown"}_");
            sb.Append($"mass:{preset.Mass:F6}_");
            sb.Append($"radius:{preset.ParticleRadius:F6}_");
            sb.Append($"restitution:{preset.Restitution:F6}_");
            sb.Append($"gravity:{preset.GravityStrength:F6}_");

            // Add composition if available
            if (preset.Composition != null && preset.Composition.Count > 0)
            {
                sb.Append("comp:");
                foreach (var comp in preset.Composition)
                {
                    sb.Append($"{comp.MDType ?? "solid"}x{comp.Ratio}_");
                }
            }

            // Add simulation parameters
            sb.Append($"particles:{particleCount}_");
            sb.Append($"frames:{frameCount}_");
            sb.Append($"tickstep:{tickStep:F6}");

            // Generate MD5 hash
            using (var md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToHexString(hashBytes).ToLower();
            }
        }
    }
}
