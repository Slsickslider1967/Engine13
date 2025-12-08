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

        public bool IsKeyDown(Key k) => KeysDown.Contains(k);

        public bool WasKeyPressed(Key k) => _keysPressedThisFrame.Contains(k);

        public bool WasKeyReleased(Key k) => _keysReleasedThisFrame.Contains(k);

        public bool IsMouseButtonDown(MouseButton b) => _mouseButtonsDown.Contains(b);

        public bool WasMouseButtonPressed(MouseButton b) => _mouseButtonsPressedThisFrame.Contains(b);

        public bool WasMouseButtonReleased(MouseButton b) => _mouseButtonsReleasedThisFrame.Contains(b);

        private readonly HashSet<Key> _keysPressedThisFrame = new();
        private readonly HashSet<Key> _keysReleasedThisFrame = new();

        private readonly HashSet<MouseButton> _mouseButtonsDown = new();
        private readonly HashSet<MouseButton> _mouseButtonsPressedThisFrame = new();
        private readonly HashSet<MouseButton> _mouseButtonsReleasedThisFrame = new();

        private bool _attached = false;
        private Sdl2Window? _window;

        public InputManager() { }
        public InputManager(Sdl2Window window) => Attach(window);

        public void Attach(Sdl2Window window)
        {
            if (_attached && _window == window)
                return;
            if (_attached)
                Detach();

            _attached = true;
            _window = window;

            window.KeyDown += OnKeyDown;
            window.KeyUp += OnKeyUp;
            window.MouseMove += OnMouseMove;
            window.MouseDown += OnMouseDown;
            window.MouseUp += OnMouseUp;
        }

        private void Detach()
        {
            if (_window == null)
                return;
            _window.KeyDown -= OnKeyDown;
            _window.KeyUp -= OnKeyUp;
            _window.MouseMove -= OnMouseMove;
            _window.MouseDown -= OnMouseDown;
            _window.MouseUp -= OnMouseUp;
            _attached = false;
            _window = null;
        }

        public void Update()
        {
            _keysPressedThisFrame.Clear();
            _keysReleasedThisFrame.Clear();
            _mouseButtonsPressedThisFrame.Clear();
            _mouseButtonsReleasedThisFrame.Clear();
        }

        private void OnKeyDown(KeyEvent Event)
        {
            if (KeysDown.Add(Event.Key))
                _keysPressedThisFrame.Add(Event.Key);

            if (Event.Key == Key.F11)
                ToggleFullscreen();
        }

        private void OnKeyUp(KeyEvent e)
        {
            if (KeysDown.Remove(e.Key))
                _keysReleasedThisFrame.Add(e.Key);
        }

        private void OnMouseMove(MouseMoveEventArgs e)
        {
            MousePosition = new Vector2(e.MousePosition.X, e.MousePosition.Y);
        }

        private void OnMouseDown(MouseEvent e)
        {
            if (_mouseButtonsDown.Add(e.MouseButton))
                _mouseButtonsPressedThisFrame.Add(e.MouseButton);
        }

        private void OnMouseUp(MouseEvent e)
        {
            if (_mouseButtonsDown.Remove(e.MouseButton))
                _mouseButtonsReleasedThisFrame.Add(e.MouseButton);
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
    }
}
