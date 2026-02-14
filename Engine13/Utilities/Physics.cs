using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;

namespace Engine13.Utilities
{
    public static class PhysicsSettings
    {
        private static float _gravitationalConstant = 9.81f,
            _airResistance = 0f,
            _wallRestitution = 0.7f;

        public static float GravitationalConstant
        {
            get => _gravitationalConstant;
            set => _gravitationalConstant = Math.Clamp(value, 0f, 50f);
        }
        public static float AirResistance
        {
            get => _airResistance;
            set => _airResistance = Math.Clamp(value, 0f, 1f);
        }
        public static float WallRestitution
        {
            get => _wallRestitution;
            set => _wallRestitution = Math.Clamp(value, 0f, 1f);
        }

        public static void Reset()
        {
            _gravitationalConstant = 9.81f;
            _airResistance = 0f;
            _wallRestitution = 0.7f;
        }
    }

    public static class PhysicsMath
    {
        public static float SafeMass(float mass, float fallback = 1f) =>
            mass > 0f ? mass : fallback;

        public static float SafeInvMass(float mass, float fallback = 0f) =>
            mass > 0f ? 1f / mass : fallback;

        public static Vector2 ClampMagnitude(Vector2 v, float max)
        {
            float lenSq = v.LengthSquared();
            if (lenSq <= max * max)
                return v;
            float len = MathF.Sqrt(lenSq);
            return len > 1e-12f ? v * (max / len) : Vector2.Zero;
        }

        public static bool TryNormalize(Vector2 v, float eps, out Vector2 dir, out float len)
        {
            len = v.Length();
            dir = len > eps ? v / len : Vector2.Zero;
            return len > eps;
        }

        public static Vector2 Project(Vector2 v, Vector2 onto) => Vector2.Dot(v, onto) * onto;

        public static Vector2 HookeDampedForce(
            Vector2 delta,
            float restLen,
            float stiffness,
            Vector2 relVel,
            float damping
        )
        {
            if (!TryNormalize(delta, 1e-6f, out var dir, out var dist))
                return Vector2.Zero;
            return (-stiffness * (dist - restLen) - damping * Vector2.Dot(relVel, dir)) * dir;
        }

        public static Vector2 ApplyImpulse(
            Vector2 vel,
            Vector2 force,
            float mass,
            float dt,
            float damping,
            float maxSpeed
        )
        {
            return ClampMagnitude((vel + force * (dt / SafeMass(mass))) * damping, maxSpeed);
        }
    }

    public struct CollisionPair(Entity a, Entity b)
    {
        public Entity EntityA = a,
            EntityB = b;
    }

