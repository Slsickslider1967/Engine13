using System;
using System.Numerics;
using Engine13.Core;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Veldrid;

namespace Engine13.Graphics
{
    public struct VertexPosition
    {
        public const uint SizeInBytes = 8;

        public float X;
        public float Y;

        public VertexPosition(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public sealed class RenderMesh
    {
        public VertexPosition[] Vertices { get; }
        public ushort[] Indices { get; }
        public DeviceBuffer VertexBuffer { get; }
        public DeviceBuffer IndexBuffer { get; }
        public int IndexCount { get; }

        public RenderMesh(GraphicsDevice device, VertexPosition[] vertices, ushort[] indices)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (vertices == null || vertices.Length == 0)
                throw new ArgumentException("Vertices cannot be null or empty", nameof(vertices));
            if (indices == null || indices.Length == 0)
                throw new ArgumentException("Indices cannot be null or empty", nameof(indices));

            Vertices = vertices;
            Indices = indices;
            IndexCount = indices.Length;

            VertexBuffer = device.ResourceFactory.CreateBuffer(
                new BufferDescription(
                    (uint)(vertices.Length * VertexPosition.SizeInBytes),
                    BufferUsage.VertexBuffer
                )
            );
            device.UpdateBuffer(VertexBuffer, 0, vertices);

            IndexBuffer = device.ResourceFactory.CreateBuffer(
                new BufferDescription(
                    (uint)(indices.Length * sizeof(ushort)),
                    BufferUsage.IndexBuffer
                )
            );
            device.UpdateBuffer(IndexBuffer, 0, indices);
        }

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }

    public class Entity
    {
        public RenderMesh RenderMesh { get; }

        public enum CollisionShapeType
        {
            ConvexPolygon,
            Circle,
        }

        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector2 Velocity { get; set; } = Vector2.Zero;
        public float Rotation { get; set; } = 0f; // Rotation in radians
        public Vector4 Colour { get; set; } = new Vector4(1f, 1f, 1f, 1f);
        public float Mass { get; set; } = 1f;
        public Vector2 Size { get; set; }
        public CollisionShapeType CollisionShape { get; set; } = CollisionShapeType.ConvexPolygon;
        public float CollisionRadius { get; set; } = 0f;

        private readonly System.Collections.Generic.Dictionary<
            Type,
            IEntityComponent
        > _componentCache = new();
        private readonly System.Collections.Generic.List<IEntityComponent> _components = new();
        public System.Collections.Generic.IReadOnlyList<IEntityComponent> Components => _components;

        public DeviceBuffer? PositionBuffer { get; private set; }
        public ResourceSet? PositionResourceSet { get; private set; }
        public DeviceBuffer? ColourBuffer { get; private set; }
        public ResourceSet? ColourResourceSet { get; private set; }

        private AABB? _cachedAABB;
        private Vector2 _cachedAABBPosition;

        public VertexPosition[] GetVertices() => RenderMesh.Vertices;

        public ushort[] GetIndices() => RenderMesh.Indices;

        public DeviceBuffer VertexBuffer => RenderMesh.VertexBuffer;
        public DeviceBuffer IndexBuffer => RenderMesh.IndexBuffer;
        public int IndexCount => RenderMesh.IndexCount;

        public AABB GetAABB()
        {
            if (_cachedAABB.HasValue && _cachedAABBPosition == Position)
            {
                return _cachedAABB.Value;
            }

            if (CollisionShape == CollisionShapeType.Circle && CollisionRadius > 0f)
            {
                Vector2 radiusVec = new Vector2(CollisionRadius, CollisionRadius);
                var circleAabb = new AABB(Position - radiusVec, Position + radiusVec);
                _cachedAABB = circleAabb;
                _cachedAABBPosition = Position;
                return circleAabb;
            }

            var vertices = RenderMesh.Vertices;
            if (vertices == null || vertices.Length == 0)
            {
                var aabb = new AABB(Vector2.Zero, Vector2.Zero);
                _cachedAABB = aabb;
                _cachedAABBPosition = Position;
                return aabb;
            }

            Vector2 min = new Vector2(float.MaxValue);
            Vector2 max = new Vector2(float.MinValue);
            foreach (var vertex in vertices)
            {
                Vector2 pos = new Vector2(vertex.X, vertex.Y) + Position;
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);
            }

