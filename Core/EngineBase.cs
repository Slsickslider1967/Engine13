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

        protected EngineBase(Sdl2Window window, GraphicsDevice graphicsDevice)
        {
            Window = window;
            GraphicsDevice = graphicsDevice;
            GameTime = new GameTime();
            PipeLineManager = new PipeLineManager(GraphicsDevice);
            PipeLineManager.LoadDefaultShaders();
            PipeLineManager.CreatePipeline();
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
                () => Draw()
            );
        }

        public void Run()
        {
            Initialize();
            _frameTimer.Start();

            _threadManager.Start();

            while (Window.Exists && _threadManager.IsRunning)
            {
                InputManager.Update(Window);
                Window.PumpEvents();

                if (!Window.Exists)
                {
                    break;
                }

                if (
                    Window.Width != GraphicsDevice.MainSwapchain.Framebuffer.Width
                    || Window.Height != GraphicsDevice.MainSwapchain.Framebuffer.Height
                )
                {
                    GraphicsDevice.ResizeMainWindow((uint)Window.Width, (uint)Window.Height);
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
