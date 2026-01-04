using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;

namespace Engine13.Utilities
{
    /// <summary>
    /// SPH kernel functions for smoothed particle hydrodynamics.
    /// </summary>
    internal static class SPHKernels
    {
        // Poly6 kernel (2D) - normalized coefficient depends on h
        public static float Poly6(float r, float h)
        {
            if (h <= 0f)
                return 0f;
            if (r < 0f)
                r = 0f;
            if (r >= h)
                return 0f;
            float hr2 = h * h - r * r;
            float coeff = 4f / (MathF.PI * MathF.Pow(h, 8));
            return coeff * hr2 * hr2 * hr2;
        }

        // Spiky gradient kernel (2D) - returns gradient vector
        public static Vector2 SpikyGradient(float r, float h, Vector2 dir)
        {
            if (h <= 0f)
                return Vector2.Zero;
            if (r <= 0f || r >= h)
                return Vector2.Zero;
            if (dir.LengthSquared() <= 1e-12f)
                return Vector2.Zero;
            float x = h - r;
            float coeff = -30f / (MathF.PI * MathF.Pow(h, 5));
            return coeff * x * x * dir;
        }

        // Viscosity laplacian kernel (2D)
        public static float ViscosityLaplacian(float r, float h)
        {
            if (h <= 0f)
                return 0f;
            if (r < 0f)
                r = 0f;
            if (r >= h)
                return 0f;
            float coeff = 40f / (MathF.PI * MathF.Pow(h, 5));
            return coeff * (h - r);
        }
    }

    /// <summary>
    /// Smoothed Particle Hydrodynamics (SPH) fluid solver.
    /// Handles all fluid physics: density calculation, pressure forces, viscosity, and integration.
    /// </summary>
    public class SPH
    {
        private readonly List<FluidParticle> _particles = new();
        private readonly Dictionary<Entity, FluidParticle> _particleMap = new();

        private float _smoothingRadius;
        private float _gasConstant;
        private float _viscosity;
        private float _restDensity;
        private float _particleRadius;
        private float _damping;
        private Vector2 _gravity;
        private float _maxVelocity;

        private float[] _densities = Array.Empty<float>();
        private float[] _pressures = Array.Empty<float>();
        private Vector2[] _forces = Array.Empty<Vector2>();
        private readonly List<Entity> _neighborBuffer = new();
        private readonly Dictionary<FluidParticle, int> _particleIndexMap = new();

        public int ParticleCount => _particles.Count;

        public SPH()
        {
            _maxVelocity = 2f;
            _damping = 0.995f; // Less damping to allow flow
        }

        /// <summary>
        /// Configure SPH parameters for the fluid simulation.
        /// </summary>
        public void Configure(
            float smoothingRadius,
            float gasConstant,
            float viscosity,
            float restDensity,
            float particleRadius,
            Vector2 gravity,
            float damping = 0.995f,
            float maxVelocity = 2f
        )
        {
            _smoothingRadius = smoothingRadius;
            _gasConstant = gasConstant;
            _viscosity = viscosity;
            _restDensity = restDensity;
            _particleRadius = particleRadius;
            _gravity = gravity;
            _damping = damping;
            _maxVelocity = maxVelocity;
        }

        /// <summary>
        /// Register an entity as a fluid particle.
        /// </summary>
        public void AddParticle(Entity entity)
        {
            if (_particleMap.ContainsKey(entity))
                return;

            var fluidParticle = new FluidParticle { Entity = entity };
            _particles.Add(fluidParticle);
            _particleMap[entity] = fluidParticle;

            // Mark components to use SPH
            var pd = entity.GetComponent<ParticleDynamics>();
            if (pd != null)
                pd.UseSPHSolver = true;

            var oc = entity.GetComponent<ObjectCollision>();
            if (oc != null)
                oc.UseSPHIntegration = true;
        }

        /// <summary>
        /// Clear all particles from the solver.
        /// </summary>
        public void Clear()
        {
            _particles.Clear();
            _particleMap.Clear();
        }

