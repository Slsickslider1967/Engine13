using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities;

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
    public float ComputedTerminalVelocityMag =>
        MathHelpers.ComputeTerminalVelocityMag(Mass, AccelerationY, DragCoefficient);

    public Gravity(
        float accelerationY,
        float initialVelocityY = 0f,
        float mass = 1f,
        float accelerationX = 0f,
        float initialVelocityX = 0f
    )
    {
        AccelerationY = accelerationY;
        AccelerationX = accelerationX;
        _velocityY = initialVelocityY;
        _velocityX = initialVelocityX;
        Mass = mass;
    }

    public void Update(Entity entity, GameTime gameTime)
    {
        var particleDynamics = entity.GetComponent<ParticleDynamics>();
        if (particleDynamics != null)
        {
            // If using SPH solver, skip - gravity handled in StepFluid()
            if (particleDynamics.UseSPHSolver)
                return;
                
            double effectiveMass = entity.Mass > 0f ? entity.Mass : 1.0;
            Forces.AddForce(
                entity,
                new Vec2(effectiveMass * AccelerationX, effectiveMass * AccelerationY)
            );
            return;
        }

        float dt = gameTime.DeltaTime;
        if (dt <= 0f)
            return;

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
            float terminalMag = MathHelpers.ComputeTerminalVelocityMag(
                massForDrag,
                AccelerationY,
                effectiveDrag
            );

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
            float terminalMag = MathHelpers.ComputeTerminalVelocityMag(
                Mass,
                AccelerationY,
                effectiveDrag
            );

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

    public ParticleDynamics() { }

    public ParticleDynamics(List<Entity> allEntities, SpatialGrid? grid = null)
    {
        _allEntities = allEntities;
        _grid = grid;
    }

    public void Update(Entity entity, GameTime gameTime)
    {
        if (UseSPHSolver)
            return;

        Vector2 totalForce = Vector2.Zero;

        // Granular materials: use inter-particle pressure
        if (_allEntities != null)
        {
            totalForce += ComputeInterParticleForces(entity);
        }

        if (VelocityDamping > 0f)
        {
            float mass = entity.Mass > 0f ? entity.Mass : 1f;
            totalForce -= VelocityDamping * mass * entity.Velocity;
        }

        if (totalForce == Vector2.Zero)
            return;

        if (MaxForceMagnitude > 0f)
        {
            float mag = totalForce.Length();
            if (mag > MaxForceMagnitude)
                totalForce *= MaxForceMagnitude / mag;
        }

        Forces.AddForce(entity, new Vec2(totalForce.X, totalForce.Y));
    }



    Vector2 ComputeInterParticleForces(Entity entity)
    {
        if ((_allEntities == null || _allEntities.Count == 0) && _grid == null)
            return Vector2.Zero;

        var oc = entity.GetComponent<ObjectCollision>();
        bool isFluid = oc?.IsFluid ?? false;

        Vector2 totalForce = Vector2.Zero;
        float h = PressureRadius;
        float hSq = h * h;
        float restDistance = h * 0.5f;

        System.Collections.Generic.IEnumerable<Entity> source;
        if (_grid != null)
        {
            _grid.GetNearbyEntities(entity.Position, _neighborBuffer);
            source = _neighborBuffer;
        }
        else
        {
            source = _allEntities!;
        }

        foreach (var other in source)
        {
            if (other == entity)
                continue;

            Vector2 delta = entity.Position - other.Position;
            float distSq = delta.LengthSquared();

            if (distSq >= hSq || distSq <= 1e-10f)
                continue;

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
                    float lateralDir =
                        MathF.Abs(dir.X) > 0.001f ? MathF.Sign(dir.X)
                        : ((entity.GetHashCode() ^ other.GetHashCode()) & 1) == 0 ? 1f
                        : -1f;
                    float lateralStrength = PressureStrength * overlap * 0.015f;
                    totalForce += new Vector2(lateralDir * lateralStrength, 0f);
                }
            }

            if (isFluid)
            {
                var otherOc = other.GetComponent<ObjectCollision>();
                if (otherOc != null && oc != null)
                {
                    float viscosityStrength = 0.15f;
                    float w = 1f - dist / h;
                    Vector2 velocityDiff = otherOc.Velocity - oc.Velocity;
                    totalForce += velocityDiff * w * viscosityStrength;
                }
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
            float velSq =
                oc != null ? oc.Velocity.LengthSquared() : entity.Velocity.LengthSquared();
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

    public static (float avgSpeed, float maxSpeed, float minSpeed) GetVelocityStats(
        List<Entity> entities
    )
    {
        if (entities.Count == 0)
            return (0f, 0f, 0f);

        float totalSpeed = 0f;
        float maxSpeed = float.MinValue;
        float minSpeed = float.MaxValue;

        foreach (var entity in entities)
        {
            var oc = entity.GetComponent<ObjectCollision>();
            float speed = oc != null ? oc.Velocity.Length() : entity.Velocity.Length();
            totalSpeed += speed;
            if (speed > maxSpeed)
                maxSpeed = speed;
            if (speed < minSpeed)
                minSpeed = speed;
        }

        return (totalSpeed / entities.Count, maxSpeed, minSpeed);
    }

    public static (Vector2 avgPos, float minY, float maxY) GetPositionStats(List<Entity> entities)
    {
        if (entities.Count == 0)
            return (Vector2.Zero, 0f, 0f);

        Vector2 totalPos = Vector2.Zero;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var entity in entities)
        {
            var pos = entity.Position;
            totalPos += pos;
            if (pos.Y < minY)
                minY = pos.Y;
            if (pos.Y > maxY)
                maxY = pos.Y;
        }

        return (totalPos / entities.Count, minY, maxY);
    }

    public static int GetGroundedCount(List<Entity> entities)
    {
        int count = 0;
        foreach (var entity in entities)
        {
            var oc = entity.GetComponent<ObjectCollision>();
            if (oc is { IsGrounded: true })
                count++;
        }
        return count;
    }
}

