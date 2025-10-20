using Veldrid;
using System;
using System.Numerics;
using Engine13.Core;
using Engine13.Utilities.Attributes;
using Engine13.Utilities;
using System.Diagnostics;

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

    public class Mesh
    {
        private VertexPosition[] _vertices;
        private ushort[] _indices;
        public VertexPosition[] GetVertices() => _vertices;
        public ushort[] GetIndices() => _indices;
        public DeviceBuffer VertexBuffer { get; private set; }
        public DeviceBuffer IndexBuffer { get; private set; }
        public int IndexCount { get; private set; }
        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector4 Color { get; set; } = new Vector4(1f, 1f, 1f, 1f);
        public float Mass { get; set; } = 1f;
        public Vector2 Size { get; set; }
        private readonly System.Collections.Generic.List<IMeshAttribute> _attributes = new();
        public DeviceBuffer? PositionBuffer { get; private set; }
        public ResourceSet? PositionResourceSet { get; private set; }
        public DeviceBuffer? ColorBuffer { get; private set; }
        public ResourceSet? ColorResourceSet { get; private set; }
        public AABB GetAABB()
        {
            if (_vertices == null || _vertices.Length == 0)
                return new AABB(Vector2.Zero, Vector2.Zero);

            Vector2 min = new Vector2(float.MaxValue);
            Vector2 max = new Vector2(float.MinValue);

            foreach (var vertex in _vertices)
            {
                Vector2 pos = new Vector2(vertex.X, vertex.Y) + Position;
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);
            }

            return new AABB(min, max);
        }

        // public Vector2 ObjectSize()
        // {
        //     if (_vertices == null || _vertices.Length == 0)
        //         return Vector2.Zero;
        //     else
        //     {
        //         Vector2 min = new Vector2(float.MaxValue);
        //         Vector2 max = new Vector2(float.MinValue);

        //         foreach (var vertex in _vertices)
        //         {
        //             Vector2 pos = new Vector2(vertex.X, vertex.Y);
        //             min = Vector2.Min(min, pos);
        //             max = Vector2.Max(max, pos);
        //         }

        //         return max - min;
        //     }
        // }

        public Mesh(GraphicsDevice GD, VertexPosition[] vertices, ushort[] indices)
        {
            _vertices = vertices;
            _indices = indices;

            VertexBuffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(vertices.Length * VertexPosition.SizeInBytes),
                BufferUsage.VertexBuffer));

            GD.UpdateBuffer(VertexBuffer, 0, vertices);

            IndexBuffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(indices.Length * sizeof(ushort)),
                BufferUsage.IndexBuffer));

            GD.UpdateBuffer(IndexBuffer, 0, indices);

            IndexCount = indices.Length;
        }

        public void AddAttribute(IMeshAttribute attribute)
        {
            if (attribute != null) _attributes.Add(attribute);
        }

        public bool RemoveAttribute<T>() where T : IMeshAttribute
        {
            int idx = _attributes.FindIndex(a => a is T);
            if (idx >= 0) { _attributes.RemoveAt(idx); return true; }
            return false;
        }

        public T? GetAttribute<T>() where T : IMeshAttribute
        {
            for (int i = 0; i < _attributes.Count; i++)
            {
                if (_attributes[i] is T attr) return attr;
            }
            return default;
        }

        public void ClearAttributes() => _attributes.Clear();

        public void UpdateAttributes(GameTime gameTime)
        {
            for (int i = 0; i < _attributes.Count; i++)
            {
                _attributes[i].Update(this, gameTime);
            }
        }

        public void EnsurePositionResources(GraphicsDevice gd, ResourceLayout layout)
        {
            if (layout == null) return;

            if (PositionBuffer == null)
            {
                PositionBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
                PositionResourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                    layout,
                    PositionBuffer
                ));
            }
        }

        public void EnsureColorResources(GraphicsDevice gd, ResourceLayout layout)
        {
            if (layout == null) return;
            if (ColorBuffer == null)
            {
                ColorBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
                ColorResourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                    layout,
                    ColorBuffer
                ));
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

        protected Engine13.Graphics.Mesh BuildMesh(Engine13.Graphics.VertexPosition[] vertices, ushort[] indices)
            => new Engine13.Graphics.Mesh(GD, vertices, indices);
    }

    public class QuadFactory : PrimitiveFactory
    {
        public QuadFactory(GraphicsDevice gd) : base(gd) { }

        public Engine13.Graphics.Mesh Quad(float Width, float Height)
        {
            var vertices = new Engine13.Graphics.VertexPosition[]
            {
                new Engine13.Graphics.VertexPosition(-0.5f * Width, -0.5f * Height),
                new Engine13.Graphics.VertexPosition(0.5f * Width, -0.5f * Height),
                new Engine13.Graphics.VertexPosition(-0.5f * Width, 0.5f * Height),
                new Engine13.Graphics.VertexPosition(0.5f * Width, 0.5f * Height)
            };
            ushort[] indices = { 0, 1, 2, 1, 3, 2 };
            var mesh = BuildMesh(vertices, indices);
            mesh.Size = new Vector2(Width, Height);
            return mesh;
        }

        public Engine13.Graphics.Mesh Quad(float Width, float Height, Vector4 color)
        {
            var mesh = Quad(Width, Height);
            mesh.Color = color;
            mesh.Size = new Vector2(Width, Height);
            return mesh;
        }

        public void Update(Engine13.Graphics.Mesh mesh, float Width, float Height)
        {
            float hx = 0.5f * Width;
            float hy = 0.5f * Height;
            var verts = new Engine13.Graphics.VertexPosition[]
            {
                new Engine13.Graphics.VertexPosition(-hx, -hy),
                new Engine13.Graphics.VertexPosition( hx, -hy),
                new Engine13.Graphics.VertexPosition(-hx,  hy),
                new Engine13.Graphics.VertexPosition( hx,  hy)
            };
            GD.UpdateBuffer(mesh.VertexBuffer, 0, verts);
            mesh.Size = new Vector2(Width, Height);
        }

        public static Engine13.Graphics.Mesh CreateQuad(GraphicsDevice GD, float Width, float Height)
            => new QuadFactory(GD).Quad(Width, Height);
        public static Engine13.Graphics.Mesh CreateQuad(GraphicsDevice GD, float Width, float Height, Vector4 color)
            => new QuadFactory(GD).Quad(Width, Height, color);

        public static void UpdateQuad(Engine13.Graphics.Mesh mesh, GraphicsDevice GD, float Width, float Height)
            => new QuadFactory(GD).Update(mesh, Width, Height);
    }

    public class CubeFactory : PrimitiveFactory
    {
        public CubeFactory(GraphicsDevice gd) : base(gd) { }
        public Engine13.Graphics.Mesh Cube(float Size)
        {
            var vertices = new Engine13.Graphics.VertexPosition[]
            {
                new Engine13.Graphics.VertexPosition(-0.5f * Size, -0.5f * Size),
                new Engine13.Graphics.VertexPosition(0.5f * Size, -0.5f * Size),
                new Engine13.Graphics.VertexPosition(-0.5f * Size, 0.5f * Size),
                new Engine13.Graphics.VertexPosition(0.5f * Size, 0.5f * Size)
            };
            ushort[] indices = { 0, 1, 2, 1, 3, 2 };
            var mesh = BuildMesh(vertices, indices);
            mesh.Size = new Vector2(Size, Size);
            return mesh;
        }

        public Engine13.Graphics.Mesh Cube(float Size, Vector4 color)
        {
            var mesh = Cube(Size);
            mesh.Color = color;
            mesh.Size = new Vector2(Size, Size);
            return mesh;
        }

        private void Update(Engine13.Graphics.Mesh mesh, float Size)
        {
            float hs = 0.5f * Size;
            var verts = new Engine13.Graphics.VertexPosition[]
            {
                new Engine13.Graphics.VertexPosition(-hs, -hs),
                new Engine13.Graphics.VertexPosition( hs, -hs),
                new Engine13.Graphics.VertexPosition(-hs,  hs),
                new Engine13.Graphics.VertexPosition( hs,  hs)
            };
            GD.UpdateBuffer(mesh.VertexBuffer, 0, verts);
            mesh.Size = new Vector2(Size, Size);
        }

        public static Engine13.Graphics.Mesh CreateCube(GraphicsDevice GD, float Size)
            => new CubeFactory(GD).Cube(Size);
        public static Engine13.Graphics.Mesh CreateCube(GraphicsDevice GD, float Size, Vector4 color)
            => new CubeFactory(GD).Cube(Size, color);

        public static void UpdateCube(Engine13.Graphics.Mesh mesh, GraphicsDevice GD, float Size)
            => new CubeFactory(GD).Update(mesh, Size);
    }

    public class SphereFactory : PrimitiveFactory
    {
        public SphereFactory(GraphicsDevice gd) : base(gd) { }

        public Engine13.Graphics.Mesh Sphere(float Radius, int LatitudeSegments = 16, int LongitudeSegments = 16)
        {
            if (LatitudeSegments < 3) LatitudeSegments = 3;
            if (LongitudeSegments < 3) LongitudeSegments = 3;

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

            var mesh = BuildMesh(vertices.ToArray(), indices.ToArray());
            mesh.Size = new Vector2(Radius * 2f, Radius * 2f);
            return mesh;
        }

        public Engine13.Graphics.Mesh Sphere(float Radius, Vector4 color, int LatitudeSegments = 16, int LongitudeSegments = 16)
        {
            var mesh = Sphere(Radius, LatitudeSegments, LongitudeSegments);
            mesh.Color = color;
            mesh.Size = new Vector2(Radius * 2f, Radius * 2f);
            return mesh;
        }

        public void Update(Engine13.Graphics.Mesh mesh, float Radius, int LatitudeSegments = 16, int LongitudeSegments = 16)
        {
            var newSphere = Sphere(Radius, LatitudeSegments, LongitudeSegments);
            GD.UpdateBuffer(mesh.VertexBuffer, 0, newSphere.GetVertices());
            mesh.Size = new Vector2(Radius * 2f, Radius * 2f);
        }

        public static Engine13.Graphics.Mesh CreateSphere(GraphicsDevice GD, float Radius, int LatitudeSegments = 16, int LongitudeSegments = 16)
            => new SphereFactory(GD).Sphere(Radius, LatitudeSegments, LongitudeSegments);
        public static Engine13.Graphics.Mesh CreateSphere(GraphicsDevice GD, float Radius, Vector4 color, int LatitudeSegments = 16, int LongitudeSegments = 16)
            => new SphereFactory(GD).Sphere(Radius, color, LatitudeSegments, LongitudeSegments);
        public static void UpdateSphere(Engine13.Graphics.Mesh mesh, GraphicsDevice GD, float Radius, int LatitudeSegments = 16, int LongitudeSegments = 16)
            => new SphereFactory(GD).Update(mesh, Radius, LatitudeSegments, LongitudeSegments);
    }

    public class TriangleFactory : PrimitiveFactory
    {
        public TriangleFactory(GraphicsDevice gd) : base(gd) { }

    }
}