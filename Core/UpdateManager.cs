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
                var pdEntities = new List<(Entity entity, ParticleDynamics pd)>();
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
                            case ParticleDynamics pd:
                                pdEntities.Add((entity, pd));
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

                // Parallel force calculation for ParticleDynamics
                Parallel.ForEach(
                    pdEntities,
                    pair =>
                    {
                        pair.pd.Update(pair.entity, gameTime);
                    }
                );

                // Parallel force calculation for Gravity
                Parallel.ForEach(
                    gravityEntities,
                    pair =>
                    {
                        pair.gravity.Update(pair.entity, gameTime);
                    }
                );
            }
            else
            {
                // Sequential path: Original behavior for smaller entity counts
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
            }

            Forces.Apply(gameTime);

            for (int i = 0; i < collisionUpdates.Count; i++)
            {
                var (entity, attr) = collisionUpdates[i];
                attr.Update(entity, gameTime);
            }

            for (int i = 0; i < lateUpdates.Count; i++)
            {
                var (entity, attr) = lateUpdates[i];
                attr.Update(entity, gameTime);
            }

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

            for (int i = 0; i < _updatables.Count; i++)
            {
                _updatables[i].Update(gameTime);
            }
        }
    }
}
