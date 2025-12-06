using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;

namespace Engine13.Input
{
    public class InputManager
    {
        public HashSet<Key> KeysDown { get; } = new HashSet<Key>();

        public Vector2 MousePosition { get; private set; } = Vector2.Zero;

        private bool _attached = false;
        private Sdl2Window? _window;

        public void Attach(Sdl2Window window)
        {
            if (_attached)
                return; //Guard for single window lifetime
            _attached = true;
            _window = window;

            window.KeyDown += (e) =>
            {
                KeysDown.Add(e.Key);

                // Toggle fullscreen with F11
                if (e.Key == Key.F11)
                {
                    ToggleFullscreen();
                }
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

        public void ToggleFullscreen()
        {
            if (_window == null)
                return;

            if (_window.WindowState == WindowState.BorderlessFullScreen)
            {
                _window.WindowState = WindowState.Normal;
            }
            else
            {
                _window.WindowState = WindowState.BorderlessFullScreen;
            }
        }

        public void Update(Sdl2Window window) { }
    }
}
