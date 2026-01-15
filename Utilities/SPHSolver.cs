using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;

namespace Engine13.Utilities
{
    public enum SPHMaterialType
    {
        Fluid,
        Granular,
    }

    internal static class SPHKernels
    {
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

        public static Vector2 SpikyGradient(float r, float h, Vector2 dir)
        {
            if (h <= 0f)
                return Vector2.Zero;
            if (r <= 0f || r >= h)
                return Vector2.Zero;
            if (dir.LengthSquared() <= 1e-12f)
                return Vector2.Zero;
            float x = h - r;
            float coeff = -10f / (MathF.PI * MathF.Pow(h, 5));
            return coeff * x * x * dir;
        }

        public static float ViscosityLaplacian(float r, float h)
        {
            if (h <= 0f)
                return 0f;
            if (r < 0f)
                r = 0f;
            if (r >= h)
                return 0f;
            float coeff = 10f / (MathF.PI * MathF.Pow(h, 5));
            return coeff * (h - r);
        }
    }

    public class SPH
    {
        private readonly List<FluidParticle> _particles = new();
        private readonly Dictionary<Entity, FluidParticle> _particleMap = new();

        private SPHMaterialType _materialType;
        private float _smoothingRadius;
        private float _gasConstant;
        private float _viscosity;
        private float _restDensity;
        private float _particleRadius;
        private float _damping;
        private Vector2 _gravity;
        private float _maxVelocity;

        private float _frictionAngle;
        private float _cohesion;
        private float _dilatancy;

        private float[] _densities = Array.Empty<float>();
        private float[] _pressures = Array.Empty<float>();
        private Vector2[] _forces = Array.Empty<Vector2>();
        private readonly List<Entity> _neighborBuffer = new();
        private readonly Dictionary<FluidParticle, int> _particleIndexMap = new();

        public float AvgDensity { get; private set; }
        public float MaxDensity { get; private set; }
        public float AvgPressure { get; private set; }
        public float AvgViscosityForce { get; private set; }
        public float AvgNeighbors { get; private set; }
        public int CollisionCount { get; private set; }
        public float AvgVelocity { get; private set; }
        public float MaxVelocity { get; private set; }

        public int ParticleCount => _particles.Count;

        public SPH()
        {
            _maxVelocity = 3.0f;
            _damping = 0.99f;
        }

        public void Configure(
            SPHMaterialType materialType,
            float smoothingRadius,
            float gasConstant,
            float viscosity,
            float restDensity,
            float particleRadius,
            Vector2 gravity,
            float damping = 0.995f,
            float maxVelocity = 2f,
            float frictionAngle = 30f,
            float cohesion = 0f,
            float dilatancy = 0f
        )
        {
            _materialType = materialType;
            _smoothingRadius = smoothingRadius;
            _gasConstant = gasConstant;
            _viscosity = viscosity;
            _restDensity = restDensity;
            _particleRadius = particleRadius;
            _gravity = gravity;
            _damping = damping;
            _maxVelocity = maxVelocity;
            _frictionAngle = frictionAngle;
            _cohesion = cohesion;
            _dilatancy = dilatancy;
        }

        public void AddParticle(Entity entity)
        {
            if (_particleMap.ContainsKey(entity))
                return;

            var fluidParticle = new FluidParticle { Entity = entity };
            _particles.Add(fluidParticle);
            _particleMap[entity] = fluidParticle;

            var pd = entity.GetComponent<ParticleDynamics>();
            if (pd != null)
                pd.UseSPHSolver = true;

            var oc = entity.GetComponent<ObjectCollision>();
            if (oc != null)
                oc.UseSPHIntegration = true;
        }

        public void Clear()
        {
            _particles.Clear();
            _particleMap.Clear();
        }

        public void Step(float dt, SpatialGrid grid)
        {
            int n = _particles.Count;
            if (n == 0)
                return;

            if (_densities.Length < n)
            {
                _densities = new float[n];
                _pressures = new float[n];
                _forces = new Vector2[n];
            }

            FindNeighbors(grid);
            ComputeDensities();
            ComputePressures();
            ComputeForces();
            AddForcesToSystem();
            ComputeDebugStats();
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

                _densities[i] = mass * SPHKernels.Poly6(0f, h);

                if (particle.Neighbors.Count == 0)
                {
                    _densities[i] = MathF.Max(_densities[i], 1e-6f);
                    continue;
                }

                Vector2 pos = particle.Entity.Position;

                for (int j = 0; j < particle.Neighbors.Count; j++)
                {
                    var neighbor = particle.Neighbors[j];
                    float neighborMass = PhysicsMath.SafeMass(neighbor.Entity.Mass);

                    float dx = pos.X - neighbor.Entity.Position.X;
                    float dy = pos.Y - neighbor.Entity.Position.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    _densities[i] += neighborMass * SPHKernels.Poly6(dist, h);
                }

                _densities[i] = MathF.Max(_densities[i], 1e-6f);
            }
        }

