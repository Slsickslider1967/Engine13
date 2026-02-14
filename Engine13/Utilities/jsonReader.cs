using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine13.Core;
using Engine13.Utilities.Attributes;
using Vortice.Direct3D11;

namespace Engine13.Utilities.JsonReader
{
    public class ParticlePresetReader
    {
        public class CompositionItem
        {
            public string Name { get; set; } = "";
            public int Ratio { get; set; } = 1;
            public string ParticleType { get; set; } = "standard";
        }

        public string Name { get; set; } = "Unknown";
        public string Description { get; set; } = "Generic particle";
        public float Mass { get; set; } = 0.004f;
        public float ParticleRadius { get; set; } = 0.01f;
        public float GravityStrength { get; set; } = 2.0f;
        public float HorizontalForce { get; set; } = 0f;
        public float Restitution { get; set; } = 0.1f;
        public float Friction { get; set; } = 0.5f;
        public bool EnableEdgeCollision { get; set; } = true;
        public float MaxForceMagnitude { get; set; } = 50f;
        public float VelocityDamping { get; set; } = 0.03f;
        public float PressureStrength { get; set; } = 2.5f;
        private int MatericalCount { get; set; } = 1;
        public float PressureRadius { get; set; } = ParticleDynamics.DefaultPressureRadius;
        public bool IsFluid { get; set; } = false;
        public bool IsSolid { get; set; } = false;
        public bool IsGranular { get; set; } = false;
        public float BondStiffness { get; set; } = 0f;
        public float BondDamping { get; set; } = 0f;
        public float SPHRestDensity { get; set; } = 1000f;
        public float SPHGasConstant { get; set; } = 2000f;
        public float SPHViscosity { get; set; } = 0.1f;
        public float SPHSurfaceTension { get; set; } = 0.0728f;
        public float GranularFrictionAngle { get; set; } = 30f;
        public float GranularCohesion { get; set; } = 0f;
        public float GranularDilatancy { get; set; } = 0f;
        public List<CompositionItem>? Composition { get; set; }

        private static Dictionary<string, ParticlePresetReader>? _Presets;

        public static ParticlePresetReader Load(string presetName)
        {
            if (_Presets == null)
            {
                string jsonPath = Path.Combine(
                    Path.GetFullPath(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..")
                    ),
                    "Utilities",
                    "ParticlePresets.json"
                );
                Console.WriteLine(
                    $"[jsonReader] Looking for JSON at: {jsonPath}, exists={File.Exists(jsonPath)}"
                );
                if (!File.Exists(jsonPath))
                    return new ParticlePresetReader { Name = presetName };
                _Presets =
                    JsonSerializer
                        .Deserialize<PresetCollection>(File.ReadAllText(jsonPath))
                        ?.Presets ?? new();
                Console.WriteLine($"[jsonReader] Loaded {_Presets.Count} presets from JSON");
            }
            if (_Presets.TryGetValue(presetName, out var preset) && preset != null)
            {
                Console.WriteLine(
                    $"[jsonReader] Loaded '{presetName}': IsFluid={preset.IsFluid}, IsGranular={preset.IsGranular}"
                );
                return preset;
            }
            return new ParticlePresetReader { Name = presetName };
        }

        public void ApplyTo(ParticleDynamics pd)
        {
            pd.MaxForceMagnitude = MaxForceMagnitude;
            pd.VelocityDamping = VelocityDamping;
            pd.PressureStrength = PressureStrength;
            pd.PressureRadius = PressureRadius;
        }

        public void ApplyTo(ParticleSystem ps) { }

        public static int GetPresetCount()
        {
            if (_Presets == null)
                Load(string.Empty);
            return _Presets?.Count ?? 0;
        }

        public string GetPresetName(int index)
        {
            if (_Presets == null)
                Load(string.Empty);
            if (_Presets == null || _Presets.Count == 0)
                return string.Empty;
            var names = _Presets.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return (index >= 0 && index < names.Count) ? names[index] : string.Empty;
        }

        private class PresetCollection
        {
            [JsonPropertyName("Presets")]
            public Dictionary<string, ParticlePresetReader> Presets { get; set; } = new();
        }
    }
}
