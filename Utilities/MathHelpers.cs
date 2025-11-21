using System;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;
using Veldrid.Sdl2;

namespace Engine13.Utilities
{
    public static class WindowBounds
    {
        private static Sdl2Window? _window;
        
        public static void SetWindow(Sdl2Window window)
        {
            _window = window;
        }
        
        public static (float left, float right, float top, float bottom) GetNormalizedBounds()
        {
            if (_window == null)
            {
                // Fallback to default bounds if no window is set
                return (-1f, 1f, -1f, 1f);
            }
            
            // Convert pixel coordinates to normalized world coordinates
            // Assuming typical 2D projection where screen goes from -1 to 1
            float aspectRatio = (float)_window.Width / _window.Height;
            
            return (-aspectRatio, aspectRatio, -1f, 1f);
        }
        
        public static Vector2 GetWindowSize()
        {
            if (_window == null)
                return new Vector2(800, 600); // Fallback size
                
            return new Vector2(_window.Width, _window.Height);
        }
    }

    public struct AABB
    {
        public Vector2 Min;
        public Vector2 Max;
        public Vector2 Center;
        public Vector2 Size;

        public AABB(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
            Center = (Min + Max) / 2f;
            Size = Max - Min;
        }

        public bool Intersects(AABB other)
        {
            return (Min.X <= other.Max.X && Max.X >= other.Min.X)
                && (Min.Y <= other.Max.Y && Max.Y >= other.Min.Y);
        }

        public static bool OverlapDepth(AABB a, AABB b, out Vector2 depth)
        {
            depth = Vector2.Zero;

            if (!a.Intersects(b))
                return false;

            float dx1 = b.Max.X - a.Min.X;
            float dx2 = a.Max.X - b.Min.X;
            float dy1 = b.Max.Y - a.Min.Y;
            float dy2 = a.Max.Y - b.Min.Y;

            // Find the smallest overlap on each axis
            float overlapX = (dx1 < dx2) ? dx1 : -dx2;
            float overlapY = (dy1 < dy2) ? dy1 : -dy2;

            if (System.MathF.Abs(overlapX) < System.MathF.Abs(overlapY))
                depth = new Vector2(overlapX, 0);
            else
                depth = new Vector2(0, overlapY);

            return true;
        }
    }

    public struct CollisionPair
    {
        public Entity EntityA;
        public Entity EntityB;

        public CollisionPair(Entity a, Entity b)
        {
            EntityA = a;
            EntityB = b;
        }
    }

    public class CollisionInfo
    {
        public Entity EntityA;
        public Entity EntityB;
        public Vector2 ContactPoint;
        public Vector2 PenetrationDepth;
        public Vector2 SeparationDirection;

        public CollisionInfo(
            Entity a,
            Entity b,
            Vector2 contactPoint,
            Vector2 penetrationDepth,
            Vector2 separationDirection
        )
        {
            EntityA = a;
            EntityB = b;
            ContactPoint = contactPoint;
            PenetrationDepth = penetrationDepth;
            SeparationDirection = separationDirection;
        }

        public static bool AreColliding(Entity a, Entity b, out CollisionInfo collisionInfo)
        {
            collisionInfo = null!;
            if (a == null || b == null)
                return false;

            var aabbA = a.GetAABB();
            var aabbB = b.GetAABB();
            if (!aabbA.Intersects(aabbB))
                return false;

            if (
                a.CollisionShape == Entity.CollisionShapeType.Circle
                && b.CollisionShape == Entity.CollisionShapeType.Circle
            )
            {
                return TryCircleCollision(a, b, out collisionInfo);
            }

            return VertexCollisionSolver.Instance.TryFindContact(a, b, out collisionInfo);
        }

