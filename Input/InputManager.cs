using Veldrid.Sdl2;
using Veldrid;
using System.Collections.Generic;
using System.Numerics;

namespace Engine13.Input
{
    public class InputManager
    {
        // Current keyboard state (set by events)
        public HashSet<Key> KeysDown { get; } = new HashSet<Key>();
        
        // Current mouse position in window coordinates
        public Vector2 MousePosition { get; private set; } = Vector2.Zero;

        private bool _attached = false;

        
        public void Attach(Sdl2Window window)
        {
            if (_attached) return;  //Guard for single window lifetime
            _attached = true;

            window.KeyDown += (e) =>
            {
                KeysDown.Add(e.Key);
            };

            window.KeyUp += (e) =>
            {
                KeysDown.Remove(e.Key);
            };

            window.MouseMove += (e) =>
            {
                MousePosition = new Vector2(e.MousePosition.X, e.MousePosition.Y);
            };
        }

        public void Update(Sdl2Window window)
        {
            
        }
    }
}