        /// <summary>
        /// Main simulation step - updates all fluid particle positions and velocities.
        /// </summary>
        public void Step(float dt, SpatialGrid grid)
        {
            int n = _particles.Count;
            if (n == 0)
                return;

            // Resize arrays if needed
            if (_densities.Length < n)
            {
                _densities = new float[n];
                _pressures = new float[n];
                _forces = new Vector2[n];
            }

            // Step 1: Find neighbors
            FindNeighbors(grid);

            // Step 2: Compute densities
            ComputeDensities();

            // Step 3: Compute pressures
            ComputePressures();

            // Step 4: Compute forces
            ComputeForces();

            // Step 5: Integrate (update velocities and positions)
            Integrate(dt);
        }

        private void FindNeighbors(SpatialGrid grid)
        {
            float h2 = _smoothingRadius * _smoothingRadius;

            for (int i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                particle.Neighbors.Clear();

                _neighborBuffer.Clear();
                grid.GetNearbyEntities(particle.Entity.Position, _neighborBuffer);

                Vector2 pos = particle.Entity.Position;

                for (int j = 0; j < _neighborBuffer.Count; j++)
                {
                    var other = _neighborBuffer[j];
                    if (other == particle.Entity)
                        continue;

                    if (!_particleMap.TryGetValue(other, out var otherParticle))
                        continue;

                    float dx = pos.X - other.Position.X;
                    float dy = pos.Y - other.Position.Y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq < h2)
                        particle.Neighbors.Add(otherParticle);
                }
            }
        }

        private void ComputeDensities()
        {
            float h = _smoothingRadius;

            for (int i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                float mass = PhysicsMath.SafeMass(particle.Entity.Mass);

                // Self-contribution
                _densities[i] = mass * SPHKernels.Poly6(0f, h);

                if (particle.Neighbors.Count == 0)
                {
                    _densities[i] = MathF.Max(_densities[i], 1e-6f);
                    continue;
                }

                Vector2 pos = particle.Entity.Position;

                // Neighbor contributions
                for (int j = 0; j < particle.Neighbors.Count; j++)
                {
                    var neighbor = particle.Neighbors[j];
                    float neighborMass = PhysicsMath.SafeMass(neighbor.Entity.Mass);

                    float dx = pos.X - neighbor.Entity.Position.X;
                    float dy = pos.Y - neighbor.Entity.Position.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    _densities[i] += neighborMass * SPHKernels.Poly6(dist, h);
                }

                // Ensure minimum density
                _densities[i] = MathF.Max(_densities[i], 1e-6f);
            }
        }

        private void ComputePressures()
        {
            // Use Tait equation: p = B * ((ρ/ρ_0)^γ - 1)
            // For weak compressibility: simplified to p = k * (ρ - ρ_0)
            for (int i = 0; i < _particles.Count; i++)
            {
                float rho = _densities[i];
                float densityError = rho - _restDensity;

                // Linear stiffness for stability (Becker & Teschner 2007)
                _pressures[i] = _gasConstant * MathF.Max(0f, densityError);
            }
        }