            var result = new AABB(min, max);
            _cachedAABB = result;
            _cachedAABBPosition = Position;
            return result;
        }

        public void InvalidateAABBCache()
        {
            _cachedAABB = null;
        }

        public Entity(RenderMesh renderMesh)
        {
            RenderMesh = renderMesh ?? throw new ArgumentNullException(nameof(renderMesh));
        }

        public void AddComponent(IEntityComponent component)
        {
            if (component == null)
                return;

            var type = component.GetType();
            if (!_componentCache.ContainsKey(type))
            {
                _components.Add(component);
                _componentCache[type] = component;
            }
        }

        public bool RemoveComponent<T>()
            where T : IEntityComponent
        {
            var type = typeof(T);
            if (_componentCache.TryGetValue(type, out var component))
            {
                _components.Remove(component);
                _componentCache.Remove(type);
                return true;
            }
            return false;
        }

        public T? GetComponent<T>()
            where T : IEntityComponent
        {
            var type = typeof(T);
            if (_componentCache.TryGetValue(type, out var component))
            {
                return (T)component;
            }
            return default;
        }

        public void ClearComponents()
        {
            _components.Clear();
            _componentCache.Clear();
        }

        public void UpdateComponents(GameTime gameTime)
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].Update(this, gameTime);
            }
        }

        public void EnsurePositionResources(GraphicsDevice gd, ResourceLayout layout)
        {
            if (layout == null)
                return;

            if (PositionBuffer == null)
            {
                PositionBuffer = gd.ResourceFactory.CreateBuffer(
                    new BufferDescription(16, BufferUsage.UniformBuffer)
                );
                PositionResourceSet = gd.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(layout, PositionBuffer)
                );
            }
        }

        public void EnsureColourResources(GraphicsDevice gd, ResourceLayout layout)
        {
            if (layout == null)
                return;
            if (ColourBuffer == null)
            {
                ColourBuffer = gd.ResourceFactory.CreateBuffer(
                    new BufferDescription(16, BufferUsage.UniformBuffer)
                );
                ColourResourceSet = gd.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(layout, ColourBuffer)
                );
            }
        }
    }
}

namespace Engine13.Primitives
{
    public abstract class PrimitiveFactory
    {
        protected GraphicsDevice GD { get; }

        protected PrimitiveFactory(GraphicsDevice gd)
        {
            GD = gd;
        }

        protected Engine13.Graphics.Entity BuildMesh(
            Engine13.Graphics.VertexPosition[] vertices,
            ushort[] indices
        )
        {
            var renderMesh = new Engine13.Graphics.RenderMesh(GD, vertices, indices);
            return new Engine13.Graphics.Entity(renderMesh);
        }
    }

    public class QuadFactory : PrimitiveFactory
    {
        public QuadFactory(GraphicsDevice gd)
            : base(gd) { }

        public Engine13.Graphics.Entity Quad(float Width, float Height)
        {
            var vertices = new Engine13.Graphics.VertexPosition[]
            {
                new Graphics.VertexPosition(-0.5f * Width, -0.5f * Height),
                new Graphics.VertexPosition(0.5f * Width, -0.5f * Height),
                new Graphics.VertexPosition(-0.5f * Width, 0.5f * Height),
                new Graphics.VertexPosition(0.5f * Width, 0.5f * Height),
            };
            ushort[] indices = { 0, 1, 2, 1, 3, 2 };
            var entity = BuildMesh(vertices, indices);
            entity.Size = new Vector2(Width, Height);
            return entity;
        }