    public class CollisionInfo(
        Entity a,
        Entity b,
        Vector2 contactPoint,
        Vector2 penetrationDepth,
        Vector2 separationDirection
    )
    {
        public Entity EntityA = a,
            EntityB = b;
        public Vector2 ContactPoint = contactPoint,
            PenetrationDepth = penetrationDepth,
            SeparationDirection = separationDirection;

        public static bool AreColliding(Entity a, Entity b, out CollisionInfo info)
        {
            info = null!;
            if (a == null || b == null)
                return false;
            if (!a.GetAABB().Intersects(b.GetAABB()))
                return false;

            if (
                a.CollisionShape == Entity.CollisionShapeType.Circle
                && b.CollisionShape == Entity.CollisionShapeType.Circle
            )
                return TryCircleCollision(a, b, out info);
            return VertexCollisionSolver.Instance.TryFindContact(a, b, out info);
        }

        public static bool AreCirclesOverlapping(Entity a, Entity b)
        {
            if (
                a?.CollisionShape != Entity.CollisionShapeType.Circle
                || b?.CollisionShape != Entity.CollisionShapeType.Circle
            )
                return false;
            float rA =
                a.CollisionRadius > 0f ? a.CollisionRadius : MathF.Max(a.Size.X, a.Size.Y) * 0.5f;
            float rB =
                b.CollisionRadius > 0f ? b.CollisionRadius : MathF.Max(b.Size.X, b.Size.Y) * 0.5f;
            return AreCirclesOverlapping(a.Position, rA, b.Position, rB);
        }

        public static bool AreCirclesOverlapping(Vector2 posA, float rA, Vector2 posB, float rB) =>
            rA > 0f && rB > 0f && (posB - posA).LengthSquared() < (rA + rB) * (rA + rB);

        static bool TryCircleCollision(Entity a, Entity b, out CollisionInfo info)
        {
            info = null!;
            float rA =
                a.CollisionRadius > 0f ? a.CollisionRadius : MathF.Max(a.Size.X, a.Size.Y) * 0.5f;
            float rB =
                b.CollisionRadius > 0f ? b.CollisionRadius : MathF.Max(b.Size.X, b.Size.Y) * 0.5f;
            if (rA <= 0f || rB <= 0f)
                return false;

            Vector2 delta = b.Position - a.Position;
            float distSq = delta.LengthSquared(),
                radiusSum = rA + rB;
            if (distSq >= radiusSum * radiusSum)
                return false;

            const float eps = 1e-12f;
            Vector2 normal;
            float penDepth;
            if (distSq > eps)
            {
                float dist = MathF.Sqrt(distSq);
                normal = delta / dist;
                penDepth = radiusSum - dist;
            }
            else
            {
                normal = ResolveZeroDistNormal(a, b, delta);
                penDepth = radiusSum;
            }
            if (penDepth <= 0f)
                return false;

            Vector2 contact =
                distSq > eps
                    ? a.Position + normal * (rA - penDepth * 0.5f)
                    : a.Position + normal * rA;
            info = new CollisionInfo(a, b, contact, normal * penDepth, normal);
            return true;
        }

        static Vector2 ResolveZeroDistNormal(Entity a, Entity b, Vector2 delta)
        {
            const float eps = 1e-12f;
            float absDx = MathF.Abs(delta.X),
                absDy = MathF.Abs(delta.Y);
            if (absDx > absDy && absDx > eps)
                return new(delta.X > 0f ? 1f : -1f, 0f);
            if (absDy > eps)
                return new(0f, delta.Y > 0f ? 1f : -1f);
            var ocA = a.GetComponent<ObjectCollision>();
            var ocB = b.GetComponent<ObjectCollision>();
            Vector2 relVel = (ocB?.Velocity ?? Vector2.Zero) - (ocA?.Velocity ?? Vector2.Zero);
            float vx = MathF.Abs(relVel.X),
                vy = MathF.Abs(relVel.Y);
            if (vx > vy && vx > eps)
                return new(relVel.X > 0f ? 1f : -1f, 0f);
            if (vy > eps)
                return new(0f, relVel.Y > 0f ? 1f : -1f);
            return Vector2.UnitY;
        }
    }

    public static class PhysicsSolver
    {
        private const float PenetrationSlop = 0.001f;
        private const float PositionalCorrectionPercent = 0.4f;
        private const float MaxPenetrationCorrection = 0.01f;
        private const float BaumgarteScalar = 0.08f;
        private const float RestitutionVelocityThreshold = 0.2f;
        private const float StaticToDynamicFrictionRatio = 0.8f;
        private const float GroundNormalThreshold = 0.7f;
        private const float RestingRelativeVelocityThreshold = 0.15f;
        private const float MassIgnoreRatio = 1000f;
        private const float MaxLinearVelocity = 15f;

