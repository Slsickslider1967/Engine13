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
            var commandList = GraphicsDevice.ResourceFactory.CreateCommandList();
            Renderer = new Renderer(GraphicsDevice, commandList, PipeLineManager);
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
            // Handle resize on the render thread to avoid race conditions
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
            Draw();
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
                    Window.Width > 0 && Window.Height > 0 &&
                    (Window.Width != GraphicsDevice.MainSwapchain.Framebuffer.Width
                    || Window.Height != GraphicsDevice.MainSwapchain.Framebuffer.Height)
                )
                {
                    _pendingWidth = (uint)Window.Width;
                    _pendingHeight = (uint)Window.Height;
                    _resizePending = true;
                }

                Thread.Sleep(1);
            }

            _threadManager.Stop();

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
                _threadManager?.Dispose();
                GraphicsDevice.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