        public static bool AreCirclesOverlapping(Entity a, Entity b)
        {
            if (a == null || b == null)
                return false;

           
            if (
                a.CollisionShape != Entity.CollisionShapeType.Circle
                || b.CollisionShape != Entity.CollisionShapeType.Circle
            )
                return false;

            
            float radiusA =
                a.CollisionRadius > 0f ? a.CollisionRadius : MathF.Max(a.Size.X, a.Size.Y) * 0.5f;
            float radiusB =
                b.CollisionRadius > 0f ? b.CollisionRadius : MathF.Max(b.Size.X, b.Size.Y) * 0.5f;
            
            if (radiusA <= 0f || radiusB <= 0f)
                return false;

            
            Vector2 delta = b.Position - a.Position;
            float distanceSq = delta.LengthSquared();
            float radiusSum = radiusA + radiusB;
            float radiusSumSq = radiusSum * radiusSum;

            return distanceSq < radiusSumSq;
        }

        /// <summary>
        /// Even more basic circle overlap - just takes positions and radii directly
        /// Fastest possible circle collision check
        /// </summary>
        public static bool AreCirclesOverlapping(
            Vector2 posA,
            float radiusA,
            Vector2 posB,
            float radiusB
        )
        {
            if (radiusA <= 0f || radiusB <= 0f)
                return false;

            Vector2 delta = posB - posA;
            float distanceSq = delta.LengthSquared();
            float radiusSum = radiusA + radiusB;
            float radiusSumSq = radiusSum * radiusSum;

            return distanceSq < radiusSumSq;
        }

        private static bool TryCircleCollision(Entity a, Entity b, out CollisionInfo collisionInfo)
        {
            collisionInfo = null!;

            float radiusA =
                a.CollisionRadius > 0f ? a.CollisionRadius : MathF.Max(a.Size.X, a.Size.Y) * 0.5f;
            float radiusB =
                b.CollisionRadius > 0f ? b.CollisionRadius : MathF.Max(b.Size.X, b.Size.Y) * 0.5f;
            if (!(radiusA > 0f) || !(radiusB > 0f))
                return false;

            Vector2 centerA = a.Position;
            Vector2 centerB = b.Position;
            Vector2 delta = centerB - centerA;
            float distanceSq = delta.LengthSquared();
            float radiusSum = radiusA + radiusB;
            float radiusSumSq = radiusSum * radiusSum;
            if (distanceSq >= radiusSumSq)
                return false;

            const float Epsilon = 1e-12f;
            Vector2 normal;
            float penetrationDepth;

            if (distanceSq > Epsilon)
            {
                float distance = MathF.Sqrt(distanceSq);
                normal = delta / distance;
                penetrationDepth = radiusSum - distance;
            }
            else
            {
                normal = Vector2.Zero;

                float absDx = MathF.Abs(delta.X);
                float absDy = MathF.Abs(delta.Y);
                if (absDx > absDy && absDx > Epsilon)
                {
                    normal = new Vector2(delta.X > 0f ? 1f : -1f, 0f);
                }
                else if (absDy > Epsilon)
                {
                    normal = new Vector2(0f, delta.Y > 0f ? 1f : -1f);
                }

                if (normal == Vector2.Zero)
                {
                    var attrA = a.GetComponent<ObjectCollision>();
                    var attrB = b.GetComponent<ObjectCollision>();
                    Vector2 relVel =
                        (attrB?.Velocity ?? Vector2.Zero) - (attrA?.Velocity ?? Vector2.Zero);
                    float absVx = MathF.Abs(relVel.X);
                    float absVy = MathF.Abs(relVel.Y);
                    if (absVx > absVy && absVx > Epsilon)
                    {
                        normal = new Vector2(relVel.X > 0f ? 1f : -1f, 0f);
                    }
                    else if (absVy > Epsilon)
                    {
                        normal = new Vector2(0f, relVel.Y > 0f ? 1f : -1f);
                    }
                }

                if (normal == Vector2.Zero)
                    normal = Vector2.UnitY;

                if (normal.LengthSquared() > Epsilon)
                    normal = Vector2.Normalize(normal);
                else
                    normal = Vector2.UnitY;

                penetrationDepth = radiusSum;
            }

            if (penetrationDepth <= 0f)
                return false;

            Vector2 penetrationVec = normal * penetrationDepth;
            Vector2 contactPoint =
                distanceSq > Epsilon
                    ? centerA + normal * (radiusA - penetrationDepth * 0.5f)
                    : centerA + normal * radiusA;

            collisionInfo = new CollisionInfo(a, b, contactPoint, penetrationVec, normal);
            return true;
        }
    }

