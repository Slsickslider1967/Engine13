using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;

namespace Engine13.Utilities.Attributes
{
    public interface IMeshAttribute
    {
        void Update(Mesh mesh, GameTime gameTime);
    }

    public sealed class Gravity : IMeshAttribute
    {
        public float Acceleration { get; set; }
        public float Mass { get; set; } = 1f;
        public float TerminalVelocityY { get; set; } = float.PositiveInfinity;
        public float DragCoefficient { get; set; } = 1f;
        private float _vy;

        public float VelocityY => _vy;
        public float MomentumY => Mass * _vy;
        public float ComputedTerminalVelocityMag
        {
            get
            {
                if (DragCoefficient <= 0f || Acceleration == 0f)
                    return float.PositiveInfinity;
                return System.MathF.Sqrt(System.MathF.Abs(Mass * Acceleration) / DragCoefficient);
            }
        }

        public Gravity(float acceleration, float initialVelocity = 0f, float mass = 1f)
        {
            Acceleration = acceleration;
            _vy = initialVelocity;
            Mass = mass;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            float dt = gameTime.DeltaTime;
            if (dt <= 0f)
                return;
            var obj = mesh.GetAttribute<ObjectCollision>();
            if (obj != null && !obj.IsStatic)
            {
                float vy = obj.Velocity.Y;
                if (!(obj.IsGrounded && vy >= 0f))
                {
                    vy += Acceleration * dt;
                }
                float massForDrag = (obj.Mass > 0f) ? obj.Mass : Mass;
                float area =
                    (mesh.Size.X > 0f && mesh.Size.Y > 0f) ? (mesh.Size.X * mesh.Size.Y) : 1f;
                float kEff = DragCoefficient * area;
                float vtMag =
                    (kEff > 0f && Acceleration != 0f)
                        ? System.MathF.Sqrt(System.MathF.Abs(massForDrag * Acceleration) / kEff)
                        : float.PositiveInfinity;
                if (float.IsFinite(vtMag))
                {
                    if (vy > vtMag)
                        vy = vtMag;
                    else if (vy < -vtMag)
                        vy = -vtMag;
                }
                if (float.IsFinite(TerminalVelocityY))
                {
                    vy = System.Math.Clamp(vy, -TerminalVelocityY, TerminalVelocityY);
                }

                obj.Velocity = new Vector2(obj.Velocity.X, vy);
            }
            else
            {
                _vy += Acceleration * dt;
                float area =
                    (mesh.Size.X > 0f && mesh.Size.Y > 0f) ? (mesh.Size.X * mesh.Size.Y) : 1f;
                float kEff = DragCoefficient * area;
                float vtMag =
                    (kEff > 0f && Acceleration != 0f)
                        ? System.MathF.Sqrt(System.MathF.Abs(Mass * Acceleration) / kEff)
                        : float.PositiveInfinity;
                if (float.IsFinite(vtMag))
                {
                    if (_vy > vtMag)
                        _vy = vtMag;
                    else if (_vy < -vtMag)
                        _vy = -vtMag;
                }
                if (float.IsFinite(TerminalVelocityY))
                {
                    _vy = System.Math.Clamp(_vy, -TerminalVelocityY, TerminalVelocityY);
                }
                var p = mesh.Position;
                p.Y += _vy * dt;
                mesh.Position = p;
            }
        }
    }

    public sealed class AtomicWiggle : IMeshAttribute
    {
        public enum WiggleMode
        {
            Local,
            Path,
        }

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

        private const float Tau = 6.2831855f;
        private const float Eps = 1e-5f;
        private float _time;
        private float _distance;
        private int _dirSign = 1;

        public AtomicWiggle(float frequency = 10f, float amplitude = 0.01f)
        {
            Mode = WiggleMode.Local;
            Frequency = frequency;
            XAmplitude = amplitude;
            YAmplitude = amplitude;
        }

