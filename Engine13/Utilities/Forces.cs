using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;

namespace Engine13.Utilities
{
    public readonly struct Vec2(double x, double y)
    {
        public static readonly Vec2 Zero = new(0.0, 0.0);
        public double X { get; } = x;
        public double Y { get; } = y;

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

        public static Vec2 operator *(Vec2 a, double b) => new(a.X * b, a.Y * b);

        public static Vec2 operator *(double b, Vec2 a) => new(a.X * b, a.Y * b);

        public static Vec2 operator /(Vec2 a, double b) => new(a.X / b, a.Y / b);

        public double LengthSquared() => X * X + Y * Y;

        public double Length() => Math.Sqrt(LengthSquared());
    }

    public static class Forces
    {
        static readonly ConcurrentDictionary<Entity, Vec2> _accumulator = new();
        public static float MaxVelocity { get; set; } = 15.0f;

        public static void Reset() => _accumulator.Clear();

        public static void AddForce(Entity entity, Vec2 force)
        {
            if (entity != null)
                _accumulator.AddOrUpdate(entity, force, (_, existing) => existing + force);
        }

        public static Vec2 GetForce(Entity entity) =>
            entity != null && _accumulator.TryGetValue(entity, out var f) ? f : Vec2.Zero;

        public static void Apply(GameTime gameTime)
        {
            double dt = gameTime.DeltaTime;
            if (dt <= 0.0 || _accumulator.IsEmpty)
                return;
            foreach (var (entity, force) in _accumulator)
            {
                var oc = entity.GetComponent<ObjectCollision>();
                if (oc == null || oc.IsStatic)
                    continue;
                oc.Velocity +=
                    new Vector2((float)force.X, (float)force.Y)
                    * ((float)dt / PhysicsMath.SafeMass(entity.Mass));
            }
        }
    }

    public sealed class Bond
    {
        public Entity A { get; set; } = null!;
        public Entity B { get; set; } = null!;
        public float RestLength,
            Stiffness,
            Damping;
    }

    public sealed class MolecularDynamicsSystem : IUpdatable
    {
        public static readonly MolecularDynamicsSystem Instance = new();
        private readonly List<Bond> _bonds = new();
        private readonly HashSet<(int, int)> _bondKeys = new();

        private MolecularDynamicsSystem() { }

        public void ClearAllBonds()
        {
            _bonds.Clear();
            _bondKeys.Clear();
        }

        public void AddBond(Entity a, Entity b, float stiffness, float damping, float restLength)
        {
            if (a == null || b == null || a == b)
                return;
            var key =
                a.GetHashCode() < b.GetHashCode()
                    ? (a.GetHashCode(), b.GetHashCode())
                    : (b.GetHashCode(), a.GetHashCode());
            if (_bondKeys.Contains(key))
                return;
            _bondKeys.Add(key);
            _bonds.Add(
                new Bond
                {
                    A = a,
                    B = b,
                    RestLength = restLength,
                    Stiffness = stiffness,
                    Damping = damping,
                }
            );
        }

        public void Update(GameTime gameTime)
        {
            if (_bonds.Count == 0)
                return;
            foreach (var bond in _bonds)
            {
                var (a, b) = (bond.A, bond.B);
                if (a == null || b == null)
                    continue;
                var ocA = a.GetComponent<ObjectCollision>();
                var ocB = b.GetComponent<ObjectCollision>();
                if ((ocA != null && ocA.IsStatic) || (ocB != null && ocB.IsStatic))
                    continue;

                Vector2 delta = b.Position - a.Position;
                Vector2 va = ocA != null ? ocA.Velocity : a.Velocity,
                    vb = ocB != null ? ocB.Velocity : b.Velocity;
                Vector2 forceVec = PhysicsMath.HookeDampedForce(
                    delta,
                    bond.RestLength,
                    bond.Stiffness,
                    vb - va,
                    bond.Damping
                );
                if (forceVec == Vector2.Zero)
                    continue;
                var f = new Vec2(forceVec.X, forceVec.Y);
                Forces.AddForce(a, f);
                Forces.AddForce(b, new Vec2(-f.X, -f.Y));
            }
        }
    }
}

