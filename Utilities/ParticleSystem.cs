using System.Linq;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Engine13.Utilities.JsonReader;
using Veldrid;

namespace Engine13.Core
{
    public class ParticleSystem
    {
        public string Name { get; }
        public ParticlePresetReader Material { get; }
        public List<Entity> Particles { get; } = new();

        private readonly SPH _sph = new();

        public ParticleSystem(string name, ParticlePresetReader material)
        {
            Name = name;
            Material = material;
        }

        public ParticleSystem(string name, string presetName)
            : this(name, ParticlePresetReader.Load(presetName)) { }

        public void CreateParticles(
            GraphicsDevice graphicsDevice,
            int particleCount,
            Vector2 origin,
            int columns,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid
        )
        {
            float particleRadius = Material.ParticleRadius;
            float diameter = particleRadius * 2f;
            float horizontalSpacing = diameter * 1.15f;
            float verticalSpacing = diameter * 1.15f;

            Particles.Clear();
            Particles.Capacity = particleCount;

            if (Material.Composition != null && Material.Composition.Count > 0)
            {
                CreateCompositionParticles(
                    graphicsDevice,
                    particleCount,
                    origin,
                    columns,
                    horizontalSpacing,
                    verticalSpacing,
                    particleRadius,
                    allEntities,
                    updateManager,
                    grid
                );
            }
            else
            {
                CreateUniformParticles(
                    graphicsDevice,
                    particleCount,
                    origin,
                    columns,
                    horizontalSpacing,
                    verticalSpacing,
                    particleRadius,
                    allEntities,
                    updateManager,
                    grid
                );
            }

            Logger.Log(
                $"[{Name}] Created {Particles.Count} particles with material '{Material.Name}'"
            );
            Logger.Log(
                $"  Material: restitution={Material.Restitution:F2}, friction={Material.Friction:F2}, mass={Material.Mass:F4}"
            );
        }

        private void CreateUniformParticles(
            GraphicsDevice graphicsDevice,
            int particleCount,
            Vector2 origin,
            int columns,
            float horizontalSpacing,
            float verticalSpacing,
            float particleRadius,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid
        )
        {
            for (int i = 0; i < particleCount; i++)
            {
                var particle = CreateSingleParticle(
                    graphicsDevice,
                    i,
                    origin,
                    columns,
                    horizontalSpacing,
                    verticalSpacing,
                    particleRadius,
                    "standard",
                    allEntities,
                    grid
                );

                RegisterParticle(particle, updateManager, grid, allEntities);
            }
        }

        private void CreateCompositionParticles(
            GraphicsDevice graphicsDevice,
            int particleCount,
            Vector2 origin,
            int columns,
            float horizontalSpacing,
            float verticalSpacing,
            float particleRadius,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid
        )
        {
            var composition = Material.Composition!;
            int totalRatio = composition.Sum(c => Math.Max(1, c.Ratio));
            var compCounts = new List<int>(composition.Count);
            int remaining = particleCount;

            for (int ci = 0; ci < composition.Count; ci++)
            {
                var c = composition[ci];
                int count =
                    (ci == composition.Count - 1)
                        ? remaining
                        : (int)Math.Round((double)particleCount * c.Ratio / totalRatio);
                compCounts.Add(Math.Max(0, count));
                remaining -= count;
            }

            int index = 0;
            for (int ci = 0; ci < composition.Count; ci++)
            {
                var comp = composition[ci];
                int compCount = compCounts[ci];

                for (int k = 0; k < compCount; k++, index++)
                {
                    string particleType = comp.ParticleType?.ToLowerInvariant() ?? "standard";
                    var particle = CreateSingleParticle(
                        graphicsDevice,
                        index,
                        origin,
                        columns,
                        horizontalSpacing,
                        verticalSpacing,
                        particleRadius,
                        particleType,
                        allEntities,
                        grid
                    );

                    RegisterParticle(particle, updateManager, grid, allEntities);
                }
            }
        }

        private Entity CreateSingleParticle(
            GraphicsDevice graphicsDevice,
            int index,
            Vector2 origin,
            int columns,
            float horizontalSpacing,
            float verticalSpacing,
            float particleRadius,
            string particleType,
            List<Entity> allEntities,
            SpatialGrid grid
        )
        {
            var particle = CircleFactory.CreateCircle(graphicsDevice, particleRadius, 8, 8);

            int column = index % columns;
            int row = index / columns;
            particle.Position = new Vector2(
                origin.X + column * horizontalSpacing,
                origin.Y + row * verticalSpacing
            );

            particle.CollisionRadius = particleRadius;
            particle.Mass = Material.Mass;

            particle.AddComponent(
                new Gravity(
                    Material.GravityStrength,
                    0f,
                    particle.Mass,
                    Material.HorizontalForce,
                    0f
                )
            );
            particle.AddComponent(
                new ObjectCollision
                {
                    Mass = particle.Mass,
                    Restitution = Material.Restitution,
                    Friction = Material.Friction,
                    IsFluid = Material.IsFluid,
                }
            );
            particle.AddComponent(
                Material.EnableEdgeCollision ? new EdgeCollision(false) : new EdgeCollision(true)
            );

            var particleDynamics = new ParticleDynamics(allEntities, grid);
            switch (particleType)
            {
                case "heavy":
                    particleDynamics.MaxForceMagnitude = 100f;
                    particleDynamics.VelocityDamping = 0.01f;
                    particleDynamics.PressureStrength = 4f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
                case "light":
                    particleDynamics.MaxForceMagnitude = 25f;
                    particleDynamics.VelocityDamping = 0.05f;
                    particleDynamics.PressureStrength = 2f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
                case "fluid":
                    particleDynamics.MaxForceMagnitude = 15f;
                    particleDynamics.VelocityDamping = 0.08f;
                    particleDynamics.PressureStrength = 1.5f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
                default:
                    particleDynamics.MaxForceMagnitude = 50f;
                    particleDynamics.VelocityDamping = 0.02f;
                    particleDynamics.PressureStrength = 2.5f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
            }

            Material.ApplyTo(particleDynamics);
            particle.AddComponent(particleDynamics);

            return particle;
        }

