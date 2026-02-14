using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Graphics;
using Veldrid;

namespace Engine13.Graphics
{
    /// <summary>
    /// Handles all rendering: frame lifecycle, mesh drawing, debug vectors, and
    /// instanced particle rendering.
    ///
    /// Design rules that prevent the Vulkan "image not acquired" validation error:
    ///   1.  NEVER call GD.UpdateBuffer() during a frame – it submits internal
    ///       staging command buffers that break the swapchain semaphore chain.
    ///       Use CL.UpdateBuffer() instead so all work is recorded into the
    ///       single per-frame command list.
    ///   2.  GD.UpdateBuffer() is ONLY used at initialisation time (before the
    ///       first frame), where it is safe because no swapchain images are in
    ///       flight.
    ///   3.  SwapBuffers is wrapped in a try/catch so that a transient
    ///       VK_ERROR_OUT_OF_DATE (e.g. mid-resize) doesn't kill the render
    ///       thread; the next frame simply re-acquires.
    /// </summary>
    public class Renderer
    {
        // ── Core references ──────────────────────────────────────────────
        private readonly GraphicsDevice _gd;
        private CommandList _cl;
        private readonly PipeLineManager _pipeline;

        // ── Projection uniform ───────────────────────────────────────────
        private DeviceBuffer? _projectionBuffer;
        private ResourceSet? _projectionSet;

        // ── Debug-line resources (single reusable quad) ──────────────────
        private DeviceBuffer? _debugVB;
        private DeviceBuffer? _debugIB;
        private DeviceBuffer? _debugPosBuffer;
        private ResourceSet? _debugPosSet;
        private DeviceBuffer? _debugColorBuffer;
        private ResourceSet? _debugColorSet;

        // ── Instanced circle rendering ───────────────────────────────────
        private DeviceBuffer? _instanceBuffer;
        private int _instanceCapacity;
        private DeviceBuffer? _circleVB;
        private DeviceBuffer? _circleIB;
        private int _circleIndexCount;

        // ── Frame state ──────────────────────────────────────────────────
        private bool _frameInProgress;
        public Renderer(GraphicsDevice gd, CommandList cl, PipeLineManager pipeline)
        {
            _gd = gd;
            _cl = cl;
            _pipeline = pipeline;
        }

        /// <summary>
        /// Called each frame by EngineBase to hand the renderer the freshly-
        /// created per-frame CommandList.
        /// </summary>
        public void SetCommandList(CommandList cl) => _cl = cl;


        public void BeginFrame(RgbaFloat clearColor)
        {
            _cl.Begin();
            _cl.SetFramebuffer(_gd.SwapchainFramebuffer);
            _cl.ClearColorTarget(0, clearColor);
            _frameInProgress = true;

            EnsureProjectionResources();
            UpdateProjection();
        }

        public void EndFrame()
        {
            if (!_frameInProgress)
                return;
            _frameInProgress = false;

            _cl.End();
            _gd.SubmitCommands(_cl);

            try
            {
                _gd.SwapBuffers(_gd.MainSwapchain);
            }
            catch (VeldridException)
            {
                try
                {
                    _gd.WaitForIdle();
                }
                catch { }
            }
        }

        private void EnsureProjectionResources()
        {
            if (_pipeline.ProjectionLayout == null)
                return;
            if (_projectionBuffer != null)
                return;

            _projectionBuffer = _gd.ResourceFactory.CreateBuffer(
                new BufferDescription(64, BufferUsage.UniformBuffer)
            );
            _projectionSet = _gd.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(_pipeline.ProjectionLayout, _projectionBuffer)
            );
        }

