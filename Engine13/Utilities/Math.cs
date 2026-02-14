using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;
using Veldrid.Sdl2;

namespace Engine13.Utilities
{
    // Simple circular buffer from first principles - for averaging/smoothing values
    public class CircularBuffer<T>
    {
        readonly T[] _buffer;
        int _head,
            _count;
        public int Count => _count;
        public int Capacity => _buffer.Length;

        public CircularBuffer(int capacity)
        {
            _buffer = new T[Math.Max(1, capacity)];
        }

        public void Add(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }

        public T this[int index] =>
            _buffer[((_head - _count + index) % _buffer.Length + _buffer.Length) % _buffer.Length];

        public void Clear()
        {
            _head = _count = 0;
        }

        public IEnumerable<T> Items()
        {
            for (int i = 0; i < _count; i++)
                yield return this[i];
        }
    }

    public static class WindowBounds
    {
        static Sdl2Window? _window;

        public static void SetWindow(Sdl2Window window) => _window = window;

        public static (float left, float right, float top, float bottom) GetNormalizedBounds() =>
            _window == null
                ? (-1f, 1f, -1f, 1f)
                : (
                    -(float)_window.Width / _window.Height,
                    (float)_window.Width / _window.Height,
                    -1f,
                    1f
                );

        public static Vector2 GetWindowSize() =>
            _window == null ? new(800, 600) : new(_window.Width, _window.Height);
    }

    public struct AABB
    {
        public Vector2 Min,
            Max,
            Center,
            Size;

        public AABB(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
            Center = (Min + Max) * 0.5f;
            Size = Max - Min;
        }

        public bool Intersects(AABB other) =>
            Min.X <= other.Max.X
            && Max.X >= other.Min.X
            && Min.Y <= other.Max.Y
            && Max.Y >= other.Min.Y;

        public static bool OverlapDepth(AABB a, AABB b, out Vector2 depth)
        {
            depth = Vector2.Zero;
            if (!a.Intersects(b))
                return false;
            float dx1 = b.Max.X - a.Min.X,
                dx2 = a.Max.X - b.Min.X;
            float dy1 = b.Max.Y - a.Min.Y,
                dy2 = a.Max.Y - b.Min.Y;
            float overlapX = dx1 < dx2 ? dx1 : -dx2;
            float overlapY = dy1 < dy2 ? dy1 : -dy2;
            depth = MathF.Abs(overlapX) < MathF.Abs(overlapY) ? new(overlapX, 0) : new(0, overlapY);
            return true;
        }
    }

    public class SpatialGrid
    {
        readonly float _cellSize;
        readonly Dictionary<(int, int), List<Entity>> _cells = new();
        readonly Dictionary<Entity, HashSet<(int, int)>> _entityCells = new();

        public SpatialGrid(float cellSize) => _cellSize = cellSize;

        (int, int) GetCellCoords(Vector2 pos) =>
            ((int)MathF.Floor(pos.X / _cellSize), (int)MathF.Floor(pos.Y / _cellSize));

        public void AddEntity(Entity entity)
        {
            var aabb = entity.GetAABB();
            var (minX, minY) = GetCellCoords(aabb.Min);
            var (maxX, maxY) = GetCellCoords(aabb.Max);
            if (!_entityCells.TryGetValue(entity, out var occupied))
                _entityCells[entity] = occupied = new();

            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var cell = (x, y);
                if (!_cells.TryGetValue(cell, out var list))
                    _cells[cell] = list = new();
                if (!list.Contains(entity))
                    list.Add(entity);
                occupied.Add(cell);
            }
        }

        void RemoveEntity(Entity entity)
        {
            if (!_entityCells.TryGetValue(entity, out var occupied))
                return;
            foreach (var c in occupied)
            {
                if (_cells.TryGetValue(c, out var list))
                {
                    list.Remove(entity);
                    if (list.Count == 0)
                        _cells.Remove(c);
                }
            }
            _entityCells.Remove(entity);
        }