    public static class PhysicsSolver
    {
        private const float PenetrationSlop = 0.0015f;
        private const float PositionalCorrectionPercent = 0.6f;
        private const float MaxPositionalCorrectionSpeed = 2.0f;
        private const float BaumgarteScalar = 0.25f;
        private const float RestitutionVelocityThreshold = 0.15f;
        private const float StaticToDynamicFrictionRatio = 0.8f;
        private const float GroundNormalThreshold = 0.6f;
        private const float RestingRelativeVelocityThreshold = 0.4f;
        private const float MassIgnoreRatio = 1000f;

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

            float invMassA =
                (objA == null || objA.IsStatic || objA.Mass <= 0f) ? 0f : 1f / objA.Mass;
            float invMassB =
                (objB == null || objB.IsStatic || objB.Mass <= 0f) ? 0f : 1f / objB.Mass;

            float massAVal = (objA == null || objA.IsStatic || objA.Mass <= 0f) ? float.PositiveInfinity : objA.Mass;
            float massBVal = (objB == null || objB.IsStatic || objB.Mass <= 0f) ? float.PositiveInfinity : objB.Mass;

            if (massAVal < float.PositiveInfinity && massBVal < float.PositiveInfinity)
            {
                if (massAVal > massBVal * MassIgnoreRatio)
                {
                    invMassA = 0f;
                }
                else if (massBVal > massAVal * MassIgnoreRatio)
                {
                    invMassB = 0f;
                }
            }

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

            float penetrationDepth = MathF.Max(mtvLen - PenetrationSlop, 0f);
            if (penetrationDepth > 0f)
            {
                float correctionSpeed = MathF.Min(
                    (penetrationDepth / deltaTime) * PositionalCorrectionPercent,
                    MaxPositionalCorrectionSpeed
                );
                float correctionMag = correctionSpeed * deltaTime;
                Vector2 correction = (correctionMag / invMassSum) * normal;
                if (objA != null && invMassA > 0f)
                    entityA.Position -= correction * invMassA;
                if (objB != null && invMassB > 0f)
                    entityB.Position += correction * invMassB;
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
                MathF.Max(restitutionCandidateA, restitutionCandidateB),
                0f,
                1f
            );
            if (MathF.Abs(relVelN) < RestitutionVelocityThreshold)
                restitution = 0f;

            float bias = 0f;
            if (penetrationDepth > 0f)
            {
                bias = MathF.Min(
                    BaumgarteScalar * penetrationDepth / deltaTime,
                    MaxPositionalCorrectionSpeed
                );
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

            Vector2 tangent = relativeVelocity - (Vector2.Dot(relativeVelocity, normal) * normal);
            float tangentLenSq = tangent.LengthSquared();
            if (tangentLenSq > 1e-12f)
            {
                tangent /= MathF.Sqrt(tangentLenSq);
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
            }

            velocityA = Vector2.Zero;
            if (objA != null)
                velocityA = objA.Velocity;

            velocityB = Vector2.Zero;
            if (objB != null)
                velocityB = objB.Velocity;
            relVelN = Vector2.Dot(velocityB - velocityA, normal);

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
    }

        public class SpatialGrid
    {
        private float cellSize;

        private System.Collections.Generic.Dictionary<
            (int, int),
            System.Collections.Generic.List<Entity>
        > cells;
        private readonly System.Collections.Generic.Dictionary<
            Entity,
            System.Collections.Generic.HashSet<(int, int)>
        > entityCells = new System.Collections.Generic.Dictionary<
            Entity,
            System.Collections.Generic.HashSet<(int, int)>
        >();

