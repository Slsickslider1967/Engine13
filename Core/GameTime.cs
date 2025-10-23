namespace Engine13.Core
{
    public class GameTime
    {
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
            DeltaTime = currentTime - _LastTime; 
            TotalTime += DeltaTime;
            _LastTime = currentTime; 
        }
    }
}