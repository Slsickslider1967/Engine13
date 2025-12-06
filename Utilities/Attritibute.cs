using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;

namespace Engine13.Utilities.Attributes;

public interface IEntityComponent
{
    void Update(Entity entity, GameTime gameTime);
}

public sealed class Gravity : IEntityComponent
{
    public float AccelerationY { get; set; }
    public float AccelerationX { get; set; }
    public float Mass { get; set; } = 1f;
    public float TerminalVelocityY { get; set; } = float.PositiveInfinity;
    public float TerminalVelocityX { get; set; } = float.PositiveInfinity;
    public float DragCoefficient { get; set; } = 1f;
    
    float _velocityY;
    float _velocityX;

    public float VelocityY => _velocityY;
    public float VelocityX => _velocityX;
    public float MomentumY => Mass * _velocityY;
    public float MomentumX => Mass * _velocityX;
    public float ComputedTerminalVelocityMag => MathHelpers.ComputeTerminalVelocityMag(Mass, AccelerationY, DragCoefficient);

    public Gravity(float accelerationY, float initialVelocityY = 0f, float mass = 1f, 
                   float accelerationX = 0f, float initialVelocityX = 0f)
    {
        AccelerationY = accelerationY;
        AccelerationX = accelerationX;
        _velocityY = initialVelocityY;
        _velocityX = initialVelocityX;
        Mass = mass;
    }

    public void Update(Entity entity, GameTime gameTime)
    {
        if (entity.GetComponent<ParticleDynamics>() != null)
        {
            double effectiveMass = entity.Mass > 0f ? entity.Mass : 1.0;
            Forces.AddForce(entity, new Vec2(effectiveMass * AccelerationX, effectiveMass * AccelerationY));
            return;
        }

        float dt = gameTime.DeltaTime;
        if (dt <= 0f) return;

        var oc = entity.GetComponent<ObjectCollision>();
        if (oc != null && !oc.IsStatic)
        {
            float vY = oc.Velocity.Y;
            float vX = oc.Velocity.X;

            if (!(oc.IsGrounded && vY >= 0f))
                vY += AccelerationY * dt;
            vX += AccelerationX * dt;

            float massForDrag = oc.Mass > 0f ? oc.Mass : Mass;
            float area = MathHelpers.ComputeArea(entity.Size);
            float effectiveDrag = DragCoefficient * area;
            float terminalMag = MathHelpers.ComputeTerminalVelocityMag(massForDrag, AccelerationY, effectiveDrag);
            
            if (float.IsFinite(terminalMag))
                vY = Math.Clamp(vY, -terminalMag, terminalMag);
            if (float.IsFinite(TerminalVelocityY))
                vY = Math.Clamp(vY, -TerminalVelocityY, TerminalVelocityY);
            if (float.IsFinite(TerminalVelocityX))
                vX = Math.Clamp(vX, -TerminalVelocityX, TerminalVelocityX);

            oc.Velocity = new Vector2(vX, vY);
        }
        else
        {
            _velocityY += AccelerationY * dt;
            _velocityX += AccelerationX * dt;

            float area = MathHelpers.ComputeArea(entity.Size);
            float effectiveDrag = DragCoefficient * area;
            float terminalMag = MathHelpers.ComputeTerminalVelocityMag(Mass, AccelerationY, effectiveDrag);
            
            if (float.IsFinite(terminalMag))
                _velocityY = Math.Clamp(_velocityY, -terminalMag, terminalMag);
            if (float.IsFinite(TerminalVelocityY))
                _velocityY = Math.Clamp(_velocityY, -TerminalVelocityY, TerminalVelocityY);
            if (float.IsFinite(TerminalVelocityX))
                _velocityX = Math.Clamp(_velocityX, -TerminalVelocityX, TerminalVelocityX);

            var pos = entity.Position;
            pos.Y += _velocityY * dt;
            pos.X += _velocityX * dt;
            entity.Position = pos;
        }
    }
}

public struct ParticleBond(Entity a, Entity b)
{
    public Entity ParticleA = a;
    public Entity ParticleB = b;
    public float RestLength = Vector2.Distance(a.Position, b.Position);
}

public sealed class ParticleDynamics : IEntityComponent
{
    readonly List<Entity>? _allEntities;
    
