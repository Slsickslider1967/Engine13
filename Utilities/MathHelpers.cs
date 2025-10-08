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

        public void AddMesh(Mesh mesh)
        {
            var cellCoords = GetCellCoords(mesh.Position);
            //Creates the list enrty if it doesn't exist
            if (!cells.TryGetValue(cellCoords, out var list))
            {
                list = new System.Collections.Generic.List<Mesh>();
                cells[cellCoords] = list;
            }
            cells[cellCoords].Add(mesh);
        }

        private void RemoveMesh(Mesh mesh, (int, int) cellCoords)
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

        public void UpdateMeshPosition(Mesh mesh)
        {
            var oldCellCoords = GetCellCoords(meshCells);
            var newCellCoords = GetCellCoords(mesh.Position);
            if (oldCellCoords != newCellCoords)
            {
                RemoveMesh(mesh, oldCellCoords);  
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

        public void Clear()
        {
            cells.Clear();
        }

    }
}