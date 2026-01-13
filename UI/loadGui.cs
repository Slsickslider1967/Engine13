using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Engine13.Utilities.JsonReader;
using ImGuiNET;

namespace Engine13.UI
{
    public class LoadGui
    {
        // Selection state
        private static bool IsSelecting = false;
        private static Vector2 SelectStart;
        private static Vector2 SelectEnd;

        private static bool _EditorWindow = false;
        private static bool _SimulationWindow = false;
        private static bool _SingularPlacementMode = false;
        private static bool _GridSnapMode = false;

        private static bool SelectionEdit = false;
        private static int _selectionPresetIndex = 0;
        private static string _selectionMaterial = "Sand";

        private const float GraphPlotHeight = 100f;

        public static void ShowLoadWindow(ref bool showWindow, ref bool startRequested)
        {
            if (!showWindow)
                return;

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 200), ImGuiCond.FirstUseEver);
            ImGui.Begin("Load Simulation", ref showWindow, ImGuiWindowFlags.NoCollapse);

            ImGui.Text("Welcome to Engine-13!");
            ImGui.Separator();
            ImGui.Text("Please load a simulation to get started.");

            ImGui.Spacing();

            if (ImGui.Button("Start Simulation"))
            {
                startRequested = true;
                showWindow = false;
            }