        public static void ResolveCollision(CollisionInfo collision, float deltaTime)
        {
            if (collision == null)
                return;

            var entityA = collision.EntityA;
            var entityB = collision.EntityB;
            if (entityA == null || entityB == null)
                return;

            var objA = entityA.GetComponent<ObjectCollision>();
            var objB = entityB.GetComponent<ObjectCollision>();

            bool isFluidCollision =
                (objA != null && objA.IsFluid) || (objB != null && objB.IsFluid);

            float invMassA =
                (objA == null || objA.IsStatic || objA.Mass <= 0f) ? 0f : 1f / objA.Mass;
            float invMassB =
                (objB == null || objB.IsStatic || objB.Mass <= 0f) ? 0f : 1f / objB.Mass;

            float massAVal =
                (objA == null || objA.IsStatic || objA.Mass <= 0f)
                    ? float.PositiveInfinity
                    : objA.Mass;
            float massBVal =
                (objB == null || objB.IsStatic || objB.Mass <= 0f)
                    ? float.PositiveInfinity
                    : objB.Mass;

            float invMassSum = invMassA + invMassB;
            if (invMassSum <= 1e-8f)
                return;

            Vector2 mtv = collision.PenetrationDepth;
            float mtvLenSq = mtv.LengthSquared();
            if (mtvLenSq <= 1e-12f)
                return;

            float mtvLen = MathF.Sqrt(mtvLenSq);
            Vector2 normal = mtv / mtvLen;

            deltaTime = MathF.Max(deltaTime, 1e-5f);

            float slop = isFluidCollision ? 0f : PenetrationSlop;
            float penetrationDepth = MathF.Max(mtvLen - slop, 0f);
            if (penetrationDepth > 0f)
            {
                float correctionPercent = isFluidCollision ? 1.0f : PositionalCorrectionPercent;
                float maxCorrection = isFluidCollision ? 1.0f : MaxPenetrationCorrection;

                float correctionMag = MathF.Min(
                    penetrationDepth * correctionPercent,
                    maxCorrection
                );
                Vector2 correction = (correctionMag / invMassSum) * normal;

                if (isFluidCollision)
                {
                    float totalRadius = (entityA.CollisionRadius + entityB.CollisionRadius);
                    Vector2 delta = entityB.Position - entityA.Position;
                    float dist = delta.Length();

                    if (dist < totalRadius)
                    {
                        if (dist < 0.0001f)
                        {
                            if (objA != null && invMassA > 0f && !objA.IsStatic)
                                entityA.Position -= new Vector2(totalRadius * 0.5f, 0f);
                            if (objB != null && invMassB > 0f && !objB.IsStatic)
                                entityB.Position += new Vector2(totalRadius * 0.5f, 0f);
                        }
                        else
                        {
                            Vector2 separationDir = delta / dist;
                            float overlap = totalRadius - dist;

                            bool aMovable = objA != null && invMassA > 0f && !objA.IsStatic;
                            bool bMovable = objB != null && invMassB > 0f && !objB.IsStatic;

                            bool aGrounded = objA != null && objA.IsGrounded;
                            bool bGrounded = objB != null && objB.IsGrounded;

                            if (aMovable && bMovable)
                            {
                                if (aGrounded && !bGrounded)
                                {
                                    entityB.Position =
                                        entityA.Position + separationDir * totalRadius;
                                }
                                else if (bGrounded && !aGrounded)
                                {
                                    entityA.Position =
                                        entityB.Position - separationDir * totalRadius;
                                }
                                else
                                {
                                    Vector2 midpoint = (entityA.Position + entityB.Position) * 0.5f;
                                    entityA.Position =
                                        midpoint - separationDir * (totalRadius * 0.5f);
                                    entityB.Position =
                                        midpoint + separationDir * (totalRadius * 0.5f);
                                }
                            }
                            else if (aMovable)
                            {
                                entityA.Position = entityB.Position - separationDir * totalRadius;
                            }
                            else if (bMovable)
                            {
                                entityB.Position = entityA.Position + separationDir * totalRadius;
                            }
                        }
                    }
                }
                else
                {
                    if (objA != null && invMassA > 0f)
                        entityA.Position -= correction * invMassA;
                    if (objB != null && invMassB > 0f)
                        entityB.Position += correction * invMassB;
                }
            }

            Vector2 velocityA = Vector2.Zero;
            if (objA != null)
                velocityA = objA.Velocity;

            Vector2 velocityB = Vector2.Zero;
            if (objB != null)
                velocityB = objB.Velocity;
            Vector2 relativeVelocity = velocityB - velocityA;
            float relVelN = Vector2.Dot(relativeVelocity, normal);

            float restitutionCandidateA = 0f;
            if (objA != null)
                restitutionCandidateA = objA.Restitution;

            float restitutionCandidateB = 0f;
            if (objB != null)
                restitutionCandidateB = objB.Restitution;

            float restitution = Math.Clamp(
                MathF.Min(restitutionCandidateA, restitutionCandidateB),
                0f,
                1f
            );

            if (MathF.Abs(relVelN) < RestitutionVelocityThreshold)
                restitution = 0f;

            if (isFluidCollision)
                restitution = 0f;

            float bias = 0f;
            if (penetrationDepth > PenetrationSlop)
            {
                if (!isFluidCollision)
                {
                    bias = BaumgarteScalar * penetrationDepth / deltaTime;
                }
            }

            bool isFluidFluid = (objA != null && objA.IsFluid) && (objB != null && objB.IsFluid);
            if (isFluidFluid)
            {
                return;
            }

            if (isFluidCollision)
            {
                if (objA != null && objA.IsFluid && invMassA > 0f)
                {
                    float velAlongNormal = Vector2.Dot(objA.Velocity, normal);
                    if (velAlongNormal < 0f)
                        objA.Velocity -= normal * velAlongNormal;
                }
                if (objB != null && objB.IsFluid && invMassB > 0f)
                {
                    float velAlongNormal = Vector2.Dot(objB.Velocity, normal);
                    if (velAlongNormal > 0f)
                        objB.Velocity -= normal * velAlongNormal;
                }
                return;
            }

            float normalImpulseScalar = (-(1f + restitution) * relVelN + bias) / invMassSum;
            if (normalImpulseScalar < 0f)
                normalImpulseScalar = 0f;

            Vector2 normalImpulse = normalImpulseScalar * normal;

            if (objA != null && invMassA > 0f)
                objA.Velocity -= normalImpulse * invMassA;
            if (objB != null && invMassB > 0f)
                objB.Velocity += normalImpulse * invMassB;

            velocityA = Vector2.Zero;
            if (objA != null)
                velocityA = objA.Velocity;

            velocityB = Vector2.Zero;
            if (objB != null)
                velocityB = objB.Velocity;
            relativeVelocity = velocityB - velocityA;

            Vector2 tangent = relativeVelocity - PhysicsMath.Project(relativeVelocity, normal);
            if (PhysicsMath.TryNormalize(tangent, 1e-12f, out tangent, out _))
            {
                float muA = 0.5f;
                if (objA != null)
                    muA = objA.Friction;

                float muB = 0.5f;
                if (objB != null)
                    muB = objB.Friction;
                float staticFriction = MathF.Sqrt(MathF.Max(muA * muB, 0f));
                float dynamicFriction = staticFriction * StaticToDynamicFrictionRatio;

                float relVelT = Vector2.Dot(relativeVelocity, tangent);
                float tangentImpulseScalar = -relVelT / invMassSum;

                float maxStaticImpulse = staticFriction * normalImpulseScalar;
                Vector2 frictionImpulse;
                if (MathF.Abs(tangentImpulseScalar) <= maxStaticImpulse)
                {
                    frictionImpulse = tangentImpulseScalar * tangent;
                }
                else
                {
                    float direction = MathF.Sign(tangentImpulseScalar);
                    frictionImpulse = -direction * dynamicFriction * normalImpulseScalar * tangent;
                }

                if (objA != null && invMassA > 0f)
                    objA.Velocity -= frictionImpulse * invMassA;
                if (objB != null && invMassB > 0f)
                    objB.Velocity += frictionImpulse * invMassB;

                if (
                    entityA.CollisionShape == Entity.CollisionShapeType.Circle
                    && objA != null
                    && invMassA > 0f
                )
                {
                    float radiusA =
                        entityA.CollisionRadius > 0f
                            ? entityA.CollisionRadius
                            : MathF.Max(entityA.Size.X, entityA.Size.Y) * 0.5f;
                    float momentOfInertia = 0.5f * objA.Mass * radiusA * radiusA;
                    if (momentOfInertia > 1e-8f)
                    {
                        float angularImpulse =
                            -radiusA * Vector2.Dot(frictionImpulse, tangent) / momentOfInertia;
                        objA.AngularVelocity += angularImpulse;
                    }
                }

                if (
                    entityB.CollisionShape == Entity.CollisionShapeType.Circle
                    && objB != null
                    && invMassB > 0f
                )
                {
                    float radiusB =
                        entityB.CollisionRadius > 0f
                            ? entityB.CollisionRadius
                            : MathF.Max(entityB.Size.X, entityB.Size.Y) * 0.5f;
                    float momentOfInertia = 0.5f * objB.Mass * radiusB * radiusB;
                    if (momentOfInertia > 1e-8f)
                    {
                        float angularImpulse =
                            radiusB * Vector2.Dot(frictionImpulse, tangent) / momentOfInertia;
                        objB.AngularVelocity += angularImpulse;
                    }
                }
            }

            velocityA = Vector2.Zero;
            if (objA != null)
                velocityA = objA.Velocity;

            velocityB = Vector2.Zero;
            if (objB != null)
                velocityB = objB.Velocity;
            relVelN = Vector2.Dot(velocityB - velocityA, normal);

            if (objA != null)
                objA.Velocity = ClampVelocity(objA.Velocity);
            if (objB != null)
                objB.Velocity = ClampVelocity(objB.Velocity);

            if (normal.Y > GroundNormalThreshold)
            {
                if (objA != null && MathF.Abs(relVelN) < RestingRelativeVelocityThreshold)
                {
                    objA.IsGrounded = true;
                    if (objA.Velocity.Y > 0f)
                        objA.Velocity = new Vector2(objA.Velocity.X, 0f);
                }
            }
            else if (normal.Y < -GroundNormalThreshold)
            {
                if (objB != null && MathF.Abs(relVelN) < RestingRelativeVelocityThreshold)
                {
                    objB.IsGrounded = true;
                    if (objB.Velocity.Y > 0f)
                        objB.Velocity = new Vector2(objB.Velocity.X, 0f);
                }
            }
        }

