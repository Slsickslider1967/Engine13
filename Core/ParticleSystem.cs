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
    public class FluidParticle
    {
        public Entity Entity { get; set; } = null!;
        public List<FluidParticle> Neighbors { get; } = new();
    }

    public class ParticleSystem
    {
        public string Name { get; }
        public ParticlePresetReader Material { get; }
        public List<Entity> Particles { get; } = new();
        
        readonly List<FluidParticle> _fluidParticles = new();
        readonly Dictionary<Entity, FluidParticle> _fluidMap = new();
        
        float _smoothingRadius;
        float _stiffness;
        float _viscosity;
        float _particleRadius;
        float _maxVelocity = 2f;
        float _damping = 0.95f;
        Vector2 _gravity;

        public ParticleSystem(string name, ParticlePresetReader material)
        {
            Name = name;
            Material = material;
        }

        /// <summary>Create a new particle system by loading a preset by name</summary>
        public ParticleSystem(string name, string presetName)
            : this(name, ParticlePresetReader.Load(presetName)) { }

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
            SpatialGrid grid
        )
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

            // TODO: Implement solid dynamics system

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
                    allEntities
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

            // Compute per-composition counts
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
                        allEntities
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
            List<Entity> allEntities
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

            // Create particle dynamics based on type
            var particleDynamics = new ParticleDynamics(allEntities);
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
            if (!Material.IsFluid) return;
            
            _smoothingRadius = Material.PressureRadius * 2f;
            _stiffness = Material.SPHGasConstant;
            _viscosity = Material.SPHViscosity;
            _particleRadius = Material.ParticleRadius;
            _gravity = new Vector2(0f, Material.GravityStrength);
            _damping = 1f - (Material.VelocityDamping * 0.25f);  // Reduce damping effect (0.08 -> 0.02 -> 0.98 multiplier)
            
            _fluidParticles.Clear();
            _fluidMap.Clear();
            
            foreach (var particle in Particles)
            {
                var fp = new FluidParticle { Entity = particle };
                _fluidParticles.Add(fp);
                _fluidMap[particle] = fp;
                
                var pd = particle.GetComponent<ParticleDynamics>();
                if (pd != null) pd.UseSPHSolver = true;
                
                var oc = particle.GetComponent<ObjectCollision>();
                if (oc != null) oc.UseSPHIntegration = true;
            }
        }

        public void StepFluid(float dt, SpatialGrid grid)
        {
            if (!Material.IsFluid || _fluidParticles.Count == 0) return;

            float h = _smoothingRadius;
            float h2 = h * h;

            foreach (var p in _fluidParticles)
            {
                p.Neighbors.Clear();
                foreach (var other in grid.GetNearbyEntities(p.Entity.Position))
                {
                    if (other == p.Entity) continue;
                    if (!_fluidMap.TryGetValue(other, out var n)) continue;
                    if (Vector2.DistanceSquared(p.Entity.Position, n.Entity.Position) < h2)
                        p.Neighbors.Add(n);
                }
            }

            foreach (var p in _fluidParticles)
            {
                var c = p.Entity.GetComponent<ObjectCollision>();
                if (c == null || c.IsStatic) continue;

                // Apply gravity as acceleration
                c.Velocity += _gravity * dt;

                // Compute pressure/separation forces
                Vector2 pressureForce = Vector2.Zero;
                Vector2 viscosityForce = Vector2.Zero;
                int count = 0;

                foreach (var n in p.Neighbors)
                {
                    Vector2 diff = p.Entity.Position - n.Entity.Position;
                    float dist = diff.Length();
                    
                    if (dist < 0.0001f)
                    {
                        diff = new Vector2((float)(Random.Shared.NextDouble() - 0.5) * 0.0001f,
                                           (float)(Random.Shared.NextDouble() - 0.5) * 0.0001f);
                        dist = 0.0001f;
                    }
                    
                    Vector2 dir = diff / dist;
                    
                    float minDist = _particleRadius * 2f;
                    if (dist < minDist)
                    {
                        float t = 1f - (dist / minDist);
                        pressureForce += dir * t * _stiffness * 0.1f;
                    }
                    else if (dist < h)
                    {
                        float t = 1f - ((dist - minDist) / (h - minDist));
                        pressureForce += dir * t * t * _stiffness * 0.01f;
                    }

                    // Viscosity: average weighted by kernel
                    var nc = n.Entity.GetComponent<ObjectCollision>();
                    if (nc != null)
                    {
                        float kernel = 1f - (dist / h);
                        viscosityForce += (nc.Velocity - c.Velocity) * kernel;
                        count++;
                    }
                }

                // Apply pressure forces as acceleration (F = ma, so a = F/m)
                float mass = p.Entity.Mass > 0f ? p.Entity.Mass : 1f;
                c.Velocity += (pressureForce / mass) * dt;

                // Apply viscosity (weighted average with current velocity)
                if (count > 0)
                {
                    viscosityForce /= count;
                    // Reduce viscosity scaling for more responsive movement
                    c.Velocity += viscosityForce * _viscosity * 0.05f * dt;
                }

                // Apply damping
                c.Velocity *= _damping;

                // Clamp to max velocity
                float speed = c.Velocity.Length();
                if (speed > _maxVelocity)
                    c.Velocity *= _maxVelocity / speed;

                // Integrate position (since UseSPHIntegration=true skips ObjectCollision.Update)
                p.Entity.Position += c.Velocity * dt;
            }
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
