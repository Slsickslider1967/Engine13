using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine13.Utilities.Attributes;

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
        public float PressureRadius { get; set; } = 0.02f;
        
        public bool IsFluid { get; set; } = false;
        public bool IsSolid { get; set; } = false;
        public float BondStiffness { get; set; } = 50.0f;
        public float BondDamping { get; set; } = 5.0f;
        
        // SPH-specific parameters for scientific fluid simulation
        public float SPHRestDensity { get; set; } = 1000f;       // kg/m³ (water = 1000)
        public float SPHGasConstant { get; set; } = 2000f;       // Pressure stiffness
        public float SPHViscosity { get; set; } = 0.1f;          // Dynamic viscosity
        public float SPHSurfaceTension { get; set; } = 0.0728f;  // Surface tension coefficient
        
        public List<CompositionItem>? Composition { get; set; }

        private static Dictionary<string, ParticlePresetReader>? _Presets;

        public static ParticlePresetReader Load(string presetName)
        {
            if (_Presets == null)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string projectRoot = Path.GetFullPath(
                    Path.Combine(baseDir, "..", "..", "..", "..", "..")
                );
                string jsonPath = Path.Combine(projectRoot, "Utilities", "ParticlePresets.json");

                if (!File.Exists(jsonPath))
                {
                    // Return default preset if file doesn't exist
                    return new ParticlePresetReader { Name = presetName };
                }

                string json = File.ReadAllText(jsonPath);
                var root = JsonSerializer.Deserialize<PresetCollection>(json);
                _Presets = root?.Presets ?? new Dictionary<string, ParticlePresetReader>();
            }

            if (_Presets.TryGetValue(presetName, out var preset))
            {
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
            pd.IsSolid = IsSolid;
            pd.BondStiffness = BondStiffness;
            pd.BondDamping = BondDamping;
        }

        private class PresetCollection
        {
            [JsonPropertyName("Presets")]
            public Dictionary<string, ParticlePresetReader> Presets { get; set; } = new();
        }
    }
}
