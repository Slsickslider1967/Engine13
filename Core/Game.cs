using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Engine13.Graphics;
using Engine13.Input;
using Engine13.Primitives;
using Engine13.UI;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Engine13.Utilities.JsonReader;
using ImGuiNET;
using SharpGen.Runtime.Win32;
using Veldrid;
using Veldrid.OpenGLBinding;
using Veldrid.Sdl2;
using Vulkan;

/// <summary>
/// This is the big main file where most of the game logic lives and runs.
/// It handles initializing the simulation, running the precomputation of frames,
/// and then playing back the recorded frames with UI controls.
/// </summary>
namespace Engine13.Core
{
    public class Game : EngineBase
    {
        private int MaxFrames = 500;
        private const float SimulationDeltaTime = 1f / 60f; // Physics step size (fixed for stability)
        private float PlaybackFps = 60f;

        private readonly List<Entity> _entities = new();
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new(0.025f); // Increased from 0.005 to 4x typical particle radius
        private readonly List<Vector2[]> _tickPositions = new();
        private readonly Stopwatch _tickTimer = Stopwatch.StartNew();
        private readonly Stopwatch _playbackTimer = new();
        private float _playbackStartOffsetSeconds = 0f; // accumulated playback position when paused

        private int _tickIndex;
        private int _tickCounter;
        private bool _simulationComplete;
        private double _lastTickTime;

        private float _lastAvgSpeed;
        private float _lastMaxSpeed;
        private Vector2 _lastAvgPos;
        private int _lastGroundedCount;
        private bool _showStartWindow = true;
        private bool _startRequested = false;
        private bool _showGuiDebug = true;

        private InputManager _inputManager;
        private ImGuiController? _imgui;
        private CsvPlotter? _csvPlotter;

        private int _selectionPresetIndex = 0;
        private string _selectionMaterial = "Sand";

        private readonly object _tickLock = new();
        private Task? _precomputeTask;
        private bool _isPrecomputing;

        private bool SimulationWindow = false;
        private bool PrecomputeWindow = false;
        private bool HasPrecomputeRun = false;
        private bool _showGraphInWindow = false;
        private bool _startRunningImmediately = true;

        private bool _overlayDensity;
        private bool _overlayPressure;
        private bool _overlayNeighbors;
        private float _overlayMin = 0f;
        private float _overlayMax = 1f;
        private Vector4[] _overlayColors = Array.Empty<Vector4>();
        private readonly Dictionary<Entity, int> _entityIndexMap = new();

        private string PrecomputeName = "Open Precompute Window";
        private string EditorName = "Open Editor Window";

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
            WindowBounds.SetWindow(window);
            _inputManager = new InputManager(window);
        }

        protected override void Initialize()
        {
            float defaultRadius = 0.01f;
            Renderer.InitializeInstancedRendering(defaultRadius, 8);

            _imgui = new ImGuiController(Window, GraphicsDevice, CommandList, _inputManager);
            _imgui.PrintDrawData = true;
            _updateManager.Register(MolecularDynamicsSystem.Instance);
            try
            {
                string csvPath = Path.Combine(Directory.GetCurrentDirectory(), "Ticks.csv");
                _csvPlotter = new CsvPlotter(csvPath);
            }
            catch
            {
                _csvPlotter = null;
            }
            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            _inputManager.Update();
            _imgui?.NewFrame(gameTime.DeltaTime);

            // Call UI methods from LoadGui
            LoadGui.DrawUI(
                ref _showStartWindow,
                ref PrecomputeWindow,
                ref HasPrecomputeRun,
                ref _showGraphInWindow,
                ref _startRunningImmediately,
                ref PrecomputeName,
                ref EditorName,
                ref _startRequested,
                ref MaxFrames,
                ref PlaybackFps,
                ref _tickIndex,
                ref _playbackStartOffsetSeconds,
                _tickCounter,
                _lastTickTime,
                _lastAvgSpeed,
                _lastMaxSpeed,
                _lastAvgPos,
                _lastGroundedCount,
                _particleSystems.Count,
                _entities.Count,
                _playbackTimer,
                _tickLock,
                _tickPositions,
                _csvPlotter,
                Stop,
                ClearAllParticles
            );

            LoadGui.DrawSelectionRect(
                ref _selectionPresetIndex,
                ref _selectionMaterial,
                _entities,
                CreateObjectsWrapper,
                RemoveParticleSystemInSelection
            );

            if (!_simulationComplete && _tickPositions.Count == 0)
            {
                if (!_startRequested && !_isPrecomputing)
                {
                    GameTime.OverrideDeltaTime(gameTime.DeltaTime);
                    return;
                }

                if (_startRequested && !_isPrecomputing)
                {
                    _startRequested = false;
                    _isPrecomputing = true;
                    _tickTimer.Restart();
                    _precomputeTask = Task.Run(() => RunPrecompute());
                }

                GameTime.OverrideDeltaTime(gameTime.DeltaTime);
                return;
            }

            GameTime.OverrideDeltaTime(gameTime.DeltaTime);
        }