        private void RegisterParticle(
            Entity particle,
            UpdateManager updateManager,
            SpatialGrid grid,
            List<Entity> allEntities
        )
        {
            Particles.Add(particle);
            allEntities.Add(particle);
            updateManager.Register(particle);
            grid.AddEntity(particle);
        }

        public void InitializeFluid()
        {
            if (!Material.IsFluid)
                return;

            // Configure the SPH solver with material properties
            _sph.Configure(
                SPHMaterialType.Fluid,
                smoothingRadius: Material.ParticleRadius * 4.0f, // Larger radius for better neighbor detection
                gasConstant: Material.SPHGasConstant * 0.5f, // Gentler pressure
                viscosity: Material.SPHViscosity,
                restDensity: Material.SPHRestDensity,
                particleRadius: Material.ParticleRadius,
                gravity: new Vector2(0f, Material.GravityStrength),
                damping: 0.99f,
                maxVelocity: 3.0f
            );
            
            // Register all particles with the SPH solver
            _sph.Clear();
            var random = new Random();
            foreach (var particle in Particles)
            {
                _sph.AddParticle(particle);
                
                // Add tiny random velocity perturbations to break lattice symmetry
                // This creates more realistic, chaotic fluid behavior
                var oc = particle.GetComponent<Engine13.Utilities.Attributes.ObjectCollision>();
                if (oc != null)
                {
                    float perturbMagnitude = 0.01f;  // Very small perturbation to avoid adding energy
                    float vx = (float)(random.NextDouble() * 2.0 - 1.0) * perturbMagnitude;
                    float vy = (float)(random.NextDouble() * 2.0 - 1.0) * perturbMagnitude;
                    oc.Velocity = new Vector2(vx, vy);
                }
            }
        }

        public void InitializeGranular()
        {
            if (!Material.IsGranular)
                return;

            // Configure the SPH solver for granular materials
            _sph.Configure(
                SPHMaterialType.Granular,
                smoothingRadius: Material.ParticleRadius * 3.0f, // Smaller radius than fluids
                gasConstant: Material.SPHGasConstant,
                viscosity: Material.SPHViscosity,
                restDensity: Material.SPHRestDensity,
                particleRadius: Material.ParticleRadius,
                gravity: new Vector2(0f, Material.GravityStrength),
                damping: 0.95f, // Higher damping than fluids
                maxVelocity: 2.0f, // Lower max velocity
                frictionAngle: Material.GranularFrictionAngle,
                cohesion: Material.GranularCohesion,
                dilatancy: Material.GranularDilatancy
            );
            
            // Register all particles with the SPH solver
            _sph.Clear();
            foreach (var particle in Particles)
            {
                _sph.AddParticle(particle);
                
                // Set initial velocity to zero - granular materials start at rest
                var oc = particle.GetComponent<Engine13.Utilities.Attributes.ObjectCollision>();
                if (oc != null)
                {
                    oc.Velocity = Vector2.Zero;
                }
            }
        }

        public int FluidCount => _sph.ParticleCount;
        public int GranularCount => _sph.ParticleCount;

        public void GetFluidDebugData(
            Dictionary<Entity, int> entityToIndex,
            Span<float> densitiesOut,
            Span<float> pressuresOut,
            Span<int> neighborCountsOut
        )
        {
            _sph.GetAllDebugData(entityToIndex, densitiesOut, pressuresOut, neighborCountsOut);
        }

        public bool TryGetEntityAt(int index, out Entity entity)
        {
            return _sph.TryGetEntityAt(index, out entity);
        }

        public void StepFluid(float dt, SpatialGrid grid)
        {
            if (!Material.IsFluid && !Material.IsGranular)
                return;
                
            _sph.Step(dt, grid);
        }

        public (int particleCount, float avgDensity, float maxDensity, float avgPressure, 
                float avgViscosity, float avgNeighbors, float avgVelocity, float maxVelocity) GetSPHDebugInfo()
        {
            if (!Material.IsFluid && !Material.IsGranular)
                return (0, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                
            return (_sph.ParticleCount, _sph.AvgDensity, _sph.MaxDensity, _sph.AvgPressure,
                    _sph.AvgViscosityForce, _sph.AvgNeighbors, _sph.AvgVelocity, _sph.MaxVelocity);
        }
    }

    public static class ParticleSystemFactory
    {
        public static ParticleSystem Create(string systemName, string presetName)
        {
            return new ParticleSystem(systemName, presetName);
        }

        public static List<ParticleSystem> CreateMultiple(
            params (string systemName, string presetName)[] systems
        )
        {
            var result = new List<ParticleSystem>(systems.Length);
            foreach (var (name, preset) in systems)
            {
                result.Add(new ParticleSystem(name, preset));
            }
            return result;
        }
    }
}
