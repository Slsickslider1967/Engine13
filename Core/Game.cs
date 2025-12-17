using System.Diagnostics;
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
using Veldrid.Sdl2;
using Vulkan;

namespace Engine13.Core
{
    public class Game : EngineBase
    {
        private const int MaxFrames = 500;
        private const float SimulationDeltaTime = 1f / 60f; // Physics step size (fixed for stability)
        private float PlaybackFps = 60f;
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

        private float _lastAvgSpeed;
        private float _lastMaxSpeed;
        private Vector2 _lastAvgPos;
        private int _lastGroundedCount;
        private bool _showStartWindow = true;
        private bool _startRequested = false;
        private bool _showGuiDebug = true;

        private InputManager _inputManager;
        private ImGuiController? _imgui;

        private static bool IsSelecting = false;
        private static Vector2 SelectStart;
        private static Vector2 SelectEnd;

        private readonly object _tickLock = new();
        private Task? _precomputeTask;
        private bool _isPrecomputing;
        private bool presets = false;
        private bool SimulationWindow = false;

        public Game(Sdl2Window window, GraphicsDevice graphicsDevice)
            : base(window, graphicsDevice)
        {
            WindowBounds.SetWindow(window);
            _inputManager = new InputManager(window);
        }

        protected override void Initialize()
        {
            CreateObjects("Sand", 1000, 0f, -1f);

            if (_particleSystems.Count > 0)
            {
                float radius = _particleSystems[0].Material.ParticleRadius;
                Renderer.InitializeInstancedRendering(radius, 8);
            }

            _imgui = new ImGuiController(Window, GraphicsDevice, CommandList, _inputManager);
            _imgui.PrintDrawData = true;
            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            _inputManager.Update();

            _imgui?.NewFrame(gameTime.DeltaTime);

            ImGui.SetNextWindowSize(new Vector2(450, 300), ImGuiCond.FirstUseEver);
            ImGui.Begin("Simulation Setup", ref _showStartWindow, ImGuiWindowFlags.None);
            ImGui.Text("Precompute simulation frames before playback.");
            ImGui.Text($"Particle systems: {_particleSystems.Count}");
            ImGui.Text($"Entities: {_entities.Count}");
            if (ImGui.Button("Start Precompute"))
            {
                _startRequested = true;
                SimulationWindow = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Stop and Quit"))
            {
                Stop();
            }
            if (ImGui.Button("Preset Select"))
            {
                presets = true;
            }
            ImGui.Separator();
            if (_showStartWindow == true)
            {
                ImGui.Text($"Ticks Computed: {_tickCounter}");
                ImGui.Text($"Tick Time: {_lastTickTime:F2} ms");
                ImGui.Text($"Avg Speed: {_lastAvgSpeed:F4} units/s");
                ImGui.Text($"Max Speed: {_lastMaxSpeed:F4} units/s");
                ImGui.Text($"Avg Pos: {_lastAvgPos.Y:F3} units");
                ImGui.Text($"Grounded Count: {_lastGroundedCount}");
            }
            ;
            if (presets)
            {
                ImGui.OpenPopup("Presets");
                ImGui.SetNextWindowSize(new Vector2(200, 150), ImGuiCond.FirstUseEver);
                ImGui.BeginPopup("Presets");
            }
            if (SimulationWindow)
            {
                ImGui.SetNextWindowSize(new Vector2(300, 200), ImGuiCond.FirstUseEver);
                ImGui.Begin("Simulation");
                if (_isPrecomputing)
                {
                    ImGui.Text("Precomputing...");
                }
                else
                {
                    if (ImGui.Button("Play/Pause"))
                    {
                        if (_playbackTimer.IsRunning)
                        {
                            _playbackTimer.Stop();
                        }
                        else
                        {
                            _playbackTimer.Start();
                        }
                    }
                    // Allow selecting a specific precomputed frame (0 .. MaxFrames-1)
                    int selectedFrame = _tickIndex;
                    if (ImGui.SliderInt("Frame Select", ref selectedFrame, 0, MaxFrames - 1))
                    {
                        _tickIndex = Math.Clamp(selectedFrame, 0, MaxFrames - 1);
                    }
                    if (ImGui.Button("Close"))
                    {
                        SimulationWindow = false;
                    }
                }
            }

            ImGui.End();

            DrawSelectionRect();

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

        private void LogInImGui()
        {
            ImGui.SetNextWindowSize(new Vector2(300, 160), ImGuiCond.FirstUseEver);
            ImGui.Begin("Simulation Debug", ref _showGuiDebug, ImGuiWindowFlags.None);
            ImGui.Text($"Ticks Computed: {_tickCounter}");
            ImGui.Text($"Tick Time: {_lastTickTime:F2} ms");
            ImGui.Text($"Avg Speed: {_lastAvgSpeed:F4} units/s");
            ImGui.Text($"Max Speed: {_lastMaxSpeed:F4} units/s");
            ImGui.Text($"Avg Pos: {_lastAvgPos.Y:F3} units");
            ImGui.Text($"Grounded Count: {_lastGroundedCount}");
            ImGui.End();
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
                _updateManager.Update(GameTime);

                foreach (var ps in _particleSystems)
                    ps.StepFluid(SimulationDeltaTime, _grid);

                _grid.UpdateAllAabb(_entities);
                RunCollisionDetection(SimulationDeltaTime);

                var positions = MathHelpers.CapturePositions(_entities);

                double tickTime = _tickTimer.Elapsed.TotalMilliseconds - tickStart;

                var (avgSpeed, maxSpeed, _) = ParticleDynamics.GetVelocityStats(_entities);
                var (avgPos, minY, maxY) = ParticleDynamics.GetPositionStats(_entities);
                int groundedCount = ParticleDynamics.GetGroundedCount(_entities);

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
                    $"Tick: {_tickCounter} | Time: {tickTime:F2}ms | AvgSpd: {avgSpeed:F4} | MaxSpd: {maxSpeed:F4} | AvgY: {avgPos.Y:F3}"
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

            _playbackTimer.Start();
        }

        private void DrawSelectionRect()
        {
            var io = ImGui.GetIO();
            var dbgList = ImGui.GetForegroundDrawList();

            if (!io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                IsSelecting = true;
                SelectStart = io.MousePos;
                SelectEnd = io.MousePos;
                Logger.Log($"Selection started at {SelectStart}");
            }

            if (IsSelecting)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    SelectEnd = io.MousePos;
                }
                else
                {
                    bool TooSmall =
                        MathF.Abs(SelectEnd.X - SelectStart.X) < 5f
                        || MathF.Abs(SelectEnd.Y - SelectStart.Y) < 5f;
                    if (TooSmall)
                    {
                        SelectStart = Vector2.Zero;
                        SelectEnd = Vector2.Zero;
                    }

                    IsSelecting = false;
                }
            }

            if (!IsSelecting && SelectStart != SelectEnd)
            {
                ImGui.Begin("SelectionInfo");
                ImGui.Text($"Selection from {SelectStart} to {SelectEnd}");
                if (ImGui.Button("Fill"))
                {
                    Logger.Log(
                        $"Yet to implement fill selection from {SelectStart} to {SelectEnd}"
                    );
                }
                if (ImGui.Button("Clear Selection"))
                {
                    SelectStart = Vector2.Zero;
                    SelectEnd = Vector2.Zero;
                }
                ImGui.End();
            }

            if (IsSelecting)
            {
                Vector2 min = new(
                    MathF.Min(SelectStart.X, SelectEnd.X),
                    MathF.Min(SelectStart.Y, SelectEnd.Y)
                );

                Vector2 max = new(
                    MathF.Max(SelectStart.X, SelectEnd.X),
                    MathF.Max(SelectStart.Y, SelectEnd.Y)
                );

                var drawList = ImGui.GetForegroundDrawList();

                drawList.AddRectFilled(
                    min,
                    max,
                    ImGui.GetColorU32(new Vector4(0f, 0.5f, 1f, 0.25f))
                );

                drawList.AddRect(
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0.5f, 1f, 1f)),
                    0.0f,
                    ImDrawFlags.None,
                    1.5f
                );
            }
            else
            {
                Vector2 min = new(
                    MathF.Min(SelectStart.X, SelectEnd.X),
                    MathF.Min(SelectStart.Y, SelectEnd.Y)
                );

                Vector2 max = new(
                    MathF.Max(SelectStart.X, SelectEnd.X),
                    MathF.Max(SelectStart.Y, SelectEnd.Y)
                );

                var drawList = ImGui.GetForegroundDrawList();

                drawList.AddRectFilled(
                    min,
                    max,
                    ImGui.GetColorU32(new Vector4(0f, 0.3f, 0.8f, 0.05f))
                );

                drawList.AddRect(
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0.3f, 0.8f, 0.8f)),
                    0.0f,
                    ImDrawFlags.None,
                    1.5f
                );
            }
        }

        protected override void Draw()
        {
            Renderer.BeginFrame(new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));

            Vector2[]? tickPositions = null;
            int tickCount;
            lock (_tickLock)
            {
                tickCount = _tickPositions.Count;
                if (tickCount > 0)
                {
                    // Playback loop - uses PlaybackFps (independent of simulation step size)
                    float elapsedSeconds = (float)_playbackTimer.Elapsed.TotalSeconds;
                    float frameTime = 1f / PlaybackFps;
                    float totalSimTime = tickCount * frameTime;
                    float playbackTime = elapsedSeconds % totalSimTime;
                    _tickIndex = (int)(playbackTime / frameTime);
                    _tickIndex = Math.Clamp(_tickIndex, 0, tickCount - 1);

                    tickPositions = _tickPositions[_tickIndex];
                }
            }

            if (tickCount == 0)
            {
                // Still render ImGui (start UI / diagnostics) even when there are no precomputed frames.
                _imgui?.Render(CommandList);
                Renderer.EndFrame();
                return;
            }

            Renderer.DrawInstanced(_entities, tickPositions!);

            _imgui?.Render(CommandList);

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

        public void Stop()
        {
            Logger.Log("Game stopping...");
            _imgui?.Dispose();
            Dispose();
            Window.Close();
        }

        private void CreateObjects(
            string materialName = "Sand",
            int particleCount = 1000,
            float topLeftX = -0.3f,
            float topLeftY = -0.4f,
            float bottomRightX = 0.3f,
            float bottomRightY = 0.4f
        )
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