        public SpatialGrid(float cellSize)
        {
            this.cellSize = cellSize;
            cells = new System.Collections.Generic.Dictionary<
                (int, int),
                System.Collections.Generic.List<Entity>
            >();
        }

        public (int, int) GetCellCoords(Vector2 position)
        {
            int x = (int)System.Math.Floor(position.X / cellSize);
            int y = (int)System.Math.Floor(position.Y / cellSize);
            return (x, y);
        }

        public void AddEntity(Entity entity)
        {
            var aabb = entity.GetAABB();
            var minCell = GetCellCoords(aabb.Min);
            var maxCell = GetCellCoords(aabb.Max);

            if (!entityCells.TryGetValue(entity, out var occupied))
            {
                occupied = new System.Collections.Generic.HashSet<(int, int)>();
                entityCells[entity] = occupied;
            }

            for (int x = minCell.Item1; x <= maxCell.Item1; x++)
            {
                for (int y = minCell.Item2; y <= maxCell.Item2; y++)
                {
                    var cell = (x, y);
                    if (!cells.TryGetValue(cell, out var list))
                    {
                        list = new System.Collections.Generic.List<Entity>();
                        cells[cell] = list;
                    }
                    if (!list.Contains(entity))
                    {
                        list.Add(entity);
                    }
                    occupied.Add(cell);
                }
            }
        }

        private void RemoveEntity(Entity entity)
        {
            if (entityCells.TryGetValue(entity, out var OccupiedCells))
            {
                foreach (var cellCoords in OccupiedCells)
                {
                    if (cells.ContainsKey(cellCoords))
                    {
                        cells[cellCoords].Remove(entity);
                        if (cells[cellCoords].Count == 0)
                        {
                            cells.Remove(cellCoords);
                        }
                    }
                }
                entityCells.Remove(entity);
            }
        }

        public void UpdateEntityPosition(Entity entity)
        {
            RemoveEntity(entity);
            AddEntity(entity);
        }

        public void UpdateAllAabb(System.Collections.Generic.IEnumerable<Entity> entities)
        {
            foreach (var e in entities)
            {
                UpdateEntityPosition(e);
            }
        }

        public System.Collections.Generic.List<Entity> GetNearbyEntities(Vector2 position)
        {
            var cellCoords = GetCellCoords(position);
            var nearbyEntities = new System.Collections.Generic.List<Entity>();

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var neighborCoords = (cellCoords.Item1 + dx, cellCoords.Item2 + dy);
                    if (cells.ContainsKey(neighborCoords))
                    {
                        nearbyEntities.AddRange(cells[neighborCoords]);
                    }
                }
            }

            return nearbyEntities;
        }

        public AABB GetCellRange(AABB aabb)
        {
            var minCell = GetCellCoords(aabb.Min);
            var maxCell = GetCellCoords(aabb.Max);
            return new AABB(
                new Vector2(minCell.Item1, minCell.Item2),
                new Vector2(maxCell.Item1, maxCell.Item2)
            );
        }

        public System.Collections.Generic.HashSet<(int, int)> GetOccupiedCells(Entity entity)
        {
            return entityCells.TryGetValue(entity, out var cells)
                ? cells
                : new System.Collections.Generic.HashSet<(int, int)>();
        }

        public System.Collections.Generic.List<CollisionPair> GetCollisionPairs()
        {
            var pairs = new System.Collections.Generic.List<CollisionPair>();
            var processedPairs = new System.Collections.Generic.HashSet<(Entity, Entity)>();

            foreach (var cell in cells.Values)
            {
                for (int i = 0; i < cell.Count; i++)
                {
                    for (int j = i + 1; j < cell.Count; j++)
                    {
                        var entityA = cell[i];
                        var entityB = cell[j];

                        var pair =
                            entityA.GetHashCode() < entityB.GetHashCode()
                                ? (entityA, entityB)
                                : (entityB, entityA);
                        if (processedPairs.Add(pair))
                        {
                            pairs.Add(new CollisionPair(pair.Item1, pair.Item2));
                        }
                    }
                }
            }

            return pairs;
        }

