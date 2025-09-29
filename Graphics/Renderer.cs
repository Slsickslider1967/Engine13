using Veldrid;
using Engine13.Graphics;
using System;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities; 

namespace Engine13.Graphics
{
    public class Renderer
    {
        private GraphicsDevice GD;
        private CommandList CL;
        private PipeLineManager _Pipeline;

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

            DeviceBuffer PosBuffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(8, BufferUsage.UniformBuffer));
            GD.UpdateBuffer(PosBuffer, 0, mesh.Position);
            
            CL.SetGraphicsResourceSet(0, GD.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _Pipeline.GetPipeline().ResourceLayouts[0],
                PosBuffer
            )));

            CL.DrawIndexed((uint)mesh.IndexCount, 1, 0, 0, 0);
        }
    }
}