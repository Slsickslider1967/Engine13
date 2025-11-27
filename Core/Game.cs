using System.Numerics;
using Engine13.Graphics;
using Engine13.Input;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Veldrid;
using Veldrid.Sdl2;

namespace Engine13.Core
{
    public class Game : EngineBase
    {
        private readonly List<Entity> _entities = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new SpatialGrid(0.025f);
        private readonly List<Vector2[]> _tickPositions = new();
        private int _tickIndex;
        private int _bufferStart;
        private const int BufferedFrames = 250;
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
            _tickIndex += StepsPerFrame;
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

            const int iterations = 50;
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
            const float particleRadius = 0.01f;
            const float diameter = particleRadius * 2f;
            const float horizontalSpacing = diameter * 1.35f;
            const float verticalSpacing = diameter * 1.35f;
            var origin = new Vector2(-0.3f, -0.4f);

            _entities.EnsureCapacity(_entities.Count + particleCount + 1);

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

                particle.AddComponent(new Gravity(9.81f, 0f, particle.Mass));
                particle.AddComponent(
                    new ObjectCollision { Mass = particle.Mass, Restitution = 0.0f }
                );
                particle.AddComponent(new EdgeCollision(false));

                // var molecularDynamics = MolecularDynamics.CreateLiquidParticle(_entities);
                // molecularDynamics.BondSpringConstant = 15f;
                // molecularDynamics.BondEquilibriumLength = diameter;
                // molecularDynamics.BondCutoffDistance = diameter * 1.25f;
                // molecularDynamics.LJ_Epsilon = 0.0025f;
                // molecularDynamics.LJ_Sigma = diameter;
                // molecularDynamics.LJ_CutoffRadius = diameter * 2f;
                // molecularDynamics.MaxForceMagnitude = 12.5f;
                // particle.AddComponent(molecularDynamics);

                var molecularDynamics = MolecularDynamics.CreateSolidParticle(_entities);
                molecularDynamics.BondSpringConstant = 2000f;          // Much stiffer!
                //molecularDynamics.BondDampingConstant = 100f;          // Strong damping for stability
                molecularDynamics.BondEquilibriumLength = diameter;
                molecularDynamics.BondCutoffDistance = diameter * 1.05f; // Very tight cutoff
                molecularDynamics.LJ_Epsilon = 0.05f;                  // Much stronger repulsion
                molecularDynamics.LJ_Sigma = diameter * 0.9f;          // Tighter packing
                molecularDynamics.LJ_CutoffRadius = diameter * 1.3f;
                molecularDynamics.MaxForceMagnitude = 200f;            // Much higher force limit
                particle.AddComponent(molecularDynamics);

                _updateManager.Register(particle);
                _entities.Add(particle);
                _grid.AddEntity(particle);
            }
        }
    }
}