        public AtomicWiggle(
            Vector2 start,
            Vector2 end,
            float amplitude,
            float wavelength,
            float speed,
            bool loop = true,
            bool pingPong = false
        )
        {
            Mode = WiggleMode.Path;
            Start = start;
            End = end;
            PathAmplitude = amplitude;
            Wavelength = wavelength;
            Speed = speed;
            Loop = loop;
            PingPong = pingPong;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            float dt = gameTime.DeltaTime;
            if (dt <= 0f)
                return;

            if (Mode == WiggleMode.Local)
            {
                UpdateLocal(ref mesh, dt);
                return;
            }

            Vector2 ab = End - Start;
            float len = ab.Length();
            if (len < Eps)
            {
                Mode = WiggleMode.Local;
                UpdateLocal(ref mesh, dt);
                return;
            }

            var dir = ab / len;
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
            var p = mesh.Position;
            p.X += x;
            p.Y += y;
            mesh.Position = p;
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
                if (_distance < 0f)
                    _distance += pathLength;
            }
            else
            {
                _distance = System.Math.Clamp(_distance, 0f, pathLength);
            }
        }
    }

    public sealed class MolecularDynamics : IMeshAttribute
    {
        
        public void Update(Mesh mesh, GameTime gameTime)
        {
            // Molecular dynamics logic to be implemented
        }
    } 

    public sealed class EdgeCollision : IMeshAttribute
    {
        private float Top = -1f,
            Left = -1f,
            Right = 1f,
            Bottom = 1f;
        private bool Loop;

        public EdgeCollision(bool loop)
        {
            Loop = loop;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            if (Loop == true)
            {
                var p = mesh.Position;
                if (p.X < Left)
                {
                    p.X = Left;
                }
                else if (p.X > Right)
                {
                    p.X = Right;
                }
                if (p.Y < Top)
                {
                    p.Y = Top;
                }
                else if (p.Y > Bottom)
                {
                    p.Y = -1f;
                }
                mesh.Position = new Vector2(p.X, p.Y);
            }
            else
            {
                var p = mesh.Position;
                var objCollision = mesh.GetAttribute<ObjectCollision>();
                const float sleepVelocity = 0.05f;
                const float recovery = 0.0005f;

                if (p.X < Left + (mesh.Size.X / 2))
                {
                    p.X = Left + (mesh.Size.X / 2);
                    if (objCollision != null)
                    {
                        float vx = objCollision.Velocity.X;
                        if (System.MathF.Abs(vx) < sleepVelocity)
                        {
                            vx = 0f;
                        }
                        else
                        {
                            vx = -vx * objCollision.Restitution;
                        }
                        objCollision.Velocity = new Vector2(vx, objCollision.Velocity.Y);
                    }
                    p.X += recovery;
                }
                else if (p.X > Right - (mesh.Size.X / 2))
                {
                    p.X = Right - (mesh.Size.X / 2);
                    if (objCollision != null)
                    {
                        float vx = objCollision.Velocity.X;
                        if (System.MathF.Abs(vx) < sleepVelocity)
                        {
                            vx = 0f;
                        }
                        else
                        {
                            vx = -vx * objCollision.Restitution;
                        }
                        objCollision.Velocity = new Vector2(vx, objCollision.Velocity.Y);
                    }
                    p.X -= recovery;
                }
                if (p.Y < Top + (mesh.Size.Y / 2))
                {
                    p.Y = Top + (mesh.Size.Y / 2);
                    if (objCollision != null)
                    {
                        float vy = objCollision.Velocity.Y;
                        if (System.MathF.Abs(vy) < sleepVelocity)
                        {
                            vy = 0f;
                        }
                        else
                        {
                            vy = -vy * objCollision.Restitution;
                        }
                        objCollision.Velocity = new Vector2(objCollision.Velocity.X, vy);
                        objCollision.IsGrounded = false;
                    }
                    p.Y += recovery;
                }
                else if (p.Y > Bottom - (mesh.Size.Y / 2))
                {
                    p.Y = Bottom - (mesh.Size.Y / 2);
                    if (objCollision != null)
                    {
                        float vy = objCollision.Velocity.Y;
                        if (System.MathF.Abs(vy) < sleepVelocity)
                        {
                            vy = 0f;
                            objCollision.IsGrounded = true;
                        }
                        else
                        {
                            vy = -vy * objCollision.Restitution;
                            objCollision.IsGrounded = false;
                        }
                        objCollision.Velocity = new Vector2(objCollision.Velocity.X, vy);
                    }
                    p.Y -= recovery;
                }
                mesh.Position = new Vector2(p.X, p.Y);
            }
        }
    }

    public sealed class ObjectCollision : IMeshAttribute
    {
        public float Mass { get; set; } = 1f;
        public float Restitution { get; set; } = 0.8f;
        public float Friction { get; set; } = 0.5f;
        public Vector2 Velocity { get; set; } = Vector2.Zero;
        public bool IsStatic { get; set; } = false;
        public bool IsGrounded { get; set; } = false;

        // Transient WasGroundedThisFrame removed; IsGrounded suffices for gravity logic

        public void Update(Mesh mesh, GameTime gameTime)
        {
            // Handle velocity and physics
            if (!IsStatic)
            {
                var pos = mesh.Position;
                pos += Velocity * gameTime.DeltaTime;
                mesh.Position = pos;
                // Damping only (micro-bounce sleep removed)
                Velocity *= 0.99f;
            }
        }
    }
}