        public void Clear()
        {
            cells.Clear();
            entityCells.Clear();
        }

        [Obsolete("Use AddEntity instead")]
        public void AddMesh(Entity entity) => AddEntity(entity);

        [Obsolete("Use UpdateEntityPosition instead")]
        public void UpdateMeshPosition(Entity entity) => UpdateEntityPosition(entity);

        [Obsolete("Use GetNearbyEntities instead")]
        public System.Collections.Generic.List<Entity> GetNearbyMeshes(Vector2 position) => GetNearbyEntities(position);
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

    public static class MathHelpers
    {
        public static float ComputeTerminalVelocityMag(float mass, float acceleration, float dragCoefficient)
        {
            if (dragCoefficient <= 0f || acceleration == 0f)
                return float.PositiveInfinity;
            return MathF.Sqrt(MathF.Abs(mass * acceleration) / dragCoefficient);
        }

        public static float ComputeArea(Vector2 size)
        {
            return (size.X > 0f && size.Y > 0f) ? (size.X * size.Y) : 1f;
        }

        public static float ComputeStepDelta(float targetFps)
        {
            return 1f / MathF.Max(targetFps, 1f);
        }

        public static Vector2[] CapturePositions(System.Collections.Generic.IReadOnlyList<Entity> entities)
        {
            var snapshot = new Vector2[entities.Count];
            for (int i = 0; i < entities.Count; i++)
            {
                snapshot[i] = entities[i].Position;
            }
            return snapshot;
        }

        public static void ApplyPositionsToEntities(System.Collections.Generic.IReadOnlyList<Entity> entities, Vector2[] positions)
        {
            if (positions == null) return;
            int count = Math.Min(entities.Count, positions.Length);
            for (int i = 0; i < count; i++)
            {
                entities[i].Position = positions[i];
            }
        }

        [Obsolete("Use ApplyPositionsToEntities instead")]
        public static void ApplyPositionsToMeshes(System.Collections.Generic.IReadOnlyList<Entity> entities, Vector2[] positions) 
            => ApplyPositionsToEntities(entities, positions);

        public static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int r = index % count;
            if (r < 0)
            {
                r += count;
            }

            return r;
        }
    }

    struct Frame
    {
        public Vector2[] Positions;
        public float TimeStamp;
        public int FrameIndex;

        public Vector2[] Velocities;
        public float[] Rotations;
        public bool isValid;

        public Frame(int MeshCount)
        {
            Positions = new Vector2[MeshCount];
            Velocities = new Vector2[MeshCount];
            Rotations = new float[MeshCount];
            TimeStamp = 0f;
            FrameIndex = 0;
            isValid = false;
        }

        public void CaptureFrom(List<Entity> entities)
        {
            int Count = Math.Min(entities.Count, Positions.Length);

            for (int i = 0; i < Count; i++)
            {
                Positions[i] = entities[i].Position;
                Velocities[i] = entities[i].GetComponent<ObjectCollision>()?.Velocity ?? Vector2.Zero;
            }

            isValid = true;
        }

        public bool ApplyTo(List<Entity> entities)
        {
            if (!isValid)
                return false;

            int Count = Math.Min(entities.Count, Positions.Length);

            for (int i = 0; i < Count; i++)
            {
                entities[i].Position = Positions[i];
                var objCollision = entities[i].GetComponent<ObjectCollision>();
                if (objCollision != null)
                {
                    objCollision.Velocity = Velocities[i];
                }
            }

            return true;
        }
        
        public void Clear()
        {
            for (int i = 0; i < Positions.Length; i++)
            {
                Positions[i] = Vector2.Zero;
                Velocities[i] = Vector2.Zero;
                Rotations[i] = 0f;
            }
            TimeStamp = 0f;
            FrameIndex = 0;
            isValid = false;
        }
    }
}
