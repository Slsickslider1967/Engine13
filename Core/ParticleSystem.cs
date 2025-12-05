using System.Numerics;
using System.Linq;
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
        public Vector2 CenterOfMass
        {
            get
            {
                if (Particles.Count == 0) return Vector2.Zero;
                
                float totalMass = 0f;
                Vector2 weightedSum = Vector2.Zero;
                
                foreach (var particle in Particles)
                {
                    weightedSum += particle.Position * particle.Mass;
                    totalMass += particle.Mass;
                }
                
                return totalMass > 0 ? weightedSum / totalMass : Vector2.Zero;
            }
        }

        /// <summary>Total mass of all particles in this system</summary>
        public float TotalMass
        {
            get
            {
                float total = 0f;
                foreach (var particle in Particles)
                {
                    total += particle.Mass;
                }
                return total;
            }
        }

        /// <summary>Average velocity of all particles</summary>
        public Vector2 AverageVelocity
        {
            get
            {
                if (Particles.Count == 0) return Vector2.Zero;
                
                Vector2 sum = Vector2.Zero;
                foreach (var particle in Particles)
                {
                    sum += particle.Velocity;
                }
                return sum / Particles.Count;
            }
        }

        public ParticleSystem(string name, ParticlePresetReader material)
        {
            Name = name;
            Material = material;
        }

        /// <summary>Create a new particle system by loading a preset by name</summary>
        public ParticleSystem(string name, string presetName)
            : this(name, ParticlePresetReader.Load(presetName))
        {
        }
        /// <param name="graphicsDevice">Graphics device for rendering</param>
        /// <param name="particleCount">Number of particles to create</param>
        /// <param name="origin">Starting position for the grid</param>
        /// <param name="columns">Number of columns in the grid layout</param>
        /// <param name="allEntities">Global entity list to add particles to</param>
        /// <param name="updateManager">Update manager to register particles with</param>
        /// <param name="grid">Spatial grid for collision detection</param>
        public void CreateParticles(
            GraphicsDevice graphicsDevice,
            int particleCount,
            Vector2 origin,
            int columns,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid)
        {
            float particleRadius = Material.ParticleRadius;
            float diameter = particleRadius * 2f;
            float horizontalSpacing = diameter * 1.15f;
            float verticalSpacing = diameter * 1.15f;

            Particles.Clear();
            Particles.Capacity = particleCount;

            // Check if we have composition (mixed particle types within the material)
            if (Material.Composition != null && Material.Composition.Count > 0)
            {
                CreateCompositionParticles(
                    graphicsDevice, particleCount, origin, columns,
                    horizontalSpacing, verticalSpacing, particleRadius,
                    allEntities, updateManager, grid);
            }
            else
            {
                CreateUniformParticles(
                    graphicsDevice, particleCount, origin, columns,
                    horizontalSpacing, verticalSpacing, particleRadius,
                    allEntities, updateManager, grid);
            }

            if (Material.IsSolid)
            {
                CreateParticleBonds(horizontalSpacing, verticalSpacing);
            }

            Logger.Log($"[{Name}] Created {Particles.Count} particles with material '{Material.Name}'");
            Logger.Log($"  Material: restitution={Material.Restitution:F2}, friction={Material.Friction:F2}, mass={Material.Mass:F4}");
            if (Material.IsSolid)
            {
                int totalBonds = Particles.Sum(p => p.GetComponent<ParticleDynamics>()?.Bonds.Count ?? 0) / 2;
                Logger.Log($"  Solid bonds created: {totalBonds}");
            }
        }

        private void CreateParticleBonds(float horizontalSpacing, float verticalSpacing)
        {
            float bondRadius = MathF.Sqrt(horizontalSpacing * horizontalSpacing + verticalSpacing * verticalSpacing) * 1.1f;
            
            foreach (var particle in Particles)
            {
                var dynamics = particle.GetComponent<ParticleDynamics>();
                if (dynamics != null && dynamics.IsSolid)
                {
                    dynamics.CreateBondsWithNeighbors(particle, Particles, bondRadius);
                }
            }
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
            SpatialGrid grid)
        {
            for (int i = 0; i < particleCount; i++)
            {
                var particle = CreateSingleParticle(
                    graphicsDevice, i, origin, columns,
                    horizontalSpacing, verticalSpacing, particleRadius,
                    "standard", allEntities);

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
            SpatialGrid grid)
        {
            var composition = Material.Composition!;
            int totalRatio = composition.Sum(c => Math.Max(1, c.Ratio));
            var compCounts = new List<int>(composition.Count);
            int remaining = particleCount;

            // Compute per-composition counts
            for (int ci = 0; ci < composition.Count; ci++)
            {
                var c = composition[ci];
                int count = (ci == composition.Count - 1)
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
                        graphicsDevice, index, origin, columns,
                        horizontalSpacing, verticalSpacing, particleRadius,
                        particleType, allEntities);

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
            List<Entity> allEntities)
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

            particle.AddComponent(new Gravity(
                Material.GravityStrength, 
                0f, 
                particle.Mass, 
                Material.HorizontalForce, 
                0f));
            particle.AddComponent(new ObjectCollision
            {
                Mass = particle.Mass,
                Restitution = Material.Restitution,
                Friction = Material.Friction,
                IsFluid = Material.IsFluid,
            });

            particle.AddComponent(
                Material.EnableEdgeCollision
                    ? new EdgeCollision(false)
                    : new EdgeCollision(true)
            );

            // Create particle dynamics based on type
            ParticleDynamics particleDynamics = particleType switch
            {
                "heavy" => ParticleDynamics.CreateHeavyParticle(allEntities),
                "light" => ParticleDynamics.CreateLightParticle(allEntities),
                "fluid" => ParticleDynamics.CreateFluidParticle(allEntities),
                _ => ParticleDynamics.CreateParticle(allEntities)
            };

            Material.ApplyTo(particleDynamics);
            particle.AddComponent(particleDynamics);

            return particle;
        }

        private void RegisterParticle(Entity particle, UpdateManager updateManager, SpatialGrid grid, List<Entity> allEntities)
        {
            Particles.Add(particle);
            allEntities.Add(particle);
            updateManager.Register(particle);
            grid.AddEntity(particle);
        }

        /// <summary>
        /// Get kinetic energy of this particle system
        /// </summary>
        public float GetKineticEnergy()
        {
            float energy = 0f;
            foreach (var particle in Particles)
            {
                float speed = particle.Velocity.Length();
                energy += 0.5f * particle.Mass * speed * speed;
            }
            return energy;
        }

        /// <summary>
        /// Get potential energy of this particle system (gravitational)
        /// </summary>
        public float GetPotentialEnergy()
        {
            float energy = 0f;
            foreach (var particle in Particles)
            {
                energy += particle.Mass * Material.GravityStrength * (particle.Position.Y + 1f);
            }
            return energy;
        }
    }

    /// <summary>
    /// Factory for creating particle systems from presets
    /// </summary>
    public static class ParticleSystemFactory
    {
        /// <summary>
        /// Create a particle system with a specific preset
        /// </summary>
        public static ParticleSystem Create(string systemName, string presetName)
        {
            return new ParticleSystem(systemName, presetName);
        }

        /// <summary>
        /// Create multiple particle systems from a list of preset names
        /// </summary>
        public static List<ParticleSystem> CreateMultiple(params (string systemName, string presetName)[] systems)
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