namespace Engine13.Utilities.Attributes
{
    public sealed class Gravity : IEntityComponent
    {
        public float AccelerationY { get; set; }
        public float AccelerationX { get; set; }
        public float Mass { get; set; } = 1f;
        public float TerminalVelocityY { get; set; } = float.PositiveInfinity;
        public float TerminalVelocityX { get; set; } = float.PositiveInfinity;
        public float DragCoefficient { get; set; } = 1f;
        float _velocityY,
            _velocityX;
        public float VelocityY => _velocityY;
        public float VelocityX => _velocityX;

        public Gravity(
            float accelY,
            float initVelY = 0f,
            float mass = 1f,
            float accelX = 0f,
            float initVelX = 0f
        )
        {
            AccelerationY = accelY;
            AccelerationX = accelX;
            _velocityY = initVelY;
            _velocityX = initVelX;
            Mass = mass;
        }

        public void Update(Entity entity, GameTime gameTime)
        {
            var pd = entity.GetComponent<ParticleDynamics>();
            if (pd?.UseSPHSolver == true)
                return;

            float gravConst = PhysicsSettings.GravitationalConstant;
            float gravRatio =
                MathF.Abs(AccelerationY) > 0.001f ? gravConst / MathF.Abs(AccelerationY) : 1f;
            double mass = entity.Mass > 0f ? entity.Mass : 1.0;

            if (pd != null)
            {
                Forces.AddForce(
                    entity,
                    new Vec2(mass * AccelerationX * gravRatio, mass * AccelerationY * gravRatio)
                );
                ApplyAirResistance(entity, (float)mass);
                return;
            }

            float dt = gameTime.DeltaTime;
            if (dt <= 0f)
                return;

            var oc = entity.GetComponent<ObjectCollision>();
            if (oc != null && !oc.IsStatic && !oc.IsFluid && !oc.UseSPHIntegration)
            {
                float vY = oc.Velocity.Y,
                    vX = oc.Velocity.X;
                if (!(oc.IsGrounded && vY >= 0f))
                    vY += AccelerationY * gravRatio * dt;
                vX += AccelerationX * gravRatio * dt;
                ApplyDragAndClamp(ref vX, ref vY, entity, oc.Mass > 0f ? oc.Mass : Mass, dt);
                oc.Velocity = new Vector2(vX, vY);
            }
            else if (oc == null || oc.IsStatic)
            {
                _velocityY += AccelerationY * gravRatio * dt;
                _velocityX += AccelerationX * gravRatio * dt;
                ApplyDragAndClamp(ref _velocityX, ref _velocityY, entity, Mass, dt);
                entity.Position += new Vector2(_velocityX, _velocityY) * dt;
            }
        }

        void ApplyAirResistance(Entity e, float mass)
        {
            if (PhysicsSettings.AirResistance <= 0f)
                return;
            var oc = e.GetComponent<ObjectCollision>();
            if (oc == null)
                return;
            float drag = PhysicsSettings.AirResistance * 5f;
            Vector2 f = -oc.Velocity * mass * drag;
            Forces.AddForce(e, new Vec2(f.X, f.Y));
        }

        void ApplyDragAndClamp(ref float vX, ref float vY, Entity e, float mass, float dt)
        {
            if (PhysicsSettings.AirResistance > 0f)
            {
                float d = PhysicsSettings.AirResistance * 2f;
                vX *= 1f - d * dt;
                vY *= 1f - d * dt;
            }
            float area = MathHelpers.ComputeArea(e.Size);
            float termMag = MathHelpers.ComputeTerminalVelocityMag(
                mass,
                AccelerationY,
                DragCoefficient * area
            );
            if (float.IsFinite(termMag))
                vY = Math.Clamp(vY, -termMag, termMag);
            if (float.IsFinite(TerminalVelocityY))
                vY = Math.Clamp(vY, -TerminalVelocityY, TerminalVelocityY);
            if (float.IsFinite(TerminalVelocityX))
                vX = Math.Clamp(vX, -TerminalVelocityX, TerminalVelocityX);
        }
    }
}
