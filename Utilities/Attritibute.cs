using Engine13.Core;
using Engine13.Graphics;
using SharpGen.Runtime.Win32;
using System.Numerics;

namespace Engine13.Utilities.Attributes
{
    public interface IMeshAttribute { void Update(Mesh mesh, GameTime gameTime); }
    public sealed class Gravity : IMeshAttribute
    {
        public float Acceleration { get; set; }
        public float Mass { get; set; } = 1f;
        public float TerminalVelocityY { get; set; } = float.PositiveInfinity; // legacy manual cap (optional)
        public float DragCoefficient { get; set; } = 1f; // k for quadratic drag; terminal |v| = sqrt(|m*g|/k)
        private float _vy;

        public float VelocityY => _vy;
        public float MomentumY => Mass *
        _vy;
        public float ComputedTerminalVelocityMag
        {
            get
            {
                if (DragCoefficient <= 0f || Acceleration == 0f) return float.PositiveInfinity;
                return System.MathF.Sqrt(System.MathF.Abs(Mass * Acceleration) / DragCoefficient);
            }
        }

        public Gravity(float acceleration, float initialVelocity = 0f, float mass = 1f)
        { Acceleration = acceleration; _vy = initialVelocity; Mass = mass; }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            float dt = gameTime.DeltaTime;
            if (dt <= 0f) return;
            _vy += Acceleration * dt;
            float vtMag = ComputedTerminalVelocityMag;
            if (float.IsFinite(vtMag))
            {
                if (_vy > vtMag) _vy = vtMag;
                else if (_vy < -vtMag) _vy = -vtMag;
            }
            if (float.IsFinite(TerminalVelocityY))
            {
                _vy = System.Math.Clamp(_vy, -TerminalVelocityY, TerminalVelocityY);
            }
            var p = mesh.Position; p.Y += _vy * dt; mesh.Position = p;
        }
    }

    public sealed class AtomicWiggle : IMeshAttribute
    {
        public enum WiggleMode { Local, Path }
        public WiggleMode Mode { get; private set; } = WiggleMode.Local;
        public float Frequency { get; set; } = 10f;
        public float XAmplitude { get; set; } = 0.1f;
        public float YAmplitude { get; set; } = 0.1f;
        public Vector2 Start { get; private set; }
        public Vector2 End { get; private set; }
        public float PathAmplitude { get; set; } = 0.1f;
        public float Wavelength { get; set; } = 0.5f;
        public float Speed { get; set; } = 0.5f;
        public bool Loop { get; set; } = true;
        public bool PingPong { get; set; } = false;

        private const float Tau = 6.2831855f;   // 2π
        private const float Eps = 1e-5f;
        private float _time;
        private float _distance;
        private int _dirSign = 1;

        public AtomicWiggle(float frequency = 10f, float amplitude = 0.01f)
        { Mode = WiggleMode.Local; Frequency = frequency; XAmplitude = amplitude; YAmplitude = amplitude; }

        public AtomicWiggle(Vector2 start, Vector2 end, float amplitude, float wavelength, float speed, bool loop = true, bool pingPong = false)
        { Mode = WiggleMode.Path; Start = start; End = end; PathAmplitude = amplitude; Wavelength = wavelength; Speed = speed; Loop = loop; PingPong = pingPong; }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            float dt = gameTime.DeltaTime;
            if (dt <= 0f) return;

            if (Mode == WiggleMode.Local)
            {
                UpdateLocal(ref mesh, dt);
                return;
            }

            Vector2 ab = End - Start; float len = ab.Length();
            if (len < Eps)
            {
                Mode = WiggleMode.Local;
                UpdateLocal(ref mesh, dt);
                return;
            }

            var dir = ab / len;             // normalized
            var normal = new Vector2(-dir.Y, dir.X);

            AdvanceDistance(len, dt);

            float wl = (Wavelength <= Eps) ? (len * 0.25f) : Wavelength;
            float angle = Tau * (_distance / wl);
            float offset = PathAmplitude * System.MathF.Sin(angle);

            mesh.Position = Start + dir * _distance + normal * offset;
        }

        private void UpdateLocal(ref Mesh mesh, float dt)
        {
            _time += dt;
            float phase = Tau * Frequency * _time;
            float x = XAmplitude * System.MathF.Sin(phase);
            float y = YAmplitude * System.MathF.Cos(-phase);
            var p = mesh.Position; p.X += x; p.Y += y; mesh.Position = p;
        }

        private void AdvanceDistance(float pathLength, float dt)
        {
            _distance += _dirSign * Speed * dt;
            if (PingPong)
            {
                if (_distance > pathLength || _distance < 0f)
                {
                    _dirSign *= -1;
                    _distance = System.Math.Clamp(_distance, 0f, pathLength);
                }
                return;
            }

            if (Loop)
            {
                _distance %= pathLength;
                if (_distance < 0f) _distance += pathLength;
            }
            else
            {
                _distance = System.Math.Clamp(_distance, 0f, pathLength);
            }
        }
    }

    public sealed class EdgeCollision : IMeshAttribute
    {
        private float Top = -1f, Left = -1f, Right = 1f, Bottom = 1f;
        public EdgeCollision() { }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            var p = mesh.Position;
            if (p.X < Left) { p.X = Left; }
            else if (p.X > Right) { p.X = Right; }
            if (p.Y < Top) { p.Y = Top; }
            else if (p.Y > Bottom) { p.Y = -1f; }
            mesh.Position = new Vector2(p.X, p.Y);
        }

    }

    public sealed class ObjectCollision : IMeshAttribute
    {
        public ObjectCollision() { }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            
        }
    }
}