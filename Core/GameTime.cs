using System.Diagnostics;

namespace Engine13.Core
{
    /// <summary>
    /// Manages game time tracking with delta time clamping for stable physics simulation.
    /// </summary>
    public sealed class GameTime
    {
        private const float MaxDeltaTime = 1f / 60f;
        private const float MinDeltaTime = 0f;

        private readonly Stopwatch _timer = new();
        private float _lastTime;

        /// <summary>Time elapsed since last frame in seconds.</summary>
        public float DeltaTime { get; private set; }

        /// <summary>Total time elapsed since game start in seconds.</summary>
        public float TotalTime { get; private set; }

        public GameTime()
        {
            _timer.Start();
            _lastTime = 0f;
            TotalTime = 0f;
            DeltaTime = 0f;
        }

        /// <summary>Updates time values. Call once per frame.</summary>
        public void Update()
        {
            float currentTime = (float)_timer.Elapsed.TotalSeconds;
            float rawDelta = currentTime - _lastTime;
            
            DeltaTime = ClampDelta(rawDelta);
            TotalTime += DeltaTime;
            _lastTime = currentTime;
        }

        /// <summary>Overrides delta time for fixed-timestep simulation.</summary>
        public void OverrideDeltaTime(float deltaTime)
        {
            DeltaTime = ClampDelta(deltaTime);
            TotalTime += DeltaTime;
        }

        private static float ClampDelta(float delta)
        {
            if (delta < MinDeltaTime) return MinDeltaTime;
            if (delta > MaxDeltaTime) return MaxDeltaTime;
            return delta;
        }
    }
}
