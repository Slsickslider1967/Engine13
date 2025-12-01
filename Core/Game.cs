using System.Numerics;
using Engine13.Graphics;
using Engine13.Input;
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
        private readonly SpatialGrid _grid = new SpatialGrid(0.0025f);
        private readonly List<Vector2[]> _tickPositions = new();
        private int _tickIndex;
        private int _bufferStart;
        private const int BufferedFrames = 500;
        private const int StepsPerFrame = 2;
        private int _tickCounter = 0;
        private System.Diagnostics.Stopwatch _tickTimer = new System.Diagnostics.Stopwatch();
        private double _lastTickTime = 0;
        private InputManager _Input = new InputManager();

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
            const float targetFps = 60f;
            float stepDelta = MathHelpers.ComputeStepDelta(targetFps);

            if (_tickPositions.Count == _bufferStart)
            {
                // Initialize CSV logs
                Logger.InitCSV(
                    "Ticks",
                    "TickCount",
                    "TickTime(ms)",
                    "KineticEnergy",
                    "PotentialEnergy",
                    "TotalEnergy"
                );

                for (int step = _tickPositions.Count; step < BufferedFrames; step++)
                {
                    double currentTime = _tickTimer.Elapsed.TotalMilliseconds;
                    double timeBetweenTicks = currentTime - _lastTickTime;
                    _lastTickTime = currentTime;

                    _tickCounter++;

                    GameTime.OverrideDeltaTime(stepDelta);
                    _updateManager.Update(GameTime);
                    _grid.UpdateAllAabb(_entities);
                    RunCollisionDetection(stepDelta);

                    var snapshot = MathHelpers.CapturePositions(_entities);
                    _tickPositions.Add(snapshot);

                    // Calculate energies
                    float kineticEnergy = MolecularDynamics.CalculateKineticEnergy(_entities);
                    float potentialEnergy = MolecularDynamics.CalculatePotentialEnergy(_entities);
                    float totalEnergy = kineticEnergy + potentialEnergy;

                    // Log tick data with energy
                    Logger.LogCSV(
                        "Ticks",
                        _tickCounter,
                        timeBetweenTicks,
                        kineticEnergy,
                        potentialEnergy,
                        totalEnergy
                    );
                    Console.WriteLine(
                        $"Tick: {_tickCounter} | Time: {timeBetweenTicks:F2}ms | KE: {kineticEnergy:F2} | PE: {potentialEnergy:F2} | Total: {totalEnergy:F2}"
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
            bool Run = false;
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
            Console.WriteLine(_tickIndex);
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

            const int iterations = 20;
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

                if (iter < iterations - 1)
                {
                    _grid.UpdateAllAabb(_entities);
                }
            }
        }

        private void CreateObjects()
        {
            const int particleCount = 500;
            const int columns = 25;
            var origin = new Vector2(-0.3f, -0.4f);

            _entities.EnsureCapacity(_entities.Count + particleCount + 1);

            var preset = MDPresetReader.Load("Water");

            // PARTICLE-SCALE: Each entity is a macroscopic particle, not an atomd
            // Use preset-defined radius, not atomic calculations
            float particleRadius = preset.ParticleRadius;

            float diameter = particleRadius * 2f;
            // Increase spacing to 1.15x so bonds can work without constant collisions
            float horizontalSpacing = diameter * 1.15f;
            float verticalSpacing = diameter * 1.15f;



            // Log particle info
            Console.WriteLine($"Creating {preset.Name} particles:");
            Console.WriteLine($"  Particle Type: {preset.Description}");
            Console.WriteLine($"  Particle Mass: {preset.Mass:F3} units");
            Console.WriteLine($"  Particle Radius: {particleRadius:F4} units");
            Console.WriteLine($"  Total Particles: {particleCount}");

            // If preset defines a composition, allocate particle counts per composition ratios.
            if (preset.Composition != null && preset.Composition.Count > 0)
            {
                int totalRatio = preset.Composition.Sum(c => Math.Max(1, c.Ratio));
                var compCounts = new List<int>(preset.Composition.Count);
                int remaining = particleCount;

                // Compute per-composition counts (last gets remainder to ensure sum==particleCount)
                for (int ci = 0; ci < preset.Composition.Count; ci++)
                {
                    var c = preset.Composition[ci];
                    int count =
                        (ci == preset.Composition.Count - 1)
                            ? remaining
                            : (int)Math.Round((double)particleCount * c.Ratio / totalRatio);
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

                        particle.CollisionRadius = particleRadius * 1.5f;
                        particle.Mass = preset.Mass;

                        particle.AddComponent(
                            new Gravity(preset.GravityStrength, 0f, particle.Mass)
                        );
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

                        // Choose MD component based on composition MDType
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

                        // Apply top-level preset settings, then allow composition-specific overrides
                        preset.ApplyTo(molecularDynamics);
                        if (comp.Charge.HasValue)
                            molecularDynamics.Charge = comp.Charge.Value;

                        molecularDynamics.BondEquilibriumLength = horizontalSpacing;
                        molecularDynamics.BondCutoffDistance = horizontalSpacing * 1.5f;
                        molecularDynamics.LJ_Sigma = diameter * 0.9f;
                        molecularDynamics.LJ_CutoffRadius = diameter * 2.0f;

                        particle.AddComponent(molecularDynamics);

                        _updateManager.Register(particle);
                        _entities.Add(particle);
                        _grid.AddEntity(particle);
                    }
                }
            }
            else
            {
                for (int i = 0; i < particleCount; i++)
                {
                    var particle = CircleFactory.CreateCircle(GraphicsDevice, particleRadius, 8, 8);
                    int column = i % columns;
                    int row = i / columns;
                    particle.Position = new Vector2(
                        origin.X + column * horizontalSpacing,
                        origin.Y + row * verticalSpacing
                    );

                    particle.CollisionRadius = particleRadius * 1.5f;
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

                    molecularDynamics.BondEquilibriumLength = horizontalSpacing;
                    molecularDynamics.BondCutoffDistance = horizontalSpacing * 3.0f;  
                    molecularDynamics.LJ_Sigma = diameter * 0.9f;
                    molecularDynamics.LJ_CutoffRadius = diameter * 2.0f;

                    particle.AddComponent(molecularDynamics);

                    _updateManager.Register(particle);
                    _entities.Add(particle);
                    _grid.AddEntity(particle);
                }
            }
        }

        public static string GenKey(string Preset,  int ParticleCount, int frameCount, int tickstep)
        {
            string fullHash = GenerateHash(Preset, ParticleCount, frameCount, tickstep);
            return fullHash.Substring(0, 16);
        }

        public static string GenerateHash(string preset, int particleCount, int frameCount, float tickStep)
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
