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

        var ObjectColission = entity.GetComponent<ObjectCollision>();
        if (ObjectColission != null && !ObjectColission.IsStatic)
        {
            if (ObjectColission.IsFluid || ObjectColission.UseSPHIntegration)
                return;
            
            float ChangeY = ObjectColission.Velocity.Y;
            float ChangeX = ObjectColission.Velocity.X;

            if (!(ObjectColission.IsGrounded && ChangeY >= 0f))
                ChangeY += AccelerationY * dt;
            ChangeX += AccelerationX * dt;

            float massForDrag = ObjectColission.Mass > 0f ? ObjectColission.Mass : Mass;
            float area = MathHelpers.ComputeArea(entity.Size);
            float effectiveDrag = DragCoefficient * area;
            float terminalMag = MathHelpers.ComputeTerminalVelocityMag(
                massForDrag,
                AccelerationY,
                effectiveDrag
            );

            if (float.IsFinite(terminalMag))
                ChangeY = Math.Clamp(ChangeY, -terminalMag, terminalMag);
            if (float.IsFinite(TerminalVelocityY))
                ChangeY = Math.Clamp(ChangeY, -TerminalVelocityY, TerminalVelocityY);
            if (float.IsFinite(TerminalVelocityX))
                ChangeX = Math.Clamp(ChangeX, -TerminalVelocityX, TerminalVelocityX);

            ObjectColission.Velocity = new Vector2(ChangeX, ChangeY);
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
            var oc = entity.GetComponent<ObjectCollision>();
            Vector2 vel = oc != null ? oc.Velocity : entity.Velocity;
            float mass = entity.Mass > 0f ? entity.Mass : 1f;
            totalForce -= VelocityDamping * mass * vel;
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

    public static void RemoveParticlesInArea(List<Entity> entities, float minX, float minY, float maxX, float maxY)
    {
        entities.RemoveAll(entity =>
        {
            var pos = entity.Position;
            return pos.X >= minX && pos.X <= maxX && pos.Y >= minY && pos.Y <= maxY;
        });
    }

    public static void RemoveAllParticles(List<Entity> entities)
    {
        entities.Clear();
    }

    Vector2 ComputeInterParticleForces(Entity entity)
    {
        if (_grid == null)
            return Vector2.Zero;

        var oc = entity.GetComponent<ObjectCollision>();
        if (oc?.IsFluid ?? false)
            return Vector2.Zero;

        Vector2 totalForce = Vector2.Zero;
        float h = PressureRadius;
        float hSq = h * h;

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
            Vector2 dir = delta / dist;

            float targetDist = (entity.CollisionRadius + other.CollisionRadius) * 1.2f;
            if (dist < targetDist)
            {
                float ratio = targetDist / dist;
                float forceMag = PressureStrength * ratio * ratio;
                totalForce += dir * forceMag;
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
        ObjectCollision ObjectCollision,
        bool isXAxis,
        float sleepVelocity,
        Entity? entity = null
    )
    {
        if (ObjectCollision == null)
            return;

        float extraDamping = ObjectCollision.IsFluid ? 0.5f : 1.0f;
        if (isXAxis)
        {
            float vX = ObjectCollision.Velocity.X;
            if (MathF.Abs(vX) < sleepVelocity)
                vX = 0f;
            else if (entity != null)
            {
                var bounds = GetBounds();
                bool atLeftWall = entity.Position.X < (bounds.left + bounds.right) * 0.5f;
                bool movingIntoWall = (atLeftWall && vX < 0) || (!atLeftWall && vX > 0);
                if (movingIntoWall)
                    vX = -vX * ObjectCollision.Restitution * extraDamping;
            }
            ObjectCollision.Velocity = new Vector2(vX, ObjectCollision.Velocity.Y);
        }
        else
        {
            float vY = ObjectCollision.Velocity.Y;
            if (MathF.Abs(vY) < sleepVelocity)
                vY = 0f;
            else if (entity != null)
            {
                var bounds = GetBounds();
                bool atTopWall = entity.Position.Y < (bounds.top + bounds.bottom) * 0.5f;
                bool movingIntoWall = (atTopWall && vY < 0) || (!atTopWall && vY > 0);
                if (movingIntoWall)
                    vY = -vY * ObjectCollision.Restitution * extraDamping;
            }
            ObjectCollision.Velocity = new Vector2(ObjectCollision.Velocity.X, vY);
        }
    }

    public void Update(Entity entity, GameTime gameTime)
    {
        var (left, right, top, bottom) = GetBounds();
        var position = entity.Position;
        
        float halfWidth, halfHeight;
        if (entity.CollisionShape == Engine13.Graphics.Entity.CollisionShapeType.Circle && entity.CollisionRadius > 0f)
        {
            halfWidth = entity.CollisionRadius;
            halfHeight = entity.CollisionRadius;
        }
        else
        {
            halfWidth = entity.Size.X * 0.5f;
            halfHeight = entity.Size.Y * 0.5f;
        }

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
            float recovery = MathF.Max(halfWidth, halfHeight) * (oc?.IsFluid == true ? 0.15f : 0.08f);

            if (position.X < left + halfWidth)
            {
                position.X = left + halfWidth + recovery;
                entity.Position = position;
                if (oc != null)
                {
                    HandleWallCollision(oc, true, sleepVelocity, entity);
                    if (oc.IsFluid)
                        oc.Velocity *= 0.8f;
                }
            }
            else if (position.X > right - halfWidth)
            {
                position.X = right - halfWidth - recovery;
                entity.Position = position;
                if (oc != null)
                {
                    HandleWallCollision(oc, true, sleepVelocity, entity);
                    if (oc.IsFluid)
                        oc.Velocity *= 0.8f;
                }
            }

            if (position.Y < top + halfHeight)
            {
                position.Y = top + halfHeight + recovery;
                entity.Position = position;
                if (oc != null)
                {
                    HandleWallCollision(oc, false, sleepVelocity, entity);
                    oc.IsGrounded = false;
                    if (oc.IsFluid)
                        oc.Velocity *= 0.8f;
                }
            }
            else if (position.Y > bottom - halfHeight)
            {
                position.Y = bottom - halfHeight - recovery;
                entity.Position = position;
                if (oc != null)
                {
                    HandleWallCollision(oc, false, sleepVelocity, entity);
                    oc.IsGrounded = MathF.Abs(oc.Velocity.Y) < sleepVelocity;
                }
            }
            else
            {
                entity.Position = position;
            }

            if (position != entity.Position)
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
    public float AngularVelocity { get; set; } = 0f; // radians per second
    public bool IsStatic { get; set; }
    public bool IsGrounded { get; set; }
    public bool IsFluid { get; set; }
    public bool UseSPHIntegration { get; set; }

    public void Update(Entity entity, GameTime gameTime)
    {
        if (!IsStatic)
        {
            // Progressive damping for non-fluid particles
            if (!IsFluid && Velocity.LengthSquared() > 0.0001f)
            {
                Velocity *= 0.999f;
            }
            
            // Settling damping for fluid particles when moving slowly
            float currentSpeed = Velocity.Length();
            if (IsFluid)
            {
                if (currentSpeed < 0.2f && currentSpeed > 0.001f)
                {
                    // Stronger damping when settling to reach rest state faster
                    Velocity *= 0.97f;
                }
                else if (currentSpeed <= 0.001f)
                {
                    // Stop completely when velocity is negligible
                    Velocity = Vector2.Zero;
                }
            }
            
            const float maxVelocity = 15f;
            if (currentSpeed > maxVelocity)
                Velocity = Velocity * (maxVelocity / currentSpeed);

            var pos = entity.Position;
            pos += Velocity * gameTime.DeltaTime;
            entity.Position = pos;
            
            entity.Rotation += AngularVelocity * gameTime.DeltaTime;
            
            if (IsGrounded && MathF.Abs(AngularVelocity) > 0.01f)
            {
                AngularVelocity *= 0.98f;
            }
        }
    }
}