        public Engine13.Graphics.Entity Quad(float Width, float Height, Vector4 colour)
        {
            var entity = Quad(Width, Height);
            entity.Colour = colour;
            entity.Size = new Vector2(Width, Height);
            return entity;
        }

        public void Update(Engine13.Graphics.Entity entity, float Width, float Height)
        {
            float hx = 0.5f * Width;
            float hy = 0.5f * Height;
            var verts = new Engine13.Graphics.VertexPosition[]
            {
                new Engine13.Graphics.VertexPosition(-hx, -hy),
                new Engine13.Graphics.VertexPosition(hx, -hy),
                new Engine13.Graphics.VertexPosition(-hx, hy),
                new Engine13.Graphics.VertexPosition(hx, hy),
            };
            GD.UpdateBuffer(entity.VertexBuffer, 0, verts);
            entity.Size = new Vector2(Width, Height);
        }

        public static Engine13.Graphics.Entity CreateQuad(
            GraphicsDevice GD,
            float Width,
            float Height
        ) => new QuadFactory(GD).Quad(Width, Height);

        public static Engine13.Graphics.Entity CreateQuad(
            GraphicsDevice GD,
            float Width,
            float Height,
            Vector4 colour
        ) => new QuadFactory(GD).Quad(Width, Height, colour);

        public static void UpdateQuad(
            Engine13.Graphics.Entity entity,
            GraphicsDevice GD,
            float Width,
            float Height
        ) => new QuadFactory(GD).Update(entity, Width, Height);
    }

    public class CubeFactory : PrimitiveFactory
    {
        public CubeFactory(GraphicsDevice gd)
            : base(gd) { }

        public Engine13.Graphics.Entity Cube(float Size)
        {
            var vertices = new Engine13.Graphics.VertexPosition[]
            {
                new Engine13.Graphics.VertexPosition(-0.5f * Size, -0.5f * Size),
                new Engine13.Graphics.VertexPosition(0.5f * Size, -0.5f * Size),
                new Engine13.Graphics.VertexPosition(-0.5f * Size, 0.5f * Size),
                new Engine13.Graphics.VertexPosition(0.5f * Size, 0.5f * Size),
            };
            ushort[] indices = { 0, 1, 2, 1, 3, 2 };
            var entity = BuildMesh(vertices, indices);
            entity.Size = new Vector2(Size, Size);
            return entity;
        }

        public Engine13.Graphics.Entity Cube(float Size, Vector4 colour)
        {
            var entity = Cube(Size);
            entity.Colour = colour;
            entity.Size = new Vector2(Size, Size);
            return entity;
        }

        private void Update(Engine13.Graphics.Entity entity, float Size)
        {
            float hs = 0.5f * Size;
            var verts = new Engine13.Graphics.VertexPosition[]
            {
                new Engine13.Graphics.VertexPosition(-hs, -hs),
                new Engine13.Graphics.VertexPosition(hs, -hs),
                new Engine13.Graphics.VertexPosition(-hs, hs),
                new Engine13.Graphics.VertexPosition(hs, hs),
            };
            GD.UpdateBuffer(entity.VertexBuffer, 0, verts);
            entity.Size = new Vector2(Size, Size);
        }

        public static Engine13.Graphics.Entity CreateCube(GraphicsDevice GD, float Size) =>
            new CubeFactory(GD).Cube(Size);

        public static Engine13.Graphics.Entity CreateCube(
            GraphicsDevice GD,
            float Size,
            Vector4 colour
        ) => new CubeFactory(GD).Cube(Size, colour);

        public static void UpdateCube(
            Engine13.Graphics.Entity entity,
            GraphicsDevice GD,
            float Size
        ) => new CubeFactory(GD).Update(entity, Size);
    }

    public class CircleFactory : PrimitiveFactory
    {
        public CircleFactory(GraphicsDevice gd)
            : base(gd) { }

