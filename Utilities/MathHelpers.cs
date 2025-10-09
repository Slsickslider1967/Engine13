using System.Numerics;
using Engine13.Graphics;

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
            return (Min.X <= other.Max.X && Max.X >= other.Min.X) &&
                   (Min.Y <= other.Max.Y && Max.Y >= other.Min.Y);
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

        public CollisionInfo(Mesh a, Mesh b, Vector2 contactPoint, Vector2 penetrationDepth, Vector2 separationDirection)
        {
            MeshA = a;
            MeshB = b;
            ContactPoint = contactPoint;
            PenetrationDepth = penetrationDepth;
            SeparationDirection = separationDirection;
        }

        public static bool AreColliding(Mesh a, Mesh b, out CollisionInfo collisionInfo)
        {
            collisionInfo = null!;

            // Use the mesh's built-in AABB calculation!
            var aabbA = a.GetAABB();
            var aabbB = b.GetAABB();

            if (!aabbA.Intersects(aabbB))
                return false;

            if (AABB.OverlapDepth(aabbA, aabbB, out Vector2 depth))
            {
                Vector2 contactPoint = (a.Position + b.Position) / 2;
                Vector2 separationDir = Vector2.Normalize(depth);
                collisionInfo = new CollisionInfo(a, b, contactPoint, depth, separationDir);
                return true;
            }

            return false;
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
        private System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<Mesh>> cells;
        private readonly System.Collections.Generic.Dictionary<Mesh, System.Collections.Generic.HashSet<(int, int)>> meshCells = new System.Collections.Generic.Dictionary<Mesh, System.Collections.Generic.HashSet<(int, int)>>();

        public SpatialGrid(float cellSize)
        {
            this.cellSize = cellSize;
            cells = new System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<Mesh>>();
        }

        public (int, int) GetCellCoords(Vector2 position)
        {
            int x = (int)System.Math.Floor(position.X / cellSize);
            int y = (int)System.Math.Floor(position.Y / cellSize);
            return (x, y);
        }

        /// <summary>
        /// 
        /// </summary>
        public void AddMesh(Mesh mesh)
        {
            AABB aabb = mesh.GetAABB();
            AABB cellRange = GetCellRange(aabb);

            
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
            if (meshCells.TryGetValue(mesh, out var oldCells))
            {
                var newCellCoords = GetCellCoords(mesh.Position);

                if (!oldCells.Contains(newCellCoords) || oldCells.Count != 1)
                {
                    RemoveMesh(mesh);

                    AddMesh(mesh);
                }
            }
            else
            {
                AddMesh(mesh);
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
            return new AABB(new Vector2(minCell.Item1, minCell.Item2), new Vector2(maxCell.Item1, maxCell.Item2));
        } 

        public System.Collections.Generic.HashSet<(int, int)> GetOccupiedCells(Mesh mesh)
        {
            return meshCells.TryGetValue(mesh, out var cells) ? cells : new System.Collections.Generic.HashSet<(int, int)>();
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
                        var pair = meshA.GetHashCode() < meshB.GetHashCode() ? (meshA, meshB) : (meshB, meshA);
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
}
