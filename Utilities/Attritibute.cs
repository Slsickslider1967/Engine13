using Engine13.Core;
using Engine13.Graphics;
using System.Numerics;

namespace Engine13.Utilities.Attributes
{
    public interface IMeshAttribute
    {
        // Called each frame to update the mesh called for update with the attrabute
        void Update(Mesh mesh, GameTime gameTime);
    }
    public sealed class GravityAttribute : IMeshAttribute
    {
        public float Acceleration { get; set; }     //Gravity in this case
        public float Mass { get; set; } = 1f;     
        public float Restitution { get; set; } = 0.6f; // 0 is no rubber 1 is max rubber
        public float GroundY { get; set; } = -1f;    // Floor 
        public float TerminalVelocityY { get; set; } = float.PositiveInfinity;
        private float VerticalVelocity; 

        // Derived
        public float VelocityY => VerticalVelocity;    
        public float MomentumY => Mass * VerticalVelocity;  

        public GravityAttribute(float acceleration, float initialVelocity = 0f, float mass = 1f, float restitution = 0.6f, float groundY = -1f)
        {
            Acceleration = acceleration;
            VerticalVelocity = initialVelocity;
            Mass = mass;
            Restitution = restitution; //Bounce
            GroundY = groundY;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            float dt = gameTime.DeltaTime;
            if (dt <= 0f) return;

            // Update velocity with Acceleration and velocity
            VerticalVelocity = System.Math.Clamp(VerticalVelocity + Acceleration * dt, -TerminalVelocityY, TerminalVelocityY);

            float currentY = mesh.Position.Y; //Current pos 
            float nextY = currentY + VerticalVelocity * dt; //Calculated next pos 

            // If it hits the floor, bound more or less
            if (VerticalVelocity < 0f && nextY <= GroundY)
            {
                float tHit = (GroundY - currentY) / VerticalVelocity;
                var p = mesh.Position;
                p.Y = GroundY;
                mesh.Position = p;
                VerticalVelocity = -VerticalVelocity * Restitution;

                float remaining = dt - tHit;
                if (remaining > 0f)
                {
                    VerticalVelocity = System.Math.Clamp(VerticalVelocity + Acceleration * remaining, -TerminalVelocityY, TerminalVelocityY);
                    p.Y += VerticalVelocity * remaining;
                    if (p.Y < GroundY && VerticalVelocity < 0f)
                    {
                        p.Y = GroundY;
                        VerticalVelocity = -VerticalVelocity * Restitution;
                    }
                    mesh.Position = p;
                }
             
                if (System.MathF.Abs(VerticalVelocity) < 1e-3f) //1 x 10^-3
                {
                    VerticalVelocity = 0f;
                    var s = mesh.Position;
                    s.Y = GroundY;
                    mesh.Position = s;
                }
            }
            else
            {
                var p = mesh.Position;
                p.Y = nextY;
                mesh.Position = p;
            }
        }
    }

    public sealed class AtomicWiggle : IMeshAttribute
    {
        // Two modes: local orbit-like wiggle (default), or path-based sine along a line
        public enum WiggleMode { Local, Path }

        // Common
        public WiggleMode Mode { get; private set; } = WiggleMode.Local;
        public float Frequency { get; set; } = 10f; // Oscillations per second
        public float XAmplitude { get; set; } = 0.1f; // Max X displacement
        public float YAmplitude { get; set; } = 0.1f; // Max Y displacement
        public Vector2 Start { get; private set; }
        public Vector2 End { get; private set; }
        public float PathAmplitude { get; set; } = 0.1f; // Normal offset amplitude
        public float Wavelength { get; set; } = 0.5f;    // Spatial wavelength along the path
        public float Speed { get; set; } = 0.5f;         // Units/sec along the path
        public bool Loop { get; set; } = true;           // Loop position along the segment
        public bool PingPong { get; set; } = false;      // Alternate direction at ends

        private float _time;
        private float _distance; // distance progressed along the path
        private int _dirSign = 1; // for ping-pong
        public AtomicWiggle(float frequency = 10f, float amplitude = 0.01f)
        {
            Mode = WiggleMode.Local;
            Frequency = frequency;
            XAmplitude = amplitude;
            YAmplitude = amplitude;
            _time = 0f;
        }

        // New constructor for path-based wiggle
        public AtomicWiggle(Vector2 start, Vector2 end, float amplitude, float wavelength, float speed, bool loop = true, bool pingPong = false)
        {
            Mode = WiggleMode.Path;
            Start = start;
            End = end;
            PathAmplitude = amplitude;
            Wavelength = wavelength;
            Speed = speed;
            Loop = loop;
            PingPong = pingPong;
            _time = 0f;
            _distance = 0f;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            if (Mode == WiggleMode.Local)
            {
                _time += gameTime.DeltaTime;
                float xOffset = XAmplitude * (float)System.Math.Sin(2 * System.Math.PI * Frequency * _time);
                float yOffset = YAmplitude * (float)System.Math.Cos(2 * System.Math.PI * Frequency * -_time);
                var p = mesh.Position;
                p.Y += yOffset;
                p.X += xOffset;
                mesh.Position = p;
                return;
            }

            // Path mode
            Vector2 AB = End - Start;
            float length = AB.Length();
            if (length < 1e-5f)
            {
                // Degenerate path: fallback to local behavior
                Mode = WiggleMode.Local;
                Update(mesh, gameTime);
                return;
            }

            var dir = Vector2.Normalize(AB);
            var normal = new Vector2(-dir.Y, dir.X);

            float dt = gameTime.DeltaTime;
            _distance += _dirSign * Speed * dt;

            // Handle ends
            if (PingPong && ( _distance > length || _distance < 0f ))
            {
                _dirSign *= -1;
                _distance = System.Math.Clamp(_distance, 0f, length);
            }
            else if (Loop)
            {
                // loop modulo length; protect against zero length above
                _distance %= length;
                if (_distance < 0f) _distance += length;
            }
            else
            {
                _distance = System.Math.Clamp(_distance, 0f, length);
            }

            // Spatial sine wave along the path
            float wl = (Wavelength <= 1e-5f) ? (length * 0.25f) : Wavelength;
            float y = PathAmplitude * (float)System.Math.Sin(2 * System.Math.PI * (_distance / wl));
            Vector2 pos = Start + dir * _distance + normal * y;
            mesh.Position = pos;
        }
    }
}