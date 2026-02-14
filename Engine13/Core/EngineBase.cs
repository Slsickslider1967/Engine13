using System;
using System.Threading;
using System.Threading.Tasks;
using Engine13.Graphics;
using Veldrid;
using Veldrid.Sdl2;

namespace Engine13.Core
{
    public abstract class EngineBase : IDisposable
    {
        protected Sdl2Window Window { get; }
        protected GraphicsDevice GraphicsDevice { get; }
        protected GameTime GameTime { get; }
        protected PipeLineManager PipeLineManager { get; }
        protected Renderer Renderer { get; }

        // Shared CommandList used by Renderer and subsystems (ImGui)
        protected CommandList CommandList { get; private set; }
        protected Input.InputManager InputManager { get; }
        private System.Diagnostics.Stopwatch _frameTimer;
        private ThreadManager _threadManager;
        private bool _disposed;
        private volatile bool _resizePending;
        private uint _pendingWidth;
        private uint _pendingHeight;

        protected EngineBase(Sdl2Window window, GraphicsDevice graphicsDevice)
        {
            Window = window;
            GraphicsDevice = graphicsDevice;
            GameTime = new GameTime();
            PipeLineManager = new PipeLineManager(GraphicsDevice);
            PipeLineManager.LoadDefaultShaders();
            PipeLineManager.CreatePipeline();
            PipeLineManager.CreateInstancedPipeline();
            // Create a single shared CommandList for the renderer and other systems (ImGui)
            CommandList = GraphicsDevice.ResourceFactory.CreateCommandList();
            Renderer = new Renderer(GraphicsDevice, CommandList, PipeLineManager);
            InputManager = new Input.InputManager();
            InputManager.Attach(Window);
            _frameTimer = new System.Diagnostics.Stopwatch();

            _threadManager = new ThreadManager(
                () =>
                {
                    GameTime.Update();
                    Update(GameTime);
                },
                () => DrawWithResize()
            );
        }

        private void DrawWithResize()
        {
            if (_disposed)
            {
                return;
            }

            if (_resizePending)
            {
                _resizePending = false;
                try
                {
                    GraphicsDevice.WaitForIdle();
                    GraphicsDevice.ResizeMainWindow(_pendingWidth, _pendingHeight);
                }
                catch (Exception)
                {
                    // Swallow resize errors during transition
                }
            }

            // Safeguard: Don't create command list if device is disposed
            if (_disposed)
            {
                return;
            }

            ///<summary>
            /// Stops vulkan from throwing a fit when shutting down the program
            /// </summary>
            CommandList frameCL;
            try
            {
                frameCL = GraphicsDevice.ResourceFactory.CreateCommandList();
            }
            catch (Exception)
            {
                // Device may be disposed during shutdown
                return;
            }

            CommandList = frameCL;
            try
            {
                // Give the renderer the new command list to record into
                Renderer.SetCommandList(frameCL);
                Draw();
            }
            finally
            {
                try
                {
                    // Wait for GPU to finish all work before disposing
                    // the command list it may still be referencing
                    GraphicsDevice.WaitForIdle();
                }
                catch { }
                try
                {
                    frameCL.Dispose();
                }
                catch { }
            }
        }

        public void Run()
        {
            Initialize();
            _frameTimer.Start();

            _threadManager.Start();

            while (Window.Exists && _threadManager.IsRunning)
            {
                Window.PumpEvents();

                if (!Window.Exists)
                {
                    break;
                }

                // Queue resize for render thread instead of doing it here
                if (
                    Window.Width > 0
                    && Window.Height > 0
                    && (
                        Window.Width != GraphicsDevice.MainSwapchain.Framebuffer.Width
                        || Window.Height != GraphicsDevice.MainSwapchain.Framebuffer.Height
                    )
                )
                {
                    _pendingWidth = (uint)Window.Width;
                    _pendingHeight = (uint)Window.Height;
                    _resizePending = true;
                }

                Thread.Sleep(1);
            }

            _threadManager.Stop();
            
            // Give threads time to fully stop before disposing resources
            System.Threading.Thread.Sleep(200);

            Dispose();
        }

        protected virtual void Initialize() { }

        protected abstract void Update(GameTime gameTime);

        protected abstract void Draw();

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Set disposed flag first to prevent any new operations
                _disposed = true;
                
                // Dispose thread manager first (stops all threads)
                _threadManager?.Dispose();
                
                // Small delay to ensure threads have fully exited
                System.Threading.Thread.Sleep(50);
                
                // Now safe to dispose graphics resources
                try
                {
                    CommandList?.Dispose();
                }
                catch { }
                
                try
                {
                    GraphicsDevice.Dispose();
                }
                catch { }
            }
            else
            {
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