        private static Vector2 ClampVelocity(Vector2 velocity) =>
            PhysicsMath.ClampMagnitude(velocity, MaxLinearVelocity);
    }

    public sealed class VertexCollisionSolver
    {
        private const float AxisEpsilon = 1e-6f;
        public static VertexCollisionSolver Instance { get; } = new VertexCollisionSolver();

        private VertexCollisionSolver() { }

        public readonly struct SetAxis
        {
            public readonly Vector2 Normal;
            public readonly int Source;
            public readonly int EdgeIndex;

            public SetAxis(Vector2 normal, int source, int edgeIndex)
            {
                Normal = normal;
                Source = source;
                EdgeIndex = edgeIndex;
            }

            public override string ToString() =>
                $"n={Normal}, src={(Source == 0 ? "A" : "B")}, edge={EdgeIndex}";
        }

        private static void BuildAxes(
            ReadOnlySpan<Vector2> VertsA,
            ReadOnlySpan<Vector2> VertsB,
            System.Collections.Generic.List<SetAxis> axes
        )
        {
            axes.Clear();
            AddAxesFromPolygon(VertsA, 0, axes);
            AddAxesFromPolygon(VertsB, 1, axes);
        }

        private static void AddAxesFromPolygon(
            ReadOnlySpan<Vector2> Vertices,
            int source,
            System.Collections.Generic.List<SetAxis> axes
        )
        {
            int n = Vertices.Length;
            if (n < 2)
                return;

            for (int i = 0; i < n; i++)
            {
                var a = Vertices[i];
                var b = Vertices[(i + 1) % n];
                var edge = b - a;
                if (edge.LengthSquared() < AxisEpsilon * AxisEpsilon)
                    continue;

                var Normal = new Vector2(-edge.Y, edge.X);
                float Len = Normal.Length();
                if (Len < AxisEpsilon)
                    continue;

                Normal /= Len;
                axes.Add(new SetAxis(Normal, source, i));
            }
        }

