using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Engine13.Core
{
    public delegate void UpdateTask();
    public delegate void RenderTask();
    
    public class ThreadManager : IDisposable
    {
        private const int TargetFps = 30;
        private const double TargetFrameTime = 1.0 / TargetFps;

        private Thread _updateThread;
        private Thread _bufferThread;
        private Thread _renderThread;
        private volatile bool _isRunning;
        private readonly object _stateLock = new object();
        
        // Queue-based task management
        private readonly ConcurrentQueue<UpdateTask> _updateTaskQueue = new ConcurrentQueue<UpdateTask>();
        private readonly ConcurrentQueue<RenderTask> _renderTaskQueue = new ConcurrentQueue<RenderTask>();
        private readonly AutoResetEvent _updateTaskAvailable = new AutoResetEvent(false);
        private readonly AutoResetEvent _renderTaskAvailable = new AutoResetEvent(false);
        
        private System.Diagnostics.Stopwatch _frameTimer;
        private readonly Action _updateAction;
        private readonly Action _drawAction;
        private bool _disposed;

        public ThreadManager(Action updateAction, Action drawAction)
        {
            _updateAction = updateAction;
            _drawAction = drawAction;
            _frameTimer = new System.Diagnostics.Stopwatch();
            
            _updateThread = new Thread(UpdateLoop) { Name = "Update Thread", IsBackground = true };
            _renderThread = new Thread(RenderLoop) { Name = "Render Thread", IsBackground = true };
            _bufferThread = new Thread(BufferLoop) { Name = "Buffer Thread", IsBackground = true };
        }

        public void Start()
        {
            _frameTimer.Start();
            _isRunning = true;
            _updateThread.Start();
            _renderThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            
            // Signal threads to wake up and exit
            _updateTaskAvailable.Set();
            _renderTaskAvailable.Set();
            
            _updateThread?.Join(1000);
            _renderThread?.Join(1000);
        }

        public bool IsRunning => _isRunning;

        public object StateLock => _stateLock;

        // Queue management methods
        public void QueueUpdateTask(UpdateTask task)
        {
            _updateTaskQueue.Enqueue(task);
            _updateTaskAvailable.Set();
        }

        public void QueueRenderTask(RenderTask task)
        {
            _renderTaskQueue.Enqueue(task);
            _renderTaskAvailable.Set();
        }

        private void UpdateLoop()
        {
            while (_isRunning)
            {
                double frameStartTime = _frameTimer.Elapsed.TotalSeconds;
                
                // Execute legacy update action if provided
                if (_updateAction != null)
                {
                    lock (_stateLock)
                    {
                        _updateAction.Invoke();
                    }
                }
                
                // Process queued update tasks
                while (_updateTaskQueue.TryDequeue(out UpdateTask task))
                {
                    lock (_stateLock)
                    {
                        task?.Invoke();
                    }
                }
                
                double frameEndTime = _frameTimer.Elapsed.TotalSeconds;
                double frameTime = frameEndTime - frameStartTime;
                double sleepTime = TargetFrameTime - frameTime;
                
                if (sleepTime > 0)
                {
                    Thread.Sleep((int)(sleepTime * 1000));
                }
            }
        }

        private void RenderLoop()
        {
            while (_isRunning)
            {
                // Wait for render tasks to be available or timeout after 16ms (~60fps)
                _renderTaskAvailable.WaitOne(16);

                if (!_isRunning)
                    break;

                // Execute legacy draw action if provided
                if (_drawAction != null)
                {
                    lock (_stateLock)
                    {
                        _drawAction.Invoke();
                    }
                }

                // Process queued render tasks
                while (_renderTaskQueue.TryDequeue(out RenderTask task))
                {
                    lock (_stateLock)
                    {
                        task?.Invoke();
                    }
                }

                Thread.Sleep(1); // Small yield to prevent busy waiting
            }
        }

        private void BufferLoop()
        {

        }
        
        ///Using a seperate thread to log to console as it has a massive over head.
        private void LoggerLoop()
        {
            while(_isRunning)
            {

            }

        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                Stop();
                _updateTaskAvailable?.Dispose();
                _renderTaskAvailable?.Dispose();
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