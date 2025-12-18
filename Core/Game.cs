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
        private const float SimulationDeltaTime = 1f / 60f; // Physics step size (fixed for stability)
        private float PlaybackFps = 60f;
        private int FrameIndex = 0;
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
        private CsvPlotter? _csvPlotter;

        private static bool IsSelecting = false;
        private static Vector2 SelectStart;
        private static Vector2 SelectEnd;

        private readonly object _tickLock = new();
        private Task? _precomputeTask;
        private bool _isPrecomputing;
        private bool presets = false;
        private bool SimulationWindow = false;
        private bool PrecomputeWindow = false;
        private bool HasPrecomputeRun = false;
        private bool _showGraphInWindow = false;

        private string PrecomputeName = "Open Precompute Window";

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
            // Try to load precompute CSV log for plotting
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
            DrawUI();

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

            // Attempt to reload the CSV we just wrote so the UI can plot it.
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
                    // Convert screen selection to normalized device coords [-1,1]
                    var winSize = WindowBounds.GetWindowSize();
                    float sx = winSize.X > 0 ? winSize.X : 1f;
                    float sy = winSize.Y > 0 ? winSize.Y : 1f;

                    float x0 = SelectStart.X / sx * 2f - 1f;
                    float y0 = SelectStart.Y / sy * 2f - 1f;
                    float x1 = SelectEnd.X / sx * 2f - 1f;
                    float y1 = SelectEnd.Y / sy * 2f - 1f;

                    // Ensure top-left / bottom-right ordering
                    float topLeftX = MathF.Min(x0, x1);
                    float topLeftY = MathF.Min(y0, y1);
                    float bottomRightX = MathF.Max(x0, x1);
                    float bottomRightY = MathF.Max(y0, y1);

                    // Create particles in the selected rectangle.
                    CreateObjects("Sand", 2000, topLeftX, topLeftY, bottomRightX, bottomRightY);

                    Logger.Log(
                        $"Filled selection with Water (2000 particles) from {SelectStart} to {SelectEnd} -> world rect ({topLeftX},{topLeftY})-({bottomRightX},{bottomRightY})"
                    );
                }
                if (ImGui.Button("Zoom"))
                {
                    Logger.Log(
                        $"Yet to implement zoom to selection from {SelectStart} to {SelectEnd}"
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
                    if (_playbackTimer.IsRunning)
                    {
                        float elapsedSeconds = (float)_playbackTimer.Elapsed.TotalSeconds;
                        float frameTime = 1f / PlaybackFps;
                        float totalSimTime = tickCount * frameTime;
                        float playbackTime = elapsedSeconds % totalSimTime;
                        _tickIndex = (int)(playbackTime / frameTime);
                        _tickIndex = Math.Clamp(_tickIndex, 0, tickCount - 1);
                    }
                    else
                    {
                        _tickIndex = Math.Clamp(_tickIndex, 0, tickCount - 1);
                    }

                    tickPositions = _tickPositions[_tickIndex];
                }
            }

            if (tickCount == 0)
            {
                _imgui?.Render(CommandList);
                Renderer.EndFrame();
                return;
            }

            Renderer.DrawInstanced(_entities, tickPositions!);

            _imgui?.Render(CommandList);

            Renderer.EndFrame();
        }

        private void DrawUI()
        {
            ImGui.ShowDemoWindow();

            ImGui.SetNextWindowSize(new Vector2(450, 300), ImGuiCond.FirstUseEver);
            ImGui.Begin("Simulation Setup", ref _showStartWindow, ImGuiWindowFlags.None);
            ImGui.Text("Precompute simulation frames before playback.");
            ImGui.Text($"Particle systems: {_particleSystems.Count}");
            ImGui.Text($"Entities: {_entities.Count}");
            if (!HasPrecomputeRun)
            {
                if (ImGui.Button(PrecomputeName))
                {
                    PrecomputeWindow = true;
                }
            }
            else
            {
                if (ImGui.Button(PrecomputeName))
                {
                    SimulationWindow = !SimulationWindow;
                }
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
                ImGui.ProgressBar(
                    (float)_tickCounter / MaxFrames,
                    new Vector2(-1, 0),
                    "Precompute Progress"
                );
            }
            else
            {
                if (!_showGraphInWindow)
                {
                    if (_csvPlotter != null)
                    {
                        ShowGraphGuiWindow(_csvPlotter);
                    }
                    else
                    {
                        ImGui.Text("No Ticks.csv found in working directory.");
                        if (ImGui.Button("Reload CSV"))
                        {
                            try
                            {
                                string csvPath = Path.Combine(
                                    Directory.GetCurrentDirectory(),
                                    "Ticks.csv"
                                );
                                _csvPlotter = new CsvPlotter(csvPath);
                            }
                            catch
                            {
                                _csvPlotter = null;
                            }
                        }
                    }
                }
                if (ImGui.Checkbox("Show Graph In Window", ref _showGraphInWindow)) { }
            }
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
                    int selectedFrame = _tickIndex;
                    int maxFrame = MaxFrames - 1;
                    lock (_tickLock)
                    {
                        if (_tickPositions.Count > 0)
                            maxFrame = Math.Min(MaxFrames - 1, _tickPositions.Count - 1);
                    }

                    if (ImGui.SliderInt("Frame Select", ref selectedFrame, 0, maxFrame))
                    {
                        _tickIndex = Math.Clamp(selectedFrame, 0, maxFrame);
                        if (_playbackTimer.IsRunning)
                            _playbackTimer.Stop();
                    }

                    if (ImGui.SliderFloat("Playback FPS", ref PlaybackFps, 1f, 240f))
                    {
                        PlaybackFps = Math.Clamp(PlaybackFps, 1f, 240f);
                    }
                    if (ImGui.Button("Close"))
                    {
                        SimulationWindow = false;
                    }
                }
            }
            if (PrecomputeWindow)
            {
                ImGui.Begin("Precompute Settings");
                if (ImGui.Button("Begin compute"))
                {
                    _startRequested = true;
                    PrecomputeWindow = false;
                    SimulationWindow = true;
                    HasPrecomputeRun = true;
                    PrecomputeName = "Show/Hide Simulation Settings";
                    _playbackTimer.Stop();
                }
                ImGui.Separator();
                ImGui.SliderInt("Max Frames", ref MaxFrames, 100, 5000);
            }
            if (_showGraphInWindow)
            {
                ImGui.Begin("Tick Graph");
                ShowGraphGuiWindow(_csvPlotter);
                ImGui.End();
            }

            ImGui.End();
        }

        private void ShowGraphGuiWindow(CsvPlotter csvPlotter)
        {
            ImGui.Text($"Ticks Computed: {_tickCounter}");

            string csvPath = Path.Combine(Directory.GetCurrentDirectory(), "Ticks.csv");

            // Prefer column 1 as timing column (if file is [tick, time, ...])
            var ySeries = _csvPlotter.GetSeries(1) ?? _csvPlotter.GetSeries(0);

            if (ySeries == null || ySeries.Length == 0)
            {
                ImGui.Text("No numeric data available in the CSV selected columns.");
            }
            else
            {
                float lastValid = 0f;
                for (int i = 0; i < ySeries.Length; i++)
                {
                    if (float.IsNaN(ySeries[i]))
                        ySeries[i] = lastValid;
                    else
                        lastValid = ySeries[i];
                }

                float minVal = float.PositiveInfinity;
                float maxVal = float.NegativeInfinity;
                for (int i = 0; i < ySeries.Length; i++)
                {
                    var v = ySeries[i];
                    if (float.IsNaN(v))
                        continue;
                    if (v < minVal)
                        minVal = v;
                    if (v > maxVal)
                        maxVal = v;
                }

                if (minVal == float.PositiveInfinity)
                {
                    ImGui.Text("Series contains no valid numeric points to plot.");
                }
                else
                {
                    ImGui.PlotLines(
                        "Tick Time (ms)",
                        ref ySeries[0],
                        ySeries.Length,
                        0,
                        $"{_tickCounter}/{MaxFrames}",
                        minVal,
                        maxVal,
                        new System.Numerics.Vector2(-1, 100)
                    );
                }

                if (ImGui.Button("Reload CSV"))
                {
                    try
                    {
                        _csvPlotter.Load();
                        Logger.Log("CSV reloaded from UI");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Reload failed: {ex.Message}");
                    }
                }
            }
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

            float radius = particleSystem.Material.ParticleRadius;
            float diameter = radius * 2f;
            float horizontalSpacing = diameter * 1.15f;
            float verticalSpacing = diameter * 1.15f;

            float width = MathF.Max(0.0001f, bottomRightX - topLeftX);
            float height = MathF.Max(0.0001f, bottomRightY - topLeftY);

            int columns = Math.Max(1, (int)MathF.Floor(width / horizontalSpacing));
            int rows = Math.Max(1, (int)MathF.Floor(height / verticalSpacing));

            // If the computed grid can't hold the requested particles, clamp to capacity so we fill the area.
            int capacity = columns * rows;
            int particlesToCreate = Math.Min(Math.Max(0, particleCount), capacity);

            // Ensure capacity in lists
            _entities.EnsureCapacity(_entities.Count + particlesToCreate + 1);

            particleSystem.CreateParticles(
                GraphicsDevice,
                particlesToCreate,
                topLeftPos,
                columns,
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
