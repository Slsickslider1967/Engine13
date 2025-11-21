using System.Collections.Generic;
using System.Numerics;
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
                Vector2 prev = _prevPositions.TryGetValue(entity, out var p) ? p : entity.Position;
                Vector2 cur = entity.Position;
                Vector2 vel = (dt > 0.0) ? (cur - prev) / (float)dt : Vector2.Zero;
                entity.Velocity = vel;
                _prevPositions[entity] = cur;
            }
            for (int i = 0; i < _updatables.Count; i++)
            {
                _updatables[i].Update(gameTime);
            }
        }
    }
}
