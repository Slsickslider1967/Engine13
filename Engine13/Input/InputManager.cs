using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;

namespace Engine13.Input
{
    public class InputManager
    {
        public float MouseWheelDelta { get; private set; }
        public HashSet<Key> KeysDown { get; } = new();
        public Vector2 MousePosition { get; private set; } = Vector2.Zero;
        public bool IsKeyDown(Key k) => KeysDown.Contains(k);
        public bool WasKeyPressed(Key k) => _keysPressedThisFrame.Contains(k);
        public bool WasKeyReleased(Key k) => _keysReleasedThisFrame.Contains(k);
        public bool IsMouseButtonDown(MouseButton b) => _mouseButtonsDown.Contains(b);
        public bool WasMouseButtonPressed(MouseButton b) => _mouseButtonsPressedThisFrame.Contains(b);
        public bool WasMouseButtonReleased(MouseButton b) => _mouseButtonsReleasedThisFrame.Contains(b);

        private readonly HashSet<Key> _keysPressedThisFrame = new(), _keysReleasedThisFrame = new();
        private readonly HashSet<MouseButton> _mouseButtonsDown = new(), _mouseButtonsPressedThisFrame = new(), _mouseButtonsReleasedThisFrame = new();
        private bool _attached; private Sdl2Window? _window;

        public InputManager() { }
        public InputManager(Sdl2Window window) => Attach(window);

        public void Attach(Sdl2Window window)
        {
            if (_attached && _window == window) return;
            if (_attached) Detach();
            _attached = true; _window = window;
            window.KeyDown += OnKeyDown; window.KeyUp += OnKeyUp;
            window.MouseMove += OnMouseMove; window.MouseDown += OnMouseDown; window.MouseUp += OnMouseUp; window.MouseWheel += OnMouseWheel;
        }

        private void Detach()
        {
            if (_window == null) return;
            _window.KeyDown -= OnKeyDown; _window.KeyUp -= OnKeyUp;
            _window.MouseMove -= OnMouseMove; _window.MouseDown -= OnMouseDown; _window.MouseUp -= OnMouseUp; _window.MouseWheel -= OnMouseWheel;
            _attached = false; _window = null;
        }

        public void Update()
        { _keysPressedThisFrame.Clear(); _keysReleasedThisFrame.Clear(); _mouseButtonsPressedThisFrame.Clear(); _mouseButtonsReleasedThisFrame.Clear(); }

        public float ConsumeMouseWheel() { float v = MouseWheelDelta; MouseWheelDelta = 0f; return v; }

        private void OnKeyDown(KeyEvent e) { if (KeysDown.Add(e.Key)) _keysPressedThisFrame.Add(e.Key); if (e.Key == Key.F11) ToggleFullscreen(); }
        private void OnKeyUp(KeyEvent e) { if (KeysDown.Remove(e.Key)) _keysReleasedThisFrame.Add(e.Key); }
        private void OnMouseMove(MouseMoveEventArgs e) => MousePosition = new Vector2(e.MousePosition.X, e.MousePosition.Y);
        private void OnMouseWheel(MouseWheelEventArgs e) { MouseWheelDelta += e.WheelDelta; if (e.WheelDelta != 0f) System.Console.WriteLine($"[InputManager] MouseWheel: {e.WheelDelta}"); }
        private void OnMouseDown(MouseEvent e) { if (_mouseButtonsDown.Add(e.MouseButton)) _mouseButtonsPressedThisFrame.Add(e.MouseButton); }
        private void OnMouseUp(MouseEvent e) { if (_mouseButtonsDown.Remove(e.MouseButton)) _mouseButtonsReleasedThisFrame.Add(e.MouseButton); }

        public void ToggleFullscreen()
        {
            if (_window == null) return;
            _window.WindowState = _window.WindowState == WindowState.BorderlessFullScreen ? WindowState.Normal : WindowState.BorderlessFullScreen;
        }
    }
}