        public Engine13.Graphics.Entity Circle(
            float Radius,
            int LatitudeSegments = 16,
            int LongitudeSegments = 16
        )
        {
            if (LatitudeSegments < 3)
                LatitudeSegments = 3;
            if (LongitudeSegments < 3)
                LongitudeSegments = 3;

            var vertices = new System.Collections.Generic.List<Engine13.Graphics.VertexPosition>();
            var indices = new System.Collections.Generic.List<ushort>();

            for (int lat = 0; lat <= LatitudeSegments; lat++)
            {
                float theta = lat * MathF.PI / LatitudeSegments;
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                for (int lon = 0; lon <= LongitudeSegments; lon++)
                {
                    float phi = lon * 2 * MathF.PI / LongitudeSegments;
                    float cosPhi = MathF.Cos(phi);

                    float x = cosPhi * sinTheta;
                    float y = cosTheta;

                    vertices.Add(new Engine13.Graphics.VertexPosition(x * Radius, y * Radius));
                }
            }

            for (int lat = 0; lat < LatitudeSegments; lat++)
            {
                for (int lon = 0; lon < LongitudeSegments; lon++)
                {
                    ushort first = (ushort)((lat * (LongitudeSegments + 1)) + lon);
                    ushort second = (ushort)(first + LongitudeSegments + 1);

                    indices.Add(first);
                    indices.Add(second);
                    indices.Add((ushort)(first + 1));

                    indices.Add(second);
                    indices.Add((ushort)(second + 1));
                    indices.Add((ushort)(first + 1));
                }
            }

            var entity = BuildMesh(vertices.ToArray(), indices.ToArray());
            entity.Size = new Vector2(Radius * 2f, Radius * 2f);
            entity.CollisionShape = Engine13.Graphics.Entity.CollisionShapeType.Circle;
            entity.CollisionRadius = Radius;
            return entity;
        }

        public Engine13.Graphics.Entity Circle(
            float Radius,
            Vector4 colour,
            int LatitudeSegments = 16,
            int LongitudeSegments = 16
        )
        {
            var entity = Circle(Radius, LatitudeSegments, LongitudeSegments);
            entity.Colour = colour;
            entity.Size = new Vector2(Radius * 2f, Radius * 2f);
            entity.CollisionShape = Engine13.Graphics.Entity.CollisionShapeType.Circle;
            entity.CollisionRadius = Radius;
            return entity;
        }

        public void Update(
            Engine13.Graphics.Entity entity,
            float Radius,
            int LatitudeSegments = 16,
            int LongitudeSegments = 16
        )
        {
            var newCircle = Circle(Radius, LatitudeSegments, LongitudeSegments);
            GD.UpdateBuffer(entity.VertexBuffer, 0, newCircle.GetVertices());
            entity.Size = new Vector2(Radius * 2f, Radius * 2f);
            entity.CollisionShape = Engine13.Graphics.Entity.CollisionShapeType.Circle;
            entity.CollisionRadius = Radius;
        }

        public static Engine13.Graphics.Entity CreateCircle(
            GraphicsDevice GD,
            float Radius,
            int LatitudeSegments = 16,
            int LongitudeSegments = 16
        ) => new CircleFactory(GD).Circle(Radius, LatitudeSegments, LongitudeSegments);

        public static Engine13.Graphics.Entity CreateCircle(
            GraphicsDevice GD,
            float Radius,
            Vector4 colour,
            int LatitudeSegments = 16,
            int LongitudeSegments = 16
        ) => new CircleFactory(GD).Circle(Radius, colour, LatitudeSegments, LongitudeSegments);

        public static void UpdateCircle(
            Engine13.Graphics.Entity entity,
            GraphicsDevice GD,
            float Radius,
            int LatitudeSegments = 16,
            int LongitudeSegments = 16
        ) => new CircleFactory(GD).Update(entity, Radius, LatitudeSegments, LongitudeSegments);
    }

    public class TriangleFactory : PrimitiveFactory
    {
        public TriangleFactory(GraphicsDevice gd)
            : base(gd) { }
    }
}
