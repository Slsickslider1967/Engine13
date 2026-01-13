using System;
using System.Collections.Concurrent;
using System.Numerics;
using Engine13.Graphics;

namespace Engine13.Utilities;

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
        if (entity == null)
            return;
        _accumulator.AddOrUpdate(entity, force, (_, existing) => existing + force);
    }

    public static Vec2 GetForce(Entity entity) =>
        entity != null && _accumulator.TryGetValue(entity, out var force) ? force : Vec2.Zero;

    public static void Apply(Core.GameTime gameTime)
    {
        double dt = gameTime.DeltaTime;
        if (dt <= 0.0 || _accumulator.IsEmpty)
            return;

        foreach (var (entity, force) in _accumulator)
        {
            var collision = entity.GetComponent<Attributes.ObjectCollision>();
            if (collision == null || collision.IsStatic)
                continue;

            float mass = PhysicsMath.SafeMass(entity.Mass);
            var forceVec = new Vector2((float)force.X, (float)force.Y);

            Vector2 deltaV = forceVec * ((float)dt / mass);
            collision.Velocity += deltaV;
        }
    }
}
