namespace Engine13.Core
{
    public class GameTime
    {
        private const float MaxDeltaTime = 1f / 60f;
        private const float MinDeltaTime = 0f;

        public float DeltaTime { get; private set; }
        public float TotalTime { get; private set; }
        private System.Diagnostics.Stopwatch _Timer = new System.Diagnostics.Stopwatch();
        private float _LastTime;

        public GameTime()
        {
            _Timer.Start();
            _LastTime = 0f;
            TotalTime = 0f;
            DeltaTime = 0f;
        }

        public void Update()
        {
            float currentTime = (float)_Timer.Elapsed.TotalSeconds;
            float rawDelta = currentTime - _LastTime;
            if (rawDelta < MinDeltaTime)
                rawDelta = MinDeltaTime;
            if (rawDelta > MaxDeltaTime)
                rawDelta = MaxDeltaTime;

            DeltaTime = rawDelta;
            TotalTime += DeltaTime;
            _LastTime = currentTime;
        }
    }
}