            ImGui.End();
        }

        public static void ShowDebugTerminal()
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 400), ImGuiCond.FirstUseEver);
            ImGui.Begin("Debug Terminal");
        }

        public static void LogInImGui(
            ref bool showGuiDebug,
            int tickCounter,
            double lastTickTime,
            float lastAvgSpeed,
            float lastMaxSpeed,
            Vector2 lastAvgPos,
            int lastGroundedCount,
            List<ParticleSystem> particleSystems
        )
        {
            ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.FirstUseEver);
            ImGui.Begin("Simulation Debug", ref showGuiDebug, ImGuiWindowFlags.None);
            ImGui.Text($"Ticks Computed: {tickCounter}");
            ImGui.Text($"Tick Time: {lastTickTime:F2} ms");
            ImGui.Separator();
            ImGui.Text("Global Stats:");
            ImGui.Text($"  Avg Speed: {lastAvgSpeed:F4} units/s");
            ImGui.Text($"  Max Speed: {lastMaxSpeed:F4} units/s");
            ImGui.Text($"  Avg Pos Y: {lastAvgPos.Y:F3} units");
            ImGui.Text($"  Grounded: {lastGroundedCount}");

            // SPH Debug Info
            var fluidSystem = particleSystems.Find(ps => ps.Material.IsFluid);
            if (fluidSystem != null)
            {
                ImGui.Separator();
                ImGui.Text("SPH Fluid Debug:");
                var debugInfo = fluidSystem.GetSPHDebugInfo();
                ImGui.Text($"  Particles: {debugInfo.particleCount}");
                ImGui.Text($"  Avg Density: {debugInfo.avgDensity:F2}");
                ImGui.Text($"  Max Density: {debugInfo.maxDensity:F2}");
                ImGui.Text($"  Avg Pressure: {debugInfo.avgPressure:F2}");
                ImGui.Text($"  Avg Viscosity: {debugInfo.avgViscosity:F3}");
                ImGui.Text($"  Avg Neighbors: {debugInfo.avgNeighbors:F1}");
                ImGui.Text($"  Avg Velocity: {debugInfo.avgVelocity:F3}");
                ImGui.Text($"  Max Velocity: {debugInfo.maxVelocity:F3}");
            }
            ImGui.End();
        }

        public static void DrawSelectionRect(
            ref int selectionPresetIndex,
            ref string selectionMaterial,
            List<Entity> entities,
            Action<
                string,
                int,
                float,
                float,
                float,
                float,
                int,
                Vector2,
                float
            > createObjectsCallback,
            Action removeParticlesCallback
        )
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

            if (!IsSelecting && SelectStart != SelectEnd && SelectionEdit)
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

                    selectionPresetIndex = Math.Clamp(
                        selectionPresetIndex,
                        0,
                        presetNames.Length - 1
                    );
                    int selected = selectionPresetIndex;
                    if (ImGui.Combo("Material", ref selected, presetNames, presetNames.Length))
                    {
                        selectionPresetIndex = Math.Clamp(selected, 0, presetNames.Length - 1);
                        selectionMaterial = presetNames[selectionPresetIndex];
                    }

                    if (string.IsNullOrWhiteSpace(selectionMaterial))
                        selectionMaterial = presetNames[selectionPresetIndex];
                }
                if (ImGui.Button("Fill"))
                {
                    var winSize = WindowBounds.GetWindowSize();
                    var bounds = WindowBounds.GetNormalizedBounds();
                    float sx = winSize.X > 0 ? winSize.X : 1f;
                    float sy = winSize.Y > 0 ? winSize.Y : 1f;

                    float x0 = bounds.left + (bounds.right - bounds.left) * (SelectStart.X / sx);
                    float y0 = bounds.top + (bounds.bottom - bounds.top) * (SelectStart.Y / sy);
                    float x1 = bounds.left + (bounds.right - bounds.left) * (SelectEnd.X / sx);
                    float y1 = bounds.top + (bounds.bottom - bounds.top) * (SelectEnd.Y / sy);

                    float minX = MathF.Min(x0, x1);
                    float maxX = MathF.Max(x0, x1);
                    float minY = MathF.Min(y0, y1);
                    float maxY = MathF.Max(y0, y1);

                    const float selectionSpacing = 1.15f;
                    var layout = CalculateFillLayout(
                        minX,
                        minY,
                        maxX,
                        maxY,
                        selectionMaterial,
                        selectionSpacing
                    );

                    createObjectsCallback(
                        selectionMaterial,
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
                ImGui.SameLine();
                if (ImGui.Button("Remove"))
                {
                    removeParticlesCallback();
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

                //Impliment EntitySelectedVisualizer to change/outline selected entities
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

        private static (int capacity, int columns, Vector2 origin) CalculateFillLayout(
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

        public static void DrawUI(
            ref bool showStartWindow,
            ref bool precomputeWindow,
            ref bool hasPrecomputeRun,
            ref bool showGraphInWindow,
            ref bool startRunningImmediately,
            ref string precomputeName,
            ref string editorName,
            ref bool startRequested,
            ref int maxFrames,
            ref float playbackFps,
            ref int tickIndex,
            ref float playbackStartOffsetSeconds,
            int tickCounter,
            double lastTickTime,
            float lastAvgSpeed,
            float lastMaxSpeed,
            Vector2 lastAvgPos,
            int lastGroundedCount,
            int particleSystemCount,
            int entityCount,
            System.Diagnostics.Stopwatch playbackTimer,
            object tickLock,
            List<Vector2[]> tickPositions,
            CsvPlotter? csvPlotter,
            Action stopCallback,
            Action clearParticlesCallback
        )
        {
            var io = ImGui.GetIO();
            var display = io.DisplaySize;
            const float margin = 10f;
            const float spacing = 6f;

            var mainSize = new Vector2(450, 300);
            var mainPos = new Vector2(margin, margin);
            ImGui.SetNextWindowSize(mainSize, ImGuiCond.Once);
            ImGui.SetNextWindowPos(mainPos, ImGuiCond.Once);
            ImGui.Begin("Simulation Setup", ref showStartWindow, ImGuiWindowFlags.None);

            var mainWinPos = ImGui.GetWindowPos();
            var mainWinSize = ImGui.GetWindowSize();
            float columnX = mainWinPos.X;
            float nextY = mainWinPos.Y + mainWinSize.Y + spacing;
            float mainRight = mainWinPos.X + mainWinSize.X;

            ImGui.Text("Precompute simulation frames before playback.");
            ImGui.Text($"Particle systems: {particleSystemCount}");
            ImGui.Text($"Entities: {entityCount}");

            if (!hasPrecomputeRun)
            {
                if (ImGui.Button(precomputeName))
                {
                    precomputeWindow = true;
                }
            }
            else
            {
                if (ImGui.Button(precomputeName))
                {
                    _SimulationWindow = !_SimulationWindow;
                    precomputeName = _SimulationWindow
                        ? "Hide Simulation Settings"
                        : "Show Simulation Settings";
                }
            }

            ///<Summary>
            /// This section toggles the editor window visibility.
            /// </Summary>
            if(!_SimulationWindow)
            {
                if (_EditorWindow)
                {
                    editorName = "Hide Editor Window";
                    if (ImGui.Button(editorName))
                    {
                        _EditorWindow = false;
                    }
                }
                else
                {
                    editorName = "Show Editor Window";
                    if (ImGui.Button(editorName))
                    {
                        _EditorWindow = true;
                    }
                }
            }

            ImGui.NewLine();
            ImGui.NewLine();
            if (ImGui.Button("Stop and Quit"))
            {
                stopCallback();
            }
            ImGui.Separator();

            if (showStartWindow == true)
            {
                ImGui.Text($"Ticks Computed: {tickCounter}");
                ImGui.Text($"Tick Time: {lastTickTime:F2} ms");
                ImGui.Text($"Avg Speed: {lastAvgSpeed:F4} units/s");
                ImGui.Text($"Max Speed: {lastMaxSpeed:F4} units/s");
                ImGui.Text($"Avg Pos: {lastAvgPos.Y:F3} units");
                ImGui.Text($"Grounded Count: {lastGroundedCount}");
                ImGui.ProgressBar(
                    (float)tickCounter / maxFrames,
                    new Vector2(-1, 0),
                    "Precompute Progress"
                );
            }
            else
            {
                if (ImGui.Checkbox("Show Graph In Window", ref showGraphInWindow)) { }
                if (!showGraphInWindow)
                {
                    if (csvPlotter != null)
                    {
                        ShowGraphGuiWindow(csvPlotter, tickCounter, maxFrames);
                    }
                    else
                    {
                        ImGui.Text("No Ticks.csv found in working directory.");
                        if (ImGui.Button("Reload CSV"))
                        {
                            try
                            {
                                csvPlotter = new CsvPlotter("Ticks.csv");
                                csvPlotter.Load();
                                Logger.Log("CSV loaded from UI");
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"CSV load failed: {ex.Message}");
                            }
                        }
                    }
                }
            }

            ///<summary>
            /// Simulation control window
            /// </summary>
            if (_SimulationWindow)
            {
                var simSize = new Vector2(300, 200);
                ImGui.SetNextWindowSize(simSize, ImGuiCond.Once);
                ImGui.SetNextWindowPos(new Vector2(columnX, nextY), ImGuiCond.Once);
                ImGui.Begin("Simulation");

                bool isPrecomputing = false; // This should be passed as parameter if needed
                if (isPrecomputing)
                {
                    ImGui.Text("Precomputing...");
                }
                else
                {
                    if (!startRunningImmediately)
                    {
                        playbackTimer.Stop();
                    }

                    if (ImGui.Button("Play/Pause"))
                    {
                        startRunningImmediately = true;
                        if (playbackTimer.IsRunning)
                        {
                            playbackStartOffsetSeconds += (float)playbackTimer.Elapsed.TotalSeconds;
                            playbackTimer.Reset();
                        }
                        else
                        {
                            playbackTimer.Restart();
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Reset"))
                    {
                        playbackStartOffsetSeconds = 0f;
                        tickIndex = 0;
                        playbackTimer.Reset();
                    }
                    ImGui.Separator();

                    int selectedFrame = tickIndex;
                    int maxFrame = maxFrames - 1;
                    lock (tickLock)
                    {
                        if (tickPositions.Count > 0)
                            maxFrame = Math.Min(maxFrames - 1, tickPositions.Count - 1);
                    }

                    if (ImGui.SliderInt("Frame Select", ref selectedFrame, 0, maxFrame))
                    {
                        tickIndex = Math.Clamp(selectedFrame, 0, maxFrame);
                        float frameTime = 1f / MathF.Max(playbackFps, 1f);
                        playbackStartOffsetSeconds = tickIndex * frameTime;
                        if (playbackTimer.IsRunning)
                            playbackTimer.Reset();
                    }

                    if (ImGui.SliderFloat("Playback FPS", ref playbackFps, 1f, 240f))
                    {
                        playbackFps = Math.Clamp(playbackFps, 1f, 240f);
                        float frameTime = 1f / playbackFps;
                        playbackStartOffsetSeconds = tickIndex * frameTime;
                        if (playbackTimer.IsRunning)
                            playbackTimer.Reset();
                    }
                    if (playbackFps != 60f)
                    {
                        if (ImGui.Button("Reset FPS"))
                        {
                            playbackFps = 60f;
                            float frameTime = 1f / MathF.Max(playbackFps, 1f);
                            playbackStartOffsetSeconds = tickIndex * frameTime;
                            if (playbackTimer.IsRunning)
                                playbackTimer.Reset();
                        }
                    }
                    if (ImGui.Button("Close"))
                    {
                        _SimulationWindow = false;
                    }
                }
                var simWinSize = ImGui.GetWindowSize();
                ImGui.End();
                nextY += simWinSize.Y + spacing;
            }


            /// <Summary>
            /// The Precompute Settings window is for the compute settings.
            /// </Summary>
            if (precomputeWindow)
            {
                var preSize = new Vector2(300, 160);
                ImGui.SetNextWindowSize(preSize, ImGuiCond.Always);
                ImGui.SetNextWindowPos(new Vector2(columnX, nextY), ImGuiCond.Always);
                ImGui.Begin("Precompute Settings");
                if (ImGui.Button("Begin compute"))
                {
                    startRequested = true;
                    precomputeWindow = false;
                    _SimulationWindow = true;
                    hasPrecomputeRun = true;
                    precomputeName = "Hide Simulation Settings";
                    playbackTimer.Stop();
                }
                ImGui.Separator();
                ImGui.SliderInt("Max Frames", ref maxFrames, 100, 5000);
                if (ImGui.Checkbox("Start Running Immediately", ref startRunningImmediately)) { }
                var preWinSize = ImGui.GetWindowSize();
                ImGui.End();
                nextY += preWinSize.Y + spacing;
            }


            ///<summary>
            /// This is for showing the graph in a separate window.
            /// </summary>
            if (showGraphInWindow)
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
                ShowGraphGuiWindow(csvPlotter, tickCounter, maxFrames);
                ImGui.End();
            }



            ///<summary>
            /// This is for editor selection/enable editor mode
            /// </summary>
            if (_EditorWindow && !_SimulationWindow)
            {
                var editorSize = new Vector2(400, 400);
                ImGui.SetNextWindowSize(editorSize, ImGuiCond.Always);
                ImGui.SetNextWindowPos(
                    new Vector2(display.X - editorSize.X - margin, margin),
                    ImGuiCond.Always
                );
                ImGui.Begin("Editor Window");
                ImGui.Text("Editor functionality goes here.");
                ImGui.Separator();
                ImGui.Checkbox("Selection Edit Mode", ref SelectionEdit);
                ImGui.Checkbox("Singular placement", ref _SingularPlacementMode);

                if (_SingularPlacementMode)
                {
                    ImGui.Separator();
                    ImGui.Checkbox("Grid Snap", ref _GridSnapMode);

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
                    }
                }

                ImGui.Separator();

                if (ImGui.Button("Clear all particles"))
                {
                    clearParticlesCallback();
                }
                ImGui.End();
            }




            ImGui.End();
        }

        public static void ShowGraphGuiWindow(
            CsvPlotter? csvPlotter,
            int tickCounter,
            int maxFrames
        )
        {
            ImGui.Text($"Ticks Computed: {tickCounter}");

            if (csvPlotter == null)
            {
                ImGui.Text("No CSV plotter available.");
                return;
            }

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
                    $"{tickCounter}/{maxFrames}",
                    minVal,
                    maxVal,
                    new Vector2(-1, GraphPlotHeight)
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

        public static Vector2 GetSelectStart() => SelectStart;

        public static Vector2 GetSelectEnd() => SelectEnd;

        public static void ClearSelection()
        {
            SelectStart = Vector2.Zero;
            SelectEnd = Vector2.Zero;
        }
    }
}
