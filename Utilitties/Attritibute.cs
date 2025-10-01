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
}