    public float MaxForceMagnitude { get; set; } = 25f;
    public float VelocityDamping { get; set; } = 0.05f;
    public float PressureStrength { get; set; } = 2f;
    public float PressureRadius { get; set; } = 0.02f;
    public bool IsSolid { get; set; }
    public float BondStiffness { get; set; } = 50f;
    public float BondDamping { get; set; } = 5f;
    public bool UseSPHSolver { get; set; }
    public List<ParticleBond> Bonds { get; } = [];

    public ParticleDynamics() { }
    public ParticleDynamics(List<Entity> allEntities) => _allEntities = allEntities;

    public void CreateBondsWithNeighbors(Entity self, List<Entity> allParticles, float bondRadius)
    {
        if (!IsSolid) return;
        Bonds.Clear();
        
        foreach (var other in allParticles)
        {
            if (other == self) continue;
            var otherDynamics = other.GetComponent<ParticleDynamics>();
            if (otherDynamics is not { IsSolid: true }) continue;
            
            if (Vector2.Distance(self.Position, other.Position) <= bondRadius)
                Bonds.Add(new ParticleBond(self, other));
        }
    }

    public static ParticleDynamics CreateParticle(List<Entity> allEntities) => new(allEntities)
    {
        MaxForceMagnitude = 50f, VelocityDamping = 0.02f, PressureStrength = 2.5f, PressureRadius = 0.02f
    };

    public static ParticleDynamics CreateHeavyParticle(List<Entity> allEntities) => new(allEntities)
    {
        MaxForceMagnitude = 100f, VelocityDamping = 0.01f, PressureStrength = 4f, PressureRadius = 0.02f
    };

    public static ParticleDynamics CreateLightParticle(List<Entity> allEntities) => new(allEntities)
    {
        MaxForceMagnitude = 25f, VelocityDamping = 0.05f, PressureStrength = 2f, PressureRadius = 0.02f
    };

    public static ParticleDynamics CreateFluidParticle(List<Entity> allEntities) => new(allEntities)
    {
        MaxForceMagnitude = 15f, VelocityDamping = 0.08f, PressureStrength = 1.5f, PressureRadius = 0.02f
    };

    public void Update(Entity entity, GameTime gameTime)
    {
        if (UseSPHSolver) return;

        Vector2 totalForce = Vector2.Zero;

        if (IsSolid && Bonds.Count > 0)
            totalForce += ComputeBondForces(entity);
        else if (_allEntities != null)
            totalForce += ComputeInterParticleForces(entity);

        if (VelocityDamping > 0f)
        {
            float mass = entity.Mass > 0f ? entity.Mass : 1f;
            totalForce -= VelocityDamping * mass * entity.Velocity;
        }

        if (totalForce == Vector2.Zero) return;

        if (MaxForceMagnitude > 0f)
        {
            float mag = totalForce.Length();
            if (mag > MaxForceMagnitude)
                totalForce *= MaxForceMagnitude / mag;
        }

        Forces.AddForce(entity, new Vec2(totalForce.X, totalForce.Y));
    }

    Vector2 ComputeBondForces(Entity entity)
    {
        if (Bonds.Count == 0) return Vector2.Zero;

        Vector2 totalBondForce = Vector2.Zero;
        Vector2 totalPosCorrection = Vector2.Zero;
        Vector2 avgVelocity = entity.Velocity;
        int velocityCount = 1;

        foreach (var bond in Bonds)
        {
            Entity other = bond.ParticleA == entity ? bond.ParticleB : bond.ParticleA;
            avgVelocity += other.Velocity;
            velocityCount++;
        }
        avgVelocity /= velocityCount;

        var oc = entity.GetComponent<ObjectCollision>();
        if (oc != null)
            oc.Velocity = Vector2.Lerp(oc.Velocity, avgVelocity, 0.3f);

        foreach (var bond in Bonds)
        {
            Entity other = bond.ParticleA == entity ? bond.ParticleB : bond.ParticleA;
            Vector2 delta = other.Position - entity.Position;
            float currentLength = delta.Length();

            if (currentLength < 1e-8f)
            {
                delta = new Vector2(0.001f, 0f);
                currentLength = 0.001f;
            }

            Vector2 direction = delta / currentLength;
            float error = currentLength - bond.RestLength;

            float springForce = BondStiffness * error;
            totalBondForce += direction * springForce;

            Vector2 relVel = other.Velocity - entity.Velocity;
            float dampingForce = BondDamping * Vector2.Dot(relVel, direction);
            totalBondForce += direction * dampingForce;

            float correctionStrength = MathF.Min(BondStiffness / 200f, 0.8f);
            float posCorrection = error * correctionStrength * 0.5f;
            totalPosCorrection += direction * posCorrection;
        }

        if (totalPosCorrection.LengthSquared() > 1e-12f)
            entity.Position += totalPosCorrection;

        return totalBondForce;
    }

