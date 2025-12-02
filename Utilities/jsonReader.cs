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
        public float Mass { get; set; } = 1f;
        public float ParticleRadius { get; set; } = 0.01f;

        public float GravityStrength { get; set; } = 9.81f;
        public float Restitution { get; set; } = 0.0f;
        public float Friction { get; set; } = 0.1f;
        public bool EnableEdgeCollision { get; set; } = true;
        public float BondSpringConstant { get; set; } = 100f;
        public float BondDampingConstant { get; set; } = 10f;
        public float BondEquilibriumLength { get; set; } = 0.2f;
        public float BondCutoffDistance { get; set; } = 0.025f;
        public float LJ_Epsilon { get; set; } = 0.005f;
        public float LJ_Sigma { get; set; } = 0.02f;
        public float LJ_CutoffRadius { get; set; } = 0.4f;
        public float MaxForceMagnitude { get; set; } = 25f;
        public int MaxBondsPerParticle { get; set; } = 4;
        public List<CompositionItem>? Composition { get; set; }
        public bool? EnableCoulomb { get; set; }
        public float? Charge { get; set; }
        public float? CoulombConstant { get; set; }
        public float? DielectricConstant { get; set; }

        public bool? EnableDipole { get; set; }
        public float? DipoleMomentX { get; set; }
        public float? DipoleMomentY { get; set; }

        private static Dictionary<string, MDPresetReader>? _Presets;

        // Element-composition functionality removed. Presets now use explicit particle-level
        // parameters (Mass, LJ_Epsilon, LJ_Sigma, etc.) instead of per-element aggregation.

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
            md.BondEquilibriumLength = BondEquilibriumLength;
            md.BondCutoffDistance = BondCutoffDistance;
            md.MaxForceMagnitude = MaxForceMagnitude;
            md.MaxBondsPerEntity = MaxBondsPerParticle;

            // Apply velocity damping - scale with bond damping constant
            // This ensures materials with high bond damping also have velocity damping
            // But keep it reasonable so objects still fall at normal speed
            float dampingRatio = BondDampingConstant / BondSpringConstant;
            md.VelocityDamping = Math.Clamp(dampingRatio * 2.0f, 0.3f, 1.5f);

            // CRITICAL: For solid materials with strong bonds, disable Lennard-Jones to prevent instability
            // LJ forces combined with strong spring forces cause energy explosion
            if (BondSpringConstant > 1000f)
            {
                md.EnableLennardJones = false;
            }

            // Apply intermolecular force parameters only if specified
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

    // Element class removed. If you need to reintroduce element-based aggregation later,
    // consider adding a separate utility class that computes derived properties from
    // per-element data. For now presets must include explicit particle-level parameters.
}
