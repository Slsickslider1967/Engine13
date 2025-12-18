using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities;

namespace Engine13.Utilities
{
    public sealed class Bond
    {
        public Entity A { get; set; } = null!;
        public Entity B { get; set; } = null!;
        public float RestLength { get; set; }
        public float Stiffness { get; set; }
        public float Damping { get; set; }
    }

    // System-level molecular dynamics / bond manager. Registered as an IUpdatable.
    public sealed class MolecularDynamicsSystem : Engine13.Core.IUpdatable
    {
        public static readonly MolecularDynamicsSystem Instance = new();

        private readonly List<Bond> _bonds = new();
        private readonly HashSet<(int, int)> _bondKeys = new();

        private MolecularDynamicsSystem() { }

        public void ClearAllBonds() {
            _bonds.Clear();
            _bondKeys.Clear();
        }

        public void AddBond(Entity a, Entity b, float stiffness, float damping, float restLength)
        {
            if (a == null || b == null) return;
            if (a == b) return;

            var key = a.GetHashCode() < b.GetHashCode() ? (a.GetHashCode(), b.GetHashCode()) : (b.GetHashCode(), a.GetHashCode());
            if (_bondKeys.Contains(key)) return;

            _bondKeys.Add(key);
            _bonds.Add(new Bond { A = a, B = b, RestLength = restLength, Stiffness = stiffness, Damping = damping });
        }

        public void Update(GameTime gameTime)
        {
            if (_bonds.Count == 0) return;

            double dt = gameTime.DeltaTime;
            foreach (var bond in _bonds)
            {
                var a = bond.A;
                var b = bond.B;
                if (a == null || b == null) continue;

                var ocA = a.GetComponent<Engine13.Utilities.Attributes.ObjectCollision>();
                var ocB = b.GetComponent<Engine13.Utilities.Attributes.ObjectCollision>();
                if ((ocA != null && ocA.IsStatic) || (ocB != null && ocB.IsStatic)) continue;

                Vector2 pa = a.Position;
                Vector2 pb = b.Position;
                Vector2 delta = pb - pa;
                float dist = delta.Length();
                if (dist <= 1e-6f) continue;
                Vector2 dir = delta / dist;

                // Hooke spring: F = -k * (x - x0)
                float stretch = dist - bond.RestLength;
                float springForce = -bond.Stiffness * stretch;

                // Damping along bond: proportional to relative velocity along direction
                Vector2 va = ocA != null ? ocA.Velocity : a.Velocity;
                Vector2 vb = ocB != null ? ocB.Velocity : b.Velocity;
                Vector2 relVel = vb - va;
                float relVelAlong = Vector2.Dot(relVel, dir);
                float dampForce = -bond.Damping * relVelAlong;

                float total = springForce + dampForce;

                // Apply equal and opposite forces
                var f = new Vec2(total * dir.X, total * dir.Y);
                Forces.AddForce(a, f);
                Forces.AddForce(b, new Vec2(-f.X, -f.Y));
            }
        }
    }
}
