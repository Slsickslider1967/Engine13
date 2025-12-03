using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine13.Utilities.Attributes;

namespace Engine13.Utilities.JsonReader
{
    public class MDPresetReader
    {
        public class CompositionItem
        {
            public string Name { get; set; } = "";
            public int Ratio { get; set; } = 1;
            public string MDType { get; set; } = ""; // e.g., Ion, Water, Solid, Liquid, Gas
            public float? Charge { get; set; }
        }

        public string Name { get; set; } = "Unknown";
        public string Description { get; set; } = "Generic particle";
        public float Mass { get; set; } = 0.004f;
        public float ParticleRadius { get; set; } = 0.01f;

        public float GravityStrength { get; set; } = 2.0f;
        public float Restitution { get; set; } = 0.1f;
        public float Friction { get; set; } = 0.5f;
        public bool EnableEdgeCollision { get; set; } = true;
        public float BondSpringConstant { get; set; } = 1000f;
        public float BondDampingConstant { get; set; } = 100f;
        public float BondEquilibriumLength { get; set; } = 0.023f;
        public float BondCutoffDistance { get; set; } = 0.028f;
        public float LJ_Epsilon { get; set; } = 0.001f;
        public float LJ_Sigma { get; set; } = 0.02f;
        public float LJ_CutoffRadius { get; set; } = 0.05f;
        public float MaxForceMagnitude { get; set; } = 2000f;
        public int MaxBondsPerParticle { get; set; } = 8;
        public List<CompositionItem>? Composition { get; set; }
        public bool? EnableCoulomb { get; set; }
        public float? Charge { get; set; }
        public float? CoulombConstant { get; set; }
        public float? DielectricConstant { get; set; }

        public bool? EnableDipole { get; set; }
        public float? DipoleMomentX { get; set; }
        public float? DipoleMomentY { get; set; }

        private static Dictionary<string, MDPresetReader>? _Presets;

        public static MDPresetReader Load(string presetName)
        {
            if (_Presets == null)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string projectRoot = Path.GetFullPath(
                    Path.Combine(baseDir, "..", "..", "..", "..", "..")
                );
                string jsonPath = Path.Combine(projectRoot, "Utilities", "MDPresets.json");

                if (!File.Exists(jsonPath))
                {
                    throw new FileNotFoundException($"MDPresets.json not found at: {jsonPath}");
                }

                string json = File.ReadAllText(jsonPath);
                var root = JsonSerializer.Deserialize<PresetCollection>(json);
                _Presets = root?.Presets ?? throw new Exception("Failed to load MD presets.");
            }

            if (_Presets.TryGetValue(presetName, out var preset))
            {
                return preset;
            }

            throw new Exception(
                $"MD preset '{presetName}' not found. Available presets: {string.Join(", ", _Presets.Keys)}"
            );
        }

        public void ApplyTo(MolecularDynamics md)
        {
            md.BondSpringConstant = BondSpringConstant;
            md.BondDampingConstant = BondDampingConstant;  // Apply bond damping!
            md.BondEquilibriumLength = BondEquilibriumLength;
            md.BondCutoffDistance = BondCutoffDistance;
            md.MaxForceMagnitude = MaxForceMagnitude;
            md.MaxBondsPerEntity = MaxBondsPerParticle;

            float dampingRatio = BondDampingConstant / (2.0f * MathF.Sqrt(BondSpringConstant * 0.004f));
            md.VelocityDamping = Math.Clamp(dampingRatio * 0.5f, 0.1f, 0.8f);

            if (BondSpringConstant > 2000f)
            {
                md.EnableLennardJones = false;
            }

            if (EnableCoulomb.HasValue)
                md.EnableCoulomb = EnableCoulomb.Value;
            else
                md.EnableCoulomb = false;

            if (Charge.HasValue)
                md.Charge = Charge.Value;
            if (CoulombConstant.HasValue)
                md.CoulombConstant = CoulombConstant.Value;
            if (DielectricConstant.HasValue)
                md.DielectricConstant = DielectricConstant.Value;

            // Handle dipole - only enable if specified
            if (EnableDipole.HasValue && EnableDipole.Value)
            {
                md.EnableDipole = true;
                float dipoleX = DipoleMomentX ?? 0f;
                float dipoleY = DipoleMomentY ?? 0f;
                md.DipoleMoment = new Vector2(dipoleX, dipoleY);
            }
            else
            {
                md.EnableDipole = false;
                md.DipoleMoment = Vector2.Zero;
            }
        }

        private class PresetCollection
        {
            [JsonPropertyName("Presets")]
            public Dictionary<string, MDPresetReader> Presets { get; set; } = new();
        }
    }
}