        private void UpdateProjection()
        {
            if (_projectionBuffer == null)
                return;

            float w = _gd.MainSwapchain.Framebuffer.Width;
            float h = _gd.MainSwapchain.Framebuffer.Height;
            float sx = h / w; // aspect correction
            var proj = new Matrix4x4(sx, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            _cl.UpdateBuffer(_projectionBuffer, 0, ref proj);
        }

        public void DrawMesh(Entity entity)
        {
            _cl.SetPipeline(_pipeline.GetPipeline());
            _cl.SetVertexBuffer(0, entity.VertexBuffer);
            _cl.SetIndexBuffer(entity.IndexBuffer, IndexFormat.UInt16);

            if (_pipeline.PositionLayout != null)
                entity.EnsurePositionResources(_gd, _pipeline.PositionLayout);

            if (entity.PositionBuffer != null)
            {
                var packed = new Vector4(entity.Position.X, entity.Position.Y, 0f, 0f);
                _cl.UpdateBuffer(entity.PositionBuffer, 0, ref packed);
            }

            if (entity.PositionResourceSet != null)
                _cl.SetGraphicsResourceSet(0, entity.PositionResourceSet);
            if (_projectionSet != null)
                _cl.SetGraphicsResourceSet(1, _projectionSet);

            if (_pipeline.ColorLayout != null)
            {
                entity.EnsureColourResources(_gd, _pipeline.ColorLayout);
                if (entity.ColourBuffer != null)
                {
                    var col = entity.Colour;
                    _cl.UpdateBuffer(entity.ColourBuffer, 0, ref col);
                }
                if (entity.ColourResourceSet != null)
                    _cl.SetGraphicsResourceSet(2, entity.ColourResourceSet);
            }

            _cl.DrawIndexed((uint)entity.IndexCount, 1, 0, 0, 0);
        }

        // ─────────────────────────────────────────────────────────────────
        // Debug velocity vector (world-space thick line drawn as a quad)
        // ─────────────────────────────────────────────────────────────────

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
                return;

            float len = MathF.Sqrt(lenSq);
            Vector2 n = new Vector2(dir.Y, -dir.X) / len;
            float hw = thickness * 0.5f;

            var verts = new VertexPosition[4]
            {
                new(start.X + n.X * hw, start.Y + n.Y * hw),
                new(end.X + n.X * hw, end.Y + n.Y * hw),
                new(end.X - n.X * hw, end.Y - n.Y * hw),
                new(start.X - n.X * hw, start.Y - n.Y * hw),
            };
            var indices = new ushort[] { 0, 1, 2, 1, 3, 2 };

            EnsureDebugBuffers(indices);

            // Record updates into the command list (never GD.UpdateBuffer)
            _cl.UpdateBuffer(_debugVB!, 0, verts);

            if (_debugPosBuffer != null)
            {
                var zero = Vector4.Zero;
                _cl.UpdateBuffer(_debugPosBuffer, 0, ref zero);
            }
            if (_debugColorBuffer != null)
                _cl.UpdateBuffer(_debugColorBuffer, 0, ref color);

            // Bind and draw
            _cl.SetPipeline(_pipeline.GetPipeline());
            _cl.SetVertexBuffer(0, _debugVB!);
            _cl.SetIndexBuffer(_debugIB!, IndexFormat.UInt16);

            if (_debugPosSet != null)
                _cl.SetGraphicsResourceSet(0, _debugPosSet);
            if (_projectionSet != null)
                _cl.SetGraphicsResourceSet(1, _projectionSet);
            if (_debugColorSet != null)
                _cl.SetGraphicsResourceSet(2, _debugColorSet);

            _cl.DrawIndexed(6, 1, 0, 0, 0);
        }

