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

        private readonly List<Entity> _entities = new();
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly UpdateManager _updateManager = new();
        private readonly SpatialGrid _grid = new(0.005f);
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

        private static bool IsSelecting = false;
        private static Vector2 SelectStart;
        private static Vector2 SelectEnd;
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
        private const float GraphPlotHeight = 100f;

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
            _updateManager.Register(Engine13.Utilities.MolecularDynamicsSystem.Instance);
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
                int matCount = ParticlePresetReader.GetPresetCount();
                if (matCount <= 0)
                {
                    ImGui.Text("No presets available");
                }
                else
                {
                    var presetNames = new string[matCount];
                    var presetReader = new ParticlePresetReader();
                    for (int i = 0; i < matCount; i++)
                        presetNames[i] = presetReader.GetPresetName(i);

                    _selectionPresetIndex = Math.Clamp(
                        _selectionPresetIndex,
                        0,
                        presetNames.Length - 1
                    );
                    int selected = _selectionPresetIndex;
                    if (ImGui.Combo("Material", ref selected, presetNames, presetNames.Length))
                    {
                        _selectionPresetIndex = Math.Clamp(selected, 0, presetNames.Length - 1);
                        _selectionMaterial = presetNames[_selectionPresetIndex];
                    }

                    if (string.IsNullOrWhiteSpace(_selectionMaterial))
                        _selectionMaterial = presetNames[_selectionPresetIndex];
                }
                if (ImGui.Button("Fill"))
                {
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

                    // Use 1.15f to match ParticleSystem.CreateParticles hardcoded spacing
                    const float selectionSpacing = 1.15f;
                    var layout = CalculateFillLayout(
                        minX,
                        minY,
                        maxX,
                        maxY,
                        _selectionMaterial,
                        selectionSpacing
                    );

                    CreateObjects(
                        _selectionMaterial,
                        layout.capacity,
                        minX,
                        minY,
                        maxX,
                        maxY,
                        layout.columns,
                        layout.origin,
                        selectionSpacing
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

        private (int capacity, int columns, Vector2 origin) CalculateFillLayout(
            float minX,
            float minY,
            float maxX,
            float maxY,
            string materialName,
            float spacingFactor = 1.15f
        )
        {
            var tempPs = ParticleSystemFactory.Create("Temp", materialName);
            float radius = tempPs.Material.ParticleRadius;

            var (capacity, columns, rows, origin) = ParticleGridLayout.CalculateParticleGrid(
                minX,
                minY,
                maxX,
                maxY,
                radius,
                spacingFactor
            );

            return (capacity, columns, origin);
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
                _overlayColors[i] = _entities[i].Color;

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

        private void DrawUI()
        {
            var io = ImGui.GetIO();
            var display = io.DisplaySize;
            const float margin = 10f;
            const float spacing = 6f;

            var mainSize = new Vector2(450, 300);
            var mainPos = new Vector2(margin, margin);
            ImGui.SetNextWindowSize(mainSize, ImGuiCond.Always);
            ImGui.SetNextWindowPos(mainPos, ImGuiCond.Always);
            ImGui.Begin("Simulation Setup", ref _showStartWindow, ImGuiWindowFlags.None);

            var mainWinPos = ImGui.GetWindowPos();
            var mainWinSize = ImGui.GetWindowSize();
            float columnX = mainWinPos.X;
            float nextY = mainWinPos.Y + mainWinSize.Y + spacing;
            float mainRight = mainWinPos.X + mainWinSize.X;
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
                if (ImGui.Checkbox("Show Graph In Window", ref _showGraphInWindow)) { }
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
            }
            if (SimulationWindow)
            {
                var simSize = new Vector2(300, 200);
                ImGui.SetNextWindowSize(simSize, ImGuiCond.Always);
                ImGui.SetNextWindowPos(new Vector2(columnX, nextY), ImGuiCond.Always);
                ImGui.Begin("Simulation");
                if (_isPrecomputing)
                {
                    ImGui.Text("Precomputing...");
                }
                else
                {
                    if (!_startRunningImmediately)
                    {
                        // Keep playback timer stopped until user presses play; accumulate offset only when running
                        _playbackTimer.Stop();
                    }
                    if (ImGui.Button("Play/Pause"))
                    {
                        _startRunningImmediately = true;
                        if (_playbackTimer.IsRunning)
                        {
                            _playbackStartOffsetSeconds += (float)
                                _playbackTimer.Elapsed.TotalSeconds;
                            _playbackTimer.Reset();
                        }
                        else
                        {
                            _playbackTimer.Restart();
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
                        float frameTime = 1f / MathF.Max(PlaybackFps, 1f);
                        _playbackStartOffsetSeconds = _tickIndex * frameTime;
                        if (_playbackTimer.IsRunning)
                            _playbackTimer.Reset();
                    }

                    if (ImGui.SliderFloat("Playback FPS", ref PlaybackFps, 1f, 240f))
                    {
                        PlaybackFps = Math.Clamp(PlaybackFps, 1f, 240f);
                        float frameTime = 1f / PlaybackFps;
                        _playbackStartOffsetSeconds = _tickIndex * frameTime;
                        if (_playbackTimer.IsRunning)
                            _playbackTimer.Reset();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Close"))
                    {
                        SimulationWindow = false;
                    }
                }
                var simWinSize = ImGui.GetWindowSize();
                ImGui.End();
                nextY += simWinSize.Y + spacing;
            }
            if (PrecomputeWindow)
            {
                var preSize = new Vector2(300, 160);
                ImGui.SetNextWindowSize(preSize, ImGuiCond.Always);
                ImGui.SetNextWindowPos(new Vector2(columnX, nextY), ImGuiCond.Always);
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
                if (ImGui.Checkbox("Start Running Immediately", ref _startRunningImmediately)) { }
                var preWinSize = ImGui.GetWindowSize();
                ImGui.End();
                nextY += preWinSize.Y + spacing;
            }
            if (_showGraphInWindow)
            {
                float graphX = mainRight + spacing;
                float graphW = MathF.Max(80f, display.X - graphX - margin);

                float buttonH = ImGui.GetFrameHeightWithSpacing();
                float titleBarApprox = 24f;
                float padding = 12f;
                float graphH = GraphPlotHeight + buttonH + titleBarApprox + padding;

                ImGui.SetNextWindowPos(new Vector2(graphX, mainWinPos.Y), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(graphW, graphH + 15), ImGuiCond.Always);
                ImGui.Begin("Tick Graph");
                ShowGraphGuiWindow(_csvPlotter);
                ImGui.End();
            }

            ImGui.End();
        }

        private void ShowGraphGuiWindow(CsvPlotter? csvPlotter)
        {
            ImGui.Text($"Ticks Computed: {_tickCounter}");

            if (csvPlotter == null)
            {
                ImGui.Text("No CSV plotter available.");
                return;
            }

            // Prefer column 1 as timing column (if file is [tick, time, ...])
            float[]? ySeries = csvPlotter.GetSeries(1) ?? csvPlotter.GetSeries(0);

            if (ySeries == null || ySeries.Length == 0)
            {
                ImGui.Text("No numeric data available in the CSV selected columns.");
                return;
            }

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
                    new System.Numerics.Vector2(-1, GraphPlotHeight)
                );
            }

            if (ImGui.Button("Reload CSV"))
            {
                try
                {
                    csvPlotter.Load();
                    Logger.Log("CSV reloaded from UI");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Reload failed: {ex.Message}");
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

            const int iterations = 6;
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
                    
                    // Skip only fluid-fluid collisions - SPH handles them
                    // But allow fluid-solid collisions for walls
                    bool isFluidFluid = objA != null && objB != null && objA.IsFluid && objB.IsFluid;
                    if (isFluidFluid)
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

                // Update grid after each iteration for better convergence
                if (iter < iterations - 1)
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

            _particleSystems.Add(particleSystem);
        }
    }
}