        private void RunPrecompute()
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

            for (int frame = 0; frame < MaxFrames; frame++)
            {
                double tickStart = _tickTimer.Elapsed.TotalMilliseconds;

                GameTime.OverrideDeltaTime(SimulationDeltaTime);

                // Reset forces before any calculations
                Forces.Reset();

                foreach (var ps in _particleSystems)
                    ps.StepFluid(SimulationDeltaTime, _grid);

                _updateManager.Update(GameTime);

                foreach (var entity in _entities)
                {
                    var edgeCollision = entity.GetComponent<EdgeCollision>();
                    if (edgeCollision != null)
                        edgeCollision.Update(entity, GameTime);
                }

                _grid.UpdateAllAabb(_entities);
                RunCollisionDetection(SimulationDeltaTime);

                var positions = MathHelpers.CapturePositions(_entities);

                double tickTime = _tickTimer.Elapsed.TotalMilliseconds - tickStart;

                var (avgSpeed, maxSpeed, _) = ParticleDynamics.GetVelocityStats(_entities);
                var (avgPos, minY, maxY) = ParticleDynamics.GetPositionStats(_entities);
                int groundedCount = ParticleDynamics.GetGroundedCount(_entities);

                // Get SPH debug info for logging
                string sphDebug = "";
                var fluidSystem = _particleSystems.Find(ps => ps.Material.IsFluid);
                if (fluidSystem != null)
                {
                    var debugInfo = fluidSystem.GetSPHDebugInfo();
                    sphDebug =
                        $" | SPH: dens={debugInfo.avgDensity:F1}/{debugInfo.maxDensity:F1} press={debugInfo.avgPressure:F2} visc={debugInfo.avgViscosity:F3} nbr={debugInfo.avgNeighbors:F1} vel={debugInfo.avgVelocity:F3}/{debugInfo.maxVelocity:F3}";
                }

                lock (_tickLock)
                {
                    _tickPositions.Add(positions);
                    _tickCounter++;
                    _lastTickTime = tickTime;
                    _lastAvgSpeed = avgSpeed;
                    _lastMaxSpeed = maxSpeed;
                    _lastAvgPos = avgPos;
                    _lastGroundedCount = groundedCount;
                }

                Logger.LogCSV(
                    "Ticks",
                    _tickCounter,
                    tickTime,
                    avgSpeed,
                    maxSpeed,
                    avgPos.Y,
                    minY,
                    maxY,
                    groundedCount
                );

                Logger.Log(
                    $"Tick: {_tickCounter} | Time: {tickTime:F2}ms | AvgSpd: {avgSpeed:F4} | MaxSpd: {maxSpeed:F4} | AvgY: {avgPos.Y:F3}{sphDebug}"
                );
            }

            Logger.CloseAllCSV();
            Logger.Log($"Simulation complete: {_tickPositions.Count} frames recorded");

            lock (_tickLock)
            {
                _simulationComplete = true;
                _isPrecomputing = false;
                _showStartWindow = false;
            }
            try
            {
                string csvPath = Path.Combine(Directory.GetCurrentDirectory(), "Ticks.csv");
                if (_csvPlotter == null)
                    _csvPlotter = new CsvPlotter(csvPath);
                else
                    _csvPlotter.Load();

                Logger.Log($"CsvPlotter loaded after precompute: {csvPath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"CsvPlotter reload failed: {ex.Message}");
            }

            _playbackTimer.Start();
        }

        private void RemoveParticleSystemInSelection()
        {
            var SelectStart = LoadGui.GetSelectStart();
            var SelectEnd = LoadGui.GetSelectEnd();

            var winSize = WindowBounds.GetWindowSize();
            var bounds = WindowBounds.GetNormalizedBounds();
            float sx = winSize.X > 0 ? winSize.X : 1f;
            float sy = winSize.Y > 0 ? winSize.Y : 1f;

            float x0 = bounds.left + (bounds.right - bounds.left) * (SelectStart.X / sx);
            // Screen Y increases downward (0 at top), world Y increases downward too (top=-1, bottom=1)
            // So we map: screen 0 -> world -1, screen height -> world 1
            float y0 = bounds.top + (bounds.bottom - bounds.top) * (SelectStart.Y / sy);
            float x1 = bounds.left + (bounds.right - bounds.left) * (SelectEnd.X / sx);
            float y1 = bounds.top + (bounds.bottom - bounds.top) * (SelectEnd.Y / sy);

            float minX = MathF.Min(x0, x1);
            float maxX = MathF.Max(x0, x1);
            float minY = MathF.Min(y0, y1);
            float maxY = MathF.Max(y0, y1);

            ParticleDynamics.RemoveParticlesInArea(_entities, minX, minY, maxX, maxY);
        }

