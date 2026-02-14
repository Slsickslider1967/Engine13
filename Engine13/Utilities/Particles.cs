using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.JsonReader;
using Veldrid;

namespace Engine13.Utilities.Attributes
{
    public interface IEntityComponent
    {
        void Update(Entity entity, GameTime gameTime);
    }

    public sealed class ParticleDynamics : IEntityComponent
    {
        public const float DefaultPressureRadius = 0.025f;
        readonly List<Entity>? _allEntities;
        readonly SpatialGrid? _grid;
        readonly List<Entity> _neighborBuffer = new();

        public float MaxForceMagnitude { get; set; } = 25f;
        public float VelocityDamping { get; set; } = 0.05f;
        public float PressureStrength { get; set; } = 2f;
        public float PressureRadius { get; set; } = DefaultPressureRadius;
        public bool UseSPHSolver { get; set; }

        public ParticleDynamics(List<Entity> allEntities, SpatialGrid? grid = null)
        {
            _allEntities = allEntities;
            _grid = grid;
        }

        public void Update(Entity entity, GameTime gameTime)
        {
            if (UseSPHSolver)
                return;
            Vector2 force =
                _allEntities != null ? ComputeInterParticleForces(entity) : Vector2.Zero;
            if (VelocityDamping > 0f)
            {
                var oc = entity.GetComponent<ObjectCollision>();
                force -=
                    VelocityDamping
                    * (entity.Mass > 0f ? entity.Mass : 1f)
                    * (oc?.Velocity ?? entity.Velocity);
            }
            if (force == Vector2.Zero)
                return;
            if (MaxForceMagnitude > 0f && force.Length() > MaxForceMagnitude)
                force = Vector2.Normalize(force) * MaxForceMagnitude;
            Forces.AddForce(entity, new Vec2(force.X, force.Y));
        }

        public static void RemoveParticlesInArea(
            List<Entity> entities,
            float minX,
            float minY,
            float maxX,
            float maxY
        ) =>
            entities.RemoveAll(e =>
                e.Position.X >= minX
                && e.Position.X <= maxX
                && e.Position.Y >= minY
                && e.Position.Y <= maxY
            );

        public static void RemoveAllParticles(List<Entity> entities) => entities.Clear();

        Vector2 ComputeInterParticleForces(Entity entity)
        {
            if (_grid == null || (entity.GetComponent<ObjectCollision>()?.IsFluid ?? false))
                return Vector2.Zero;
            Vector2 force = Vector2.Zero;
            float hSq = PressureRadius * PressureRadius;
            _grid.GetNearbyEntities(entity.Position, _neighborBuffer);
            foreach (var other in _neighborBuffer)
            {
                if (other == entity)
                    continue;
                Vector2 delta = entity.Position - other.Position;
                float distSq = delta.LengthSquared();
                if (distSq >= hSq || distSq <= 1e-10f)
                    continue;
                float dist = MathF.Sqrt(distSq);
                float target = (entity.CollisionRadius + other.CollisionRadius) * 1.2f;
                if (dist < target)
                    force += (delta / dist) * PressureStrength * (target / dist) * (target / dist);
            }
            return force;
        }

        public static float CalculateKineticEnergy(List<Entity> entities) =>
            entities.Sum(e =>
                0.5f
                * (e.Mass > 0f ? e.Mass : 1f)
                * (e.GetComponent<ObjectCollision>()?.Velocity ?? e.Velocity).LengthSquared()
            );

        public static float CalculatePotentialEnergy(List<Entity> entities)
        {
            var bounds = WindowBounds.GetNormalizedBounds();
            return entities.Sum(e =>
                (e.Mass > 0f ? e.Mass : 1f)
                * MathF.Abs(e.GetComponent<Gravity>()?.AccelerationY ?? 0f)
                * (bounds.bottom - e.Position.Y)
            );
        }

        public static float CalculateTotalEnergy(List<Entity> entities) =>
            CalculateKineticEnergy(entities) + CalculatePotentialEnergy(entities);

