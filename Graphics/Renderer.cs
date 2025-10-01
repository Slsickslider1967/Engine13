using Veldrid;
using Engine13.Graphics;
using System;
using System.Numerics;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities; 

namespace Engine13.Graphics
{
    public class Renderer
    {
        private GraphicsDevice GD;
        private CommandList CL;
        private PipeLineManager _Pipeline;
        private DeviceBuffer? _ProjectionBuffer;
        private ResourceSet? _ProjectionSet;

        public Renderer(GraphicsDevice _GD, CommandList _CL, PipeLineManager _Pipeline)
        {
            GD = _GD;
            CL = GD.ResourceFactory.CreateCommandList();
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
                    _ProjectionBuffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
                    _ProjectionSet = GD.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                        _Pipeline.ProjectionLayout,
                        _ProjectionBuffer
                    ));
                }

                // Build a simple orthographic matrix that preserves aspect ratio.
                float w = GD.MainSwapchain.Framebuffer.Width;
                float h = GD.MainSwapchain.Framebuffer.Height;
                // Map world -1..1 to clip space with aspect-correct scaling.
                // Scale X by h/w so a unit circle remains a circle.
                float sx = h / w; // aspect correction
                var proj = new Matrix4x4(
                    sx, 0,  0, 0,
                    0,  1,  0, 0,
                    0,  0,  1, 0,
                    0,  0,  0, 1
                );
                GD.UpdateBuffer(_ProjectionBuffer, 0, proj);
            }
        }

        public void EndFrame()
        {
            CL.End();
            GD.SubmitCommands(CL);
            GD.SwapBuffers(GD.MainSwapchain);
        }

        public void DrawMesh(Mesh mesh)
        {
            CL.SetPipeline(_Pipeline.GetPipeline());
            CL.SetVertexBuffer(0, mesh.VertexBuffer);
            CL.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt16);

            // Ensure the mesh has its position resources created (once). Use the pipeline manager's PositionLayout.
            if (_Pipeline.PositionLayout != null)
            {
                mesh.EnsurePositionResources(GD, _Pipeline.PositionLayout);
            }

            // Update the mesh's position buffer with the current position
            if (mesh.PositionBuffer != null)
            {
                var packed = new Vector4(mesh.Position.X, mesh.Position.Y, 0f, 0f);
                GD.UpdateBuffer(mesh.PositionBuffer, 0, packed);
            }

            // Bind the mesh's cached resource set
            if (mesh.PositionResourceSet != null)
            {
                CL.SetGraphicsResourceSet(0, mesh.PositionResourceSet);
            }

            // Bind projection set at slot 1 if created
            if (_ProjectionSet != null)
            {
                CL.SetGraphicsResourceSet(1, _ProjectionSet);
            }

            // Ensure and update per-mesh color (set=2)
            if (_Pipeline.ColorLayout != null)
            {
                mesh.EnsureColorResources(GD, _Pipeline.ColorLayout);
                if (mesh.ColorBuffer != null)
                {
                    GD.UpdateBuffer(mesh.ColorBuffer, 0, mesh.Color);
                }
                if (mesh.ColorResourceSet != null)
                {
                    CL.SetGraphicsResourceSet(2, mesh.ColorResourceSet);
                }
            }

            CL.DrawIndexed((uint)mesh.IndexCount, 1, 0, 0, 0);
        }
    }
}