        private void ClearAllParticles()
        {
            ParticleDynamics.RemoveAllParticles(_entities);
        }

        protected override void Draw()
        {
            Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

            Vector2[]? tickPositions = null;
            Vector4[]? colors = null;
            int tickCount;
            lock (_tickLock)
            {
                tickCount = _tickPositions.Count;
                if (tickCount > 0)
                {
                    float frameTime = 1f / MathF.Max(PlaybackFps, 1f);
                    float totalSimTime = MathF.Max(frameTime, tickCount * frameTime);

                    float playbackTime = _playbackStartOffsetSeconds;
                    if (_playbackTimer.IsRunning)
                        playbackTime += (float)_playbackTimer.Elapsed.TotalSeconds;

                    // Wrap playback time to recorded range
                    playbackTime %= totalSimTime;

                    _tickIndex = Math.Clamp((int)(playbackTime / frameTime), 0, tickCount - 1);
                    tickPositions = _tickPositions[_tickIndex];
                }
            }

            if (tickCount == 0)
            {
                tickPositions = new Vector2[_entities.Count];
                for (int i = 0; i < _entities.Count; i++)
                    tickPositions[i] = _entities[i].Position;

                colors = BuildOverlayColors(tickPositions);
                Renderer.DrawInstanced(_entities, tickPositions, colors);
                _imgui?.Render(CommandList);
                Renderer.EndFrame();
                return;
            }

            if (tickPositions == null || tickPositions.Length < _entities.Count)
            {
                tickPositions = new Vector2[_entities.Count];
                for (int i = 0; i < _entities.Count; i++)
                    tickPositions[i] = _entities[i].Position;
            }

            colors = BuildOverlayColors(tickPositions);
            Renderer.DrawInstanced(_entities, tickPositions, colors);

            _imgui?.Render(CommandList);

            Renderer.EndFrame();
        }

        private Vector4[] BuildOverlayColors(Vector2[] positions)
        {
            int count = Math.Min(_entities.Count, positions.Length);
            if (_overlayColors.Length < count)
                Array.Resize(ref _overlayColors, count);

            for (int i = 0; i < count; i++)
                _overlayColors[i] = _entities[i].Colour;

            if (
                (!_overlayDensity && !_overlayPressure && !_overlayNeighbors)
                || _particleSystems.Count == 0
            )
                return _overlayColors;

            _entityIndexMap.Clear();
            for (int i = 0; i < count; i++)
                _entityIndexMap[_entities[i]] = i;

            var ps = _particleSystems.Find(p => p.Material.IsFluid);
            if (ps == null)
                return _overlayColors;

            int fluidCount = ps.FluidCount;
            if (fluidCount == 0)
                return _overlayColors;

            Span<float> dens = fluidCount <= 256 ? stackalloc float[256] : new float[fluidCount];
            Span<float> pres = fluidCount <= 256 ? stackalloc float[256] : new float[fluidCount];
            Span<int> neigh = fluidCount <= 256 ? stackalloc int[256] : new int[fluidCount];

            ps.GetFluidDebugData(_entityIndexMap, dens, pres, neigh);

            float minVal = _overlayMin;
            float maxVal = _overlayMax;
            if (maxVal <= minVal + 1e-6f)
                maxVal = minVal + 1f;
            float invRange = 1f / (maxVal - minVal);

            for (int i = 0; i < fluidCount; i++)
            {
                if (!ps.TryGetEntityAt(i, out var e))
                    continue;
                if (!_entityIndexMap.TryGetValue(e, out int idx))
                    continue;

                float v =
                    _overlayDensity ? dens[i]
                    : _overlayPressure ? pres[i]
                    : neigh[i];
                float t = Math.Clamp((v - minVal) * invRange, 0f, 1f);
                _overlayColors[idx] = MapGradient(t);
            }

            return _overlayColors;
        }

        private static Vector4 MapGradient(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            float r = t;
            float g = 1f - MathF.Abs(t - 0.5f) * 2f;
            float b = 1f - t;
            if (g < 0f)
                g = 0f;
            return new Vector4(r, g, b, 1f);
        }

