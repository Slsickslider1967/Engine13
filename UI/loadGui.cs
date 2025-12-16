using ImGuiNET;
using Vulkan;

namespace Engine13.UI
{
    public static partial class LoadGui
    {
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
            string debugText = Console.In.ReadToEnd();
            ImGui.Text(debugText);
            ImGui.End();
        }
    }
}
