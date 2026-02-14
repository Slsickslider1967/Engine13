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

namespace Engine13.Core
{
    public class Game : EngineBase
    {
        private int MaxFrames = 500;
        private const float SimulationDeltaTime = 1f / 60f;
        private float PlaybackFps = 60f;
        private readonly List<Entity> _entities = new();
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new(0.025f);
        private readonly List<Vector2[]> _tickPositions = new();
        private readonly Stopwatch _tickTimer = Stopwatch.StartNew();
        private readonly Stopwatch _playbackTimer = new();
        private float _playbackStartOffsetSeconds = 0f;
        private int _tickIndex,
            _tickCounter;
        private bool _simulationComplete;
        private double _lastTickTime;
        private float _lastAvgSpeed,
            _lastMaxSpeed;
        private Vector2 _lastAvgPos;
        private int _lastGroundedCount;
        private bool _showStartWindow = true,
            _startRequested = false,
            _showGuiDebug = true;
        private InputManager _inputManager;
        private ImGuiController? _imgui;
        private CsvPlotter? _csvPlotter;
        private int _selectionPresetIndex = 0;
        private string _selectionMaterial = "Sand";
        private readonly object _tickLock = new();
        private Task? _precomputeTask;
        private bool _isPrecomputing;
        private bool SimulationWindow = false,
            PrecomputeWindow = false,
            HasPrecomputeRun = false;
        private bool _showGraphInWindow = false,
            _startRunningImmediately = true;
        private bool _overlayDensity,
            _overlayPressure,
            _overlayNeighbors;
        private float _overlayMin = 0f,
            _overlayMax = 1f;
        private Vector4[] _overlayColours = Array.Empty<Vector4>();
        private readonly Dictionary<Entity, int> _entityIndexMap = new();
        private string PrecomputeName = "Open Precompute Window",
            EditorName = "Open Editor Window";

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
            WindowBounds.SetWindow(window);
            _inputManager = new InputManager(window);
        }

