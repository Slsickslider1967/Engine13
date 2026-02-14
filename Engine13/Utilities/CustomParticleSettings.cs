using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using ImGuiNET;
using NativeFileDialogSharp;

namespace Engine13.Utilities
{
    [AttributeUsage(AttributeTargets.Property)]
    public abstract class ParticleSettingAttribute(string displayName) : Attribute
    {
        public string DisplayName { get; set; } = displayName;
        public string? Tooltip { get; set; }
        public int Order { get; set; } = 0;
        public abstract bool RenderControl(PropertyInfo property, object instance);

        protected void ShowTooltip()
        {
            if (!string.IsNullOrEmpty(Tooltip) && ImGui.IsItemHovered())
                ImGui.SetTooltip(Tooltip);
        }
    }

    public class FloatSliderAttribute(string displayName, float min = 0f, float max = 1f)
        : ParticleSettingAttribute(displayName)
    {
        public float Min { get; set; } = min;
        public float Max { get; set; } = max;
        public string Format { get; set; } = "%.2f";

        public override bool RenderControl(PropertyInfo p, object inst)
        {
            float v = (float)p.GetValue(inst)!;
            bool c = ImGui.SliderFloat(DisplayName, ref v, Min, Max, Format);
            if (c)
                p.SetValue(inst, v);
            ShowTooltip();
            return c;
        }
    }

    public class IntSliderAttribute(string displayName, int min = 0, int max = 100)
        : ParticleSettingAttribute(displayName)
    {
        public int Min { get; set; } = min;
        public int Max { get; set; } = max;

        public override bool RenderControl(PropertyInfo p, object inst)
        {
            int v = (int)p.GetValue(inst)!;
            bool c = ImGui.SliderInt(DisplayName, ref v, Min, Max);
            if (c)
                p.SetValue(inst, v);
            ShowTooltip();
            return c;
        }
    }

    public class CheckboxAttribute(string displayName) : ParticleSettingAttribute(displayName)
    {
        public override bool RenderControl(PropertyInfo p, object inst)
        {
            bool v = (bool)p.GetValue(inst)!;
            bool c = ImGui.Checkbox(DisplayName, ref v);
            if (c)
                p.SetValue(inst, v);
            ShowTooltip();
            return c;
        }
    }

    public class ColorPickerAttribute(string displayName) : ParticleSettingAttribute(displayName)
    {
        public override bool RenderControl(PropertyInfo p, object inst)
        {
            var v = (Vector3)p.GetValue(inst)!;
            bool c = ImGui.ColorEdit3(DisplayName, ref v);
            if (c)
                p.SetValue(inst, v);
            ShowTooltip();
            return c;
        }
    }

    public class Vector2InputAttribute(string displayName) : ParticleSettingAttribute(displayName)
    {
        public override bool RenderControl(PropertyInfo p, object inst)
        {
            var v = (Vector2)p.GetValue(inst)!;
            bool c = ImGui.DragFloat2(DisplayName, ref v);
            if (c)
                p.SetValue(inst, v);
            ShowTooltip();
            return c;
        }
    }

    public static class CustomParticleSettings
    {
        [FloatSlider("Particle Radius", 0.1f, 5f)]
        public static float ParticleRadius { get; set; } = 0.5f;

        [ColorPicker("Particle Color")]
        public static Vector3 ParticleColor { get; set; } = new(1f, 1f, 1f);

        [FloatSlider("Mass Multiplier", 0.1f, 10f)]
        public static float MassMultiplier { get; set; } = 1.0f;

        [FloatSlider("Inter-Particle Elasticity", 0f, 1f)]
        public static float InterParticleElasticity { get; set; } = 0.5f;

        [FloatSlider("Charge", -10f, 10f)]
        public static float Charge { get; set; } = 0f;

        [FloatSlider("Charge Strength", 0f, 100f)]
        public static float ChargeStrength { get; set; } = 10f;

        [FloatSlider("Friction Coefficient", 0f, 1f)]
        public static float FrictionCoefficient { get; set; } = 0.3f;

        [FloatSlider("Drag Coefficient", 0f, 2f)]
        public static float DragCoefficient { get; set; } = 0.1f;

        [IntSlider("Lifetime (frames)", 0, 10000)]
        public static int ParticleLifetime { get; set; } = 0;

        [FloatSlider("Attraction Radius", 0f, 50f)]
        public static float AttractionRadius { get; set; } = 5f;

        [Checkbox("Affected by Gravity")]
        public static bool AffectedByGravity { get; set; } = true;

        [FloatSlider("Bounciness (vs Walls)", 0f, 1f)]
        public static float WallBounciness { get; set; } = 0.5f;

        public static int GridSnapSize { get; set; } = 5;

