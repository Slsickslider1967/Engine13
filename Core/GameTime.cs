namespace Engine13.Core
{
    public class GameTime
    {
        public float DeltaTime { get; private set; } // Time in seconds since last frame
        public float TotalTime { get; private set; } // Total time in seconds since the start of the game
        private System.Diagnostics.Stopwatch _Timer = new System.Diagnostics.Stopwatch();
        private float _LastTime;  // Time in seconds at the last frame

        public GameTime()
        {
            _Timer.Start();
            _LastTime = 0f;
            TotalTime = 0f;
            DeltaTime = 0f;
        }

        public void Update()
        {
            float currentTime = (float)_Timer.Elapsed.TotalSeconds; // Get the current time in seconds
            DeltaTime = currentTime - _LastTime; // Calculate delta time
            TotalTime += DeltaTime; // Update total time
            _LastTime = currentTime; // Update last time to current time
        }
    }
}