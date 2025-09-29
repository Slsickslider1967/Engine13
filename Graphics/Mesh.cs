using Veldrid;
using System;

namespace Engine13.Graphics
{
    public struct VertexPosition
    {
        public const uint SizeInBytes = 8; // 2 floats (X, Y) * 4 bytes each

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
        // Declare class members
        private VertexPosition[] _vertices;
        private ushort[] _indices;

        public DeviceBuffer VertexBuffer { get; private set; }
        public DeviceBuffer IndexBuffer { get; private set; }
        public int IndexCount { get; private set; }

        public Mesh(GraphicsDevice GD, VertexPosition[] vertices, ushort[] indices)
        {
            _vertices = vertices;
            _indices = indices;

            // Create vertex buffer
            VertexBuffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(vertices.Length * VertexPosition.SizeInBytes),
                BufferUsage.VertexBuffer));

            // Upload vertex data
            GD.UpdateBuffer(VertexBuffer, 0, vertices);

            // Create index buffer
            IndexBuffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(indices.Length * sizeof(ushort)),
                BufferUsage.IndexBuffer));

            // Upload index data
            GD.UpdateBuffer(IndexBuffer, 0, indices);

            IndexCount = indices.Length;
        }

        public static Mesh CreateQuad(GraphicsDevice GD, float Width, float Height)
        {
            VertexPosition[] vertices = new VertexPosition[]
            {
                new VertexPosition(-0.5f * Width, -0.5f * Height), // Bottom-left
                new VertexPosition(0.5f * Width, -0.5f * Height), // Bottom-right
                new VertexPosition(-0.5f * Width, 0.5f * Height), // Top-right
                new VertexPosition(0.5f * Width, 0.5f * Height)  // Top-left
            };

            ushort[] indices =
            {
                0, 1, 2,
                1, 3, 2
            };

            return new Mesh(GD, vertices, indices);

        }
        
        public void UpdateQuad(GraphicsDevice GD, float Width, float Height)
        {
            float hx = 0.5f * Width;
            float hy = 0.5f * Height;

            var verts = new VertexPosition[]
            {
                new VertexPosition(-hx, -hy),
                new VertexPosition( hx, -hy),
                new VertexPosition(-hx,  hy),
                new VertexPosition( hx,  hy)
            };
            GD.UpdateBuffer(VertexBuffer, 0, verts);
        } 
    }


}