using System.Collections.Generic;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;

namespace Engine13.Core
{
    // Central manager that updates meshes and arbitrary updatable components each frame
    public class UpdateManager
    {
        private readonly List<Mesh> _meshes = new();
        private readonly List<IUpdatable> _updatables = new();
        private readonly Dictionary<Mesh, Vector2> _prevPositions = new();

        public void Register(Mesh mesh)
        {
            if (mesh != null)
            {
                _meshes.Add(mesh);
                _prevPositions[mesh] = mesh.Position;
                mesh.Velocity = Vector2.Zero;
            }
        }

        public bool Unregister(Mesh mesh)
        {
            _prevPositions.Remove(mesh);
            return _meshes.Remove(mesh);
        }

        public void Register(IUpdatable updatable)
        {
            if (updatable != null)
                _updatables.Add(updatable);
        }

        public bool Unregister(IUpdatable updatable) => _updatables.Remove(updatable);

        public void Clear()
        {
            _meshes.Clear();
            _updatables.Clear();
            _prevPositions.Clear();
        }

        public void Update(GameTime gameTime)
        {
            Forces.Reset();

            var collisionUpdates = new List<(Mesh mesh, ObjectCollision attr)>();
            var lateUpdates = new List<(Mesh mesh, IMeshAttribute attr)>();

            for (int i = 0; i < _meshes.Count; i++)
            {
                var mesh = _meshes[i];
                var attributes = mesh.Attributes;

                for (int j = 0; j < attributes.Count; j++)
                {
                    var attribute = attributes[j];
                    switch (attribute)
                    {
                        case ObjectCollision objectCollision:
                            collisionUpdates.Add((mesh, objectCollision));
                            break;
                        case EdgeCollision edgeCollision:
                            lateUpdates.Add((mesh, edgeCollision));
                            break;
                        default:
                            attribute.Update(mesh, gameTime);
                            break;
                    }
                }
            }

            Forces.Apply(gameTime);

            for (int i = 0; i < collisionUpdates.Count; i++)
            {
                var (mesh, attr) = collisionUpdates[i];
                attr.Update(mesh, gameTime);
            }

            for (int i = 0; i < lateUpdates.Count; i++)
            {
                var (mesh, attr) = lateUpdates[i];
                attr.Update(mesh, gameTime);
            }

            // Update per-mesh instantaneous velocity from position delta
            double dt = gameTime.DeltaTime;
            for (int i = 0; i < _meshes.Count; i++)
            {
                var mesh = _meshes[i];
                Vector2 prev = _prevPositions.TryGetValue(mesh, out var p) ? p : mesh.Position;
                Vector2 cur = mesh.Position;
                Vector2 vel = (dt > 0.0) ? (cur - prev) / (float)dt : Vector2.Zero;
                mesh.Velocity = vel;
                _prevPositions[mesh] = cur;
            }
            for (int i = 0; i < _updatables.Count; i++)
            {
                _updatables[i].Update(gameTime);
            }
        }
    }
}