        private void EnsureDebugBuffers(ushort[] indices)
        {
            if (_debugVB == null)
            {
                _debugVB = _gd.ResourceFactory.CreateBuffer(
                    new BufferDescription(
                        (uint)(4 * VertexPosition.SizeInBytes),
                        BufferUsage.VertexBuffer
                    )
                );
            }
            if (_debugIB == null)
            {
                _debugIB = _gd.ResourceFactory.CreateBuffer(
                    new BufferDescription((uint)(6 * sizeof(ushort)), BufferUsage.IndexBuffer)
                );
                // First-time init via command list
                _cl.UpdateBuffer(_debugIB, 0, indices);
            }
            if (_pipeline.PositionLayout != null && _debugPosBuffer == null)
            {
                _debugPosBuffer = _gd.ResourceFactory.CreateBuffer(
                    new BufferDescription(16, BufferUsage.UniformBuffer)
                );
                _debugPosSet = _gd.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(_pipeline.PositionLayout, _debugPosBuffer)
                );
            }
            if (_pipeline.ColorLayout != null && _debugColorBuffer == null)
            {
                _debugColorBuffer = _gd.ResourceFactory.CreateBuffer(
                    new BufferDescription(16, BufferUsage.UniformBuffer)
                );
                _debugColorSet = _gd.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(_pipeline.ColorLayout, _debugColorBuffer)
                );
            }
        }


        /// <summary>
        /// One-time setup of the shared circle mesh.  Called during
        /// Initialize(), BEFORE the first frame – so GD.UpdateBuffer is safe
        /// here (no swapchain images are in flight yet).
        /// </summary>
        public void InitializeInstancedRendering(float circleRadius, int segments)
        {
            var vertices = new VertexPosition[segments + 1];
            vertices[0] = new VertexPosition(0, 0);
            for (int i = 0; i < segments; i++)
            {
                float angle = 2f * MathF.PI * i / segments;
                vertices[i + 1] = new VertexPosition(
                    MathF.Cos(angle) * circleRadius,
                    MathF.Sin(angle) * circleRadius
                );
            }

            var indices = new ushort[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                indices[i * 3] = 0;
                indices[i * 3 + 1] = (ushort)(i + 1);
                indices[i * 3 + 2] = (ushort)((i + 1) % segments + 1);
            }

            _circleVB = _gd.ResourceFactory.CreateBuffer(
                new BufferDescription(
                    (uint)(vertices.Length * VertexPosition.SizeInBytes),
                    BufferUsage.VertexBuffer
                )
            );
            _gd.UpdateBuffer(_circleVB, 0, vertices);

            _circleIB = _gd.ResourceFactory.CreateBuffer(
                new BufferDescription(
                    (uint)(indices.Length * sizeof(ushort)),
                    BufferUsage.IndexBuffer
                )
            );
            _gd.UpdateBuffer(_circleIB, 0, indices);

            _circleIndexCount = indices.Length;
        }

        public void DrawInstanced(
            List<Entity> entities,
            Vector2[] positions,
            Vector4[]? colors = null
        )
        {
            var instancedPipeline = _pipeline.GetInstancedPipeline();
            if (instancedPipeline == null || _circleVB == null)
                return;
            if (entities.Count == 0 || positions.Length == 0)
                return;

            int count = Math.Min(entities.Count, positions.Length);
            int requiredBytes = count * 24;


            if (_instanceBuffer == null || _instanceCapacity < count)
            {
                _instanceBuffer?.Dispose();
                _instanceBuffer = _gd.ResourceFactory.CreateBuffer(
                    new BufferDescription((uint)requiredBytes, BufferUsage.VertexBuffer)
                );
                _instanceCapacity = count;
            }

            var data = new float[count * 6];
            for (int i = 0; i < count; i++)
            {
                int o = i * 6;
                data[o] = positions[i].X;
                data[o + 1] = positions[i].Y;
                var c = (colors != null && i < colors.Length) ? colors[i] : entities[i].Colour;
                data[o + 2] = c.X;
                data[o + 3] = c.Y;
                data[o + 4] = c.Z;
                data[o + 5] = c.W;
            }

            _cl.UpdateBuffer(_instanceBuffer, 0, data);

            _cl.SetPipeline(instancedPipeline);
            _cl.SetVertexBuffer(0, _circleVB);
            _cl.SetVertexBuffer(1, _instanceBuffer);
            _cl.SetIndexBuffer(_circleIB!, IndexFormat.UInt16);

            if (_projectionSet != null)
                _cl.SetGraphicsResourceSet(0, _projectionSet);

            _cl.DrawIndexed((uint)_circleIndexCount, (uint)count, 0, 0, 0);
        }
    }
}