        protected override void Initialize()
        {
            Renderer.InitializeInstancedRendering(0.01f, 8);
            _imgui = new ImGuiController(Window, GraphicsDevice, CommandList, _inputManager)
            {
                PrintDrawData = true,
            };
            _updateManager.Register(MolecularDynamicsSystem.Instance);
            try
            {
                _csvPlotter = new CsvPlotter(
                    Path.Combine(Directory.GetCurrentDirectory(), "Ticks.csv")
                );
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
                ref _tickCounter,
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
                RemoveParticleSystemInSelection,
                CreateSingleParticle
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
                Forces.Reset();
                foreach (var ps in _particleSystems)
                    ps.StepFluid(SimulationDeltaTime, _grid);
                _updateManager.Update(GameTime);
                foreach (var e in _entities)
                    e.GetComponent<EdgeCollision>()?.Update(e, GameTime);
                _grid.UpdateAllAabb(_entities);
                RunCollisionDetection(SimulationDeltaTime);
                var positions = MathHelpers.CapturePositions(_entities);
                double tickTime = _tickTimer.Elapsed.TotalMilliseconds - tickStart;
                var (avgSpeed, maxSpeed, _) = ParticleDynamics.GetVelocityStats(_entities);
                var (avgPos, minY, maxY) = ParticleDynamics.GetPositionStats(_entities);
                int groundedCount = ParticleDynamics.GetGroundedCount(_entities);
                string sphDebug = "";
                var fluidSystem = _particleSystems.Find(ps => ps.Material.IsFluid);
                if (fluidSystem != null)
                {
                    var d = fluidSystem.GetSPHDebugInfo();
                    sphDebug =
                        $" | SPH: dens={d.avgDensity:F1}/{d.maxDensity:F1} press={d.avgPressure:F2} visc={d.avgViscosity:F3} nbr={d.avgNeighbors:F1} vel={d.avgVelocity:F3}/{d.maxVelocity:F3}";
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
                _csvPlotter =
                    _csvPlotter == null
                        ? new CsvPlotter(Path.Combine(Directory.GetCurrentDirectory(), "Ticks.csv"))
                        : _csvPlotter;
                _csvPlotter.Load();
            }
            catch (Exception ex)
            {
                Logger.Log($"CsvPlotter reload failed: {ex.Message}");
            }
            _playbackTimer.Start();
        }

        private void RemoveParticleSystemInSelection()
        {
            var start = LoadGui.GetSelectStart();
            var end = LoadGui.GetSelectEnd();
            var winSize = WindowBounds.GetWindowSize();
            var bounds = WindowBounds.GetNormalizedBounds();
            float sx = winSize.X > 0 ? winSize.X : 1f,
                sy = winSize.Y > 0 ? winSize.Y : 1f;
            float x0 = bounds.left + (bounds.right - bounds.left) * (start.X / sx);
            float y0 = bounds.top + (bounds.bottom - bounds.top) * (start.Y / sy);
            float x1 = bounds.left + (bounds.right - bounds.left) * (end.X / sx);
            float y1 = bounds.top + (bounds.bottom - bounds.top) * (end.Y / sy);
            ParticleDynamics.RemoveParticlesInArea(
                _entities,
                MathF.Min(x0, x1),
                MathF.Min(y0, y1),
                MathF.Max(x0, x1),
                MathF.Max(y0, y1)
            );
        }

        private void ClearAllParticles()
        {
            ParticleDynamics.RemoveAllParticles(_entities);
            _simulationComplete = false;
            _isPrecomputing = false;
        }

        private void CreateSingleParticle(string materialName, float worldX, float worldY)
        {
            var sys = _particleSystems.Find(ps => ps.Name.Contains(materialName));
            if (sys == null)
            {
                sys = ParticleSystemFactory.Create($"{materialName}System", materialName);
                sys.Material.ApplyTo(sys);
                _particleSystems.Add(sys);
            }
            float r = sys.Material.ParticleRadius;
            var p = CircleFactory.CreateCircle(GraphicsDevice, r, 8, 8);
            p.Position = new Vector2(worldX, worldY);
            p.CollisionRadius = r;
            p.Mass = sys.Material.Mass;
            p.AddComponent(
                new Gravity(
                    sys.Material.GravityStrength,
                    0f,
                    p.Mass,
                    sys.Material.HorizontalForce,
                    0f
                )
            );
            p.AddComponent(
                new ObjectCollision
                {
                    Mass = p.Mass,
                    Restitution = sys.Material.Restitution,
                    Friction = sys.Material.Friction,
                    IsFluid = sys.Material.IsFluid,
                }
            );
            p.AddComponent(new EdgeCollision(!sys.Material.EnableEdgeCollision));
            _entities.Add(p);
            sys.Particles.Add(p);
            _grid.AddEntity(p);
            if (sys.Material.IsFluid)
                sys.InitializeFluid();
        }

        protected override void Draw()
        {
            Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));
            Vector2[]? tickPositions = null;
            Vector4[]? colours = null;
            int tickCount;
            lock (_tickLock)
            {
                tickCount = _tickPositions.Count;
                if (tickCount > 0)
                {
                    float frameTime = 1f / MathF.Max(PlaybackFps, 1f),
                        totalSimTime = MathF.Max(frameTime, tickCount * frameTime);
                    float playbackTime =
                        _playbackStartOffsetSeconds
                        + (
                            _playbackTimer.IsRunning
                                ? (float)_playbackTimer.Elapsed.TotalSeconds
                                : 0f
                        );
                    _tickIndex = Math.Clamp(
                        (int)((playbackTime % totalSimTime) / frameTime),
                        0,
                        tickCount - 1
                    );
                    tickPositions = _tickPositions[_tickIndex];
                }
            }
            if (tickCount == 0 || tickPositions == null || tickPositions.Length < _entities.Count)
            {
                tickPositions = new Vector2[_entities.Count];
                for (int i = 0; i < _entities.Count; i++)
                    tickPositions[i] = _entities[i].Position;
            }
            colours = BuildOverlayColours(tickPositions);
            Renderer.DrawInstanced(_entities, tickPositions, colours);
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
            if (ps == null || ps.FluidCount == 0)
                return _overlayColors;
            int fluidCount = ps.FluidCount;
            Span<float> dens = fluidCount <= 256 ? stackalloc float[256] : new float[fluidCount];
            Span<float> pres = fluidCount <= 256 ? stackalloc float[256] : new float[fluidCount];
            Span<int> neigh = fluidCount <= 256 ? stackalloc int[256] : new int[fluidCount];
            ps.GetFluidDebugData(_entityIndexMap, dens, pres, neigh);
            float minVal = _overlayMin,
                maxVal = _overlayMax <= _overlayMin + 1e-6f ? _overlayMin + 1f : _overlayMax;
            float invRange = 1f / (maxVal - minVal);
            for (int i = 0; i < fluidCount; i++)
            {
                if (
                    !ps.TryGetEntityAt(i, out var e) || !_entityIndexMap.TryGetValue(e, out int idx)
                )
                    continue;
                float v =
                    _overlayDensity ? dens[i]
                    : _overlayPressure ? pres[i]
                    : neigh[i];
                _overlayColours[idx] = MapGradient(Math.Clamp((v - minVal) * invRange, 0f, 1f));
            }
            return _overlayColours;
        }

        private static Vector4 MapGradient(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector4(t, MathF.Max(0f, 1f - MathF.Abs(t - 0.5f) * 2f), 1f - t, 1f);
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
        ) =>
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

        private void RunCollisionDetection(float stepDelta)
        {
            foreach (var e in _entities)
            {
                var c = e.GetComponent<ObjectCollision>();
                if (c != null)
                    c.IsGrounded = false;
            }
            for (int iter = 0; iter < 30; iter++)
            {
                var pairs = _grid.GetCollisionPairs();
                if (pairs.Count == 0)
                    break;
                bool anyContacts = false;
                foreach (var pair in pairs)
                {
                    if (CollisionInfo.AreColliding(pair.EntityA, pair.EntityB, out var info))
                    {
                        anyContacts = true;
                        PhysicsSolver.ResolveCollision(info, stepDelta);
                    }
                }
                if (!anyContacts)
                    break;
                if (iter < 29)
                    _grid.UpdateAllAabb(_entities);
            }
        }

        public void Stop()
        {
            Logger.Log("Game stopping...");
            _imgui?.Dispose();
            _imgui = null;
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
            var ps = ParticleSystemFactory.Create($"{materialName}Object", materialName);
            ps.Material.ApplyTo(ps);
            var (capacity, columns, _, origin) = ParticleGridLayout.CalculateParticleGrid(
                minX,
                minY,
                maxX,
                maxY,
                ps.Material.ParticleRadius,
                spacingFactor
            );
            int finalColumns = columnsOverride ?? columns;
            Vector2 finalOrigin = originOverride ?? origin;
            int toCreate = Math.Min(Math.Max(0, particleCount), capacity);
            _entities.EnsureCapacity(_entities.Count + toCreate + 1);
            ps.CreateParticles(
                GraphicsDevice,
                toCreate,
                finalOrigin,
                finalColumns,
                _entities,
                _updateManager,
                _grid
            );
            var mat = ps.Material;
            if (mat != null && (mat.IsSolid || mat.BondStiffness > 0f))
            {
                float bondThreshold = mat.ParticleRadius * 2.2f;
                var neighbors = new List<Entity>();
                var createdSet = new HashSet<Entity>(ps.Particles);
                foreach (var e in ps.Particles)
                {
                    _grid.GetNearbyEntities(e.Position, neighbors);
                    foreach (var other in neighbors)
                    {
                        if (
                            other == e
                            || !createdSet.Contains(other)
                            || e.GetHashCode() >= other.GetHashCode()
                        )
                            continue;
                        float dist = Vector2.Distance(e.Position, other.Position);
                        if (dist <= bondThreshold)
                            MolecularDynamicsSystem.Instance.AddBond(
                                e,
                                other,
                                mat.BondStiffness > 0f ? mat.BondStiffness : 100f,
                                mat.BondDamping,
                                dist
                            );
                    }
                }
            }
            if (ps.Material.IsFluid)
            {
                ps.InitializeFluid();
                Logger.Log($"[Fluid] Initialized {ps.Particles.Count} particles");
            }
            if (ps.Material.IsGranular)
            {
                ps.InitializeGranular();
                Logger.Log($"[Granular] Initialized {ps.Particles.Count} particles");
            }
            _particleSystems.Add(ps);
        }
    }
}