public sealed class EdgeCollision : IEntityComponent
{
    readonly bool _loop;

    public EdgeCollision(bool loop) => _loop = loop;

    static (float left, float right, float top, float bottom) GetBounds() =>
        WindowBounds.GetNormalizedBounds();

    void HandleWallCollision(
        ObjectCollision oc,
        bool isXAxis,
        float sleepVelocity,
        Entity? entity = null
    )
    {
        if (oc == null)
            return;

        if (isXAxis)
        {
            float vX = oc.Velocity.X;
            vX = MathF.Abs(vX) < sleepVelocity ? 0f : -vX * oc.Restitution;
            oc.Velocity = new Vector2(vX, oc.Velocity.Y);
        }
        else
        {
            float vY = oc.Velocity.Y;
            vY = MathF.Abs(vY) < sleepVelocity ? 0f : -vY * oc.Restitution;
            oc.Velocity = new Vector2(oc.Velocity.X, vY);
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
            if (position.X < left + halfWidth)
                position.X = left + halfWidth;
            else if (position.X > right - halfWidth)
                position.X = right - halfWidth;

            if (position.Y < top + halfHeight)
                position.Y = top + halfHeight;
            else if (position.Y > bottom - halfHeight)
                position.Y = bottom - halfHeight;

            entity.Position = position;
        }
        else
        {
            var oc = entity.GetComponent<ObjectCollision>();
            const float sleepVelocity = 0.05f;
            // Use particle radius for recovery to prevent tunneling
            float recovery = MathF.Max(halfWidth, halfHeight) * 0.1f;

            if (position.X < left + halfWidth)
            {
                position.X = left + halfWidth + recovery;
                if (oc != null)
                    HandleWallCollision(oc, true, sleepVelocity, entity);
            }
            else if (position.X > right - halfWidth)
            {
                position.X = right - halfWidth - recovery;
                if (oc != null)
                    HandleWallCollision(oc, true, sleepVelocity, entity);
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
        if (UseSPHIntegration)
            return;

        if (!IsStatic)
        {
            var pos = entity.Position;
            pos += Velocity * gameTime.DeltaTime;
            entity.Position = pos;
        }
    }
}