        private static void DedupeAxes(System.Collections.Generic.List<SetAxis> axis)
        {
            if (axis == null || axis.Count <= 1)
                return;

            var outList = new System.Collections.Generic.List<SetAxis>(axis.Count);
            const float parallelDot = 0.9995f; // treat near-parallel (including opposite) as duplicate

            for (int i = 0; i < axis.Count; i++)
            {
                var a = axis[i];
                var n = a.Normal;

                // normalize to be safe
                float len = n.Length();
                if (len <= AxisEpsilon)
                    continue;
                n /= len;

                // Canonicalize hemisphere so n and -n dedupe together
                if (n.Y < 0f || (System.MathF.Abs(n.Y) <= AxisEpsilon && n.X < 0f))
                    n = -n;

                bool keep = true;
                for (int j = 0; j < outList.Count; j++)
                {
                    float dot = Vector2.Dot(n, outList[j].Normal);
                    if (System.MathF.Abs(dot) > parallelDot)
                    {
                        keep = false;
                        break;
                    }
                }

                if (keep)
                    outList.Add(new SetAxis(n, a.Source, a.EdgeIndex));
            }

            axis.Clear();
            axis.AddRange(outList);
        }

        public bool TryFindContact(Entity entityA, Entity entityB, out CollisionInfo collision)
        {
            collision = null!;
            if (entityA == null || entityB == null)
                return false;

            var rawA = entityA.GetVertices();
            var rawB = entityB.GetVertices();
            if (rawA == null || rawB == null)
                return false;
            if (rawA.Length < 2 || rawB.Length < 2)
                return false;

            var worldA = new Vector2[rawA.Length];
            var worldB = new Vector2[rawB.Length];
            int countA = CopyWorldSpaceVertices(entityA, worldA.AsSpan());
            int countB = CopyWorldSpaceVertices(entityB, worldB.AsSpan());
            if (countA < 2 || countB < 2)
                return false;

            if (countA != worldA.Length)
            {
                var tmp = new Vector2[countA];
                System.Array.Copy(worldA, tmp, countA);
                worldA = tmp;
            }
            if (countB != worldB.Length)
            {
                var tmp = new Vector2[countB];
                System.Array.Copy(worldB, tmp, countB);
                worldB = tmp;
            }

            var axes = new System.Collections.Generic.List<SetAxis>(worldA.Length + worldB.Length);
            BuildAxes(worldA, worldB, axes);
            DedupeAxes(axes);
            if (axes.Count == 0)
                return false;

            float minOverlap = float.MaxValue;
            Vector2 minAxis = Vector2.Zero;
            int minAxisSource = -1;
            int minAxisEdge = -1;

            foreach (var a in axes)
            {
                var axis = a.Normal;

                float minA = float.MaxValue;
                float maxA = float.MinValue;
                for (int i = 0; i < worldA.Length; i++)
                {
                    float p = Vector2.Dot(worldA[i], axis);
                    if (p < minA)
                        minA = p;
                    if (p > maxA)
                        maxA = p;
                }

                float minB = float.MaxValue;
                float maxB = float.MinValue;
                for (int i = 0; i < worldB.Length; i++)
                {
                    float p = Vector2.Dot(worldB[i], axis);
                    if (p < minB)
                        minB = p;
                    if (p > maxB)
                        maxB = p;
                }

                float overlap = System.MathF.Min(maxA, maxB) - System.MathF.Max(minA, minB);
                if (overlap <= 0f)
                {
                    return false;
                }

                if (overlap < minOverlap)
                {
                    minOverlap = overlap;
                    minAxis = axis;
                    minAxisSource = a.Source;
                    minAxisEdge = a.EdgeIndex;
                }
            }

            if (minOverlap == float.MaxValue)
                return false;

            Vector2 centerA = Vector2.Zero;
            for (int i = 0; i < worldA.Length; i++)
                centerA += worldA[i];
            centerA /= worldA.Length;
            Vector2 centerB = Vector2.Zero;
            for (int i = 0; i < worldB.Length; i++)
                centerB += worldB[i];
            centerB /= worldB.Length;

            Vector2 normal = minAxis;
            if (Vector2.Dot(centerB - centerA, normal) < 0f)
                normal = -normal;

            Vector2 supportA = worldA[0];
            float bestA = Vector2.Dot(supportA, normal);
            for (int i = 1; i < worldA.Length; i++)
            {
                float p = Vector2.Dot(worldA[i], normal);
                if (p > bestA)
                {
                    bestA = p;
                    supportA = worldA[i];
                }
            }

            Vector2 supportB = worldB[0];
            float bestB = Vector2.Dot(supportB, normal);
            for (int i = 1; i < worldB.Length; i++)
            {
                float p = Vector2.Dot(worldB[i], normal);
                if (p < bestB)
                {
                    bestB = p;
                    supportB = worldB[i];
                }
            }

            Vector2 contactPoint = (supportA + supportB) * 0.5f;
            Vector2 penetrationVec = normal * minOverlap;

            collision = new CollisionInfo(entityA, entityB, contactPoint, penetrationVec, normal);
            return true;
        }

