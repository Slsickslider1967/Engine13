using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine13.Core;
using Engine13.Utilities.Attributes;
using Vortice.Direct3D11;

namespace Engine13.Utilities.JsonReader
{
    /// <summary>
    /// Preset reader for particle simulation parameters.
    /// Handles particle-scale physics properties only.
    /// </summary>
    public class ParticlePresetReader
    {
        public class CompositionItem
        {
            public string Name { get; set; } = "";
            public int Ratio { get; set; } = 1;
            public string ParticleType { get; set; } = "standard"; // standard, heavy, light, fluid
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

        // Bond/spring parameters (Hooke's law)
        public float BondStiffness { get; set; } = 0f;
        public float BondDamping { get; set; } = 0f;

        // SPH-specific parameters for scientific fluid simulation
        public float SPHRestDensity { get; set; } = 1000f; // kg/m³ (water = 1000)
        public float SPHGasConstant { get; set; } = 2000f; // Pressure stiffness
        public float SPHViscosity { get; set; } = 0.1f; // Dynamic viscosity
        public float SPHSurfaceTension { get; set; } = 0.0728f; // Surface tension coefficient
        
        // Granular material parameters
        public float GranularFrictionAngle { get; set; } = 30f; // Internal friction angle (degrees)
        public float GranularCohesion { get; set; } = 0f; // Cohesive strength
        public float GranularDilatancy { get; set; } = 0f; // Volume expansion during shear

        public List<CompositionItem>? Composition { get; set; }

        private static Dictionary<string, ParticlePresetReader>? _Presets;

        public static ParticlePresetReader Load(string presetName)
        {
            if (_Presets == null)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string projectRoot = Path.GetFullPath(
                    Path.Combine(baseDir, "..", "..", "..", "..")
                );
                string jsonPath = Path.Combine(projectRoot, "Utilities", "ParticlePresets.json");

                Console.WriteLine($"[jsonReader] Looking for JSON at: {jsonPath}");
                Console.WriteLine($"[jsonReader] File.Exists = {File.Exists(jsonPath)}");

                if (!File.Exists(jsonPath))
                {
                    Console.WriteLine($"[jsonReader] File not found! Returning default preset.");
                    // Return default preset if file doesn't exist
                    return new ParticlePresetReader { Name = presetName };
                }

                string json = File.ReadAllText(jsonPath);
                var root = JsonSerializer.Deserialize<PresetCollection>(json);
                _Presets = root?.Presets ?? new Dictionary<string, ParticlePresetReader>();
                Console.WriteLine($"[jsonReader] Loaded {_Presets.Count} presets from JSON");
            }

            if (_Presets.TryGetValue(presetName, out var preset) && preset != null)
            {
                // Debug output
                Console.WriteLine($"[jsonReader] Loaded '{presetName}': IsFluid={preset.IsFluid}, IsGranular={preset.IsGranular}");
                return preset;
            }

            // Return default if preset not found
            return new ParticlePresetReader { Name = presetName };
        }

        public void ApplyTo(ParticleDynamics pd)
        {
            pd.MaxForceMagnitude = MaxForceMagnitude;
            pd.VelocityDamping = VelocityDamping;
            pd.PressureStrength = PressureStrength;
            pd.PressureRadius = PressureRadius;
        }

        public void ApplyTo(ParticleSystem ps)
        {
            if (ps == null)
                return;
        }
        public static int GetPresetCount()
        {
            if (_Presets == null)
            {
                Load(string.Empty);
            }
            return _Presets?.Count ?? 0;
        }

        public string GetPresetName(int index)
        {
            if (_Presets == null)
            {
                Load(string.Empty);
            }

            if (_Presets == null || _Presets.Count == 0)
                return string.Empty;

            var names = new List<string>(_Presets.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);

            return (index >= 0 && index < names.Count) ? names[index] : string.Empty;
        }

        private class PresetCollection
        {
            [JsonPropertyName("Presets")]
            public Dictionary<string, ParticlePresetReader> Presets { get; set; } = new();
        }
    }
}
