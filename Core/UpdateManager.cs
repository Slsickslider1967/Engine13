using System.Collections.Generic;
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
            Forces.Reset();

            for (int i = 0; i < _meshes.Count; i++)
            {
                _meshes[i].UpdateAttributes(gameTime);
            }

            Engine13.Utilities.Attributes.MolecularDynamics.Step(gameTime);
            for (int i = 0; i < _updatables.Count; i++)
            {
                _updatables[i].Update(gameTime);
            }
        }
    }
}
