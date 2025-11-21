using System;
using System.Numerics;
using Engine13.Graphics;
using Veldrid;

namespace Engine13.Graphics
{
    public class Renderer
    {
        private GraphicsDevice GD;
        private CommandList CL;
        private PipeLineManager _Pipeline;
        private DeviceBuffer? _ProjectionBuffer;
        private ResourceSet? _ProjectionSet;

        // Debug drawing resources (reused buffers for a single quad line)
        private DeviceBuffer? _DebugVertexBuffer;
        private DeviceBuffer? _DebugIndexBuffer;
        private DeviceBuffer? _DebugPositionBuffer;
        private ResourceSet? _DebugPositionSet;
        private DeviceBuffer? _DebugColorBuffer;
        private ResourceSet? _DebugColorSet;

        public Renderer(GraphicsDevice _GD, CommandList _CL, PipeLineManager _Pipeline)
        {
            GD = _GD;
            CL = _CL;
            this._Pipeline = _Pipeline;
        }

        public void BeginFrame(RgbaFloat clearColor)
        {
            CL.Begin();
            CL.SetFramebuffer(GD.SwapchainFramebuffer);
            CL.ClearColorTarget(0, clearColor);

            // Ensure projection resources exist and update them each frame (handles window resizes)
            if (_Pipeline.ProjectionLayout != null)
            {
                if (_ProjectionBuffer == null)
                {
                    _ProjectionBuffer = GD.ResourceFactory.CreateBuffer(
                        new BufferDescription(64, BufferUsage.UniformBuffer)
                    );
                    _ProjectionSet = GD.ResourceFactory.CreateResourceSet(
                        new ResourceSetDescription(_Pipeline.ProjectionLayout, _ProjectionBuffer)
                    );
                }

                // Build a simple orthographic matrix that preserves aspect ratio.
                float w = GD.MainSwapchain.Framebuffer.Width;
                float h = GD.MainSwapchain.Framebuffer.Height;
                float sx = h / w; // aspect correction
                var proj = new Matrix4x4(sx, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                GD.UpdateBuffer(_ProjectionBuffer, 0, proj);
            }
        }

        public void EndFrame()
        {
            CL.End();
            GD.SubmitCommands(CL);
            GD.SwapBuffers(GD.MainSwapchain);
        }

        public void DrawMesh(Entity entity)
        {
            CL.SetPipeline(_Pipeline.GetPipeline());
            CL.SetVertexBuffer(0, entity.VertexBuffer);
            CL.SetIndexBuffer(entity.IndexBuffer, IndexFormat.UInt16);

            if (_Pipeline.PositionLayout != null)
            {
                entity.EnsurePositionResources(GD, _Pipeline.PositionLayout);
            }

            if (entity.PositionBuffer != null)
            {
                var packed = new Vector4(entity.Position.X, entity.Position.Y, 0f, 0f);
                GD.UpdateBuffer(entity.PositionBuffer, 0, packed);
            }

            if (entity.PositionResourceSet != null)
            {
                CL.SetGraphicsResourceSet(0, entity.PositionResourceSet);
            }

            if (_ProjectionSet != null)
            {
                CL.SetGraphicsResourceSet(1, _ProjectionSet);
            }

            if (_Pipeline.ColorLayout != null)
            {
                entity.EnsureColorResources(GD, _Pipeline.ColorLayout);
                if (entity.ColorBuffer != null)
                {
                    GD.UpdateBuffer(entity.ColorBuffer, 0, entity.Color);
                }
                if (entity.ColorResourceSet != null)
                {
                    CL.SetGraphicsResourceSet(2, entity.ColorResourceSet);
                }
            }

            CL.DrawIndexed((uint)entity.IndexCount, 1, 0, 0, 0);
        }

        // Draw a world-space velocity vector as a thick line (quad) from a to a + velocity*scale
        public void DrawVelocityVector(
            Vector2 start,
            Vector2 velocity,
            float scale,
            Vector4 color,
            float thickness = 0.01f
        )
        {
            Vector2 end = start + velocity * scale;
            Vector2 dir = end - start;
            float lenSq = dir.LengthSquared();
            if (lenSq < 1e-12f)
                return; // nothing to draw
            float len = MathF.Sqrt(lenSq);
            Vector2 n = new Vector2(dir.Y, -dir.X) / len; // normalized perpendicular (clockwise)
            float hw = thickness * 0.5f;

            // Build quad vertices in world space (we'll use meshPosition = 0)
            var v0 = new Engine13.Graphics.VertexPosition(start.X + n.X * hw, start.Y + n.Y * hw);
            var v1 = new Engine13.Graphics.VertexPosition(end.X + n.X * hw, end.Y + n.Y * hw);
            var v2 = new Engine13.Graphics.VertexPosition(end.X - n.X * hw, end.Y - n.Y * hw);
            var v3 = new Engine13.Graphics.VertexPosition(start.X - n.X * hw, start.Y - n.Y * hw);
            var verts = new Engine13.Graphics.VertexPosition[4] { v0, v1, v2, v3 };
            // Use the same winding as regular meshes (clockwise) to avoid being culled
            var indices = new ushort[6] { 0, 1, 2, 1, 3, 2 };

            // Ensure buffers exist
            if (_DebugVertexBuffer == null)
            {
                _DebugVertexBuffer = GD.ResourceFactory.CreateBuffer(
                    new BufferDescription(
                        (uint)(4 * Engine13.Graphics.VertexPosition.SizeInBytes),
                        BufferUsage.VertexBuffer
                    )
                );
            }
            if (_DebugIndexBuffer == null)
            {
                _DebugIndexBuffer = GD.ResourceFactory.CreateBuffer(
                    new BufferDescription((uint)(6 * sizeof(ushort)), BufferUsage.IndexBuffer)
                );
                GD.UpdateBuffer(_DebugIndexBuffer, 0, indices);
            }

            // Ensure debug position/color resources (meshPosition=0, per-draw color)
            if (_Pipeline.PositionLayout != null && _DebugPositionBuffer == null)
            {
                _DebugPositionBuffer = GD.ResourceFactory.CreateBuffer(
                    new BufferDescription(16, BufferUsage.UniformBuffer)
                );
                _DebugPositionSet = GD.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(_Pipeline.PositionLayout, _DebugPositionBuffer)
                );
            }
            if (_Pipeline.ColorLayout != null && _DebugColorBuffer == null)
            {
                _DebugColorBuffer = GD.ResourceFactory.CreateBuffer(
                    new BufferDescription(16, BufferUsage.UniformBuffer)
                );
                _DebugColorSet = GD.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(_Pipeline.ColorLayout, _DebugColorBuffer)
                );
            }

            // Update vertex and uniforms
            GD.UpdateBuffer(_DebugVertexBuffer!, 0, verts);
            if (_DebugPositionBuffer != null)
            {
                var zero = new Vector4(0f, 0f, 0f, 0f);
                GD.UpdateBuffer(_DebugPositionBuffer, 0, zero);
            }
            if (_DebugColorBuffer != null)
            {
                GD.UpdateBuffer(_DebugColorBuffer, 0, color);
            }

            // Bind pipeline and resources (reuse projection set from this renderer)
            CL.SetPipeline(_Pipeline.GetPipeline());
            CL.SetVertexBuffer(0, _DebugVertexBuffer!);
            CL.SetIndexBuffer(_DebugIndexBuffer!, IndexFormat.UInt16);

            if (_DebugPositionSet != null)
                CL.SetGraphicsResourceSet(0, _DebugPositionSet);
            if (_ProjectionSet != null)
                CL.SetGraphicsResourceSet(1, _ProjectionSet);
            if (_DebugColorSet != null)
                CL.SetGraphicsResourceSet(2, _DebugColorSet);

            CL.DrawIndexed(6, 1, 0, 0, 0);
        }
    }
}
