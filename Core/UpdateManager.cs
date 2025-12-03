using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Engine13.Graphics;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;

namespace Engine13.Core
{
    public class UpdateManager
    {
        private readonly List<Entity> _entities = new();
        private readonly List<IUpdatable> _updatables = new();
        private readonly Dictionary<Entity, Vector2> _prevPositions = new();

        // Performance timing
        private readonly Stopwatch _timer = new();
        private long _frameCount = 0;
        private double _mdTime = 0,
            _gravityTime = 0,
            _collisionTime = 0,
            _edgeTime = 0,
            _forcesTime = 0,
            _velocityTime = 0;
        private const int REPORT_INTERVAL = 5; // Report every 5 frames for debugging

        public bool EnableParallelForces { get; set; } = true;
        public int ParallelThreshold { get; set; } = 100;

        public void Register(Entity entity)
        {
            if (entity != null)
            {
                _entities.Add(entity);
                _prevPositions[entity] = entity.Position;
                entity.Velocity = Vector2.Zero;
            }
        }

        public bool Unregister(Entity entity)
        {
            _prevPositions.Remove(entity);
            return _entities.Remove(entity);
        }

        public void Register(IUpdatable updatable)
        {
            if (updatable != null)
                _updatables.Add(updatable);
        }

        public bool Unregister(IUpdatable updatable) => _updatables.Remove(updatable);

        public void Clear()
        {
            _entities.Clear();
            _updatables.Clear();
            _prevPositions.Clear();
        }

        public void Update(GameTime gameTime)
        {
            Forces.Reset();

            var collisionUpdates = new List<(Entity entity, ObjectCollision attr)>();
            var lateUpdates = new List<(Entity entity, IEntityComponent attr)>();

            bool useParallel = EnableParallelForces && _entities.Count >= ParallelThreshold;

            if (useParallel)
            {
                var mdEntities = new List<(Entity entity, MolecularDynamics md)>();
                var gravityEntities = new List<(Entity entity, Gravity gravity)>();

                // First pass: categorize components
                for (int i = 0; i < _entities.Count; i++)
                {
                    var entity = _entities[i];
                    var components = entity.Components;

                    for (int j = 0; j < components.Count; j++)
                    {
                        var component = components[j];
                        switch (component)
                        {
                            case MolecularDynamics md:
                                mdEntities.Add((entity, md));
                                break;
                            case Gravity gravity:
                                gravityEntities.Add((entity, gravity));
                                break;
                            case ObjectCollision objectCollision:
                                collisionUpdates.Add((entity, objectCollision));
                                break;
                            case EdgeCollision edgeCollision:
                                lateUpdates.Add((entity, edgeCollision));
                                break;
                            default:
                                component.Update(entity, gameTime);
                                break;
                        }
                    }
                }

                // Parallel force calculation for MolecularDynamics
                _timer.Restart();
                Parallel.ForEach(
                    mdEntities,
                    pair =>
                    {
                        pair.md.Update(pair.entity, gameTime);
                    }
                );
                _timer.Stop();
                _mdTime += _timer.Elapsed.TotalMilliseconds;

                // Parallel force calculation for Gravity
                _timer.Restart();
                Parallel.ForEach(
                    gravityEntities,
                    pair =>
                    {
                        pair.gravity.Update(pair.entity, gameTime);
                    }
                );
                _timer.Stop();
                _gravityTime += _timer.Elapsed.TotalMilliseconds;
            }
            else
            {
                // Sequential path: Original behavior for smaller entity counts
                _timer.Restart();
                for (int i = 0; i < _entities.Count; i++)
                {
                    var entity = _entities[i];
                    var components = entity.Components;

                    for (int j = 0; j < components.Count; j++)
                    {
                        var component = components[j];
                        switch (component)
                        {
                            case ObjectCollision objectCollision:
                                collisionUpdates.Add((entity, objectCollision));
                                break;
                            case EdgeCollision edgeCollision:
                                lateUpdates.Add((entity, edgeCollision));
                                break;
                            default:
                                component.Update(entity, gameTime);
                                break;
                        }
                    }
                }
                _timer.Stop();
                _mdTime += _timer.Elapsed.TotalMilliseconds;
            }

            _timer.Restart();
            Forces.Apply(gameTime);
            _timer.Stop();
            _forcesTime += _timer.Elapsed.TotalMilliseconds;

            _timer.Restart();
            for (int i = 0; i < collisionUpdates.Count; i++)
            {
                var (entity, attr) = collisionUpdates[i];
                attr.Update(entity, gameTime);
            }
            _timer.Stop();
            _collisionTime += _timer.Elapsed.TotalMilliseconds;

            _timer.Restart();
            for (int i = 0; i < lateUpdates.Count; i++)
            {
                var (entity, attr) = lateUpdates[i];
                attr.Update(entity, gameTime);
            }
            _timer.Stop();
            _edgeTime += _timer.Elapsed.TotalMilliseconds;

            _timer.Restart();
            double dt = gameTime.DeltaTime;
            for (int i = 0; i < _entities.Count; i++)
            {
                var entity = _entities[i];
                var objCollision = entity.GetComponent<ObjectCollision>();

                // Only calculate velocity from position for entities without physics component
                if (objCollision == null)
                {
                    Vector2 prev = _prevPositions.TryGetValue(entity, out var p)
                        ? p
                        : entity.Position;
                    Vector2 cur = entity.Position;
                    Vector2 vel = (dt > 0.0) ? (cur - prev) / (float)dt : Vector2.Zero;
                    entity.Velocity = vel;
                }
                else
                {
                    // For entities with ObjectCollision, sync Entity.Velocity with component velocity
                    entity.Velocity = objCollision.Velocity;
                }

                _prevPositions[entity] = entity.Position;
            }
            _timer.Stop();
            _velocityTime += _timer.Elapsed.TotalMilliseconds;

            for (int i = 0; i < _updatables.Count; i++)
            {
                _updatables[i].Update(gameTime);
            }

            // Performance report
            _frameCount++;
            if (_frameCount % REPORT_INTERVAL == 0)
            {
                double avgMd = _mdTime / REPORT_INTERVAL;
                double avgGravity = _gravityTime / REPORT_INTERVAL;
                double avgForces = _forcesTime / REPORT_INTERVAL;
                double avgCollision = _collisionTime / REPORT_INTERVAL;
                double avgEdge = _edgeTime / REPORT_INTERVAL;
                double avgVelocity = _velocityTime / REPORT_INTERVAL;
                double total =
                    avgMd + avgGravity + avgForces + avgCollision + avgEdge + avgVelocity;

                //Logger.Log($"UpdateManager Performance ({REPORT_INTERVAL} frames): MD={avgMd:F1}ms, Gravity={avgGravity:F1}ms, Forces={avgForces:F1}ms, Collision={avgCollision:F1}ms, Total={total:F1}ms ({1000.0/total:F0}fps)");

                // Reset accumulators
                _mdTime =
                    _gravityTime =
                    _forcesTime =
                    _collisionTime =
                    _edgeTime =
                    _velocityTime =
                        0;
            }
        }
    }
}
