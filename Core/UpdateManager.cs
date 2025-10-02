using System.Collections.Generic;
using Engine13.Graphics;

namespace Engine13.Core
{
    // Central manager that updates meshes and arbitrary updatable components each frame
    public class UpdateManager
    {
        private readonly List<Mesh> _meshes = new();
        private readonly List<IUpdatable> _updatables = new();

        public void Register(Mesh mesh)
        {
            if (mesh != null) _meshes.Add(mesh);
        }

        public bool Unregister(Mesh mesh) => _meshes.Remove(mesh);

        public void Register(IUpdatable updatable)
        {
            if (updatable != null) _updatables.Add(updatable);
        }

        public bool Unregister(IUpdatable updatable) => _updatables.Remove(updatable);

        public void Clear()
        {
            _meshes.Clear();
            _updatables.Clear();
        }

        public void Update(GameTime gameTime)
        {
            // Update attribute-driven mesh behavior (e.g., gravity)
            for (int i = 0; i < _meshes.Count; i++)
            {
                _meshes[i].UpdateAttributes(gameTime);
            }

            // Update any other registered updatables
            for (int i = 0; i < _updatables.Count; i++)
            {
                _updatables[i].Update(gameTime);
            }
        }
    }
}
