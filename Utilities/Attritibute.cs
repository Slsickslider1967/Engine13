using Engine13.Core;
using Engine13.Graphics;

namespace Engine13.Utilities.Attributes
{
    // Contract for mesh-attached behaviors
    public interface IMeshAttribute
    {
        // Called each frame to mutate the mesh (position, color, etc.)
        void Update(Mesh mesh, GameTime gameTime);
    }

    // Constant-acceleration gravity applied to a mesh's Y
    public sealed class GravityAttribute : IMeshAttribute
    {
        // Acceleration in units/sec^2 (negative if +Y is up)
        public float Acceleration { get; set; }

        // Internal vertical velocity state (units/sec)
        private float _vy;

        public GravityAttribute(float acceleration, float initialVelocity = 0f)
        {
            Acceleration = acceleration;
            _vy = initialVelocity;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            float dt = gameTime.DeltaTime;
            _vy += Acceleration * dt;              // v = v + a*dt
            var p = mesh.Position;
            p.Y += _vy * dt;                       // y = y + v*dt
            mesh.Position = p;
        }
    }

    public sealed class AtomicWiggle : IMeshAttribute
    {
        public float Frequency { get; set; } = 10f; // Oscillations per second
        public float Amplitude { get; set; } = 0.1f; // Max displacement in units

        private float _time;

        public AtomicWiggle(float frequency = 10f, float amplitude = 0.1f)
        {
            Frequency = frequency;
            Amplitude = amplitude;
            _time = 0f;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            _time += gameTime.DeltaTime;
            float offset = Amplitude * (float)System.Math.Sin(2 * System.Math.PI * Frequency * _time);
            var p = mesh.Position;
            p.Y += offset;
            mesh.Position = p;
        }
    }
}