        private void ComputePressures()
        {
            // Tait equation of state: pressure based on density deviation from rest
            for (int i = 0; i < _particles.Count; i++)
            {
                float density = _densities[i];
                // Only create pressure when density exceeds rest (compression)
                // This prevents clumping by not creating negative/attractive pressure
                float densityRatio = density / MathF.Max(_restDensity, 1e-6f);
                float pressure = _gasConstant * MathF.Max(densityRatio - 1f, 0f);
                _pressures[i] = pressure;
            }
        }

        private void ComputeForces()
        {
            if (_materialType == SPHMaterialType.Granular)
                ComputeGranularForces();
            else
                ComputeFluidForces();
        }

        private void ComputeFluidForces()
        {
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

                Vector2 pressureForce = Vector2.Zero;
                Vector2 viscosityForce = Vector2.Zero;
                Vector2 pos = particle.Entity.Position;
                Vector2 vel = oc.Velocity;
                float mass = PhysicsMath.SafeMass(particle.Entity.Mass);
                float density = MathF.Max(_densities[i], 1e-6f);
                float pressure = _pressures[i];

                for (int nIdx = 0; nIdx < particle.Neighbors.Count; nIdx++)
                {
                    var neighbor = particle.Neighbors[nIdx];

                    if (!_particleIndexMap.TryGetValue(neighbor, out int j))
                        continue;

                    var neighborOc = neighbor.Entity.GetComponent<ObjectCollision>();
                    if (neighborOc == null)
                        continue;

                    Vector2 diff = pos - neighbor.Entity.Position;
                    float distSq = diff.LengthSquared();

                    // Skip near-coincident particles to avoid numerical instability
                    float minDist = _particleRadius * 0.1f;
                    if (distSq <= minDist * minDist)
                        continue;

                    float dist = MathF.Sqrt(distSq);

                    if (dist >= _smoothingRadius)
                        continue;

                    Vector2 dir = diff / dist;

                    float neighborDensity = MathF.Max(_densities[j], 1e-6f);
                    float neighborPressure = _pressures[j];
                    float massJ = PhysicsMath.SafeMass(neighbor.Entity.Mass);

                    // Pressure force using spiky kernel gradient (standard SPH)
                    // This creates repulsion that spreads particles apart
                    Vector2 gradW = SPHKernels.SpikyGradient(dist, _smoothingRadius, dir);
                    float pressureAccel = (pressure / (density * density))
                        + (neighborPressure / (neighborDensity * neighborDensity));
                    pressureForce += -mass * massJ * pressureAccel * gradW;

                    // Viscosity force to smooth velocity differences and reduce jitter
                    Vector2 velDiff = neighborOc.Velocity - vel;
                    float laplacian = SPHKernels.ViscosityLaplacian(dist, _smoothingRadius);
                    viscosityForce += _viscosity * massJ * (velDiff / neighborDensity) * laplacian;
                }

                // Combine forces with gentle velocity damping to prevent energy gain
                Vector2 totalForce = pressureForce + viscosityForce;
                
                // Apply damping proportional to velocity to dissipate energy
                float dampFactor = 1f - _damping; // damping=0.99 -> dampFactor=0.01
                totalForce -= vel * mass * dampFactor * 2f;

                _forces[i] = totalForce;
            }
        }

        private void ComputeGranularForces()
        {
            float targetSeparation = _particleRadius * 2f;
            float tanFriction = MathF.Tan(_frictionAngle * MathF.PI / 180f);

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

                Vector2 totalForce = Vector2.Zero;
                Vector2 pos = particle.Entity.Position;
                Vector2 vel = oc.Velocity;

                for (int nIdx = 0; nIdx < particle.Neighbors.Count; nIdx++)
                {
                    var neighbor = particle.Neighbors[nIdx];

                    if (!_particleIndexMap.TryGetValue(neighbor, out int j))
                        continue;

                    var neighborOc = neighbor.Entity.GetComponent<ObjectCollision>();
                    if (neighborOc == null)
                        continue;

                    Vector2 diff = pos - neighbor.Entity.Position;
                    float distSq = diff.LengthSquared();

                    if (distSq <= 1e-8f)
                        continue;

                    float dist = MathF.Sqrt(distSq);

                    if (dist >= targetSeparation)
                        continue;

                    Vector2 dir = diff / dist;
                    float overlap = targetSeparation - dist;

                    float normalForce = _gasConstant * overlap;

                    Vector2 relVel = vel - neighborOc.Velocity;

                    float relVelNormal = Vector2.Dot(relVel, dir);
                    Vector2 relVelTangent = relVel - dir * relVelNormal;
                    float tangentSpeed = relVelTangent.Length();

                    Vector2 pressureForce = dir * normalForce;

                    Vector2 frictionForce = Vector2.Zero;
                    if (tangentSpeed > 1e-6f)
                    {
                        Vector2 tangentDir = relVelTangent / tangentSpeed;
                        float frictionMag = tanFriction * normalForce;
                        frictionForce = -tangentDir * frictionMag;
                    }

                    Vector2 cohesionForce = Vector2.Zero;
                    if (_cohesion > 0f)
                    {
                        float cohesionRange = targetSeparation * 1.2f;
                        if (dist < cohesionRange)
                        {
                            float cohesionStrength = _cohesion * (1f - dist / cohesionRange);
                            cohesionForce = -dir * cohesionStrength;
                        }
                    }

                    Vector2 viscousForce = -_viscosity * relVel;

                    totalForce += pressureForce + frictionForce + cohesionForce + viscousForce;
                }

                _forces[i] = totalForce;
            }
        }

        private void AddForcesToSystem()
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                var oc = particle.Entity.GetComponent<ObjectCollision>();

                if (oc == null || oc.IsStatic)
                    continue;

                float mass = PhysicsMath.SafeMass(particle.Entity.Mass);
                
                var bounds = Engine13.Utilities.WindowBounds.GetNormalizedBounds();
                float floorY = bounds.bottom;
                float distToFloor = floorY - particle.Entity.Position.Y;
                bool nearFloor = distToFloor <= _particleRadius * 2f;

                Vector2 sphForce = _forces[i];
                
                // Near floor: prevent upward SPH forces that would cause bouncing
                if (nearFloor && sphForce.Y > 0f)
                    sphForce.Y = 0f;

                Vector2 gravityForce = _gravity * mass;
                
                Vector2 totalForce = sphForce + gravityForce;
                
                // Clamp total force to prevent explosions
                // Use gravity as reference: max acceleration = 10x gravity
                float gravMag = MathF.Abs(_gravity.Y);
                float maxAccel = (_materialType == SPHMaterialType.Granular ? 15f : 10f) * gravMag;
                float maxForce = maxAccel * mass;
                
                float forceMag = totalForce.Length();
                if (forceMag > maxForce && forceMag > 1e-8f)
                    totalForce *= maxForce / forceMag;
                
                // Extra safety: cap upward force to prevent floating
                // Upward force should never exceed 1.5x weight
                float maxUpwardForce = 1.5f * gravMag * mass;
                if (totalForce.Y < -maxUpwardForce)
                    totalForce.Y = -maxUpwardForce;

                Forces.AddForce(particle.Entity, new Vec2(totalForce.X, totalForce.Y));
            }
        }

        private void ComputeDebugStats()
        {
            if (_particles.Count == 0)
            {
                AvgDensity = MaxDensity = AvgPressure = AvgViscosityForce = AvgNeighbors = 0f;
                AvgVelocity = MaxVelocity = 0f;
                return;
            }

            float sumDensity = 0f,
                maxDens = 0f;
            float sumPressure = 0f;
            float sumViscosity = 0f;
            float sumNeighbors = 0f;
            float sumVel = 0f,
                maxVel = 0f;

            for (int i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                sumDensity += _densities[i];
                maxDens = MathF.Max(maxDens, _densities[i]);
                sumPressure += _pressures[i];
                sumViscosity += _forces[i].Length();
                sumNeighbors += particle.Neighbors.Count;

                var oc = particle.Entity.GetComponent<ObjectCollision>();
                if (oc != null)
                {
                    float vel = oc.Velocity.Length();
                    sumVel += vel;
                    maxVel = MathF.Max(maxVel, vel);
                }
            }

            int count = _particles.Count;
            AvgDensity = sumDensity / count;
            MaxDensity = maxDens;
            AvgPressure = sumPressure / count;
            AvgViscosityForce = sumViscosity / count;
            AvgNeighbors = sumNeighbors / count;
            AvgVelocity = sumVel / count;
            MaxVelocity = maxVel;
        }

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

    public class FluidParticle
    {
        public Entity Entity { get; set; } = null!;
        public List<FluidParticle> Neighbors { get; } = new();
    }
}