        private void ComputeForces()
        {
            float h = _smoothingRadius;
            float h2 = h * h;

            // Build index map once
            _particleIndexMap.Clear();
            for (int i = 0; i < _particles.Count; i++)
                _particleIndexMap[_particles[i]] = i;

            for (int i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                var oc = particle.Entity.GetComponent<ObjectCollision>();

                if (oc == null || oc.IsStatic)
                {
                    _forces[i] = Vector2.Zero;
                    continue;
                }

                if (particle.Neighbors.Count == 0)
                {
                    _forces[i] = Vector2.Zero;
                    continue;
                }

                Vector2 pressureForce = Vector2.Zero;
                Vector2 viscosityForce = Vector2.Zero;
                Vector2 pos = particle.Entity.Position;
                float rho_i = _densities[i];
                float p_i = _pressures[i];
                float mass_i = PhysicsMath.SafeMass(particle.Entity.Mass);

                for (int nIdx = 0; nIdx < particle.Neighbors.Count; nIdx++)
                {
                    var neighbor = particle.Neighbors[nIdx];

                    if (!_particleIndexMap.TryGetValue(neighbor, out int j))
                        continue;

                    float dx = pos.X - neighbor.Entity.Position.X;
                    float dy = pos.Y - neighbor.Entity.Position.Y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= 1e-6f || distSq >= h2)
                        continue;

                    float dist = MathF.Sqrt(distSq);
                    Vector2 dir = new Vector2(dx / dist, dy / dist);

                    float rho_j = _densities[j];
                    float p_j = _pressures[j];
                    float mass_j = PhysicsMath.SafeMass(neighbor.Entity.Mass);

                    // Strong pressure-based repulsion
                    if (p_i > 0f || p_j > 0f)
                    {
                        float avgPressure = (p_i + p_j) * 0.5f;
                        float q = 1f - (dist / h);
                        // Direct force proportional to pressure and inverse distance
                        pressureForce += dir * (avgPressure * q * 50f);
                    }
                    
                    // Strong close-range repulsion to prevent overlap
                    float minDist = _particleRadius * 2.0f;
                    if (dist < minDist)
                    {
                        float penetration = minDist - dist;
                        float repulsionForce = (penetration / minDist) * 100f;
                        pressureForce += dir * repulsionForce;
                    }

                    // Viscosity damping
                    var oc_j = neighbor.Entity.GetComponent<ObjectCollision>();
                    if (oc_j != null)
                    {
                        Vector2 velDiff = oc_j.Velocity - oc.Velocity;
                        float q = 1f - (dist / h);
                        viscosityForce += velDiff * (q * _viscosity * 0.5f);
                    }
                }

                _forces[i] = pressureForce + viscosityForce;
            }
        }

        private void Integrate(float dt)
        {
            // Realistic limits for water simulation
            const float maxAccel = 500f; // Higher for strong forces
            
            for (int i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                var oc = particle.Entity.GetComponent<ObjectCollision>();
                
                if (oc == null || oc.IsStatic)
                    continue;
                
                // Use forces directly (already scaled appropriately)
                Vector2 accel = _forces[i] + _gravity;
                
                // Clamp acceleration to prevent instability
                float accelMag = accel.Length();
                if (accelMag > maxAccel)
                    accel *= maxAccel / accelMag;
                
                // Update velocity
                oc.Velocity += accel * dt;
                oc.Velocity *= _damping;
                
                // Limit velocity
                float maxVel = _maxVelocity * 3f;
                oc.Velocity = PhysicsMath.ClampMagnitude(oc.Velocity, maxVel);
                
                // Update position
                particle.Entity.Position += oc.Velocity * dt;
            }
        }        /// <summary>
        /// Get debug information for a specific particle.
        /// </summary>
        public bool TryGetDebugInfo(
            Entity entity,
            out float density,
            out float pressure,
            out int neighborCount
        )
        {
            density = 0f;
            pressure = 0f;
            neighborCount = 0;

            if (!_particleMap.TryGetValue(entity, out var particle))
                return false;

            int index = _particles.IndexOf(particle);
            if (index < 0 || index >= _densities.Length)
                return false;

            density = _densities[index];
            pressure = _pressures[index];
            neighborCount = particle.Neighbors.Count;
            return true;
        }

        /// <summary>
        /// Get all particle debug data.
        /// </summary>
        public void GetAllDebugData(
            Dictionary<Entity, int> entityToIndex,
            Span<float> densitiesOut,
            Span<float> pressuresOut,
            Span<int> neighborCountsOut
        )
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                entityToIndex[particle.Entity] = i;

                if (i < densitiesOut.Length)
                    densitiesOut[i] = _densities[i];
                if (i < pressuresOut.Length)
                    pressuresOut[i] = _pressures[i];
                if (i < neighborCountsOut.Length)
                    neighborCountsOut[i] = particle.Neighbors.Count;
            }
        }

        /// <summary>
        /// Try to get entity at specific index.
        /// </summary>
        public bool TryGetEntityAt(int index, out Entity entity)
        {
            if (index >= 0 && index < _particles.Count)
            {
                entity = _particles[index].Entity;
                return true;
            }

            entity = null!;
            return false;
        }
    }

    /// <summary>
    /// Represents a single fluid particle with its neighbors.
    /// </summary>
    public class FluidParticle
    {
        public Entity Entity { get; set; } = null!;
        public List<FluidParticle> Neighbors { get; } = new();
    }
}