        private void CreateObjectsWrapper(
            string materialName,
            int particleCount,
            float minX,
            float minY,
            float maxX,
            float maxY,
            int columns,
            Vector2 origin,
            float spacingFactor
        )
        {
            CreateObjects(
                materialName,
                particleCount,
                minX,
                minY,
                maxX,
                maxY,
                columns,
                origin,
                spacingFactor
            );
        }

        private void RunCollisionDetection(float stepDelta)
        {
            foreach (var entity in _entities)
            {
                var collision = entity.GetComponent<ObjectCollision>();
                if (collision != null)
                    collision.IsGrounded = false;
            }

            const int iterations = 30;

            for (int Itteration = 0; Itteration < iterations; Itteration++)
            {
                var collisionPairs = _grid.GetCollisionPairs();
                if (collisionPairs.Count == 0)
                    break;

                bool anyContacts = false;

                foreach (var pair in collisionPairs)
                {
                    var objA = pair.EntityA.GetComponent<ObjectCollision>();
                    var objB = pair.EntityB.GetComponent<ObjectCollision>();

                    // Process ALL collisions - let ResolveCollision handle fluid logic

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

                // Update grid after each iteration for better convergence
                if (Itteration < iterations - 1)
                    _grid.UpdateAllAabb(_entities);
            }
        }

        public void Stop()
        {
            Logger.Log("Game stopping...");
            if (_imgui != null)
            {
                _imgui.Dispose();
                _imgui = null;
            }
            Dispose();
            Window.Close();
        }

        private void CreateObjects(
            string materialName = "Sand",
            int particleCount = 1000,
            float minX = -0.3f,
            float minY = -0.4f,
            float maxX = 0.3f,
            float maxY = 0.4f,
            int? columnsOverride = null,
            Vector2? originOverride = null,
            float spacingFactor = 1.15f
        )
        {
            var particleSystem = ParticleSystemFactory.Create(
                $"{materialName}Object",
                materialName
            );

            particleSystem.Material.ApplyTo(particleSystem);

            float radius = particleSystem.Material.ParticleRadius;

            // Use the helper to calculate proper grid layout
            var (capacity, columns, rows, origin) = ParticleGridLayout.CalculateParticleGrid(
                minX,
                minY,
                maxX,
                maxY,
                radius,
                spacingFactor
            );

            // Allow overrides if provided
            int finalColumns = columnsOverride ?? columns;
            Vector2 finalOrigin = originOverride ?? origin;

            int particlesToCreate = Math.Min(Math.Max(0, particleCount), capacity);

            _entities.EnsureCapacity(_entities.Count + particlesToCreate + 1);

            particleSystem.CreateParticles(
                GraphicsDevice,
                particlesToCreate,
                finalOrigin,
                finalColumns,
                _entities,
                _updateManager,
                _grid
            );

            // If material requests solid/bonded behavior, create Hooke-law bonds between nearby
            // particles using the preset stiffness/damping values.
            var mat = particleSystem.Material;
            if (mat != null && (mat.IsSolid || mat.BondStiffness > 0f))
            {
                float bondThreshold = mat.ParticleRadius * 2.2f; // allow near neighbors
                var neighbors = new System.Collections.Generic.List<Entity>();

                // Optimize membership test for particles created by this system
                var createdSet = new System.Collections.Generic.HashSet<Entity>(
                    particleSystem.Particles
                );

                foreach (var e in particleSystem.Particles)
                {
                    _grid.GetNearbyEntities(e.Position, neighbors);
                    foreach (var other in neighbors)
                    {
                        if (other == e)
                            continue;
                        if (!createdSet.Contains(other))
                            continue; // only bond within same system
                        // Only create one bond per unordered pair
                        if (e.GetHashCode() >= other.GetHashCode())
                            continue;
                        float dist = Vector2.Distance(e.Position, other.Position);
                        if (dist <= bondThreshold)
                        {
                            float rest = dist;
                            float k = mat.BondStiffness > 0f ? mat.BondStiffness : 100f;
                            float c = mat.BondDamping;
                            Engine13.Utilities.MolecularDynamicsSystem.Instance.AddBond(
                                e,
                                other,
                                k,
                                c,
                                rest
                            );
                        }
                    }
                }
            }

            if (particleSystem.Material.IsFluid)
            {
                particleSystem.InitializeFluid();
                Logger.Log($"[Fluid] Initialized {particleSystem.Particles.Count} particles");
            }

            if (particleSystem.Material.IsGranular)
            {
                particleSystem.InitializeGranular();
                Logger.Log(
                    $"[Granular] Initialized {particleSystem.Particles.Count} particles with friction angle={particleSystem.Material.GranularFrictionAngle}°"
                );
            }

            _particleSystems.Add(particleSystem);
        }
    }
}
