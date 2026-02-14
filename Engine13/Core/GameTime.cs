using System.Diagnostics;

namespace Engine13.Core
{
    public sealed class GameTime
    {
        private const float MaxDeltaTime = 1f / 60f, MinDeltaTime = 0f;
        private readonly Stopwatch _timer = new();
        private float _lastTime;

        public float DeltaTime { get; private set; }
        public float TotalTime { get; private set; }

        public GameTime() { _timer.Start(); _lastTime = TotalTime = DeltaTime = 0f; }

        public void Update()
        {
            float current = (float)_timer.Elapsed.TotalSeconds;
            DeltaTime = ClampDelta(current - _lastTime);
            TotalTime += DeltaTime;
            _lastTime = current;
        }

        public void OverrideDeltaTime(float dt) { DeltaTime = ClampDelta(dt); TotalTime += DeltaTime; }
        private static float ClampDelta(float d) => d < MinDeltaTime ? MinDeltaTime : (d > MaxDeltaTime ? MaxDeltaTime : d);
    }
}