        public int CopyWorldSpaceVertices(Entity entity, Span<Vector2> destination)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var vertices = entity.GetVertices();
            int count = Math.Min(vertices.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] = new Vector2(vertices[i].X, vertices[i].Y) + entity.Position;
            }

            return count;
        }
    }
}

namespace Engine13.Utilities.Attributes
{
    public sealed class ObjectCollision : IEntityComponent
    {
        public float Mass { get; set; } = 1f;
        public float Restitution { get; set; } = 0.8f;
        public float Friction { get; set; } = 0.5f;
        public Vector2 Velocity { get; set; } = Vector2.Zero;
        public float AngularVelocity { get; set; }
        public bool IsStatic { get; set; }
        public bool IsGrounded { get; set; }
        public bool IsFluid { get; set; }
        public bool UseSPHIntegration { get; set; }

        public void Update(Entity entity, GameTime gameTime)
        {
            if (IsStatic)
                return;
            if (!IsFluid && Velocity.LengthSquared() > 0.0001f)
                Velocity *= 0.999f;
            float spd = Velocity.Length();
            if (IsFluid)
            {
                if (spd < 0.2f && spd > 0.001f)
                    Velocity *= 0.97f;
                else if (spd <= 0.001f)
                    Velocity = Vector2.Zero;
            }
            if (spd > 15f)
                Velocity *= 15f / spd;
            entity.Position += Velocity * gameTime.DeltaTime;
            entity.Rotation += AngularVelocity * gameTime.DeltaTime;
            if (IsGrounded && MathF.Abs(AngularVelocity) > 0.01f)
                AngularVelocity *= 0.98f;
        }
    }
}