    Vector2 ComputeInterParticleForces(Entity entity)
    {
        if (_allEntities == null || _allEntities.Count == 0) return Vector2.Zero;

        var oc = entity.GetComponent<ObjectCollision>();
        bool isFluid = oc?.IsFluid ?? false;

        Vector2 totalForce = Vector2.Zero;
        float h = PressureRadius;
        float hSq = h * h;
        float restDistance = h * 0.5f;

        foreach (var other in _allEntities)
        {
            if (other == entity) continue;

            Vector2 delta = entity.Position - other.Position;
            float distSq = delta.LengthSquared();

            if (distSq >= hSq || distSq <= 1e-10f) continue;

            float dist = MathF.Sqrt(distSq);
            Vector2 dir = delta / dist;

            if (dist < restDistance)
            {
                float overlap = 1f - dist / restDistance;
                float pressureMag = PressureStrength * overlap * 0.1f;
                totalForce += dir * pressureMag;

                float verticalAlignment = MathF.Abs(dir.Y);
                if (verticalAlignment > 0.95f)
                {
                    float lateralDir = MathF.Abs(dir.X) > 0.001f 
                        ? MathF.Sign(dir.X) 
                        : ((entity.GetHashCode() ^ other.GetHashCode()) & 1) == 0 ? 1f : -1f;
                    float lateralStrength = PressureStrength * overlap * 0.015f;
                    totalForce += new Vector2(lateralDir * lateralStrength, 0f);
                }
            }

            if (isFluid)
            {
                float viscosityStrength = 0.15f;
                float w = 1f - dist / h;
                Vector2 velocityDiff = other.Velocity - entity.Velocity;
                totalForce += velocityDiff * w * viscosityStrength;
            }
        }

        return totalForce;
    }

    public static float CalculateKineticEnergy(List<Entity> entities)
    {
        float totalKE = 0f;
        foreach (var entity in entities)
        {
            float mass = entity.Mass > 0f ? entity.Mass : 1f;
            var oc = entity.GetComponent<ObjectCollision>();
            float velSq = oc != null ? oc.Velocity.LengthSquared() : entity.Velocity.LengthSquared();
            totalKE += 0.5f * mass * velSq;
        }
        return totalKE;
    }

    public static float CalculatePotentialEnergy(List<Entity> entities)
    {
        float totalPE = 0f;
        var bounds = WindowBounds.GetNormalizedBounds();
        float groundLevel = bounds.bottom;

        foreach (var entity in entities)
        {
            float mass = entity.Mass > 0f ? entity.Mass : 1f;
            var gravity = entity.GetComponent<Gravity>();
            float g = gravity?.AccelerationY ?? 0f;
            float height = groundLevel - entity.Position.Y;
            totalPE += mass * MathF.Abs(g) * height;
        }
        return totalPE;
    }

    public static float CalculateTotalEnergy(List<Entity> entities) => 
        CalculateKineticEnergy(entities) + CalculatePotentialEnergy(entities);

    public static (float avgSpeed, float maxSpeed, float minSpeed) GetVelocityStats(List<Entity> entities)
    {
        if (entities.Count == 0) return (0f, 0f, 0f);

        float totalSpeed = 0f;
        float maxSpeed = float.MinValue;
        float minSpeed = float.MaxValue;

        foreach (var entity in entities)
        {
            var oc = entity.GetComponent<ObjectCollision>();
            float speed = oc != null ? oc.Velocity.Length() : entity.Velocity.Length();
            totalSpeed += speed;
            if (speed > maxSpeed) maxSpeed = speed;
            if (speed < minSpeed) minSpeed = speed;
        }

        return (totalSpeed / entities.Count, maxSpeed, minSpeed);
    }

    public static (Vector2 avgPos, float minY, float maxY) GetPositionStats(List<Entity> entities)
    {
        if (entities.Count == 0) return (Vector2.Zero, 0f, 0f);

        Vector2 totalPos = Vector2.Zero;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var entity in entities)
        {
            var pos = entity.Position;
            totalPos += pos;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.Y > maxY) maxY = pos.Y;
        }

        return (totalPos / entities.Count, minY, maxY);
    }

    public static int GetGroundedCount(List<Entity> entities)
    {
        int count = 0;
        foreach (var entity in entities)
        {
            var oc = entity.GetComponent<ObjectCollision>();
            if (oc is { IsGrounded: true }) count++;
        }
        return count;
    }
}

