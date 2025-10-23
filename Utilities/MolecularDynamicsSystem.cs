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

    // public static class MolecularDynamics
    // {
    //     public static Vec2 MinimumImage(Vec2 position)
    //     {
    //         double x = position.X;
    //         double y = position.Y;
    //         if (x > 1.0)
    //         {
    //             x -= 2.0;
    //         }
    //         if (x < -1.0)
    //         {
    //             x += 2.0;
    //         }
    //         if (y > 1.0)
    //         {
    //             y -= 2.0;
    //         }
    //         if (y < -1.0)
    //         {
    //             y += 2.0;
    //         }
    //         return new Vec2(x, y);
    //     }

    //     public static void AccumulatedLJForce
    //     (
    //         ref Vec2 TotalForce,
    //         Vec2 Pos_i,
    //         Vec2 Pos_j,
    //         double epsilon,
    //         double sigma,
    //         double rcut
    //     )
    //     {
    //         Vec2 Dr = MolecularDynamics.MinimumImage(Pos_i - Pos_j);
    //         double r2 = Dr.Norm2();
    //         double rcut2 = rcut * rcut;

    //         if (r2 <= 1e-12 || r2 > rcut2)
    //         {
    //             return;
    //         }

    //         double invr2 = 1.0 / r2;
    //         double sr2 = (sigma * sigma) * invr2;
    //         double sr6 = sr2 * sr2 * sr2;

    //         double forceScalar = 24.0 * epsilon * (2.0 * sr6 * sr6 - sr6) * invr2;
    //         TotalForce += Dr * forceScalar;
    //     }
    // }

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
    }
}
