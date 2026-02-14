using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Engine13.Utilities.JsonReader;
using ImGuiNET;
using NativeFileDialogSharp;
using Veldrid;
using Veldrid.Sdl2;
using Vulkan;

namespace Engine13.UI
{
    public class LoadGui
    {
        static bool IsSelecting,
            _EditorWindow,
            _SimulationWindow,
            _SingularPlacementMode,
            _GridSnapMode,
            SelectionEdit,
            _CustomParticleEditorMode;
        static Vector2 SelectStart,
            SelectEnd;
        static int _selectionPresetIndex;
        static string _selectionMaterial = "Sand";
        static List<Entity> _selectedEntities = new();
        const float GraphPlotHeight = 100f;

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
            Action removeParticlesCallback,
            Action<string, float, float> createSingleParticleCallback
        )
        {
            var io = ImGui.GetIO();
            var winSize = WindowBounds.GetWindowSize();
            var bounds = WindowBounds.GetNormalizedBounds();
            float sx = winSize.X > 0 ? winSize.X : 1f,
                sy = winSize.Y > 0 ? winSize.Y : 1f;

            if (
                _SingularPlacementMode
                && !io.WantCaptureMouse
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            )
            {
                float worldX = bounds.left + (bounds.right - bounds.left) * (io.MousePos.X / sx);
                float worldY = bounds.top + (bounds.bottom - bounds.top) * (io.MousePos.Y / sy);
                if (_GridSnapMode)
                {
                    float gs = CustomParticleSettings.GridSnapSize * 0.1f;
                    // Snap relative to bounds origin to align with visual grid
                    float offsetX = worldX - bounds.left;
                    float offsetY = worldY - bounds.top;
                    offsetX = MathF.Round(offsetX / gs) * gs;
                    offsetY = MathF.Round(offsetY / gs) * gs;
                    worldX = bounds.left + offsetX;
                    worldY = bounds.top + offsetY;
                }
                createSingleParticleCallback(_selectionMaterial, worldX, worldY);
                return;
            }

            if (
                !io.WantCaptureMouse
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && !_SingularPlacementMode
            )
            {
                IsSelecting = true;
                SelectStart = SelectEnd = io.MousePos;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Escape) && IsSelecting)
            {
                IsSelecting = false;
                SelectStart = SelectEnd = Vector2.Zero;
            }