        public static void RenderUI()
        {
            var cats = new Dictionary<string, List<(PropertyInfo, ParticleSettingAttribute)>>
            {
                ["Visual"] = new(),
                ["Physics"] = new(),
                ["Interaction"] = new(),
                ["Other"] = new(),
            };
            foreach (var (p, a) in GetOrderedSettingsProperties())
                cats[DetermineCategory(p.Name)].Add((p, a));
            foreach (var cat in cats.Where(c => c.Value.Count > 0))
                if (ImGui.CollapsingHeader(cat.Key, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    foreach (var (p, a) in cat.Value)
                        a.RenderControl(p, null!);
                    ImGui.Unindent();
                }

            ImGui.Separator();
            if (ImGui.Button("Save Preset"))
            {
                var result = Dialog.FileSave("json", Directory.GetCurrentDirectory());
                if (result.IsOk)
                {
                    SavePreset(result.Path);
                    Console.WriteLine($"Preset saved to: {result.Path}");
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Load Preset"))
            {
                var result = Dialog.FileOpen("json", Directory.GetCurrentDirectory());
                if (result.IsOk && LoadPreset(result.Path))
                {
                    Console.WriteLine($"Preset loaded from: {result.Path}");
                }
            }
        }

        private static void SavePreset(string filePath)
        {
            var settings = new Dictionary<string, object>();
            foreach (
                var prop in typeof(CustomParticleSettings).GetProperties(
                    BindingFlags.Public | BindingFlags.Static
                )
            )
            {
                if (prop.GetCustomAttribute<ParticleSettingAttribute>() != null)
                {
                    var value = prop.GetValue(null);
                    if (value is Vector2 v2)
                        settings[prop.Name] = new { X = v2.X, Y = v2.Y };
                    else if (value is Vector3 v3)
                        settings[prop.Name] = new
                        {
                            X = v3.X,
                            Y = v3.Y,
                            Z = v3.Z,
                        };
                    else
                        settings[prop.Name] = value!;
                }
            }
            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }

        public static bool LoadPreset(string filePath)
        {
            if (!File.Exists(filePath))
                return false;
            try
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (settings == null)
                    return false;

                foreach (var kvp in settings)
                {
                    var prop = typeof(CustomParticleSettings).GetProperty(kvp.Key);
                    if (prop == null)
                        continue;

                    if (prop.PropertyType == typeof(Vector2))
                        prop.SetValue(
                            null,
                            new Vector2(
                                kvp.Value.GetProperty("X").GetSingle(),
                                kvp.Value.GetProperty("Y").GetSingle()
                            )
                        );
                    else if (prop.PropertyType == typeof(Vector3))
                        prop.SetValue(
                            null,
                            new Vector3(
                                kvp.Value.GetProperty("X").GetSingle(),
                                kvp.Value.GetProperty("Y").GetSingle(),
                                kvp.Value.GetProperty("Z").GetSingle()
                            )
                        );
                    else if (prop.PropertyType == typeof(float))
                        prop.SetValue(null, kvp.Value.GetSingle());
                    else if (prop.PropertyType == typeof(int))
                        prop.SetValue(null, kvp.Value.GetInt32());
                    else if (prop.PropertyType == typeof(bool))
                        prop.SetValue(null, kvp.Value.GetBoolean());
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void ResetToDefaults()
        {
            ParticleRadius = 0.5f;
            ParticleColor = new(1f, 1f, 1f);
            MassMultiplier = 1.0f;
            InterParticleElasticity = 0.5f;
            Charge = 0f;
            ChargeStrength = 10f;
            FrictionCoefficient = 0.3f;
            DragCoefficient = 0.1f;
            ParticleLifetime = 0;
            AttractionRadius = 5f;
            AffectedByGravity = true;
            WallBounciness = 0.5f;
        }

        public static T? GetSetting<T>(string name) =>
            typeof(CustomParticleSettings).GetProperty(name) is { } p
                ? (T?)p.GetValue(null)
                : default;

        public static void SetSetting(string name, object value) =>
            typeof(CustomParticleSettings).GetProperty(name)?.SetValue(null, value);

        private static List<(
            PropertyInfo,
            ParticleSettingAttribute
        )> GetOrderedSettingsProperties() =>
            typeof(CustomParticleSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Select(p => (p, p.GetCustomAttribute<ParticleSettingAttribute>()))
                .Where(x => x.Item2 != null)
                .Select(x => (x.p, x.Item2!))
                .OrderBy(x => x.Item2.Order)
                .ToList();

        private static string DetermineCategory(string name) =>
            name.Contains("Mass")
            || name.Contains("Friction")
            || name.Contains("Elasticity")
            || name.Contains("Drag")
            || name.Contains("Bounciness")
            || name.Contains("Gravity")
                ? "Physics"
            : name.Contains("Color") || name.Contains("Radius") ? "Visual"
            : name.Contains("Charge") || name.Contains("Attraction") || name.Contains("Lifetime")
                ? "Interaction"
            : "Other";
    }
}
