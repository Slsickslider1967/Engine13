using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities;
using Vortice.DXGI;

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
        private float _velocityY;

        public float VelocityY => _velocityY;
        public float MomentumY => Mass * _velocityY;
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
            _velocityY = initialVelocity;
            Mass = mass;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            // If MD is active on this mesh, contribute gravity as a force and exit.
            var molecularDynamics = mesh.GetAttribute<MolecularDynamics>();
            if (molecularDynamics != null)
            {
                double effectiveMass = (mesh.Mass > 0f) ? mesh.Mass : 1.0;
                Forces.AddForce(mesh, new Vec2(0.0, effectiveMass * Acceleration));
                return;
            }

            float deltaTime = gameTime.DeltaTime;
            if (deltaTime <= 0f)
                return;

            var objectCollision = mesh.GetAttribute<ObjectCollision>();
            if (objectCollision != null && !objectCollision.IsStatic)
            {
                float velocityY = objectCollision.Velocity.Y;
                if (!(objectCollision.IsGrounded && velocityY >= 0f))
                {
                    velocityY += Acceleration * deltaTime;
                }
                float massForDrag = (objectCollision.Mass > 0f) ? objectCollision.Mass : Mass;
                float area =
                    (mesh.Size.X > 0f && mesh.Size.Y > 0f) ? (mesh.Size.X * mesh.Size.Y) : 1f;
                float effectiveDrag = DragCoefficient * area;
                float terminalVelocityMagnitude =
                    (effectiveDrag > 0f && Acceleration != 0f)
                        ? System.MathF.Sqrt(
                            System.MathF.Abs(massForDrag * Acceleration) / effectiveDrag
                        )
                        : float.PositiveInfinity;
                if (float.IsFinite(terminalVelocityMagnitude))
                {
                    if (velocityY > terminalVelocityMagnitude)
                        velocityY = terminalVelocityMagnitude;
                    else if (velocityY < -terminalVelocityMagnitude)
                        velocityY = -terminalVelocityMagnitude;
                }
                if (float.IsFinite(TerminalVelocityY))
                {
                    velocityY = System.Math.Clamp(velocityY, -TerminalVelocityY, TerminalVelocityY);
                }

                objectCollision.Velocity = new Vector2(objectCollision.Velocity.X, velocityY);
            }
            else
            {
                _velocityY += Acceleration * deltaTime;
                float area =
                    (mesh.Size.X > 0f && mesh.Size.Y > 0f) ? (mesh.Size.X * mesh.Size.Y) : 1f;
                float effectiveDrag = DragCoefficient * area;
                float terminalVelocityMagnitude =
                    (effectiveDrag > 0f && Acceleration != 0f)
                        ? System.MathF.Sqrt(System.MathF.Abs(Mass * Acceleration) / effectiveDrag)
                        : float.PositiveInfinity;
                if (float.IsFinite(terminalVelocityMagnitude))
                {
                    if (_velocityY > terminalVelocityMagnitude)
                        _velocityY = terminalVelocityMagnitude;
                    else if (_velocityY < -terminalVelocityMagnitude)
                        _velocityY = -terminalVelocityMagnitude;
                }
                if (float.IsFinite(TerminalVelocityY))
                {
                    _velocityY = System.Math.Clamp(
                        _velocityY,
                        -TerminalVelocityY,
                        TerminalVelocityY
                    );
                }
                var position = mesh.Position;
                position.Y += _velocityY * deltaTime;
                mesh.Position = position;
            }
        }
    }

    public sealed class MolecularDynamics : IMeshAttribute
    {
        private static readonly Random _random = new Random();

        private Vector2 _restAnchor;
        private bool _restAnchorInitialized;
        private float _phaseX;
        private float _phaseY;

        public float SpringConstant { get; set; } = 10f;
        public float DampingRatio { get; set; } = 1.1f;
        public float DriveAmplitude { get; set; } = 0.005f;
        public Vector2 DriveAxisWeights { get; set; } = new Vector2(0f, 1f);
        public float DriveFrequency { get; set; } = 4f;
        public bool EnableDrive { get; set; } = true;
        public float AnchorFollowFactor { get; set; } = 0f;
        public float MaxForceMagnitude { get; set; } = 5f;

        private void EnsureAnchorInitialized(Mesh mesh)
        {
            if (_restAnchorInitialized)
                return;

            _restAnchor = mesh.Position;
            _phaseX = (float)(_random.NextDouble() * (MathF.PI * 2f));
            _phaseY = (float)(_random.NextDouble() * (MathF.PI * 2f));
            _restAnchorInitialized = true;
        }

        public void ResetRestAnchor(Vector2 newAnchor)
        {
            _restAnchor = newAnchor;
            _restAnchorInitialized = true;
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            EnsureAnchorInitialized(mesh);

            if (AnchorFollowFactor > 0f)
            {
                float followT = Math.Clamp(AnchorFollowFactor * gameTime.DeltaTime, 0f, 1f);
                _restAnchor = Vector2.Lerp(_restAnchor, mesh.Position, followT);
            }

            float mass = (mesh.Mass > 0f) ? mesh.Mass : 1f;
            float springConstant = MathF.Max(SpringConstant, 1e-4f);
            float dampingCoefficient =
                2f * MathF.Sqrt(springConstant * mass) * MathF.Max(DampingRatio, 0f);

            Vector2 desiredOffset = Vector2.Zero;
            Vector2 desiredVelocity = Vector2.Zero;
            if (EnableDrive && DriveAmplitude != 0f && DriveFrequency > 0f)
            {
                float angularFrequency = 2f * MathF.PI * DriveFrequency;
                float time = gameTime.TotalTime;
                float phaseX = angularFrequency * time + _phaseX;
                float phaseY = angularFrequency * time + _phaseY;

                float amplitude = DriveAmplitude;
                desiredOffset = new Vector2(
                    DriveAxisWeights.X * amplitude * MathF.Sin(phaseX),
                    DriveAxisWeights.Y * amplitude * MathF.Sin(phaseY)
                );

                desiredVelocity = new Vector2(
                    DriveAxisWeights.X * amplitude * angularFrequency * MathF.Cos(phaseX),
                    DriveAxisWeights.Y * amplitude * angularFrequency * MathF.Cos(phaseY)
                );
            }

            Vector2 desiredPosition = _restAnchor + desiredOffset;
            Vector2 displacementError = mesh.Position - desiredPosition;
            Vector2 velocityError = mesh.Velocity - desiredVelocity;

            Vector2 springForce = -springConstant * displacementError;
            Vector2 dampingForce = -dampingCoefficient * velocityError;
            Vector2 totalForce = springForce + dampingForce;
            if (MaxForceMagnitude > 0f)
            {
                float magnitude = totalForce.Length();
                if (magnitude > MaxForceMagnitude)
                {
                    float scale = MaxForceMagnitude / magnitude;
                    totalForce *= scale;
                }
            }

            Forces.AddForce(mesh, new Vec2(totalForce.X, totalForce.Y));
        }
    }

    public sealed class EdgeCollision : IMeshAttribute
    {
        private bool Loop;

        public EdgeCollision(bool loop)
        {
            Loop = loop;
        }

        private (float left, float right, float top, float bottom) GetBounds()
        {
            return WindowBounds.GetNormalizedBounds();
        }

        private void HandleWallCollision(ObjectCollision objectCollision, bool isXAxis, float sleepVelocity)
        {
            if (objectCollision == null) return;
            
            if (isXAxis)
            {
                float velocityX = objectCollision.Velocity.X;
                if (MathF.Abs(velocityX) < sleepVelocity)
                {
                    velocityX = 0f;
                }
                else
                {
                    velocityX = -velocityX * objectCollision.Restitution;
                }
                objectCollision.Velocity = new Vector2(velocityX, objectCollision.Velocity.Y);
            }
            else
            {
                float velocityY = objectCollision.Velocity.Y;
                if (MathF.Abs(velocityY) < sleepVelocity)
                {
                    velocityY = 0f;
                }
                else
                {
                    velocityY = -velocityY * objectCollision.Restitution;
                }
                objectCollision.Velocity = new Vector2(objectCollision.Velocity.X, velocityY);
            }
        }

        public void Update(Mesh mesh, GameTime gameTime)
        {
            var (left, right, top, bottom) = GetBounds();
            
            if (Loop)
            {
                var position = mesh.Position;
                var halfWidth = mesh.Size.X * 0.5f;
                var halfHeight = mesh.Size.Y * 0.5f;
                
                // Horizontal boundaries
                if (position.X < left + halfWidth)
                {
                    position.X = left + halfWidth;
                }
                else if (position.X > right - halfWidth)
                {
                    position.X = right - halfWidth;
                }
                
                // Vertical boundaries
                if (position.Y < top + halfHeight)
                {
                    position.Y = top + halfHeight;
                }
                else if (position.Y > bottom - halfHeight)
                {
                    position.Y = bottom - halfHeight;
                }
                
                mesh.Position = position;
            }
            else
            {
                var position = mesh.Position;
                var objectCollision = mesh.GetAttribute<ObjectCollision>();
                const float sleepVelocity = 0.05f;
                const float recovery = 0.0005f;
                
                var halfWidth = mesh.Size.X * 0.5f;
                var halfHeight = mesh.Size.Y * 0.5f;

                if (position.X < left + halfWidth)
                {
                    position.X = left + halfWidth + recovery;
                    if (objectCollision != null)
                        HandleWallCollision(objectCollision, true, sleepVelocity);
                }
                // Right wall collision  
                else if (position.X > right - halfWidth)
                {
                    position.X = right - halfWidth - recovery;
                    if (objectCollision != null)
                        HandleWallCollision(objectCollision, true, sleepVelocity);
                }

                // Top wall collision
                if (position.Y < top + halfHeight)
                {
                    position.Y = top + halfHeight + recovery;
                    if (objectCollision != null)
                    {
                        HandleWallCollision(objectCollision, false, sleepVelocity);
                        objectCollision.IsGrounded = false;
                    }
                }
                // Bottom wall collision (floor)
                else if (position.Y > bottom - halfHeight)
                {
                    position.Y = bottom - halfHeight - recovery;
                    if (objectCollision != null)
                    {
                        HandleWallCollision(objectCollision, false, sleepVelocity);
                        objectCollision.IsGrounded = 
                            MathF.Abs(objectCollision.Velocity.Y) < sleepVelocity;
                    }
                }

                mesh.Position = position;
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