        public void UpdateEntityPosition(Entity e)
        {
            RemoveEntity(e);
            AddEntity(e);
        }

        public void UpdateAllAabb(IEnumerable<Entity> entities)
        {
            foreach (var e in entities)
                UpdateEntityPosition(e);
        }

        public List<Entity> GetNearbyEntities(Vector2 pos)
        {
            var result = new List<Entity>();
            GetNearbyEntities(pos, result);
            return result;
        }

        public void GetNearbyEntities(Vector2 pos, List<Entity> outList)
        {
            outList.Clear();
            var (cx, cy) = GetCellCoords(pos);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (_cells.TryGetValue((cx + dx, cy + dy), out var list))
                    outList.AddRange(list);
        }

        public HashSet<(int, int)> GetOccupiedCells(Entity e) =>
            _entityCells.TryGetValue(e, out var c) ? c : new();

        public List<CollisionPair> GetCollisionPairs()
        {
            var pairs = new List<CollisionPair>();
            var seen = new HashSet<(Entity, Entity)>();
            foreach (var cell in _cells.Values)
            {
                for (int i = 0; i < cell.Count; i++)
                for (int j = i + 1; j < cell.Count; j++)
                {
                    var (a, b) =
                        cell[i].GetHashCode() < cell[j].GetHashCode()
                            ? (cell[i], cell[j])
                            : (cell[j], cell[i]);
                    if (seen.Add((a, b)))
                        pairs.Add(new CollisionPair(a, b));
                }
            }
            return pairs;
        }

        public void Clear()
        {
            _cells.Clear();
            _entityCells.Clear();
        }
    }

    public static class MathHelpers
    {
        public static float ComputeTerminalVelocityMag(
            float mass,
            float acceleration,
            float dragCoefficient
        )
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

        public static Vector2[] CapturePositions(
            System.Collections.Generic.IReadOnlyList<Entity> entities
        )
        {
            var snapshot = new Vector2[entities.Count];
            for (int i = 0; i < entities.Count; i++)
            {
                snapshot[i] = entities[i].Position;
            }
            return snapshot;
        }

        public static void ApplyPositionsToEntities(
            System.Collections.Generic.IReadOnlyList<Entity> entities,
            Vector2[] positions
        )
        {
            if (positions == null)
                return;
            int count = Math.Min(entities.Count, positions.Length);
            for (int i = 0; i < count; i++)
            {
                entities[i].Position = positions[i];
            }
        }

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

    public static class ParticleGridLayout
    {
        public static (int capacity, int columns, int rows, Vector2 origin) CalculateParticleGrid(
            float selectionMinX,
            float selectionMinY,
            float selectionMaxX,
            float selectionMaxY,
            float particleRadius,
            float spacingFactor = 1.15f
        )
        {
            float selectionWidth = MathF.Max(0.0001f, selectionMaxX - selectionMinX);
            float selectionHeight = MathF.Max(0.0001f, selectionMaxY - selectionMinY);

            float diameter = particleRadius * 2f;
            float horizontalSpacing = diameter * spacingFactor;
            float verticalSpacing = diameter * spacingFactor;

            float availableWidth = selectionWidth - (2f * particleRadius);
            float availableHeight = selectionHeight - (2f * particleRadius);

            int columns =
                availableWidth > 0
                    ? Math.Max(1, (int)MathF.Floor(availableWidth / horizontalSpacing) + 1)
                    : 1;
            int rows =
                availableHeight > 0
                    ? Math.Max(1, (int)MathF.Floor(availableHeight / verticalSpacing) + 1)
                    : 1;

            int capacity = columns * rows;

            float gridWidth = (columns - 1) * horizontalSpacing;
            float gridHeight = (rows - 1) * verticalSpacing;

            float originX = selectionMinX + particleRadius + (availableWidth - gridWidth) / 2f;
            float originY = selectionMinY + particleRadius + (availableHeight - gridHeight) / 2f;

            var origin = new Vector2(originX, originY);

            return (capacity, columns, rows, origin);
        }
    }
}