            if (IsSelecting)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                    SelectEnd = io.MousePos;
                else
                {
                    bool tooSmall =
                        MathF.Abs(SelectEnd.X - SelectStart.X) < 5f
                        || MathF.Abs(SelectEnd.Y - SelectStart.Y) < 5f;
                    if (tooSmall)
                        SelectStart = SelectEnd = Vector2.Zero;
                    else
                        UpdateSelectedEntities(entities);
                    IsSelecting = false;
                }
            }

            if (!IsSelecting && SelectStart != SelectEnd && SelectionEdit)
            {
                ImGui.SetNextWindowSize(new Vector2(250, 150), ImGuiCond.Always);
                ImGui.SetNextWindowPos(
                    new Vector2(SelectEnd.X, SelectEnd.Y - 150),
                    ImGuiCond.Always
                );
                ImGui.Begin("SelectionInfo");
                int matCount = ParticlePresetReader.GetPresetCount();
                if (matCount > 0)
                {
                    var names = new string[matCount];
                    var reader = new ParticlePresetReader();
                    for (int i = 0; i < matCount; i++)
                        names[i] = reader.GetPresetName(i);
                    selectionPresetIndex = Math.Clamp(selectionPresetIndex, 0, names.Length - 1);
                    int sel = selectionPresetIndex;
                    if (ImGui.Combo("Material", ref sel, names, names.Length))
                    {
                        selectionPresetIndex = Math.Clamp(sel, 0, names.Length - 1);
                        selectionMaterial = names[selectionPresetIndex];
                    }
                    if (string.IsNullOrWhiteSpace(selectionMaterial))
                        selectionMaterial = names[selectionPresetIndex];
                }
                else
                    ImGui.Text("No presets available");
                if (ImGui.Button("Fill"))
                {
                    float x0 = bounds.left + (bounds.right - bounds.left) * (SelectStart.X / sx);
                    float y0 = bounds.top + (bounds.bottom - bounds.top) * (SelectStart.Y / sy);
                    float x1 = bounds.left + (bounds.right - bounds.left) * (SelectEnd.X / sx);
                    float y1 = bounds.top + (bounds.bottom - bounds.top) * (SelectEnd.Y / sy);

                    float minX = MathF.Min(x0, x1),
                        maxX = MathF.Max(x0, x1);
                    float minY = MathF.Min(y0, y1),
                        maxY = MathF.Max(y0, y1);

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
                ImGui.SameLine();
                if (ImGui.Button("Create bonds"))
                {
                    // Create bonds between selected entities that are within a certain distance
                    float bondDistance = 0.5f;
                    var selectedList = new List<Entity>(_selectedEntities);
                    for (int i = 0; i < selectedList.Count; i++)
                    {
                        for (int j = i + 1; j < selectedList.Count; j++)
                        {
                            var e1 = selectedList[i];
                            var e2 = selectedList[j];
                            float dist = Vector2.Distance(e1.Position, e2.Position);
                            // if (dist <= bondDistance)
                            // {
                            //     // Create a bond between e1 and e2
                            //     Logger.Log($"Creating bond between Entity {e1.Id} and Entity {e2.Id}");
                            //     // Implement bond creation logic here, e.g., add to a list of bonds
                            // }
                        }
                    }
                }
                if (ImGui.Button("Clear Selection"))
                {
                    SelectStart = Vector2.Zero;
                    SelectEnd = Vector2.Zero;
                }
                ImGui.End();
            }

            if (IsSelecting || SelectStart != SelectEnd)
            {
                var min = new Vector2(
                    MathF.Min(SelectStart.X, SelectEnd.X),
                    MathF.Min(SelectStart.Y, SelectEnd.Y)
                );
                var max = new Vector2(
                    MathF.Max(SelectStart.X, SelectEnd.X),
                    MathF.Max(SelectStart.Y, SelectEnd.Y)
                );
                var drawList = ImGui.GetForegroundDrawList();
                float alpha = IsSelecting ? 0.25f : 0.05f;
                float borderAlpha = IsSelecting ? 1f : 0.8f;
                drawList.AddRectFilled(
                    min,
                    max,
                    ImGui.GetColorU32(
                        new Vector4(0f, IsSelecting ? 0.5f : 0.3f, IsSelecting ? 1f : 0.8f, alpha)
                    )
                );
                drawList.AddRect(
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(
                        new Vector4(
                            0f,
                            IsSelecting ? 0.5f : 0.3f,
                            IsSelecting ? 1f : 0.8f,
                            borderAlpha
                        )
                    ),
                    0f,
                    ImDrawFlags.None,
                    1.5f
                );
                DrawSelectedEntitiesHighlight();
            }
        }

        private static void UpdateSelectedEntities(List<Entity> entities)
        {
            _selectedEntities.Clear();
            var (minX, minY, maxX, maxY) = ScreenToWorldRect(SelectStart, SelectEnd);
            foreach (var e in entities)
                if (
                    e.Position.X >= minX
                    && e.Position.X <= maxX
                    && e.Position.Y >= minY
                    && e.Position.Y <= maxY
                )
                    _selectedEntities.Add(e);
            Logger.Log($"Selected {_selectedEntities.Count} entities");
        }

        private static (float minX, float minY, float maxX, float maxY) ScreenToWorldRect(
            Vector2 start,
            Vector2 end
        )
        {
            var winSize = WindowBounds.GetWindowSize();
            var bounds = WindowBounds.GetNormalizedBounds();
            float sx = winSize.X > 0 ? winSize.X : 1f,
                sy = winSize.Y > 0 ? winSize.Y : 1f;
            float x0 = bounds.left + (bounds.right - bounds.left) * (start.X / sx);
            float y0 = bounds.top + (bounds.bottom - bounds.top) * (start.Y / sy);
            float x1 = bounds.left + (bounds.right - bounds.left) * (end.X / sx);
            float y1 = bounds.top + (bounds.bottom - bounds.top) * (end.Y / sy);
            return (MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Max(x0, x1), MathF.Max(y0, y1));
        }

        private static void DrawSelectedEntitiesHighlight()
        {
            if (_selectedEntities.Count == 0)
                return;
            var drawList = ImGui.GetForegroundDrawList();
            var winSize = WindowBounds.GetWindowSize();
            var bounds = WindowBounds.GetNormalizedBounds();
            float sx = winSize.X > 0 ? winSize.X : 1f,
                sy = winSize.Y > 0 ? winSize.Y : 1f;
            uint col = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.8f));
            foreach (var e in _selectedEntities)
            {
                float scrX = ((e.Position.X - bounds.left) / (bounds.right - bounds.left)) * sx;
                float scrY = ((e.Position.Y - bounds.top) / (bounds.bottom - bounds.top)) * sy;
                drawList.AddCircle(new Vector2(scrX, scrY), 8f, col, 12, 2f);
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
            var (capacity, columns, _, origin) = ParticleGridLayout.CalculateParticleGrid(
                minX,
                minY,
                maxX,
                maxY,
                tempPs.Material.ParticleRadius,
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
            ref int tickCounter,
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
            bool ShowPreview = !startRunningImmediately;

            var mainSize = new Vector2(450, 320);
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
                    precomputeWindow = !precomputeWindow;
                    precomputeName = "Hide Precompute Settings";
                }
                if (precomputeWindow == false)
                {
                    precomputeName = "Show Precompute Settings";
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
            if (!_SimulationWindow && !hasPrecomputeRun)
            {
                editorName = _EditorWindow ? "Hide Editor Window" : "Show Editor Window";
                if (ImGui.Button(editorName))
                    _EditorWindow = !_EditorWindow;

                ImGui.SameLine();

                if (ImGui.Button("Add custom particle from .json"))
                {
                    var result = Dialog.FileOpen("json", Directory.GetCurrentDirectory());
                    if (result.IsOk && CustomParticleSettings.LoadPreset(result.Path))
                    {
                        Logger.Log($"Preset loaded from: {result.Path}");

                        // Add to ParticlePresets.json
                        try
                        {
                            AddPresetToParticlePresetsJson(result.Path);
                            Logger.Log("Preset added to ParticlePresets.json");
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(
                                $"Failed to add preset to ParticlePresets.json: {ex.Message}"
                            );
                        }
                    }
                }
            }

            ImGui.NewLine();
            ImGui.NewLine();
            if (_SimulationWindow && hasPrecomputeRun)
            {
                if (ImGui.Button("Reset Simulation"))
                {
                    // Reset all simulation state
                    tickCounter = 0;
                    tickIndex = 0;
                    playbackStartOffsetSeconds = 0f;
                    playbackTimer.Stop();
                    playbackTimer.Reset();

                    // Clear precomputed data
                    lock (tickLock)
                    {
                        tickPositions.Clear();
                    }

                    // Clear CSV plotter data
                    if (csvPlotter != null)
                    {
                        try
                        {
                            //csvPlotter.Clear();
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"CSV clear failed: {ex.Message}");
                        }
                    }

                    // Clear particles
                    clearParticlesCallback();

                    // Reset UI state - keep startRequested false to allow reconfiguration
                    startRequested = false;
                    _SimulationWindow = false;
                    precomputeWindow = true; // Open precompute window
                    hasPrecomputeRun = false;
                    precomputeName = "Hide Precompute Settings";
                    _EditorWindow = true; // Open editor for changes

                    Logger.Log("Simulation reset - ready for new configuration");
                }
            }
            if (ImGui.Button("Stop and Quit"))
                stopCallback();
            ImGui.Separator();

            if (showStartWindow)
            {
                ImGui.Text(
                    $"Ticks: {tickCounter} | Time: {lastTickTime:F2}ms | AvgSpd: {lastAvgSpeed:F4} | MaxSpd: {lastMaxSpeed:F4}"
                );
                ImGui.Text($"AvgY: {lastAvgPos.Y:F3} | Grounded: {lastGroundedCount}");
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

            if (_SimulationWindow)
            {
                ImGui.SetNextWindowSize(new Vector2(300, 200), ImGuiCond.Once);
                ImGui.SetNextWindowPos(new Vector2(columnX, nextY), ImGuiCond.Once);
                ImGui.Begin("Simulation");
                if (!startRunningImmediately)
                    playbackTimer.Stop();
                if (ImGui.Button("Play/Pause"))
                {
                    startRunningImmediately = true;
                    if (playbackTimer.IsRunning)
                    {
                        playbackStartOffsetSeconds += (float)playbackTimer.Elapsed.TotalSeconds;
                        playbackTimer.Reset();
                    }
                    else
                        playbackTimer.Restart();
                }
                ImGui.SameLine();
                if (ImGui.Button("Reset"))
                {
                    playbackStartOffsetSeconds = 0f;
                    tickIndex = 0;
                    playbackTimer.Reset();
                }
                ImGui.Separator();

                int selectedFrame = tickIndex,
                    maxFrame = maxFrames - 1;
                lock (tickLock)
                {
                    if (tickPositions.Count > 0)
                        maxFrame = Math.Min(maxFrames - 1, tickPositions.Count - 1);
                }
                if (ImGui.SliderInt("Frame Select", ref selectedFrame, 0, maxFrame))
                {
                    tickIndex = Math.Clamp(selectedFrame, 0, maxFrame);
                    playbackStartOffsetSeconds = tickIndex * (1f / MathF.Max(playbackFps, 1f));
                    if (playbackTimer.IsRunning)
                        playbackTimer.Reset();
                }
                if (ImGui.SliderFloat("Playback FPS", ref playbackFps, 1f, 240f))
                {
                    playbackFps = Math.Clamp(playbackFps, 1f, 240f);
                    playbackStartOffsetSeconds = tickIndex * (1f / playbackFps);
                    if (playbackTimer.IsRunning)
                        playbackTimer.Reset();
                }
                if (playbackFps != 60f && ImGui.Button("Reset FPS"))
                {
                    playbackFps = 60f;
                    playbackStartOffsetSeconds = tickIndex * (1f / playbackFps);
                    if (playbackTimer.IsRunning)
                        playbackTimer.Reset();
                }
                if (ImGui.Button("Close"))
                    _SimulationWindow = false;
                var simWinSize = ImGui.GetWindowSize();
                ImGui.End();
                nextY += simWinSize.Y + spacing;
            }

            if (precomputeWindow)
            {
                ImGui.SetNextWindowSize(new Vector2(330, 205), ImGuiCond.Once);
                ImGui.SetNextWindowPos(new Vector2(columnX, nextY), ImGuiCond.Once);
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
                ImGui.Separator();
                ImGui.Text("Physics:");
                float g = PhysicsSettings.GravitationalConstant,
                    air = PhysicsSettings.AirResistance,
                    wall = PhysicsSettings.WallRestitution;
                if (ImGui.SliderFloat("Gravity", ref g, 0f, 50f))
                    PhysicsSettings.GravitationalConstant = g;
                if (ImGui.SliderFloat("Air Resist", ref air, 0f, 1f))
                    PhysicsSettings.AirResistance = air;
                if (ImGui.SliderFloat("Bounciness", ref wall, 0f, 1f))
                    PhysicsSettings.WallRestitution = wall;
                if (ImGui.Button("Reset Physics"))
                    PhysicsSettings.Reset();
                ImGui.Separator();
                ImGui.Checkbox("Don't Show Preview of Compute", ref startRunningImmediately);
                var preWinSize = ImGui.GetWindowSize();
                ImGui.End();
                nextY += preWinSize.Y + spacing;
            }

            if (showGraphInWindow)
            {
                float graphX = mainRight + spacing,
                    graphW = MathF.Max(80f, display.X - graphX - margin);
                float graphH = GraphPlotHeight + ImGui.GetFrameHeightWithSpacing() + 36f;
                ImGui.SetNextWindowPos(new Vector2(graphX, mainWinPos.Y), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(graphW, graphH + 15), ImGuiCond.Always);
                ImGui.Begin("Tick Graph");
                ShowGraphGuiWindow(csvPlotter, tickCounter, maxFrames);
                ImGui.End();
            }

            /// <summary>
            /// Editor window for particle and simulation settings.
            /// </summary>
            if (_EditorWindow && !_SimulationWindow && !hasPrecomputeRun)
            {
                ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.Once);
                ImGui.SetNextWindowPos(
                    new Vector2(display.X - 600 - margin, margin),
                    ImGuiCond.Once
                );
                ImGui.Begin("Editor Window");
                ImGui.Separator();
                ImGui.Checkbox("Selection Edit Mode", ref SelectionEdit);
                ImGui.Checkbox("Singular placement", ref _SingularPlacementMode);

                if (_SingularPlacementMode)
                {
                    ImGui.Separator();
                    ImGui.Checkbox("Grid Snap", ref _GridSnapMode);
                    ImGui.Text(_GridSnapMode ? "Grid snap enabled." : "No grid snap.");
                    if (_GridSnapMode)
                    {
                        int gridSize = CustomParticleSettings.GridSnapSize;
                        if (ImGui.SliderInt("Grid Size", ref gridSize, 1, 100))
                            CustomParticleSettings.GridSnapSize = gridSize;
                        ShowGrid();
                    }
                    int matCount = ParticlePresetReader.GetPresetCount();
                    if (matCount > 0)
                    {
                        var names = new string[matCount];
                        var reader = new ParticlePresetReader();
                        for (int i = 0; i < matCount; i++)
                            names[i] = reader.GetPresetName(i);
                        _selectionPresetIndex = Math.Clamp(
                            _selectionPresetIndex,
                            0,
                            names.Length - 1
                        );
                        int sel = _selectionPresetIndex;
                        if (ImGui.Combo("Material", ref sel, names, names.Length))
                        {
                            _selectionPresetIndex = Math.Clamp(sel, 0, names.Length - 1);
                            _selectionMaterial = names[_selectionPresetIndex];
                        }
                    }
                    else
                        ImGui.Text("No presets available");
                }
                ImGui.Separator();
                ImGui.Checkbox("Custom Particle Editor", ref _CustomParticleEditorMode);
                if (_CustomParticleEditorMode)
                {
                    ImGui.Text("Custom Particle Editor:");
                    CustomParticleSettings.RenderUI();
                }
                ImGui.Separator();
                if (ImGui.Button("Reset to Defaults"))
                    CustomParticleSettings.ResetToDefaults();
                ImGui.Separator();
                if (ImGui.Button("Clear all particles"))
                    clearParticlesCallback();
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
            ImGui.Text($"Ticks: {tickCounter}");
            if (csvPlotter == null)
            {
                ImGui.Text("No CSV plotter.");
                return;
            }
            float[]? ySeries = csvPlotter.GetSeries(1) ?? csvPlotter.GetSeries(0);
            if (ySeries == null || ySeries.Length == 0)
            {
                ImGui.Text("No data.");
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
            float minVal = float.PositiveInfinity,
                maxVal = float.NegativeInfinity;
            foreach (var v in ySeries)
            {
                if (!float.IsNaN(v))
                {
                    if (v < minVal)
                        minVal = v;
                    if (v > maxVal)
                        maxVal = v;
                }
            }
            if (minVal == float.PositiveInfinity)
                ImGui.Text("No valid points.");
            else
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
            if (ImGui.Button("Reload CSV"))
            {
                try
                {
                    csvPlotter.Load();
                    Logger.Log("CSV reloaded");
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
            SelectStart = SelectEnd = Vector2.Zero;
        }

        private static void AddPresetToParticlePresetsJson(string sourceFilePath)
        {
            string presetName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string presetsPath = Path.Combine(
                Path.GetFullPath(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..")
                ),
                "Utilities",
                "ParticlePresets.json"
            );

            if (!File.Exists(presetsPath))
            {
                Logger.Log($"ParticlePresets.json not found at: {presetsPath}");
                return;
            }

            // Read existing presets
            var jsonText = File.ReadAllText(presetsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            // Read source preset
            var sourceJson = File.ReadAllText(sourceFilePath);
            var sourceDoc = System.Text.Json.JsonDocument.Parse(sourceJson);

            // Build new JSON with added preset
            using var stream = new MemoryStream();
            using (
                var writer = new System.Text.Json.Utf8JsonWriter(
                    stream,
                    new System.Text.Json.JsonWriterOptions { Indented = true }
                )
            )
            {
                writer.WriteStartObject();
                writer.WritePropertyName("Presets");
                writer.WriteStartObject();

                if (root.TryGetProperty("Presets", out var presets))
                {
                    foreach (var preset in presets.EnumerateObject())
                    {
                        writer.WritePropertyName(preset.Name);
                        preset.Value.WriteTo(writer);
                    }
                }
                writer.WritePropertyName(presetName);
                sourceDoc.RootElement.WriteTo(writer);

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            // Write back to file
            File.WriteAllText(presetsPath, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }

        private static void ShowGrid()
        {
            var drawList = ImGui.GetForegroundDrawList();
            var winSize = WindowBounds.GetWindowSize();
            var bounds = WindowBounds.GetNormalizedBounds();
            float sx = winSize.X > 0 ? winSize.X : 1f,
                sy = winSize.Y > 0 ? winSize.Y : 1f;
            float worldGridSize = CustomParticleSettings.GridSnapSize * 0.1f;
            float worldW = bounds.right - bounds.left,
                worldH = bounds.bottom - bounds.top;
            uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.15f));
            for (int i = 0; i <= (int)(worldW / worldGridSize) + 1; i++)
            {
                float worldX = bounds.left + i * worldGridSize;
                if (worldX > bounds.right)
                    break;
                float scrX = ((worldX - bounds.left) / worldW) * sx;
                drawList.AddLine(new Vector2(scrX, 0), new Vector2(scrX, sy), gridColor, 1f);
            }
            for (int i = 0; i <= (int)(worldH / worldGridSize) + 1; i++)
            {
                float worldY = bounds.top + i * worldGridSize;
                if (worldY > bounds.bottom)
                    break;
                float scrY = ((worldY - bounds.top) / worldH) * sy;
                drawList.AddLine(new Vector2(0, scrY), new Vector2(sx, scrY), gridColor, 1f);
            }
        }
    }
}