public sealed class EdgeCollision : IEntityComponent
{
    readonly bool _loop;

    public EdgeCollision(bool loop) => _loop = loop;

    static (float left, float right, float top, float bottom) GetBounds() => WindowBounds.GetNormalizedBounds();

    void HandleWallCollision(ObjectCollision oc, bool isXAxis, float sleepVelocity, Entity? entity = null)
    {
        if (oc == null) return;

        var dynamics = entity?.GetComponent<ParticleDynamics>();
        bool isSolid = dynamics?.IsSolid ?? false;

        if (isXAxis)
        {
            float vX = oc.Velocity.X;
            vX = MathF.Abs(vX) < sleepVelocity ? 0f : -vX * oc.Restitution;
            oc.Velocity = new Vector2(vX, oc.Velocity.Y);
            
            if (isSolid && dynamics != null)
                PropagateVelocityToBonds(dynamics, oc.Velocity);
        }
        else
        {
            float vY = oc.Velocity.Y;
            vY = MathF.Abs(vY) < sleepVelocity ? 0f : -vY * oc.Restitution;
            oc.Velocity = new Vector2(oc.Velocity.X, vY);
            
            if (isSolid && dynamics != null)
                PropagateVelocityToBonds(dynamics, oc.Velocity);
        }
    }

    static void PropagateVelocityToBonds(ParticleDynamics dynamics, Vector2 newVelocity)
    {
        foreach (var bond in dynamics.Bonds)
        {
            var otherCollision = bond.ParticleB.GetComponent<ObjectCollision>();
            if (otherCollision != null)
                otherCollision.Velocity = Vector2.Lerp(otherCollision.Velocity, newVelocity, 0.5f);
        }
    }

    public void Update(Entity entity, GameTime gameTime)
    {
        var (left, right, top, bottom) = GetBounds();
        var position = entity.Position;
        float halfWidth = entity.Size.X * 0.5f;
        float halfHeight = entity.Size.Y * 0.5f;

        if (_loop)
        {
            if (position.X < left + halfWidth) position.X = left + halfWidth;
            else if (position.X > right - halfWidth) position.X = right - halfWidth;

            if (position.Y < top + halfHeight) position.Y = top + halfHeight;
            else if (position.Y > bottom - halfHeight) position.Y = bottom - halfHeight;

            entity.Position = position;
        }
        else
        {
            var oc = entity.GetComponent<ObjectCollision>();
            const float sleepVelocity = 0.05f;
            const float recovery = 0.0005f;

            if (position.X < left + halfWidth)
            {
                position.X = left + halfWidth + recovery;
                if (oc != null) HandleWallCollision(oc, true, sleepVelocity, entity);
            }
            else if (position.X > right - halfWidth)
            {
                position.X = right - halfWidth - recovery;
                if (oc != null) HandleWallCollision(oc, true, sleepVelocity, entity);
            }

            if (position.Y < top + halfHeight)
            {
                position.Y = top + halfHeight + recovery;
                if (oc != null)
                {
                    HandleWallCollision(oc, false, sleepVelocity, entity);
                    oc.IsGrounded = false;
                }
            }
            else if (position.Y > bottom - halfHeight)
            {
                position.Y = bottom - halfHeight - recovery;
                if (oc != null)
                {
                    HandleWallCollision(oc, false, sleepVelocity, entity);
                    oc.IsGrounded = MathF.Abs(oc.Velocity.Y) < sleepVelocity;
                }
            }

            entity.Position = position;
        }
    }
}

public sealed class ObjectCollision : IEntityComponent
{
    public float Mass { get; set; } = 1f;
    public float Restitution { get; set; } = 0.8f;
    public float Friction { get; set; } = 0.5f;
    public Vector2 Velocity { get; set; } = Vector2.Zero;
    public bool IsStatic { get; set; }
    public bool IsGrounded { get; set; }
    public bool IsFluid { get; set; }
    public bool UseSPHIntegration { get; set; }

    public void Update(Entity entity, GameTime gameTime)
    {
        if (UseSPHIntegration) return;

        if (!IsStatic)
        {
            var pos = entity.Position;
            pos += Velocity * gameTime.DeltaTime;
            entity.Position = pos;
        }
    }
}
