using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities;

namespace Engine13.Utilities.Attributes
{
    public interface IEntityComponent
    {
        void Update(Entity entity, GameTime gameTime);
    }

    public sealed class Gravity : IEntityComponent
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
                return MathHelpers.ComputeTerminalVelocityMag(Mass, Acceleration, DragCoefficient);
            }
        }

        public Gravity(float acceleration, float initialVelocity = 0f, float mass = 1f)
        {
            Acceleration = acceleration;
            _velocityY = initialVelocity;
            Mass = mass;
        }

        public void Update(Entity entity, GameTime gameTime)
        {
            var molecularDynamics = entity.GetComponent<MolecularDynamics>();
            if (molecularDynamics != null)
            {
                double effectiveMass = (entity.Mass > 0f) ? entity.Mass : 1.0;
                Forces.AddForce(entity, new Vec2(0.0, effectiveMass * Acceleration));
                return;
            }

            float deltaTime = gameTime.DeltaTime;
            if (deltaTime <= 0f)
                return;

            var objectCollision = entity.GetComponent<ObjectCollision>();
            if (objectCollision != null && !objectCollision.IsStatic)
            {
                float velocityY = objectCollision.Velocity.Y;
                if (!(objectCollision.IsGrounded && velocityY >= 0f))
                {
                    velocityY += Acceleration * deltaTime;
                }
                float massForDrag = (objectCollision.Mass > 0f) ? objectCollision.Mass : Mass;
                float area = MathHelpers.ComputeArea(entity.Size);
                float effectiveDrag = DragCoefficient * area;
                float terminalVelocityMagnitude = MathHelpers.ComputeTerminalVelocityMag(
                    massForDrag,
                    Acceleration,
                    effectiveDrag
                );
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
                float area = MathHelpers.ComputeArea(entity.Size);
                float effectiveDrag = DragCoefficient * area;
                float terminalVelocityMagnitude = MathHelpers.ComputeTerminalVelocityMag(
                    Mass,
                    Acceleration,
                    effectiveDrag
                );
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
                var position = entity.Position;
                position.Y += _velocityY * deltaTime;
                entity.Position = position;
            }
        }
    }

    public sealed class MolecularDynamics : IEntityComponent
    {
        private static readonly Random _random = new Random();
        private static readonly HashSet<(Entity, Entity)> _globalBonds = new();
        private static readonly Dictionary<Entity, int> _bondCounts = new();

        private readonly List<Entity>? _allEntities;

        private Vector2 _restAnchor;
        private bool _restAnchorInitialized;
        private float _phaseX;
        private float _phaseY;

        // Local (single-particle) oscillator parameters
        public bool EnableAnchorOscillation { get; set; } = false;
        public float SpringConstant { get; set; } = 10f;
        public float DampingRatio { get; set; } = 1.1f;
        public float DriveAmplitude { get; set; } = 0.005f;
        public float DriveFrequency { get; set; } = 4f;
        public bool EnableDrive { get; set; } = true;

        // Multi-particle interaction parameters
        public bool EnableInteractions { get; set; } = true;
        public bool EnableBonds { get; set; } = true;
        public bool EnableLennardJones { get; set; } = true;
        public int MaxBondsPerEntity { get; set; } = 6;
        public float BondSpringConstant { get; set; } = 50f;
        public float BondEquilibriumLength { get; set; } = 0.025f;
        public float BondCutoffDistance { get; set; } = 0.035f;
        public float LJ_Epsilon { get; set; } = 0.005f;
        public float LJ_Sigma { get; set; } = 0.02f;
        public float LJ_CutoffRadius { get; set; } = 0.08f;

        public float MaxForceMagnitude { get; set; } = 25f;

        public MolecularDynamics() { }

        public MolecularDynamics(List<Entity> allEntities)
        {
            _allEntities = allEntities;
        }

        private void EnsureAnchorInitialized(Entity entity)
        {
            if (_restAnchorInitialized)
                return;

            _restAnchor = entity.Position;
            _phaseX = (float)(_random.NextDouble() * MathF.PI * 2f);
            _phaseY = (float)(_random.NextDouble() * MathF.PI * 2f);
            _restAnchorInitialized = true;
        }

        public void Update(Entity entity, GameTime gameTime)
        {
            Vector2 totalForce = Vector2.Zero;

            if (EnableAnchorOscillation)
            {
                totalForce += ComputeAnchorForce(entity, gameTime);
            }

            if (EnableInteractions && _allEntities != null && _allEntities.Count > 0)
            {
                totalForce += ComputeInteractionForces(entity);
            }

            if (totalForce == Vector2.Zero)
                return;

            if (MaxForceMagnitude > 0f)
            {
                float magnitude = totalForce.Length();
                if (magnitude > MaxForceMagnitude)
                {
                    totalForce *= MaxForceMagnitude / magnitude;
                }
            }

            Forces.AddForce(entity, new Vec2(totalForce.X, totalForce.Y));
        }

        private Vector2 ComputeAnchorForce(Entity entity, GameTime gameTime)
        {
            EnsureAnchorInitialized(entity);

            float mass = (entity.Mass > 0f) ? entity.Mass : 1f;

            float k = MathF.Max(SpringConstant, 1e-4f);
            float c = 2f * MathF.Sqrt(k * mass) * MathF.Max(DampingRatio, 0f);

            Vector2 targetPosition = _restAnchor;
            Vector2 targetVelocity = Vector2.Zero;

            if (EnableDrive && DriveAmplitude > 0f && DriveFrequency > 0f)
            {
                float omega = 2f * MathF.PI * DriveFrequency;
                float t = gameTime.TotalTime;

                float offsetX = DriveAmplitude * MathF.Sin(omega * t + _phaseX);
                float offsetY = DriveAmplitude * MathF.Sin(omega * t + _phaseY);
                targetPosition += new Vector2(offsetX, offsetY);

                float velX = DriveAmplitude * omega * MathF.Cos(omega * t + _phaseX);
                float velY = DriveAmplitude * omega * MathF.Cos(omega * t + _phaseY);
                targetVelocity = new Vector2(velX, velY);
            }

            Vector2 posError = entity.Position - targetPosition;
            Vector2 velError = entity.Velocity - targetVelocity;

            Vector2 springForce = -k * posError;
            Vector2 dampingForce = -c * velError;
            return springForce + dampingForce;
        }

        private Vector2 ComputeInteractionForces(Entity entity)
        {
            if (_allEntities == null || _allEntities.Count == 0)
                return Vector2.Zero;

            Vector2 totalForce = Vector2.Zero;

            if (EnableBonds)
            {
                UpdateBonds(entity);
            }

            for (int i = 0; i < _allEntities.Count; i++)
            {
                var other = _allEntities[i];
                if (other == entity)
                    continue;

                Vector2 delta = other.Position - entity.Position;
                float distSq = delta.LengthSquared();
                if (distSq < 1e-8f)
                    continue;

                float dist = MathF.Sqrt(distSq);
                Vector2 direction = delta / dist;

                bool isBonded = EnableBonds && IsBonded(entity, other);

                if (isBonded)
                {
                    float displacement = dist - BondEquilibriumLength;
                    float forceMagnitude = BondSpringConstant * displacement;
                    totalForce += direction * forceMagnitude;
                }
                else if (EnableLennardJones && dist < LJ_CutoffRadius)
                {
                    float sigmaOverR = LJ_Sigma / dist;
                    float sr6 = sigmaOverR * sigmaOverR * sigmaOverR;
                    sr6 *= sr6;
                    float sr12 = sr6 * sr6;

                    float forceMagnitude = 24f * LJ_Epsilon / dist * (sr6 - 2f * sr12);
                    totalForce += direction * forceMagnitude;
                }
            }

            return totalForce;
        }

        private void UpdateBonds(Entity entity)
        {
            if (_allEntities == null || _allEntities.Count == 0)
                return;

            int availableSlots = int.MaxValue;
            if (MaxBondsPerEntity > 0)
            {
                int current = GetBondCount(entity);
                if (current >= MaxBondsPerEntity)
                    return;
                availableSlots = MaxBondsPerEntity - current;
            }

            float bondCutoffSq = BondCutoffDistance * BondCutoffDistance;

            for (int i = 0; i < _allEntities.Count && availableSlots > 0; i++)
            {
                var other = _allEntities[i];
                if (other == entity)
                    continue;

                if (MaxBondsPerEntity > 0 && GetBondCount(other) >= MaxBondsPerEntity)
                    continue;

                float distSq = (other.Position - entity.Position).LengthSquared();
                if (distSq >= bondCutoffSq)
                    continue;

                bool aFirst = entity.GetHashCode() < other.GetHashCode();
                var bond = aFirst ? (entity, other) : (other, entity);

                if (_globalBonds.Add(bond))
                {
                    IncrementBondCount(entity);
                    IncrementBondCount(other);
                    availableSlots--;
                }
            }
        }

        private static int GetBondCount(Entity entity)
        {
            return _bondCounts.TryGetValue(entity, out var count) ? count : 0;
        }

        private static void IncrementBondCount(Entity entity)
        {
            _bondCounts[entity] = GetBondCount(entity) + 1;
        }

        private static bool IsBonded(Entity a, Entity b)
        {
            var bond = a.GetHashCode() < b.GetHashCode() ? (a, b) : (b, a);
            return _globalBonds.Contains(bond);
        }

        public static float CalculateKineticEnergy(List<Entity> entities)
        {
            float totalKE = 0f;
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                float mass = entity.Mass > 0f ? entity.Mass : 1f;
                float velSq = entity.Velocity.LengthSquared();
                totalKE += 0.5f * mass * velSq;
            }
            return totalKE;
        }

        public static float CalculatePotentialEnergy(List<Entity> entities)
        {
            float totalPE = 0f;
            var processedPairs = new HashSet<(Entity, Entity)>();

            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                var comp = entity.GetComponent<MolecularDynamics>();
                if (comp == null)
                    continue;

                for (int j = i + 1; j < entities.Count; j++)
                {
                    var other = entities[j];
                    if (!processedPairs.Add((entity, other)))
                        continue;

                    Vector2 delta = other.Position - entity.Position;
                    float dist = delta.Length();
                    if (dist < 1e-8f)
                        continue;

                    bool bonded = comp.EnableBonds && IsBonded(entity, other);

                    if (bonded && comp.EnableBonds)
                    {
                        float displacement = dist - comp.BondEquilibriumLength;
                        totalPE += 0.5f * comp.BondSpringConstant * displacement * displacement;
                    }
                    else if (comp.EnableLennardJones && dist < comp.LJ_CutoffRadius)
                    {
                        float sigmaOverR = comp.LJ_Sigma / dist;
                        float sr6 = sigmaOverR * sigmaOverR * sigmaOverR;
                        sr6 *= sr6;
                        float sr12 = sr6 * sr6;
                        totalPE += 4f * comp.LJ_Epsilon * (sr12 - sr6);
                    }
                }
            }

            return totalPE;
        }

        public static void ClearAllBonds()
        {
            _globalBonds.Clear();
            _bondCounts.Clear();
        }
    }

    public sealed class EdgeCollision : IEntityComponent
    {
        private readonly bool _loop;

        public EdgeCollision(bool loop)
        {
            _loop = loop;
        }

        private (float left, float right, float top, float bottom) GetBounds()
        {
            return WindowBounds.GetNormalizedBounds();
        }

        private void HandleWallCollision(
            ObjectCollision objectCollision,
            bool isXAxis,
            float sleepVelocity
        )
        {
            if (objectCollision == null)
                return;

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

        public void Update(Entity entity, GameTime gameTime)
        {
            var (left, right, top, bottom) = GetBounds();

            if (_loop)
            {
                var position = entity.Position;
                var halfWidth = entity.Size.X * 0.5f;
                var halfHeight = entity.Size.Y * 0.5f;

                if (position.X < left + halfWidth)
                {
                    position.X = left + halfWidth;
                }
                else if (position.X > right - halfWidth)
                {
                    position.X = right - halfWidth;
                }

                if (position.Y < top + halfHeight)
                {
                    position.Y = top + halfHeight;
                }
                else if (position.Y > bottom - halfHeight)
                {
                    position.Y = bottom - halfHeight;
                }

                entity.Position = position;
            }
            else
            {
                var position = entity.Position;
                var objectCollision = entity.GetComponent<ObjectCollision>();
                const float sleepVelocity = 0.05f;
                const float recovery = 0.0005f;

                var halfWidth = entity.Size.X * 0.5f;
                var halfHeight = entity.Size.Y * 0.5f;

                if (position.X < left + halfWidth)
                {
                    position.X = left + halfWidth + recovery;
                    if (objectCollision != null)
                        HandleWallCollision(objectCollision, true, sleepVelocity);
                }
                else if (position.X > right - halfWidth)
                {
                    position.X = right - halfWidth - recovery;
                    if (objectCollision != null)
                        HandleWallCollision(objectCollision, true, sleepVelocity);
                }

                if (position.Y < top + halfHeight)
                {
                    position.Y = top + halfHeight + recovery;
                    if (objectCollision != null)
                    {
                        HandleWallCollision(objectCollision, false, sleepVelocity);
                        objectCollision.IsGrounded = false;
                    }
                }
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

                entity.Position = position;
            }
        }
    }

    public sealed class ObjectCollision : IEntityComponent
    {
        public float Mass { get; set; } = 1f;
        public float Restitution { get; set; } = 0.8f;
        public float Friction { get; set; } = 0.5f;
        public Vector2 Velocity { get; set; } = Vector2.Zero;
        public bool IsStatic { get; set; } = false;
        public bool IsGrounded { get; set; } = false;

        public void Update(Entity entity, GameTime gameTime)
        {
            if (!IsStatic)
            {
                var pos = entity.Position;
                pos += Velocity * gameTime.DeltaTime;
                entity.Position = pos;
                Velocity *= 0.99f;
            }
        }
    }
}