        public static (float avgSpeed, float maxSpeed, float minSpeed) GetVelocityStats(
            List<Entity> entities
        )
        {
            if (entities.Count == 0)
                return (0f, 0f, 0f);
            float total = 0f,
                max = float.MinValue,
                min = float.MaxValue;
            foreach (var e in entities)
            {
                float spd = (e.GetComponent<ObjectCollision>()?.Velocity ?? e.Velocity).Length();
                total += spd;
                max = MathF.Max(max, spd);
                min = MathF.Min(min, spd);
            }
            return (total / entities.Count, max, min);
        }

        public static (Vector2 avgPos, float minY, float maxY) GetPositionStats(
            List<Entity> entities
        )
        {
            if (entities.Count == 0)
                return (Vector2.Zero, 0f, 0f);
            Vector2 total = Vector2.Zero;
            float minY = float.MaxValue,
                maxY = float.MinValue;
            foreach (var e in entities)
            {
                total += e.Position;
                minY = MathF.Min(minY, e.Position.Y);
                maxY = MathF.Max(maxY, e.Position.Y);
            }
            return (total / entities.Count, minY, maxY);
        }

        public static int GetGroundedCount(List<Entity> entities) =>
            entities.Count(e => e.GetComponent<ObjectCollision>()?.IsGrounded == true);
    }

    public sealed class EdgeCollision(bool loop) : IEntityComponent
    {
        static (float l, float r, float t, float b) GetBounds() =>
            WindowBounds.GetNormalizedBounds();

        void HandleWallCollision(ObjectCollision oc, bool isX, float sleepVel, Entity? e)
        {
            if (oc == null)
                return;
            float rest =
                PhysicsSettings.WallRestitution * oc.Restitution * (oc.IsFluid ? 0.5f : 1f);
            var bounds = GetBounds();
            if (isX)
            {
                float vX = oc.Velocity.X;
                if (MathF.Abs(vX) < sleepVel)
                    vX = 0f;
                else if (
                    e != null
                    && (
                        (e.Position.X < (bounds.l + bounds.r) * 0.5f && vX < 0)
                        || (e.Position.X >= (bounds.l + bounds.r) * 0.5f && vX > 0)
                    )
                )
                    vX = -vX * rest;
                oc.Velocity = new Vector2(vX, oc.Velocity.Y);
            }
            else
            {
                float vY = oc.Velocity.Y;
                if (MathF.Abs(vY) < sleepVel)
                    vY = 0f;
                else if (
                    e != null
                    && (
                        (e.Position.Y < (bounds.t + bounds.b) * 0.5f && vY < 0)
                        || (e.Position.Y >= (bounds.t + bounds.b) * 0.5f && vY > 0)
                    )
                )
                    vY = -vY * rest;
                oc.Velocity = new Vector2(oc.Velocity.X, vY);
            }
        }

