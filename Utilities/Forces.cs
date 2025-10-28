using System;
using System.Collections.ObjectModel;
using Engine13.Graphics;

namespace Engine13.Utilities
{
    public readonly struct Vec2
    {
        public double X { get; }
        public double Y { get; }

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X + b.X, a.Y + b.Y);
        }

        public static Vec2 operator -(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X - b.X, a.Y - b.Y);
        }

        public static Vec2 operator *(Vec2 a, double b)
        {
            return new Vec2(a.X * b, a.Y * b);
        }

        public static Vec2 operator *(double b, Vec2 a)
        {
            return new Vec2(a.X * b, a.Y * b);
        }

        public static Vec2 operator /(Vec2 a, double b)
        {
            return new Vec2(a.X / b, a.Y / b);
        }

        public double Norm2() => X * X + Y * Y;
    }

    public static class Forces
    {
        private static readonly System.Collections.Generic.Dictionary<Mesh, Vec2> _acc =
            new System.Collections.Generic.Dictionary<Mesh, Vec2>();

        public static void Reset()
        {
            _acc.Clear();
        }

        public static void AddForce(Mesh mesh, Vec2 Force)
        {
            if (_acc.TryGetValue(mesh, out var Cur))
                _acc[mesh] = new Vec2(Cur.X + Force.X, Cur.Y + Force.Y);
            else
                _acc[mesh] = Force;
        }

        public static Vec2 GetForce(Mesh mesh)
        {
            return _acc.TryGetValue(mesh, out var f) ? f : new Vec2(0.0, 0.0);
        }

        // Apply accumulated forces to meshes by updating their ObjectCollision velocity.
        // This should be called once per frame after all attributes have contributed forces.
        public static void Apply(Engine13.Core.GameTime gameTime)
        {
            double dt = gameTime.DeltaTime;
            if (dt <= 0.0)
                return;
            if (_acc.Count == 0)
                return;

            foreach (var kv in _acc)
            {
                Mesh mesh = kv.Key;
                Vec2 f = kv.Value;
                var obj = mesh.GetAttribute<Engine13.Utilities.Attributes.ObjectCollision>();
                if (obj == null || obj.IsStatic)
                    continue;
                double mass = (mesh.Mass > 0f) ? mesh.Mass : 1.0;
                var dv = new System.Numerics.Vector2(
                    (float)(f.X / mass * dt),
                    (float)(f.Y / mass * dt)
                );
                obj.Velocity += dv;
            }
        }
    }
}
