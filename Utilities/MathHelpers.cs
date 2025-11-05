using System;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;

namespace Engine13.Utilities
{
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
        public Mesh MeshA;
        public Mesh MeshB;

        public CollisionPair(Mesh a, Mesh b)
        {
            MeshA = a;
            MeshB = b;
        }
    }

    public class CollisionInfo
    {
        public Mesh MeshA;
        public Mesh MeshB;
        public Vector2 ContactPoint;
        public Vector2 PenetrationDepth;
        public Vector2 SeparationDirection;

        public CollisionInfo(
            Mesh a,
            Mesh b,
            Vector2 contactPoint,
            Vector2 penetrationDepth,
            Vector2 separationDirection
        )
        {
            MeshA = a;
            MeshB = b;
            ContactPoint = contactPoint;
            PenetrationDepth = penetrationDepth;
            SeparationDirection = separationDirection;
            // Debug logging removed to avoid spamming console during collisions
        }

        public static bool AreColliding(Mesh a, Mesh b, out CollisionInfo collisionInfo)
        {
            collisionInfo = null!;

            return VertexCollisionSolver.Instance.TryFindContact(a, b, out collisionInfo);
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

        public static void ResolveCollision(CollisionInfo collision, float deltaTime)
        {
            if (collision == null)
                return;

            var meshA = collision.MeshA;
            var meshB = collision.MeshB;
            if (meshA == null || meshB == null)
                return;

            var objA = meshA.GetAttribute<ObjectCollision>();
            var objB = meshB.GetAttribute<ObjectCollision>();

            float invMassA = (objA == null || objA.IsStatic || objA.Mass <= 0f) ? 0f : 1f / objA.Mass;
            float invMassB = (objB == null || objB.IsStatic || objB.Mass <= 0f) ? 0f : 1f / objB.Mass;
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
                    meshA.Position -= correction * invMassA;
                if (objB != null && invMassB > 0f)
                    meshB.Position += correction * invMassB;
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

            float restitution = Math.Clamp(MathF.Max(restitutionCandidateA, restitutionCandidateB), 0f, 1f);
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

    ///Turning the screan in a spatial grid
    public class SpatialGrid
    {
        private float cellSize;

        /// <summary>
        /// (int, int) mesh cooirds in the grid
        /// List<Mesh> list of corrisponding meshes in that cell
        /// </summary>
        private System.Collections.Generic.Dictionary<
            (int, int),
            System.Collections.Generic.List<Mesh>
        > cells;
        private readonly System.Collections.Generic.Dictionary<
            Mesh,
            System.Collections.Generic.HashSet<(int, int)>
        > meshCells = new System.Collections.Generic.Dictionary<
            Mesh,
            System.Collections.Generic.HashSet<(int, int)>
        >();

        public SpatialGrid(float cellSize)
        {
            this.cellSize = cellSize;
            cells = new System.Collections.Generic.Dictionary<
                (int, int),
                System.Collections.Generic.List<Mesh>
            >();
        }

        public (int, int) GetCellCoords(Vector2 position)
        {
            int x = (int)System.Math.Floor(position.X / cellSize);
            int y = (int)System.Math.Floor(position.Y / cellSize);
            return (x, y);
        }

        /// <summary>
        /// Register a mesh into every grid cell overlapped by its world-space AABB.
        /// </summary>
        public void AddMesh(Mesh mesh)
        {
            var aabb = mesh.GetAABB();
            var minCell = GetCellCoords(aabb.Min);
            var maxCell = GetCellCoords(aabb.Max);

            if (!meshCells.TryGetValue(mesh, out var occupied))
            {
                occupied = new System.Collections.Generic.HashSet<(int, int)>();
                meshCells[mesh] = occupied;
            }

            for (int x = minCell.Item1; x <= maxCell.Item1; x++)
            {
                for (int y = minCell.Item2; y <= maxCell.Item2; y++)
                {
                    var cell = (x, y);
                    if (!cells.TryGetValue(cell, out var list))
                    {
                        list = new System.Collections.Generic.List<Mesh>();
                        cells[cell] = list;
                    }
                    if (!list.Contains(mesh))
                    {
                        list.Add(mesh);
                    }
                    occupied.Add(cell);
                }
            }
        }

        private void RemoveMesh(Mesh mesh)
        {
            if (meshCells.TryGetValue(mesh, out var OccupiedCells))
            {
                foreach (var cellCoords in OccupiedCells)
                {
                    if (cells.ContainsKey(cellCoords))
                    {
                        cells[cellCoords].Remove(mesh);
                        if (cells[cellCoords].Count == 0)
                        {
                            cells.Remove(cellCoords);
                        }
                    }
                }
                meshCells.Remove(mesh);
            }
        }

        public void UpdateMeshPosition(Mesh mesh)
        {
            // Re-register mesh into all cells overlapped by its current AABB
            RemoveMesh(mesh);
            AddMesh(mesh);
        }

        public void UpdateAllAabb(System.Collections.Generic.IEnumerable<Mesh> meshes)
        {
            foreach (var m in meshes)
            {
                UpdateMeshPosition(m);
            }
        }

        public System.Collections.Generic.List<Mesh> GetNearbyMeshes(Vector2 position)
        {
            var cellCoords = GetCellCoords(position);
            var nearbyMeshes = new System.Collections.Generic.List<Mesh>();

            //Cheaking in a grid of 8
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var neighborCoords = (cellCoords.Item1 + dx, cellCoords.Item2 + dy);
                    if (cells.ContainsKey(neighborCoords))
                    {
                        nearbyMeshes.AddRange(cells[neighborCoords]);
                    }
                }
            }

            return nearbyMeshes;
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

        public System.Collections.Generic.HashSet<(int, int)> GetOccupiedCells(Mesh mesh)
        {
            return meshCells.TryGetValue(mesh, out var cells)
                ? cells
                : new System.Collections.Generic.HashSet<(int, int)>();
        }

        public System.Collections.Generic.List<CollisionPair> GetCollisionPairs()
        {
            var pairs = new System.Collections.Generic.List<CollisionPair>();
            var processedPairs = new System.Collections.Generic.HashSet<(Mesh, Mesh)>();

            foreach (var cell in cells.Values)
            {
                // Check all mesh pairs within each cell
                for (int i = 0; i < cell.Count; i++)
                {
                    for (int j = i + 1; j < cell.Count; j++)
                    {
                        var meshA = cell[i];
                        var meshB = cell[j];

                        // Avoid duplicate pairs (A,B) and (B,A)
                        var pair =
                            meshA.GetHashCode() < meshB.GetHashCode()
                                ? (meshA, meshB)
                                : (meshB, meshA);
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
            meshCells.Clear();
        }
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

            public override string ToString()
                => $"n={Normal}, src={(Source == 0 ? "A" : "B")}, edge={EdgeIndex}";

        }

        private static void BuildAxes(ReadOnlySpan<Vector2> VertsA, ReadOnlySpan<Vector2> VertsB, System.Collections.Generic.List<SetAxis> axes)
        {
            axes.Clear();
            AddAxesFromPolygon(VertsA, 0, axes);
            AddAxesFromPolygon(VertsB, 1, axes);
        }

        private static void AddAxesFromPolygon(ReadOnlySpan<Vector2> Vertices, int source, System.Collections.Generic.List<SetAxis> axes)
        {
            int n = Vertices.Length;
            if (n < 2) return;

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
            if (axis == null || axis.Count == 0)
                return;

            var OutList = new System.Collections.Generic.List<SetAxis>(axis.Count);
            foreach (var a in axis)
            {
                if (!OutList.Contains(a))
                    OutList.Add(a);
            }

            axis.Clear();
            axis.AddRange(OutList);
        }

        public bool TryFindContact(Mesh meshA, Mesh meshB, out CollisionInfo collision)
        {
            collision = null!;
            if (meshA == null || meshB == null)
                return false;



            return false;
        }

        public int CopyWorldSpaceVertices(Mesh mesh, Span<Vector2> destination)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            var vertices = mesh.GetVertices();
            int count = Math.Min(vertices.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] = new Vector2(vertices[i].X, vertices[i].Y) + mesh.Position;
            }

            return count;
        }
    }

    public class MathHelpers { }
}