        public void Update(Entity entity, GameTime gameTime)
        {
            var (left, right, top, bottom) = GetBounds();
            var pos = entity.Position;
            float hw =
                entity.CollisionShape == Entity.CollisionShapeType.Circle
                && entity.CollisionRadius > 0f
                    ? entity.CollisionRadius
                    : entity.Size.X * 0.5f;
            float hh =
                entity.CollisionShape == Entity.CollisionShapeType.Circle
                && entity.CollisionRadius > 0f
                    ? entity.CollisionRadius
                    : entity.Size.Y * 0.5f;

            if (loop)
            {
                pos.X = Math.Clamp(pos.X, left + hw, right - hw);
                pos.Y = Math.Clamp(pos.Y, top + hh, bottom - hh);
                entity.Position = pos;
                return;
            }

            var oc = entity.GetComponent<ObjectCollision>();
            const float sleepVel = 0.05f;
            float recovery = MathF.Max(hw, hh) * (oc?.IsFluid == true ? 0.15f : 0.08f);

            if (pos.X < left + hw)
            {
                pos.X = left + hw + recovery;
                entity.Position = pos;
                if (oc != null)
                {
                    HandleWallCollision(oc, true, sleepVel, entity);
                    if (oc.IsFluid)
                        oc.Velocity *= 0.8f;
                }
            }
            else if (pos.X > right - hw)
            {
                pos.X = right - hw - recovery;
                entity.Position = pos;
                if (oc != null)
                {
                    HandleWallCollision(oc, true, sleepVel, entity);
                    if (oc.IsFluid)
                        oc.Velocity *= 0.8f;
                }
            }

            if (pos.Y < top + hh)
            {
                pos.Y = top + hh + recovery;
                entity.Position = pos;
                if (oc != null)
                {
                    HandleWallCollision(oc, false, sleepVel, entity);
                    oc.IsGrounded = false;
                    if (oc.IsFluid)
                        oc.Velocity *= 0.8f;
                }
            }
            else if (pos.Y > bottom - hh)
            {
                pos.Y = bottom - hh - recovery;
                entity.Position = pos;
                if (oc != null)
                {
                    HandleWallCollision(oc, false, sleepVel, entity);
                    oc.IsGrounded = MathF.Abs(oc.Velocity.Y) < sleepVel;
                }
            }
            else
                entity.Position = pos;
        }
    }
}

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
            if (h <= 0f || r < 0f)
            {
                r = MathF.Max(0f, r);
            }
            if (r >= h)
                return 0f;
            float hr2 = h * h - r * r;
            return (4f / (MathF.PI * MathF.Pow(h, 8))) * hr2 * hr2 * hr2;
        }

        public static Vector2 SpikyGradient(float r, float h, Vector2 dir)
        {
            if (h <= 0f || r <= 0f || r >= h || dir.LengthSquared() <= 1e-12f)
                return Vector2.Zero;
            float x = h - r;
            return (-10f / (MathF.PI * MathF.Pow(h, 5))) * x * x * dir;
        }

        public static float ViscosityLaplacian(float r, float h)
        {
            if (h <= 0f || r >= h)
                return 0f;
            if (r < 0f)
                r = 0f;
            return (10f / (MathF.PI * MathF.Pow(h, 5))) * (h - r);
        }
    }

    public class SPH
    {
        private readonly List<FluidParticle> _particles = new();
        private readonly Dictionary<Entity, FluidParticle> _particleMap = new();
        private SPHMaterialType _materialType;
        private float _smoothingRadius,
            _gasConstant,
            _viscosity,
            _restDensity,
            _particleRadius,
            _damping,
            _maxVelocity;
        private Vector2 _gravity;
        private float _frictionAngle,
            _cohesion,
            _dilatancy;
        private float[] _densities = Array.Empty<float>(),
            _pressures = Array.Empty<float>();
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
            var fp = new FluidParticle { Entity = entity };
            _particles.Add(fp);
            _particleMap[entity] = fp;
            var pd = entity.GetComponent<Attributes.ParticleDynamics>();
            if (pd != null)
                pd.UseSPHSolver = true;
            var oc = entity.GetComponent<Attributes.ObjectCollision>();
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
                var p = _particles[i];
                p.Neighbors.Clear();
                _neighborBuffer.Clear();
                grid.GetNearbyEntities(p.Entity.Position, _neighborBuffer);
                Vector2 pos = p.Entity.Position;
                for (int j = 0; j < _neighborBuffer.Count; j++)
                {
                    var other = _neighborBuffer[j];
                    if (other == p.Entity || !_particleMap.TryGetValue(other, out var op))
                        continue;
                    float dx = pos.X - other.Position.X,
                        dy = pos.Y - other.Position.Y;
                    if (dx * dx + dy * dy < h2)
                        p.Neighbors.Add(op);
                }
            }
        }

        private void ComputeDensities()
        {
            float h = _smoothingRadius;
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                float mass = PhysicsMath.SafeMass(p.Entity.Mass);
                _densities[i] = mass * SPHKernels.Poly6(0f, h);
                if (p.Neighbors.Count == 0)
                {
                    _densities[i] = MathF.Max(_densities[i], 1e-6f);
                    continue;
                }
                Vector2 pos = p.Entity.Position;
                for (int j = 0; j < p.Neighbors.Count; j++)
                {
                    var nb = p.Neighbors[j];
                    float dx = pos.X - nb.Entity.Position.X,
                        dy = pos.Y - nb.Entity.Position.Y;
                    _densities[i] +=
                        PhysicsMath.SafeMass(nb.Entity.Mass)
                        * SPHKernels.Poly6(MathF.Sqrt(dx * dx + dy * dy), h);
                }
                _densities[i] = MathF.Max(_densities[i], 1e-6f);
            }
        }

        private void ComputePressures()
        {
            for (int i = 0; i < _particles.Count; i++)
                _pressures[i] =
                    _gasConstant
                    * MathF.Max(_densities[i] / MathF.Max(_restDensity, 1e-6f) - 1f, 0f);
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
                var oc = particle.Entity.GetComponent<Attributes.ObjectCollision>();

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

                    var neighborOc = neighbor.Entity.GetComponent<Attributes.ObjectCollision>();
                    if (neighborOc == null)
                        continue;

                    Vector2 diff = pos - neighbor.Entity.Position;
                    float distSq = diff.LengthSquared();

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

                    Vector2 gradW = SPHKernels.SpikyGradient(dist, _smoothingRadius, dir);
                    float pressureAccel =
                        (pressure / (density * density))
                        + (neighborPressure / (neighborDensity * neighborDensity));
                    pressureForce += -mass * massJ * pressureAccel * gradW;

                    float stickThreshold = _particleRadius * 0.95f;
                    if (dist < stickThreshold)
                    {
                        float penetration = stickThreshold - dist;
                        float stickForce = _gasConstant * penetration * 0.3f;
                        pressureForce += dir * massJ * stickForce;
                    }

                    Vector2 velDiff = neighborOc.Velocity - vel;
                    float laplacian = SPHKernels.ViscosityLaplacian(dist, _smoothingRadius);
                    viscosityForce += _viscosity * massJ * (velDiff / neighborDensity) * laplacian;
                }

                Vector2 totalForce = pressureForce + (viscosityForce);

                float dampFactor = (1f - _damping);
                float velMag = vel.Length();

                totalForce -= vel * mass * dampFactor * 2f;

                if (velMag > 0.5f)
                {
                    float dragCoeff = 0.15f;
                    totalForce -= vel * mass * dragCoeff * velMag;
                }

                if (vel.Y < -0.1f)
                {
                    totalForce.Y += vel.Y * mass * 0.5f;
                }

                if (velMag < 0.3f && velMag > 0.01f)
                {
                    totalForce -= vel * mass * 1.5f;
                }

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
                var oc = particle.Entity.GetComponent<Attributes.ObjectCollision>();

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

                    var neighborOc = neighbor.Entity.GetComponent<Attributes.ObjectCollision>();
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
                var oc = particle.Entity.GetComponent<Attributes.ObjectCollision>();

                if (oc == null || oc.IsStatic)
                    continue;

                float mass = PhysicsMath.SafeMass(particle.Entity.Mass);

                var bounds = WindowBounds.GetNormalizedBounds();
                float floorY = bounds.bottom;
                float distToFloor = floorY - particle.Entity.Position.Y;
                bool nearFloor = distToFloor <= _particleRadius * 2f;

                Vector2 sphForce = _forces[i];

                if (nearFloor && sphForce.Y > 0f)
                    sphForce.Y = 0f;

                if (nearFloor)
                {
                    float groundFrictionCoeff = 2.0f;
                    Vector2 lateralVel = new Vector2(oc.Velocity.X, 0f);
                    float lateralSpeed = MathF.Abs(lateralVel.X);
                    if (lateralSpeed > 0.01f)
                    {
                        Vector2 frictionForce = -lateralVel * mass * groundFrictionCoeff;
                        sphForce += frictionForce;
                    }
                }

                Vector2 gravityForce = _gravity * mass;

                float gravityConstant = PhysicsSettings.GravitationalConstant;
                float currentGravMag = _gravity.Length();
                if (currentGravMag > 0.001f)
                {
                    Vector2 gravityDir = _gravity / currentGravMag;
                    gravityForce = gravityDir * gravityConstant * mass;
                }

                if (PhysicsSettings.AirResistance > 0f)
                {
                    float dragCoeff = PhysicsSettings.AirResistance * 3f;
                    Vector2 airResistanceForce = -oc.Velocity * mass * dragCoeff;
                    sphForce += airResistanceForce;
                }

                Vector2 totalForce = sphForce + gravityForce;

                float gravMag = MathF.Abs(_gravity.Y);
                float maxAccel = (_materialType == SPHMaterialType.Granular ? 15f : 10f) * gravMag;
                float maxForce = maxAccel * mass;

                float forceMag = totalForce.Length();
                if (forceMag > maxForce && forceMag > 1e-8f)
                    totalForce *= maxForce / forceMag;

                float maxUpwardForce = 0.5f * gravMag * mass;
                if (totalForce.Y < -maxUpwardForce)
                    totalForce.Y = -maxUpwardForce;

                Forces.AddForce(particle.Entity, new Vec2(totalForce.X, totalForce.Y));
            }
        }

        private void ComputeDebugStats()
        {
            if (_particles.Count == 0)
            {
                AvgDensity =
                    MaxDensity =
                    AvgPressure =
                    AvgViscosityForce =
                    AvgNeighbors =
                    AvgVelocity =
                    MaxVelocity =
                        0f;
                return;
            }
            float sumD = 0f,
                maxD = 0f,
                sumP = 0f,
                sumV = 0f,
                sumN = 0f,
                sumVel = 0f,
                maxVel = 0f;
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                sumD += _densities[i];
                maxD = MathF.Max(maxD, _densities[i]);
                sumP += _pressures[i];
                sumV += _forces[i].Length();
                sumN += p.Neighbors.Count;
                var oc = p.Entity.GetComponent<Attributes.ObjectCollision>();
                if (oc != null)
                {
                    float v = oc.Velocity.Length();
                    sumVel += v;
                    maxVel = MathF.Max(maxVel, v);
                }
            }
            int n = _particles.Count;
            AvgDensity = sumD / n;
            MaxDensity = maxD;
            AvgPressure = sumP / n;
            AvgViscosityForce = sumV / n;
            AvgNeighbors = sumN / n;
            AvgVelocity = sumVel / n;
            MaxVelocity = maxVel;
        }

        public bool TryGetDebugInfo(
            Entity entity,
            out float density,
            out float pressure,
            out int neighborCount
        )
        {
            density = pressure = 0f;
            neighborCount = 0;
            if (!_particleMap.TryGetValue(entity, out var p))
                return false;
            int idx = _particles.IndexOf(p);
            if (idx < 0 || idx >= _densities.Length)
                return false;
            density = _densities[idx];
            pressure = _pressures[idx];
            neighborCount = p.Neighbors.Count;
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
                var p = _particles[i];
                entityToIndex[p.Entity] = i;
                if (i < densitiesOut.Length)
                    densitiesOut[i] = _densities[i];
                if (i < pressuresOut.Length)
                    pressuresOut[i] = _pressures[i];
                if (i < neighborCountsOut.Length)
                    neighborCountsOut[i] = p.Neighbors.Count;
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

namespace Engine13.Core
{
    using Engine13.Utilities;
    using Engine13.Utilities.Attributes;
    using Engine13.Utilities.JsonReader;

    public class ParticleSystem(string name, ParticlePresetReader material)
    {
        public string Name { get; } = name;
        public ParticlePresetReader Material { get; } = material;
        public List<Entity> Particles { get; } = new();
        private readonly SPH _sph = new();

        public ParticleSystem(string name, string presetName)
            : this(name, ParticlePresetReader.Load(presetName)) { }

        public void CreateParticles(
            GraphicsDevice gd,
            int count,
            Vector2 origin,
            int cols,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid
        )
        {
            float radius = Material.ParticleRadius,
                spacing = radius * 2f * 1.15f;
            Particles.Clear();
            Particles.Capacity = count;

            if (Material.Composition?.Count > 0)
                CreateCompositionParticles(
                    gd,
                    count,
                    origin,
                    cols,
                    spacing,
                    radius,
                    allEntities,
                    updateManager,
                    grid
                );
            else
                CreateUniformParticles(
                    gd,
                    count,
                    origin,
                    cols,
                    spacing,
                    radius,
                    allEntities,
                    updateManager,
                    grid
                );

            Logger.Log(
                $"[{Name}] Created {Particles.Count} particles with material '{Material.Name}'"
            );
            Logger.Log(
                $"  Material: restitution={Material.Restitution:F2}, friction={Material.Friction:F2}, mass={Material.Mass:F4}"
            );
        }

        private void CreateUniformParticles(
            GraphicsDevice gd,
            int count,
            Vector2 origin,
            int cols,
            float spacing,
            float radius,
            List<Entity> all,
            UpdateManager um,
            SpatialGrid grid
        )
        {
            for (int i = 0; i < count; i++)
                RegisterParticle(
                    CreateSingleParticle(
                        gd,
                        i,
                        origin,
                        cols,
                        spacing,
                        radius,
                        "standard",
                        all,
                        grid
                    ),
                    um,
                    grid,
                    all
                );
        }

        private void CreateCompositionParticles(
            GraphicsDevice gd,
            int count,
            Vector2 origin,
            int cols,
            float spacing,
            float radius,
            List<Entity> all,
            UpdateManager um,
            SpatialGrid grid
        )
        {
            var comp = Material.Composition!;
            int totalRatio = comp.Sum(c => Math.Max(1, c.Ratio)),
                remaining = count;
            var counts = comp.Select(
                    (c, i) =>
                    {
                        int n =
                            (i == comp.Count - 1)
                                ? remaining
                                : (int)Math.Round((double)count * c.Ratio / totalRatio);
                        remaining -= n;
                        return Math.Max(0, n);
                    }
                )
                .ToList();

            int idx = 0;
            for (int ci = 0; ci < comp.Count; ci++)
            for (int k = 0; k < counts[ci]; k++, idx++)
                RegisterParticle(
                    CreateSingleParticle(
                        gd,
                        idx,
                        origin,
                        cols,
                        spacing,
                        radius,
                        comp[ci].ParticleType?.ToLowerInvariant() ?? "standard",
                        all,
                        grid
                    ),
                    um,
                    grid,
                    all
                );
        }

        private Entity CreateSingleParticle(
            GraphicsDevice gd,
            int idx,
            Vector2 origin,
            int cols,
            float spacing,
            float radius,
            string type,
            List<Entity> all,
            SpatialGrid grid
        )
        {
            var p = CircleFactory.CreateCircle(gd, radius, 8, 8);
            p.Position = new Vector2(
                origin.X + (idx % cols) * spacing,
                origin.Y + (idx / cols) * spacing
            );
            p.CollisionRadius = radius;
            p.Mass = Material.Mass;

            p.AddComponent(
                new Gravity(Material.GravityStrength, 0f, p.Mass, Material.HorizontalForce, 0f)
            );
            p.AddComponent(
                new ObjectCollision
                {
                    Mass = p.Mass,
                    Restitution = Material.Restitution,
                    Friction = Material.Friction,
                    IsFluid = Material.IsFluid,
                }
            );
            p.AddComponent(new EdgeCollision(!Material.EnableEdgeCollision));

            var pd = new ParticleDynamics(all, grid);
            (pd.MaxForceMagnitude, pd.VelocityDamping, pd.PressureStrength) = type switch
            {
                "heavy" => (100f, 0.01f, 4f),
                "light" => (25f, 0.05f, 2f),
                "fluid" => (15f, 0.08f, 1.5f),
                _ => (50f, 0.02f, 2.5f),
            };
            pd.PressureRadius = ParticleDynamics.DefaultPressureRadius;
            Material.ApplyTo(pd);
            p.AddComponent(pd);
            return p;
        }

        private void RegisterParticle(
            Entity p,
            UpdateManager um,
            SpatialGrid grid,
            List<Entity> all
        )
        {
            Particles.Add(p);
            all.Add(p);
            um.Register(p);
            grid.AddEntity(p);
        }

        public void InitializeFluid()
        {
            if (!Material.IsFluid)
                return;
            _sph.Configure(
                SPHMaterialType.Fluid,
                Material.ParticleRadius * 4f,
                Material.SPHGasConstant * 0.5f,
                Material.SPHViscosity,
                Material.SPHRestDensity,
                Material.ParticleRadius,
                new Vector2(0f, Material.GravityStrength),
                0.99f,
                3.0f
            );
            _sph.Clear();
            var rng = new Random();
            foreach (var p in Particles)
            {
                _sph.AddParticle(p);
                var oc = p.GetComponent<ObjectCollision>();
                if (oc != null)
                    oc.Velocity = new Vector2(
                        (float)(rng.NextDouble() * 2 - 1) * 0.01f,
                        (float)(rng.NextDouble() * 2 - 1) * 0.01f
                    );
            }
        }

        public void InitializeGranular()
        {
            if (!Material.IsGranular)
                return;
            _sph.Configure(
                SPHMaterialType.Granular,
                Material.ParticleRadius * 3f,
                Material.SPHGasConstant,
                Material.SPHViscosity,
                Material.SPHRestDensity,
                Material.ParticleRadius,
                new Vector2(0f, Material.GravityStrength),
                0.95f,
                2.0f,
                Material.GranularFrictionAngle,
                Material.GranularCohesion,
                Material.GranularDilatancy
            );
            _sph.Clear();
            foreach (var p in Particles)
            {
                _sph.AddParticle(p);
                p.GetComponent<ObjectCollision>()?.Let(oc => oc.Velocity = Vector2.Zero);
            }
        }

        public int FluidCount => _sph.ParticleCount;
        public int GranularCount => _sph.ParticleCount;

        public void GetFluidDebugData(
            Dictionary<Entity, int> map,
            Span<float> d,
            Span<float> pr,
            Span<int> n
        ) => _sph.GetAllDebugData(map, d, pr, n);

        public bool TryGetEntityAt(int idx, out Entity e) => _sph.TryGetEntityAt(idx, out e);

        public void StepFluid(float dt, SpatialGrid grid)
        {
            if (Material.IsFluid || Material.IsGranular)
                _sph.Step(dt, grid);
        }

        public (
            int particleCount,
            float avgDensity,
            float maxDensity,
            float avgPressure,
            float avgViscosity,
            float avgNeighbors,
            float avgVelocity,
            float maxVelocity
        ) GetSPHDebugInfo() =>
            (Material.IsFluid || Material.IsGranular)
                ? (
                    _sph.ParticleCount,
                    _sph.AvgDensity,
                    _sph.MaxDensity,
                    _sph.AvgPressure,
                    _sph.AvgViscosityForce,
                    _sph.AvgNeighbors,
                    _sph.AvgVelocity,
                    _sph.MaxVelocity
                )
                : (0, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
    }

    public static class ParticleSystemFactory
    {
        public static ParticleSystem Create(string name, string preset) => new(name, preset);

        public static List<ParticleSystem> CreateMultiple(
            params (string name, string preset)[] systems
        ) => systems.Select(s => new ParticleSystem(s.name, s.preset)).ToList();
    }

    public static class ObjectExtensions
    {
        public static void Let<T>(this T obj, Action<T> action)
            where T : class
        {
            if (obj != null)
                action(obj);
        }
    }
}
