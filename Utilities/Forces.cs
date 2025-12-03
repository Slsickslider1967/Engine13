using System;
using System.Collections.Concurrent;
using System.Numerics;
using Engine13.Graphics;

namespace Engine13.Utilities
{
    /// <summary>
    /// Immutable 2D vector using double precision for physics calculations.
    /// </summary>
    public readonly struct Vec2
    {
        public static readonly Vec2 Zero = new(0.0, 0.0);

        public double X { get; }
        public double Y { get; }

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double b) => new(a.X * b, a.Y * b);
        public static Vec2 operator *(double b, Vec2 a) => new(a.X * b, a.Y * b);
        public static Vec2 operator /(Vec2 a, double b) => new(a.X / b, a.Y / b);

        public double LengthSquared() => X * X + Y * Y;
        public double Length() => Math.Sqrt(LengthSquared());
    }

    /// <summary>
    /// Thread-safe force accumulator for physics simulation.
    /// Collects forces during update and applies them at the end of the physics step.
    /// </summary>
    public static class Forces
    {
        private static readonly ConcurrentDictionary<Entity, Vec2> _accumulator = new();

        /// <summary>Maximum velocity magnitude to prevent simulation instability.</summary>
        public static float MaxVelocity { get; set; } = 1.5f;

        /// <summary>Per-frame velocity damping factor (0.0-1.0).</summary>
        public static float VelocityDamping { get; set; } = 0.95f;

        /// <summary>Clears all accumulated forces. Call at the start of each physics step.</summary>
        public static void Reset() => _accumulator.Clear();

        /// <summary>Adds a force to the specified entity's accumulator.</summary>
        public static void AddForce(Entity entity, Vec2 force)
        {
            if (entity == null) return;
            
            _accumulator.AddOrUpdate(
                entity,
                force,
                (_, existing) => existing + force
            );
        }

        /// <summary>Gets the currently accumulated force for an entity.</summary>
        public static Vec2 GetForce(Entity entity)
        {
            if (entity == null) return Vec2.Zero;
            return _accumulator.TryGetValue(entity, out var force) ? force : Vec2.Zero;
        }

        /// <summary>Applies all accumulated forces to entities and updates their velocities.</summary>
        public static void Apply(Engine13.Core.GameTime gameTime)
        {
            double dt = gameTime.DeltaTime;
            if (dt <= 0.0 || _accumulator.IsEmpty)
                return;

            foreach (var (entity, force) in _accumulator)
            {
                var collision = entity.GetComponent<Engine13.Utilities.Attributes.ObjectCollision>();
                if (collision == null || collision.IsStatic)
                    continue;

                double mass = entity.Mass > 0f ? entity.Mass : 1.0;
                var deltaVelocity = new Vector2(
                    (float)(force.X / mass * dt),
                    (float)(force.Y / mass * dt)
                );

                collision.Velocity *= VelocityDamping;
                collision.Velocity += deltaVelocity;

                // Clamp velocity magnitude to prevent simulation explosion
                float speed = collision.Velocity.Length();
                if (speed > MaxVelocity)
                {
                    collision.Velocity *= MaxVelocity / speed;
                }
            }
        }
    }
}
